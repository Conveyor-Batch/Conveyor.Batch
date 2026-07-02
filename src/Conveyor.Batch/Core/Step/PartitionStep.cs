using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// A step that divides its workload into partitions via an <see cref="IPartitioner"/>,
/// executes a worker step once per partition via an <see cref="IPartitionHandler"/>, and
/// aggregates the resulting statuses into a single manager <see cref="StepExecution"/>.
/// </summary>
internal sealed class PartitionStep : IStep
{
    private readonly IStep _workerStep;
    private readonly IPartitioner _partitioner;
    private readonly IPartitionHandler _partitionHandler;
    private readonly IJobRepository _repository;
    private readonly int _gridSize;

    /// <inheritdoc />
    public string Name { get; }

    internal PartitionStep(
        string name,
        IStep workerStep,
        IPartitioner partitioner,
        IPartitionHandler partitionHandler,
        IJobRepository repository,
        int gridSize)
    {
        Name = name;
        _workerStep = workerStep;
        _partitioner = partitioner;
        _partitionHandler = partitionHandler;
        _repository = repository;
        _gridSize = gridSize;
    }

    /// <inheritdoc />
    public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
    {
        var managerExecution = await _repository.CreateStepExecutionAsync(jobExecution, Name).ConfigureAwait(false);
        managerExecution.Status = BatchStatus.Started;
        await _repository.UpdateStepExecutionAsync(managerExecution).ConfigureAwait(false);

        try
        {
            var partitions = _partitioner.Partition(_gridSize);
            var readOnlyPartitions = new Dictionary<string, BatchExecutionContext>(partitions);

            var results = await _partitionHandler
                .HandleAsync(_workerStep, managerExecution, readOnlyPartitions, cancellationToken)
                .ConfigureAwait(false);

            managerExecution.Status = AggregateStatus(results);
            if (managerExecution.Status == BatchStatus.Failed)
                managerExecution.FailureException = results.FirstOrDefault(r => r.Status == BatchStatus.Failed)?.FailureException;
        }
        catch (Exception ex)
        {
            managerExecution.Status = BatchStatus.Failed;
            managerExecution.FailureException = ex;
        }
        finally
        {
            managerExecution.EndTime = DateTimeOffset.UtcNow;
            await _repository.UpdateStepExecutionAsync(managerExecution).ConfigureAwait(false);
        }

        return managerExecution;
    }

    /// <summary>
    /// Aggregates partition results into a single manager status: <see cref="BatchStatus.Failed"/>
    /// if any partition failed, otherwise <see cref="BatchStatus.Stopped"/> if any partition
    /// stopped, otherwise <see cref="BatchStatus.Completed"/> (including the vacuous case of
    /// zero partitions).
    /// </summary>
    private static BatchStatus AggregateStatus(IReadOnlyList<StepExecution> results)
    {
        if (results.Any(r => r.Status == BatchStatus.Failed))
            return BatchStatus.Failed;

        // No worker step in this codebase currently produces BatchStatus.Stopped (e.g.
        // ChunkOrientedStep only ever completes or fails) — this branch is kept for forward
        // compatibility with future step types that support a graceful stop.
        if (results.Any(r => r.Status == BatchStatus.Stopped))
            return BatchStatus.Stopped;

        return BatchStatus.Completed;
    }
}
