using System.Diagnostics;
using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.UnitTests.Engine;

public sealed class ConcurrentChunkOrientedEngineTests
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

    private sealed class IdentityProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult<T?>(item);
    }

    private sealed class DelayProcessor<T>(int delayMs) : IItemProcessor<T, T>
    {
        public async ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct)
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            return item;
        }
    }

    /// <summary>
    /// Directly measures how many items are processed concurrently, via entry/exit counting
    /// around a delay, rather than inferring parallelism from wall-clock elapsed time — the
    /// latter is sensitive to CI runner speed/load and produces a flaky test.
    /// </summary>
    private sealed class ConcurrencyTrackingProcessor<T>(int delayMs) : IItemProcessor<T, T>
    {
        private int _current;
        private int _maxObserved;

        public int MaxObservedConcurrency => _maxObserved;

        public async ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref _current);
            InterlockedMax(ref _maxObserved, current);
            try
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                return item;
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class ThrowOnPredicateProcessor<T>(Func<T, bool> shouldThrow, Func<Exception> exceptionFactory) : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct)
        {
            if (shouldThrow(item))
                throw exceptionFactory();
            return ValueTask.FromResult<T?>(item);
        }
    }

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        private readonly List<IReadOnlyList<T>> _chunks = [];
        private readonly object _lock = new();

        public IReadOnlyList<IReadOnlyList<T>> Chunks => _chunks;

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            lock (_lock)
                _chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConcurrencyTrackingWriter<T> : IItemWriter<T>
    {
        private readonly List<IReadOnlyList<T>> _chunks = [];
        private int _current;
        private int _maxObserved;

        public IReadOnlyList<IReadOnlyList<T>> Chunks => _chunks;
        public int MaxObservedConcurrency => _maxObserved;

        public async ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref _current);
            InterlockedMax(ref _maxObserved, current);
            try
            {
                await Task.Delay(1, ct).ConfigureAwait(false);
                lock (_chunks)
                    _chunks.Add(items.ToList());
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int initial;
        do
        {
            initial = target;
            if (value <= initial) return;
        } while (Interlocked.CompareExchange(ref target, value, initial) != initial);
    }

    private sealed class AlwaysSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => true;
    }

    private sealed class NeverSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => false;
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllItemsProcessed_NoneDropped()
    {
        var items = Enumerable.Range(1, 100).ToList();
        var writer = new CapturingWriter<int>();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(items),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10,
            degreeOfParallelism: 4);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        var written = writer.Chunks.SelectMany(c => c).OrderBy(x => x).ToList();
        Assert.Equal(items.OrderBy(x => x).ToList(), written);
    }

    [Fact]
    public async Task ParallelismActuallyOccurs()
    {
        var items = Enumerable.Range(1, 20).ToList();
        var writer = new CapturingWriter<int>();
        var processor = new ConcurrencyTrackingProcessor<int>(50);
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(items),
            processor,
            writer,
            chunkSize: 10,
            degreeOfParallelism: 4);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        // Measures actual concurrent overlap directly rather than inferring parallelism from
        // wall-clock elapsed time, which is flaky under slower/shared CI runners.
        Assert.True(processor.MaxObservedConcurrency > 1,
            $"Expected multiple workers to process items concurrently, observed max concurrency of {processor.MaxObservedConcurrency}.");
        Assert.Equal(20, writer.Chunks.Sum(c => c.Count));
    }

    [Fact]
    public async Task SkipPolicy_SkippableItems_NotWritten()
    {
        var processor = new ThrowOnPredicateProcessor<int>(
            item => item % 2 == 0,
            () => new InvalidOperationException("even item"));

        var writer = new CapturingWriter<int>();
        var context = MakeContext();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 10)),
            processor,
            writer,
            chunkSize: 10,
            degreeOfParallelism: 4,
            skipPolicy: new AlwaysSkipPolicy());

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(5, context.SkipCount);
        var written = writer.Chunks.SelectMany(c => c).OrderBy(x => x).ToList();
        Assert.Equal([1, 3, 5, 7, 9], written);
    }

    [Fact]
    public async Task NonSkippableException_PropagatesAndCancelsOtherWorkers()
    {
        var processor = new ThrowOnPredicateProcessor<int>(
            item => item == 3,
            () => new InvalidOperationException("fatal"));

        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 10)),
            processor,
            new CapturingWriter<int>(),
            chunkSize: 10,
            degreeOfParallelism: 4,
            skipPolicy: new NeverSkipPolicy());

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(MakeContext(), CancellationToken.None));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Engine should not hang after a fatal exception.");
    }

    [Fact]
    public async Task CancellationMidRun_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(200);

        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 100)),
            new DelayProcessor<int>(20),
            new CapturingWriter<int>(),
            chunkSize: 10,
            degreeOfParallelism: 4);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ExecuteAsync(MakeContext(), cts.Token));
        stopwatch.Stop();

        // Sequential (100 * 20ms) would take 2000ms; unhindered parallel (DOP 4) ~500ms.
        // Cancellation at 200ms must cut the run short well before either completes.
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Expected prompt cancellation, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task WriterCalledSequentially()
    {
        var writer = new ConcurrencyTrackingWriter<int>();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 200)),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 5,
            degreeOfParallelism: 8);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Equal(1, writer.MaxObservedConcurrency);
        Assert.Equal(200, writer.Chunks.Sum(c => c.Count));
    }

    [Fact]
    public async Task ChunkSizeRespected()
    {
        var writer = new CapturingWriter<int>();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 30)),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10,
            degreeOfParallelism: 3);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.All(writer.Chunks, chunk => Assert.True(chunk.Count <= 10));
        Assert.Equal(30, writer.Chunks.Sum(c => c.Count));
    }

    [Fact]
    public async Task EmptyInput_NoWritesCalled()
    {
        var writer = new CapturingWriter<int>();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>([]),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10,
            degreeOfParallelism: 4);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Empty(writer.Chunks);
    }

    [Fact]
    public async Task FlushRemaining_TailChunkWritten()
    {
        var writer = new CapturingWriter<int>();
        var engine = new ConcurrentChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 7)),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 3,
            degreeOfParallelism: 2);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Equal(7, writer.Chunks.Sum(c => c.Count));
        Assert.All(writer.Chunks, chunk => Assert.True(chunk.Count <= 3));
        Assert.True(writer.Chunks.Count >= 3);
        Assert.Contains(writer.Chunks, chunk => chunk.Count < 3);
    }

    [Fact]
    public async Task StepBuilder_DegreeOfParallelism_UsesCorrectEngine()
    {
        var repository = new InMemoryJobRepository();
        var writer = new CapturingWriter<int>();

        var step = new StepBuilder<int, int>(repository)
            .Reader(new ListReader<int>(Enumerable.Range(1, 50)))
            .Processor(new IdentityProcessor<int>())
            .Writer(writer)
            .ChunkSize(10)
            .DegreeOfParallelism(4)
            .Build("concurrent-step");

        var jobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } };

        var stepExecution = await step.ExecuteAsync(jobExecution, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, stepExecution.Status);
        Assert.Equal(50, stepExecution.WriteCount);
        Assert.Equal(50, writer.Chunks.Sum(c => c.Count));
    }
}
