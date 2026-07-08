using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Listeners;

namespace Conveyor.Batch.UnitTests.Listeners;

public sealed class InMemoryDeadLetterWriterTests
{
    [Fact]
    public async Task ConcurrentWrites_AllEntriesCaptured()
    {
        var writer = new InMemoryDeadLetterWriter();

        var tasks = Enumerable.Range(0, 50).Select(i => writer.WriteAsync(
            new DeadLetterEntry
            {
                JobName = "job",
                StepName = "step",
                ItemJson = i.ToString(),
                ItemTypeName = typeof(int).FullName!,
                ExceptionType = typeof(InvalidOperationException).FullName!,
                ExceptionMessage = "bad",
                SkipCountAtTime = i,
                OccurredAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None).AsTask());

        await Task.WhenAll(tasks);

        Assert.Equal(50, writer.Entries.Count);
    }
}
