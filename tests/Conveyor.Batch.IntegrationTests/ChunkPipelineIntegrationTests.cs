using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.IntegrationTests;

public sealed class ChunkPipelineIntegrationTests
{
    // ── Fakes ──────────────────────────────────────────────────────────

    private sealed class RangeReader(int count) : IItemReader<int>
    {
        public async IAsyncEnumerable<int> ReadAsync(
            StepExecutionContext ctx,
            [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
    }

    private sealed class SlowRangeReader(int count, int delayMs) : IItemReader<int>
    {
        public async IAsyncEnumerable<int> ReadAsync(
            StepExecutionContext ctx,
            [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(delayMs, ct);
                yield return i;
            }
        }
    }

    private sealed class IdentityProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult<T?>(item);
    }

    /// <summary>Throws for items divisible by the given divisor.</summary>
    private sealed class FaultyProcessor<T>(int faultEvery, Func<T, int> selector) : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct)
        {
            if (selector(item) % faultEvery == 0)
                throw new InvalidOperationException($"Bad item: {item}");
            return ValueTask.FromResult<T?>(item);
        }
    }

    private sealed class CapturingWriter : IItemWriter<int>
    {
        private readonly List<int> _all = [];
        public IReadOnlyList<int> Written => _all;

        public ValueTask WriteAsync(IReadOnlyList<int> items, StepExecutionContext ctx, CancellationToken ct)
        {
            _all.AddRange(items);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => true;
    }

    private static StepExecutionContext MakeContext() =>
        new(new StepExecution
        {
            StepName = "pipeline-step",
            JobExecution = new JobExecution
            {
                JobInstance = new JobInstance { JobName = "pipeline-job" }
            }
        });

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FullPipeline_1000Items_ChunkSize50_AllItemsWritten()
    {
        const int itemCount = 1000;
        const int chunkSize = 50;

        var writer = new CapturingWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new RangeReader(itemCount),
            new IdentityProcessor<int>(),
            writer,
            chunkSize);

        var context = MakeContext();
        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(itemCount, writer.Written.Count);
        Assert.Equal(itemCount, context.WriteCount);
        // Verify all expected items were written (0..999)
        var expected = Enumerable.Range(0, itemCount).ToHashSet();
        Assert.All(writer.Written, item => Assert.Contains(item, expected));
    }

    [Fact]
    public async Task PipelineWithSkip_5BadItemsAmong100_5SkippedAnd95Written()
    {
        // Items 0..99; items divisible by 20 are bad: 0,20,40,60,80 → 5 bad items
        const int itemCount = 100;

        var writer = new CapturingWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new RangeReader(itemCount),
            new FaultyProcessor<int>(faultEvery: 20, selector: x => x),
            writer,
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy());

        var context = MakeContext();
        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(5, context.SkipCount);
        Assert.Equal(95, writer.Written.Count);
        Assert.Equal(95, context.WriteCount);
    }

    [Fact]
    public async Task PipelineWithCancellation_CancelAfter200ms_ThrowsAndPartialResultsConsistent()
    {
        using var cts = new CancellationTokenSource(millisecondsDelay: 200);

        var writer = new CapturingWriter();
        // Each item takes 50ms — with 1000 items we'll be cancelled well before the end
        var engine = new ChunkOrientedEngine<int, int>(
            new SlowRangeReader(count: 1000, delayMs: 50),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 5);

        var context = MakeContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ExecuteAsync(context, cts.Token));

        // WriteCount must equal the actual number of items stored
        Assert.Equal(writer.Written.Count, (int)context.WriteCount);
        // Some items should have been processed but not all 1000
        Assert.True(context.WriteCount < 1000);
    }

    [Fact]
    public async Task EmptySource_JobCompletesWithZeroReadsAndZeroWrites()
    {
        var writer = new CapturingWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new RangeReader(0),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10);

        var context = MakeContext();
        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(writer.Written);
        Assert.Equal(0, context.WriteCount);
        Assert.Equal(0, context.SkipCount);
    }

    [Fact]
    public async Task FullPipeline_ThroughJobRepository_WriteCountPersisted()
    {
        var repo = new InMemoryJobRepository();
        var parameters = JobParameters.Empty;

        var instance = await repo.CreateJobInstanceAsync("PipelineJob", parameters);
        var jobExecution = await repo.CreateJobExecutionAsync(instance, parameters);
        var stepExecution = await repo.CreateStepExecutionAsync(jobExecution, "chunk-step");

        var writer = new CapturingWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new RangeReader(50),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10);

        var context = new StepExecutionContext(stepExecution);
        await engine.ExecuteAsync(context, CancellationToken.None);

        stepExecution.Status = BatchStatus.Completed;
        await repo.UpdateStepExecutionAsync(stepExecution);

        Assert.Equal(50, writer.Written.Count);
        Assert.Equal(50, stepExecution.WriteCount);
    }
}
