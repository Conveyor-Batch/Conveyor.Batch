using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Processors;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.UnitTests.Processors;

public sealed class ProcessorChainIntegrationTests
{
    private static StepExecutionContext MakeContext() =>
        new(new StepExecution { StepName = "test", JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } } });

    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class ParseProcessor : IItemProcessor<string, int>
    {
        public ValueTask<int> ProcessAsync(string item, StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(int.Parse(item));
    }

    private sealed class FormatProcessor : IItemProcessor<int, string>
    {
        public ValueTask<string?> ProcessAsync(int item, StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>($"Value:{item}");
    }

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
        {
            Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task FullPipeline_ChainedProcessorInsideEngine()
    {
        var items = Enumerable.Range(1, 10).Select(i => i.ToString()).ToList();
        var writer = new CapturingWriter<string>();
        var chain = new ProcessorChain<string, int, string>(new ParseProcessor(), new FormatProcessor());
        var engine = new ChunkOrientedEngine<string, string>(
            new ListReader<string>(items),
            chain,
            writer,
            chunkSize: 4);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        var expected = Enumerable.Range(1, 10).Select(i => $"Value:{i}").ToList();
        Assert.Equal(expected, writer.Chunks.SelectMany(c => c));
    }
}
