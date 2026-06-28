using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.UnitTests.Engine;

public sealed class ChunkOrientedEngineTests
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

    private sealed class ThrowingProcessor<T>(Exception ex) : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            throw ex;
    }

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => true;
    }

    private sealed class NeverSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => false;
    }

    private sealed class LimitedSkipPolicy(int limit) : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => skipCount < limit;
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_AllItemsProcessedAndWrittenInChunks()
    {
        var items = Enumerable.Range(1, 10).ToList();
        var writer = new CapturingWriter<int>();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(items),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 3);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        // 10 items / chunk 3 → chunks of 3, 3, 3, 1
        Assert.Equal(4, writer.Chunks.Count);
        Assert.Equal([3, 3, 3, 1], writer.Chunks.Select(c => c.Count));
        Assert.Equal(items, writer.Chunks.SelectMany(c => c));
    }

    [Fact]
    public async Task EmptyInput_NoWritesCalled_JobCompletes()
    {
        var writer = new CapturingWriter<int>();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([]),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Empty(writer.Chunks);
    }

    [Fact]
    public async Task SkipPolicy_SkippableException_ItemSkippedAndCountIncremented()
    {
        var processor = new ThrowingProcessor<int>(new InvalidOperationException("bad item"));
        var writer = new CapturingWriter<int>();
        var context = MakeContext();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1, 2, 3]),
            processor,
            writer,
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy());

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(3, context.SkipCount);
        Assert.Empty(writer.Chunks);
    }

    [Fact]
    public async Task SkipPolicy_NonSkippableException_PropagatesException()
    {
        var processor = new ThrowingProcessor<int>(new InvalidOperationException("fatal"));
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1]),
            processor,
            new CapturingWriter<int>(),
            chunkSize: 10,
            skipPolicy: new NeverSkipPolicy());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(MakeContext(), CancellationToken.None));
    }

    [Fact]
    public async Task SkipPolicy_LimitedSkips_StopsSkippingAfterLimit()
    {
        var processor = new ThrowingProcessor<int>(new InvalidOperationException("bad"));
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1, 2, 3]),
            processor,
            new CapturingWriter<int>(),
            chunkSize: 10,
            skipPolicy: new LimitedSkipPolicy(limit: 2)); // 3rd item throws and is not skipped

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(MakeContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_TokenCancelledMidStream_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        int callCount = 0;

        var processor = new FuncProcessor<int, int>(async (item, ctx, ct) =>
        {
            if (++callCount == 3) await cts.CancelAsync();
            return item;
        });

        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 100)),
            processor,
            new CapturingWriter<int>(),
            chunkSize: 50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ExecuteAsync(MakeContext(), cts.Token));
    }

    [Fact]
    public async Task NullProcessorOutput_ItemFilteredFromChunk()
    {
        // Use strings so null return is a valid reference-type null (not a value type issue)
        var processor = new FuncProcessor<string, string>((item, _, _) =>
            new ValueTask<string?>(item.Length % 2 == 0 ? item : null));

        var writer = new CapturingWriter<string>();
        var engine = new ChunkOrientedEngine<string, string>(
            new ListReader<string>(["ab", "c", "de", "f", "gh"]),
            processor,
            writer,
            chunkSize: 10);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        // "ab", "de", "gh" have even length → kept; "c", "f" → filtered
        Assert.Equal(["ab", "de", "gh"], writer.Chunks.SelectMany(c => c));
    }

    [Fact]
    public async Task FlushRemaining_TailChunkWrittenWhenNotFullChunkSize()
    {
        var writer = new CapturingWriter<int>();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 7)),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 3);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        // 7 items / chunk 3 → 3, 3, 1
        Assert.Equal(3, writer.Chunks.Count);
        Assert.Single(writer.Chunks.Last());
    }

    [Fact]
    public async Task WriteCount_TrackedCorrectly()
    {
        var context = MakeContext();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 10)),
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 4);

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(10, context.WriteCount);
    }

    [Fact]
    public async Task ChunkListener_HooksCalledAtCorrectPoints()
    {
        var listener = new RecordingChunkListener();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1, 2]),
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 10,
            listener: listener);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Contains("BeforeWrite", listener.Events);
        Assert.Contains("AfterWrite", listener.Events);
    }

    // ──────────────────────────────────────────────────────────────
    // Helper fakes
    // ──────────────────────────────────────────────────────────────

    private sealed class FuncProcessor<TIn, TOut>(Func<TIn, StepExecutionContext, CancellationToken, ValueTask<TOut?>> func) : IItemProcessor<TIn, TOut>
    {
        public FuncProcessor(Func<TIn, StepExecutionContext, CancellationToken, TOut?> sync)
            : this((item, ctx, ct) => ValueTask.FromResult(sync(item, ctx, ct))) { }

        public ValueTask<TOut?> ProcessAsync(TIn item, StepExecutionContext ctx, CancellationToken ct) =>
            func(item, ctx, ct);
    }

    private sealed class RecordingChunkListener : IChunkListener
    {
        public List<string> Events { get; } = [];

        public ValueTask BeforeChunkAsync(StepExecutionContext ctx, CancellationToken ct) { Events.Add("BeforeChunk"); return ValueTask.CompletedTask; }
        public ValueTask AfterChunkAsync(StepExecutionContext ctx, CancellationToken ct) { Events.Add("AfterChunk"); return ValueTask.CompletedTask; }
        public ValueTask OnChunkErrorAsync(StepExecutionContext ctx, Exception ex, CancellationToken ct) { Events.Add("OnChunkError"); return ValueTask.CompletedTask; }
        public ValueTask BeforeWriteAsync<T>(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct) { Events.Add("BeforeWrite"); return ValueTask.CompletedTask; }
        public ValueTask AfterWriteAsync<T>(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct) { Events.Add("AfterWrite"); return ValueTask.CompletedTask; }
        public ValueTask OnSkipAsync<T>(T item, Exception ex, StepExecutionContext ctx, CancellationToken ct) { Events.Add("OnSkip"); return ValueTask.CompletedTask; }
    }
}
