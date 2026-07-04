using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Core.Writers;

namespace Conveyor.Batch.UnitTests.Writers;

public sealed class CompositeItemWriterTests
{
    private static StepExecutionContext MakeContext() =>
        new(new StepExecution { StepName = "test", JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } } });

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
        {
            Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderRecordingWriter<T>(string name, List<string> callOrder) : IItemWriter<T>
    {
        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
        {
            callOrder.Add(name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingWriter<T>(Exception ex) : IItemWriter<T>
    {
        public bool WasCalled { get; private set; }

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw ex;
        }
    }

    private sealed class TrackingWriter<T> : IItemWriter<T>
    {
        public bool WasCalled { get; private set; }

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AllWritersReceiveFullChunk()
    {
        var w1 = new CapturingWriter<int>();
        var w2 = new CapturingWriter<int>();
        var w3 = new CapturingWriter<int>();
        var composite = new CompositeItemWriter<int>([w1, w2, w3]);

        await composite.WriteAsync([1, 2, 3], MakeContext(), CancellationToken.None);

        Assert.Equal([1, 2, 3], w1.Chunks.Single());
        Assert.Equal([1, 2, 3], w2.Chunks.Single());
        Assert.Equal([1, 2, 3], w3.Chunks.Single());
    }

    [Fact]
    public async Task WritersCalledInOrder()
    {
        var callOrder = new List<string>();
        var w1 = new OrderRecordingWriter<int>("first", callOrder);
        var w2 = new OrderRecordingWriter<int>("second", callOrder);
        var w3 = new OrderRecordingWriter<int>("third", callOrder);
        var composite = new CompositeItemWriter<int>([w1, w2, w3]);

        await composite.WriteAsync([1, 2, 3], MakeContext(), CancellationToken.None);

        Assert.Equal(["first", "second", "third"], callOrder);
    }

    [Fact]
    public async Task FirstWriterThrows_RemainingWritersSkipped()
    {
        var w1 = new ThrowingWriter<int>(new InvalidOperationException("write failed"));
        var w2 = new TrackingWriter<int>();
        var w3 = new TrackingWriter<int>();
        var composite = new CompositeItemWriter<int>([w1, w2, w3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            composite.WriteAsync([1, 2, 3], MakeContext(), CancellationToken.None).AsTask());

        Assert.False(w2.WasCalled);
        Assert.False(w3.WasCalled);
    }

    [Fact]
    public void EmptyWriters_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CompositeItemWriter<int>([]));
    }

    [Fact]
    public async Task SingleWriter_WorksCorrectly()
    {
        var w1 = new CapturingWriter<int>();
        var composite = new CompositeItemWriter<int>([w1]);

        await composite.WriteAsync([1, 2, 3], MakeContext(), CancellationToken.None);

        Assert.Equal([1, 2, 3], w1.Chunks.Single());
    }
}
