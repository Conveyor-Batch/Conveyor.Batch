using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.UnitTests.Engine;

public sealed class GracefulShutdownTests
{
    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private static StepExecutionContext MakeContext() =>
        new(new StepExecution { StepName = "test", JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } } });

    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class DelayedListReader<T>(IEnumerable<T> items, int delayMs) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                yield return item;
            }
        }
    }

    private sealed class IdentityProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult<T?>(item);
    }

    /// <summary>
    /// Cancels <paramref name="stopCts"/> once the n-th item has been processed, simulating a
    /// stop signal (e.g. SIGTERM) arriving mid-run. Safe to call from multiple concurrent
    /// workers (used by the concurrent-engine test).
    /// </summary>
    private sealed class CancelAfterNProcessor<T>(int n, CancellationTokenSource stopCts) : IItemProcessor<T, T>
    {
        private int _count;

        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _count) == n)
                stopCts.Cancel();

            return ValueTask.FromResult<T?>(item);
        }
    }

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        private readonly object _lock = new();

        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            lock (_lock)
                Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Cancels <paramref name="stopCts"/> the moment the first write begins (simulating the
    /// stop signal arriving exactly as a chunk starts committing), then delays for
    /// <paramref name="delayMs"/> observing the token the engine passes in (the abort token),
    /// so the drain-timeout deadline started by that cancellation can cut the write off early.
    /// </summary>
    private sealed class CancelThenDelayWriter<T>(CancellationTokenSource stopCts, int delayMs) : IItemWriter<T>
    {
        public async ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            stopCts.Cancel();
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }

    private sealed class CheckpointingReader<T>(IEnumerable<T> items) : IItemReader<T>, IItemStream
    {
        private readonly List<T> _items = items.ToList();

        public List<string> Calls { get; } = [];

        public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken ct)
        {
            Calls.Add("Open");
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken ct)
        {
            Calls.Add("Update");
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken ct)
        {
            Calls.Add("Close");
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in _items)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class SpyJobRepository(IJobRepository inner) : IJobRepository
    {
        public int UpdateStepExecutionCallCount { get; private set; }

        public Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters) =>
            inner.CreateJobInstanceAsync(jobName, parameters);

        public Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters) =>
            inner.CreateJobExecutionAsync(instance, parameters);

        public Task UpdateJobExecutionAsync(JobExecution execution) =>
            inner.UpdateJobExecutionAsync(execution);

        public Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName) =>
            inner.CreateStepExecutionAsync(jobExecution, stepName);

        public Task UpdateStepExecutionAsync(StepExecution stepExecution)
        {
            UpdateStepExecutionCallCount++;
            return inner.UpdateStepExecutionAsync(stepExecution);
        }

        public Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters) =>
            inner.GetLastJobExecutionAsync(jobName, parameters);

        public Task<JobExecution?> GetRunningJobExecutionAsync(
            string jobName, JobParameters parameters, CancellationToken cancellationToken = default) =>
            inner.GetRunningJobExecutionAsync(jobName, parameters, cancellationToken);

        public Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance) =>
            inner.GetJobExecutionsAsync(instance);

        public Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName) =>
            inner.GetLastStepExecutionAsync(jobExecutionId, stepName);
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopRequested_CurrentChunkCommitted_ThenStops()
    {
        using var cts = new CancellationTokenSource();
        var writer = new CapturingWriter<int>();
        var context = MakeContext();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 10)),
            new CancelAfterNProcessor<int>(3, cts),
            writer,
            chunkSize: 5,
            gracefulShutdown: GracefulShutdownOptions.Default);

        await engine.ExecuteAsync(context, cts.Token);

        Assert.Equal(BatchStatus.Stopped, context.StepExecution.Status);
        Assert.Single(writer.Chunks);
        Assert.Equal([1, 2, 3], writer.Chunks[0]);
    }

    [Fact]
    public async Task StopRequested_NoMoreItemsRead_AfterStop()
    {
        using var cts = new CancellationTokenSource();
        var writer = new CapturingWriter<int>();
        var context = MakeContext();
        var engine = new ChunkOrientedEngine<int, int>(
            new DelayedListReader<int>(Enumerable.Range(1, 100), delayMs: 2),
            new CancelAfterNProcessor<int>(10, cts),
            writer,
            chunkSize: 7,
            gracefulShutdown: GracefulShutdownOptions.Default);

        await engine.ExecuteAsync(context, cts.Token);

        Assert.Equal(BatchStatus.Stopped, context.StepExecution.Status);
        var totalWritten = writer.Chunks.Sum(c => c.Count);
        Assert.True(totalWritten < 100, $"Expected fewer than 100 items written, got {totalWritten}");
        Assert.Equal(10, totalWritten);
    }

    [Fact]
    public async Task DrainTimeoutExpires_StatusFailed()
    {
        using var cts = new CancellationTokenSource();
        var repository = new InMemoryJobRepository();
        var writer = new CancelThenDelayWriter<int>(cts, delayMs: 500);

        var step = new StepBuilder<int, int>(repository)
            .Reader(new ListReader<int>([1]))
            .Processor(new IdentityProcessor<int>())
            .Writer(writer)
            .ChunkSize(1)
            .GracefulShutdown(new GracefulShutdownOptions { DrainTimeout = TimeSpan.FromMilliseconds(100) })
            .Build("drain-timeout-step");

        var jobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } };

        var stepExecution = await step.ExecuteAsync(jobExecution, cts.Token);

        Assert.Equal(BatchStatus.Failed, stepExecution.Status);
        Assert.IsAssignableFrom<OperationCanceledException>(stepExecution.FailureException);
    }

    [Fact]
    public async Task NoGracefulShutdown_HardCancel_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 100)),
            new CancelAfterNProcessor<int>(3, cts),
            new CapturingWriter<int>(),
            chunkSize: 50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ExecuteAsync(MakeContext(), cts.Token));
    }

    [Fact]
    public async Task GracefulShutdown_EmptyPartialChunk_NoExtraWrite()
    {
        using var cts = new CancellationTokenSource();
        var writer = new CapturingWriter<int>();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 5)),
            new CancelAfterNProcessor<int>(5, cts),
            writer,
            chunkSize: 5,
            gracefulShutdown: GracefulShutdownOptions.Default);

        await engine.ExecuteAsync(MakeContext(), cts.Token);

        Assert.Single(writer.Chunks);
        Assert.Equal(5, writer.Chunks[0].Count);
    }

    [Fact]
    public async Task ConcurrentEngine_StopRequested_DrainsFlushed()
    {
        using var cts = new CancellationTokenSource();
        var writer = new CapturingWriter<int>();
        var context = MakeContext();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new DelayedListReader<int>(Enumerable.Range(1, 100), delayMs: 2),
            new CancelAfterNProcessor<int>(20, cts),
            writer,
            chunkSize: 7,
            degreeOfParallelism: 4,
            gracefulShutdown: GracefulShutdownOptions.Default);

        await engine.ExecuteAsync(context, cts.Token);

        Assert.Equal(BatchStatus.Stopped, context.StepExecution.Status);
        Assert.True(writer.Chunks.Count > 0, "Expected at least one chunk to be written.");
        var totalWritten = writer.Chunks.Sum(c => c.Count);
        Assert.True(totalWritten is > 0 and < 100, $"Expected a partial write, got {totalWritten}");
    }

    [Fact]
    public async Task CheckpointPersisted_AfterGracefulStop()
    {
        using var cts = new CancellationTokenSource();
        var reader = new CheckpointingReader<int>(Enumerable.Range(1, 10));
        var writer = new CapturingWriter<int>();
        var context = MakeContext();
        var repository = new SpyJobRepository(new InMemoryJobRepository());

        var engine = new ChunkOrientedEngine<int, int>(
            reader,
            new CancelAfterNProcessor<int>(3, cts),
            writer,
            chunkSize: 2,
            jobRepository: repository,
            gracefulShutdown: GracefulShutdownOptions.Default);

        await engine.ExecuteAsync(context, cts.Token);

        Assert.Equal(BatchStatus.Stopped, context.StepExecution.Status);
        Assert.Contains("Update", reader.Calls);
        Assert.True(repository.UpdateStepExecutionCallCount > 0);
    }
}
