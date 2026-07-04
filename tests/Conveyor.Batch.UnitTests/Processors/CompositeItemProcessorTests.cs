using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Processors;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.UnitTests.Processors;

public sealed class CompositeItemProcessorTests
{
    private static StepExecutionContext MakeContext() =>
        new(new StepExecution { StepName = "test", JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } } });

    private sealed class SuffixProcessor(string suffix) : IItemProcessor<string, string>
    {
        public bool WasCalled { get; private set; }

        public ValueTask<string?> ProcessAsync(string item, StepExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult<string?>(item + suffix);
        }
    }

    private sealed class NullReturningProcessor : IItemProcessor<string, string>
    {
        public bool WasCalled { get; private set; }

        public ValueTask<string?> ProcessAsync(string item, StepExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult<string?>(null);
        }
    }

    private sealed class ParseProcessor : IItemProcessor<string, int?>
    {
        public bool WasCalled { get; private set; }

        public ValueTask<int?> ProcessAsync(string item, StepExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult<int?>(int.Parse(item));
        }
    }

    private sealed class NullParseProcessor : IItemProcessor<string, int?>
    {
        public ValueTask<int?> ProcessAsync(string item, StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<int?>(null);
    }

    private sealed class FormatProcessor : IItemProcessor<int?, string>
    {
        public bool WasCalled { get; private set; }

        public ValueTask<string?> ProcessAsync(int? item, StepExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult<string?>($"Value:{item}");
        }
    }

    [Fact]
    public async Task Chain_AllProcessorsRun_ResultIsLastOutput()
    {
        var p1 = new SuffixProcessor("-p1");
        var p2 = new SuffixProcessor("-p2");
        var p3 = new SuffixProcessor("-p3");
        var composite = new CompositeItemProcessor<string>([p1, p2, p3]);

        var result = await composite.ProcessAsync("a", MakeContext(), CancellationToken.None);

        Assert.Equal("a-p1-p2-p3", result);
    }

    [Fact]
    public async Task Chain_FirstReturnsNull_RemainingSkipped()
    {
        var p1 = new NullReturningProcessor();
        var p2 = new SuffixProcessor("-p2");
        var p3 = new SuffixProcessor("-p3");
        var composite = new CompositeItemProcessor<string>([p1, p2, p3]);

        var result = await composite.ProcessAsync("a", MakeContext(), CancellationToken.None);

        Assert.Null(result);
        Assert.False(p2.WasCalled);
        Assert.False(p3.WasCalled);
    }

    [Fact]
    public async Task Chain_MiddleReturnsNull_RemainingSkipped()
    {
        var p1 = new SuffixProcessor("-p1");
        var p2 = new NullReturningProcessor();
        var p3 = new SuffixProcessor("-p3");
        var composite = new CompositeItemProcessor<string>([p1, p2, p3]);

        var result = await composite.ProcessAsync("a", MakeContext(), CancellationToken.None);

        Assert.Null(result);
        Assert.False(p3.WasCalled);
    }

    [Fact]
    public void EmptyProcessors_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CompositeItemProcessor<int>([]));
    }

    [Fact]
    public async Task SingleProcessor_WorksCorrectly()
    {
        var p1 = new SuffixProcessor("-p1");
        var composite = new CompositeItemProcessor<string>([p1]);

        var direct = await p1.ProcessAsync("a", MakeContext(), CancellationToken.None);
        var viaComposite = await composite.ProcessAsync("a", MakeContext(), CancellationToken.None);

        Assert.Equal(direct, viaComposite);
    }

    [Fact]
    public async Task ProcessorChain_BothProcessorsRun_TypeFlowsCorrectly()
    {
        var parse = new ParseProcessor();
        var format = new FormatProcessor();
        var chain = new ProcessorChain<string, int?, string>(parse, format);

        var result = await chain.ProcessAsync("42", MakeContext(), CancellationToken.None);

        Assert.Equal("Value:42", result);
    }

    [Fact]
    public async Task ProcessorChain_FirstReturnsNull_SecondNotCalled()
    {
        var parse = new NullParseProcessor();
        var format = new FormatProcessor();
        var chain = new ProcessorChain<string, int?, string>(parse, format);

        var result = await chain.ProcessAsync("not-a-number", MakeContext(), CancellationToken.None);

        Assert.Null(result);
        Assert.False(format.WasCalled);
    }
}
