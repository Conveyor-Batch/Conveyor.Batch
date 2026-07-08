using System.Runtime.CompilerServices;
using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Listeners;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.UnitTests.Listeners;

public sealed class DeadLetterChunkListenerTests
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

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SkippedItem_WrittenToDeadLetter()
    {
        var exception = new InvalidOperationException("bad item");
        var deadLetterWriter = new InMemoryDeadLetterWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1, 2, 3]),
            new ThrowingProcessor<int>(exception),
            new CapturingWriter<int>(),
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy(),
            listener: new DeadLetterChunkListener(deadLetterWriter));

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Equal(3, deadLetterWriter.Entries.Count);
        Assert.All(deadLetterWriter.Entries, e =>
        {
            Assert.Equal("bad item", e.ExceptionMessage);
            Assert.Equal("test", e.StepName);
        });
    }

    [Fact]
    public async Task DeadLetterEntry_ContainsSerializedItem()
    {
        var deadLetterWriter = new InMemoryDeadLetterWriter();
        var engine = new ChunkOrientedEngine<string, string>(
            new ListReader<string>(["bad-record"]),
            new ThrowingProcessor<string>(new InvalidOperationException("bad")),
            new CapturingWriter<string>(),
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy(),
            listener: new DeadLetterChunkListener(deadLetterWriter));

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        var entry = Assert.Single(deadLetterWriter.Entries);
        Assert.Equal("bad-record", JsonSerializer.Deserialize<string>(entry.ItemJson));
    }

    [Fact]
    public async Task DeadLetterEntry_OccurredAt_IsRecentUtc()
    {
        var deadLetterWriter = new InMemoryDeadLetterWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1]),
            new ThrowingProcessor<int>(new InvalidOperationException("bad")),
            new CapturingWriter<int>(),
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy(),
            listener: new DeadLetterChunkListener(deadLetterWriter));

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        var entry = Assert.Single(deadLetterWriter.Entries);
        Assert.True(DateTimeOffset.UtcNow - entry.OccurredAt < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DeadLetterEntry_SkipCountAtTime_IsCorrect()
    {
        var deadLetterWriter = new InMemoryDeadLetterWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1, 2, 3]),
            new ThrowingProcessor<int>(new InvalidOperationException("bad")),
            new CapturingWriter<int>(),
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy(),
            listener: new DeadLetterChunkListener(deadLetterWriter));

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Equal([0L, 1L, 2L], deadLetterWriter.Entries.Select(e => e.SkipCountAtTime));
    }

    [Fact]
    public async Task NonSkippedItems_NotWrittenToDeadLetter()
    {
        var deadLetterWriter = new InMemoryDeadLetterWriter();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>(Enumerable.Range(1, 10)),
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 10,
            listener: new DeadLetterChunkListener(deadLetterWriter));

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Empty(deadLetterWriter.Entries);
    }

    [Fact]
    public async Task CompositeChunkListener_BothListenersCalled()
    {
        var writerA = new InMemoryDeadLetterWriter();
        var writerB = new InMemoryDeadLetterWriter();
        var composite = new CompositeChunkListener([
            new DeadLetterChunkListener(writerA),
            new DeadLetterChunkListener(writerB)
        ]);

        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1]),
            new ThrowingProcessor<int>(new InvalidOperationException("bad")),
            new CapturingWriter<int>(),
            chunkSize: 10,
            skipPolicy: new AlwaysSkipPolicy(),
            listener: composite);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Single(writerA.Entries);
        Assert.Single(writerB.Entries);
    }

    [Fact]
    public async Task StepBuilder_DeadLetter_WiresCorrectly()
    {
        var repository = new InMemoryJobRepository();
        var instance = await repository.CreateJobInstanceAsync("job1", JobParameters.Empty);
        var jobExecution = await repository.CreateJobExecutionAsync(instance, JobParameters.Empty);

        var deadLetterWriter = new InMemoryDeadLetterWriter();
        var step = new StepBuilder<int, int>(repository)
            .Reader(new ListReader<int>([1, 2, 3]))
            .Processor(new ThrowingProcessor<int>(new InvalidOperationException("bad")))
            .Writer(new CapturingWriter<int>())
            .SkipPolicy(new AlwaysSkipPolicy())
            .DeadLetter(deadLetterWriter)
            .Build("step1");

        await step.ExecuteAsync(jobExecution, CancellationToken.None);

        Assert.Equal(3, deadLetterWriter.Entries.Count);
    }
}
