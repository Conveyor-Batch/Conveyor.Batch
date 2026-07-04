using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Job.Flow;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IntegrationTests;

/// <summary>
/// End-to-end tests for <see cref="FluentJobBuilder"/> using real chunk-oriented steps built via
/// <see cref="StepBuilder{TInput,TOutput}"/>.
/// </summary>
public sealed class FlowJobIntegrationTests
{
    // ── Fakes ──────────────────────────────────────────────────────────

    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(
            StepExecutionContext ctx,
            [EnumeratorCancellation] CancellationToken ct)
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

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        public int TotalWritten { get; private set; }

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            TotalWritten += items.Count;
            return ValueTask.CompletedTask;
        }
    }

    private static IStep BuildStep<T>(
        string name, IJobRepository repo, IEnumerable<T> items, CapturingWriter<T> writer, int chunkSize = 10) =>
        new StepBuilder<T, T>(repo)
            .Reader(new ListReader<T>(items))
            .Processor(new IdentityProcessor<T>())
            .Writer(writer)
            .ChunkSize(chunkSize)
            .Build(name);

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task End_To_End_ThreeStepConditionalFlow()
    {
        var repo = new InMemoryJobRepository();
        var writer = new CapturingWriter<int>();

        var step1 = BuildStep("step-1", repo, Enumerable.Range(0, 10), writer);
        var step2 = BuildStep("step-2", repo, Enumerable.Range(0, 5), writer);
        var step3 = BuildStep("step-3", repo, Enumerable.Range(0, 0), writer);

        var job = new FluentJobBuilder("three-step-flow", repo)
            .Start(step1)
                .On("COMPLETED").To(step2)
            .From(step2)
                .On("COMPLETED").To(step3)
            .From(step3)
                .On("COMPLETED").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(15, writer.TotalWritten);

        foreach (var name in new[] { "step-1", "step-2", "step-3" })
        {
            var stepExecution = await repo.GetLastStepExecutionAsync(execution.Id, name);
            Assert.NotNull(stepExecution);
            Assert.Equal(BatchStatus.Completed, stepExecution.Status);
        }
    }
}
