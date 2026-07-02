using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IntegrationTests;

/// <summary>
/// End-to-end restart tests exercising the actual job/step restart-trigger wiring
/// (<c>SequentialJob</c> detecting a prior failed execution, <c>ChunkOrientedStep</c> cloning
/// the previous step's checkpoint) — the piece the engine-level unit tests in
/// <c>RestartabilityTests.cs</c> don't cover.
/// </summary>
public sealed class RestartJobIntegrationTests
{
    // ── Fakes ──────────────────────────────────────────────────────────

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

    private sealed class CountingWriter<T> : IItemWriter<T>
    {
        public int TotalWritten { get; private set; }

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            TotalWritten += items.Count;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Restart-aware reader over a fixed list of items, checkpointing its index.</summary>
    private sealed class RestartableRangeReader<T>(IEnumerable<T> items) : IItemReader<T>, IItemStream
    {
        private const string IndexKey = "RestartableRangeReader.index";
        private readonly List<T> _items = items.ToList();
        private int _skip;
        private int _index;

        public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken ct)
        {
            _skip = context.Get<int>(IndexKey);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken ct)
        {
            context.Put(IndexKey, _index);
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken ct) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            _index = _skip;
            for (int i = _skip; i < _items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                _index = i + 1;
                yield return _items[i];
            }
        }
    }

    /// <summary>Writer that throws when the Nth call to <see cref="WriteAsync"/> is made, and succeeds otherwise.</summary>
    private sealed class FailOnCallNumberWriter<T>(int failOnCallNumber) : IItemWriter<T>
    {
        private int _callCount;
        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            _callCount++;
            if (_callCount == failOnCallNumber)
                throw new InvalidOperationException("simulated writer failure");
            Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    // ── Test ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoStepJob_SecondStepFailsThenSucceeds_RestartResumesFromCheckpoint()
    {
        var repository = new InMemoryJobRepository();

        var step1Writer = new CountingWriter<int>();
        var step1 = new StepBuilder<int, int>(repository)
            .Reader(new ListReader<int>([1, 2, 3]))
            .Processor(new IdentityProcessor<int>())
            .Writer(step1Writer)
            .ChunkSize(10)
            .Build("step-one");

        var step2Writer = new FailOnCallNumberWriter<int>(failOnCallNumber: 2);
        var step2 = new StepBuilder<int, int>(repository)
            .Reader(new RestartableRangeReader<int>(Enumerable.Range(1, 6)))
            .Processor(new IdentityProcessor<int>())
            .Writer(step2Writer)
            .ChunkSize(3)
            .Build("step-two");

        var job = new JobBuilder("restart-job", repository)
            .AddStep(step1)
            .AddStep(step2)
            .Build();

        var parameters = JobParameters.Empty;

        // First run: step one succeeds, step two's first chunk commits (and checkpoints)
        // then its second chunk's write throws, failing the job.
        var firstExecution = await job.ExecuteAsync(parameters, CancellationToken.None);

        Assert.Equal(BatchStatus.Failed, firstExecution.Status);
        Assert.Null(firstExecution.RestartedFromExecutionId);

        var step2AfterFirstRun = await repository.GetLastStepExecutionAsync(firstExecution.Id, "step-two");
        Assert.NotNull(step2AfterFirstRun);
        Assert.Equal(BatchStatus.Failed, step2AfterFirstRun.Status);
        Assert.Equal(3, step2AfterFirstRun.ExecutionContext.Get<int>("RestartableRangeReader.index"));
        Assert.Single(step2Writer.Chunks);
        Assert.Equal([1, 2, 3], step2Writer.Chunks[0]);

        // Second run with identical parameters: should detect the prior failed execution,
        // resume step two from its saved checkpoint, and complete successfully.
        var secondExecution = await job.ExecuteAsync(parameters, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, secondExecution.Status);
        Assert.Equal(firstExecution.Id, secondExecution.RestartedFromExecutionId);

        var step2AfterSecondRun = await repository.GetLastStepExecutionAsync(secondExecution.Id, "step-two");
        Assert.NotNull(step2AfterSecondRun);
        Assert.True(step2AfterSecondRun.IsRestart);
        Assert.Equal(BatchStatus.Completed, step2AfterSecondRun.Status);

        // Only the remaining items (4,5,6) should have been written on the restart — no
        // duplicate reprocessing of the already-committed 1,2,3 chunk.
        Assert.Equal(2, step2Writer.Chunks.Count);
        Assert.Equal([4, 5, 6], step2Writer.Chunks[1]);
    }
}
