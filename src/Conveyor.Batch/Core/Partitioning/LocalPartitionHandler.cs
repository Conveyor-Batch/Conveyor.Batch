using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Partitioning;

/// <summary>
/// Executes each partition's worker step locally, in parallel, within the current process.
/// </summary>
public sealed class LocalPartitionHandler : IPartitionHandler
{
    private readonly IJobRepository _repository;
    private readonly int _maxDegreeOfParallelism;

    /// <summary>
    /// Initializes a new <see cref="LocalPartitionHandler"/>.
    /// </summary>
    /// <param name="repository">The job repository used to persist per-partition step executions.</param>
    /// <param name="maxDegreeOfParallelism">
    /// The maximum number of partitions to run concurrently, or <c>-1</c> for unbounded
    /// parallelism (all partitions start immediately).
    /// </param>
    public LocalPartitionHandler(IJobRepository repository, int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (maxDegreeOfParallelism != -1)
            ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        _repository = repository;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StepExecution>> HandleAsync(
        IStep workerStep,
        StepExecution managerExecution,
        IReadOnlyDictionary<string, BatchExecutionContext> partitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerStep);
        ArgumentNullException.ThrowIfNull(managerExecution);
        ArgumentNullException.ThrowIfNull(partitions);

        using var semaphore = _maxDegreeOfParallelism > 0
            ? new SemaphoreSlim(_maxDegreeOfParallelism, _maxDegreeOfParallelism)
            : null;

        var tasks = partitions.Select(kvp =>
            RunPartitionAsync(kvp.Key, kvp.Value, workerStep, managerExecution, semaphore, cancellationToken));

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<StepExecution> RunPartitionAsync(
        string partitionName,
        BatchExecutionContext partitionContext,
        IStep workerStep,
        StepExecution managerExecution,
        SemaphoreSlim? semaphore,
        CancellationToken cancellationToken)
    {
        var managerJobExecution = managerExecution.JobExecution;

        if (semaphore is null)
            return await RunWorkerAsync(partitionName, partitionContext, workerStep, managerJobExecution, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // Cancelled while still queued behind MaxDegreeOfParallelism — no worker ever ran
            // for this partition. Persist a Failed record instead of letting the exception
            // propagate out of Task.WhenAll, which would discard the results of sibling
            // partitions that had already completed successfully.
            return await PersistFailedPartitionAsync(managerJobExecution, partitionName, partitionContext, ex)
                .ConfigureAwait(false);
        }

        try
        {
            return await RunWorkerAsync(partitionName, partitionContext, workerStep, managerJobExecution, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<StepExecution> RunWorkerAsync(
        string partitionName,
        BatchExecutionContext partitionContext,
        IStep workerStep,
        JobExecution managerJobExecution,
        CancellationToken cancellationToken)
    {
        var tracked = await _repository
            .CreateStepExecutionAsync(managerJobExecution, partitionName)
            .ConfigureAwait(false);
        tracked.ExecutionContext = ClonePartitionContext(partitionContext);
        tracked.Status = BatchStatus.Started;
        await _repository.UpdateStepExecutionAsync(tracked).ConfigureAwait(false);

        var clonedJobExecution = new JobExecution
        {
            Id = managerJobExecution.Id,
            JobInstance = managerJobExecution.JobInstance,
            Parameters = managerJobExecution.Parameters,
            Status = managerJobExecution.Status,
            StartTime = managerJobExecution.StartTime,
            ExecutionContext = ClonePartitionContext(partitionContext)
        };

        StepExecution workerResult;
        try
        {
            workerResult = await workerStep.ExecuteAsync(clonedJobExecution, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            workerResult = new StepExecution
            {
                StepName = partitionName,
                JobExecution = clonedJobExecution,
                Status = BatchStatus.Failed,
                FailureException = ex,
                EndTime = DateTimeOffset.UtcNow
            };
        }

        tracked.Status = workerResult.Status;
        tracked.EndTime = workerResult.EndTime;
        tracked.FailureException = workerResult.FailureException;
        tracked.CopyCountersFrom(workerResult);

        await _repository.UpdateStepExecutionAsync(tracked).ConfigureAwait(false);

        return tracked;
    }

    private async Task<StepExecution> PersistFailedPartitionAsync(
        JobExecution managerJobExecution,
        string partitionName,
        BatchExecutionContext partitionContext,
        Exception exception)
    {
        var tracked = await _repository
            .CreateStepExecutionAsync(managerJobExecution, partitionName)
            .ConfigureAwait(false);
        tracked.ExecutionContext = ClonePartitionContext(partitionContext);
        tracked.Status = BatchStatus.Failed;
        tracked.FailureException = exception;
        tracked.EndTime = DateTimeOffset.UtcNow;
        await _repository.UpdateStepExecutionAsync(tracked).ConfigureAwait(false);

        return tracked;
    }

    /// <summary>
    /// Creates an independent copy of <paramref name="context"/> so the manager-tracked
    /// partition record and the cloned <see cref="JobExecution"/> handed to the worker never
    /// alias the same mutable <see cref="BatchExecutionContext"/> instance.
    /// </summary>
    private static BatchExecutionContext ClonePartitionContext(BatchExecutionContext context) =>
        BatchExecutionContext.FromDictionary(new Dictionary<string, string>(context.ToDictionary()));
}
