using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Partitioning;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.UnitTests.Partitioning;

public sealed class PartitionStepTests
{
    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private static JobExecution MakeJobExecution() =>
        new() { JobInstance = new JobInstance { JobName = "job" } };

    private sealed class ConfigurableStep : IStep
    {
        private readonly BatchStatus _resultStatus;
        private readonly TimeSpan _delay;
        private readonly bool _shouldThrow;
        private readonly Func<JobExecution, bool>? _shouldThrowFor;
        private readonly Action? _onExecuting;

        public string Name { get; }

        public ConfigurableStep(
            string name,
            BatchStatus resultStatus = BatchStatus.Completed,
            TimeSpan delay = default,
            bool shouldThrow = false,
            Func<JobExecution, bool>? shouldThrowFor = null,
            Action? onExecuting = null)
        {
            Name = name;
            _resultStatus = resultStatus;
            _delay = delay;
            _shouldThrow = shouldThrow;
            _shouldThrowFor = shouldThrowFor;
            _onExecuting = onExecuting;
        }

        public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
        {
            _onExecuting?.Invoke();

            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

            if (_shouldThrow || (_shouldThrowFor?.Invoke(jobExecution) ?? false))
                throw new InvalidOperationException($"Worker {Name} failed intentionally.");

            return new StepExecution
            {
                StepName = Name,
                JobExecution = jobExecution,
                Status = _resultStatus,
                EndTime = DateTimeOffset.UtcNow
            };
        }
    }

    private sealed class ConcurrencyTrackingStep(TimeSpan delay) : IStep
    {
        private int _current;
        private int _max;

        public int MaxObservedConcurrency => _max;

        public string Name => "worker";

        public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref _current);
            TrackMax(ref _max, current);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }

            return new StepExecution
            {
                StepName = Name,
                JobExecution = jobExecution,
                Status = BatchStatus.Completed,
                EndTime = DateTimeOffset.UtcNow
            };
        }

        private static void TrackMax(ref int location, int value)
        {
            int initial;
            do
            {
                initial = location;
                if (value <= initial) return;
            }
            while (Interlocked.CompareExchange(ref location, value, initial) != initial);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllPartitionsSucceed_ManagerStatusCompleted()
    {
        var repository = new InMemoryJobRepository();
        var worker = new ConfigurableStep("worker");
        var step = new PartitionStepBuilder(repository)
            .Worker(worker)
            .Partitioner(new RangePartitioner(1, 100))
            .GridSize(4)
            .Build("partition-step");

        var result = await step.ExecuteAsync(MakeJobExecution(), CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, result.Status);
    }

    [Fact]
    public async Task OnePartitionFails_ManagerStatusFailed()
    {
        var repository = new InMemoryJobRepository();
        var worker = new ConfigurableStep(
            "worker",
            shouldThrowFor: jobExecution => jobExecution.ExecutionContext.Get<long>("partition.minValue") == 1);
        var step = new PartitionStepBuilder(repository)
            .Worker(worker)
            .Partitioner(new RangePartitioner(1, 100))
            .GridSize(4)
            .Build("partition-step");

        var result = await step.ExecuteAsync(MakeJobExecution(), CancellationToken.None);

        Assert.Equal(BatchStatus.Failed, result.Status);
        Assert.NotNull(result.FailureException);
    }

    [Fact]
    public async Task PartitionsRunInParallel()
    {
        var repository = new InMemoryJobRepository();
        var worker = new ConcurrencyTrackingStep(TimeSpan.FromMilliseconds(200));
        var step = new PartitionStepBuilder(repository)
            .Worker(worker)
            .Partitioner(new RangePartitioner(1, 100))
            .GridSize(4)
            .Build("partition-step");

        await step.ExecuteAsync(MakeJobExecution(), CancellationToken.None);

        // Measures actual concurrent overlap directly rather than inferring parallelism from
        // wall-clock elapsed time, which is flaky under slower/shared CI runners.
        Assert.True(worker.MaxObservedConcurrency > 1,
            $"Expected multiple partitions to run concurrently, observed max concurrency of {worker.MaxObservedConcurrency}.");
    }

    [Fact]
    public async Task MaxDegreeOfParallelism_Respected()
    {
        var repository = new InMemoryJobRepository();
        var worker = new ConcurrencyTrackingStep(TimeSpan.FromMilliseconds(50));
        var step = new PartitionStepBuilder(repository)
            .Worker(worker)
            .Partitioner(new RangePartitioner(1, 800))
            .GridSize(8)
            .MaxDegreeOfParallelism(2)
            .Build("partition-step");

        await step.ExecuteAsync(MakeJobExecution(), CancellationToken.None);

        Assert.True(worker.MaxObservedConcurrency <= 2,
            $"Observed max concurrency {worker.MaxObservedConcurrency} exceeded the configured cap of 2.");
    }

    [Fact]
    public async Task CancellationToken_PropagatedToWorkers()
    {
        var repository = new InMemoryJobRepository();
        var startedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new ConfigurableStep(
            "worker",
            delay: TimeSpan.FromSeconds(2),
            onExecuting: () => startedSignal.TrySetResult());
        var step = new PartitionStepBuilder(repository)
            .Worker(worker)
            .Partitioner(new RangePartitioner(1, 100))
            .GridSize(4)
            .Build("partition-step");

        using var cts = new CancellationTokenSource();
        var executeTask = step.ExecuteAsync(MakeJobExecution(), cts.Token);

        await startedSignal.Task;
        await cts.CancelAsync();

        var result = await executeTask;

        Assert.Equal(BatchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CancellationWhileQueuedBehindSemaphore_PartitionMarkedFailed_SiblingsStillReturned()
    {
        var repository = new InMemoryJobRepository();
        var jobInstance = await repository.CreateJobInstanceAsync("job", JobParameters.Empty);
        var jobExecution = await repository.CreateJobExecutionAsync(jobInstance, JobParameters.Empty);
        var managerExecution = await repository.CreateStepExecutionAsync(jobExecution, "manager");

        var startedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new ConfigurableStep(
            "worker",
            delay: TimeSpan.FromSeconds(2),
            onExecuting: () => startedSignal.TrySetResult());

        var partitioner = new RangePartitioner(1, 3);
        IReadOnlyDictionary<string, BatchExecutionContext> partitions =
            new Dictionary<string, BatchExecutionContext>(partitioner.Partition(3));

        // Cap of 1: only one partition can run at a time, so the other two are left waiting
        // on the semaphore when cancellation fires.
        var handler = new LocalPartitionHandler(repository, maxDegreeOfParallelism: 1);

        using var cts = new CancellationTokenSource();
        var handleTask = handler.HandleAsync(worker, managerExecution, partitions, cts.Token);

        await startedSignal.Task;
        await cts.CancelAsync();

        // Before the fix, cancelling a partition still queued behind the semaphore would let
        // an OperationCanceledException propagate out of Task.WhenAll, causing HandleAsync
        // itself to throw and discarding the other partitions' results entirely.
        var results = await handleTask;

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(BatchStatus.Failed, r.Status));
        Assert.All(results, r => Assert.NotNull(r.FailureException));
    }

    [Fact]
    public async Task Repository_PersistsEachPartitionStepExecution()
    {
        var repository = new InMemoryJobRepository();
        var jobInstance = await repository.CreateJobInstanceAsync("job", JobParameters.Empty);
        var jobExecution = await repository.CreateJobExecutionAsync(jobInstance, JobParameters.Empty);
        var managerExecution = await repository.CreateStepExecutionAsync(jobExecution, "manager");

        var partitioner = new RangePartitioner(1, 100);
        IReadOnlyDictionary<string, BatchExecutionContext> partitions = new Dictionary<string, BatchExecutionContext>(partitioner.Partition(4));

        var handler = new LocalPartitionHandler(repository);
        var worker = new ConfigurableStep("worker");

        var results = await handler.HandleAsync(worker, managerExecution, partitions, CancellationToken.None);

        Assert.Equal(4, results.Count);
        for (int i = 0; i < 4; i++)
            Assert.Contains(results, r => r.StepName == $"partition{i}");

        var allIds = results.Select(r => r.Id).Append(managerExecution.Id).ToList();
        Assert.Equal(5, allIds.Distinct().Count());
        Assert.All(allIds, id => Assert.NotEqual(0, id));
    }
}
