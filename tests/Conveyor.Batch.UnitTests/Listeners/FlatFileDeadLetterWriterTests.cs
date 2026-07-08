using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.IO;

namespace Conveyor.Batch.UnitTests.Listeners;

public sealed class FlatFileDeadLetterWriterTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"conveyor_batch_dlq_{Guid.NewGuid()}.jsonl");

    private static DeadLetterEntry MakeEntry(int i) => new()
    {
        JobName = "job",
        StepName = "step",
        ItemJson = i.ToString(),
        ItemTypeName = typeof(int).FullName!,
        ExceptionType = typeof(InvalidOperationException).FullName!,
        ExceptionMessage = "bad",
        SkipCountAtTime = i,
        OccurredAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task WriteAsync_AppendsOneJsonLinePerEntry()
    {
        var writer = new FlatFileDeadLetterWriter(_filePath);

        await writer.WriteAsync(MakeEntry(0), CancellationToken.None);
        await writer.WriteAsync(MakeEntry(1), CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(_filePath);
        Assert.Equal(2, lines.Length);
        Assert.Equal("job", JsonSerializer.Deserialize<DeadLetterEntry>(lines[0])!.JobName);
        Assert.Equal("1", JsonSerializer.Deserialize<DeadLetterEntry>(lines[1])!.ItemJson);
    }

    [Fact]
    public async Task ConcurrentWrites_AllLinesWrittenWithoutInterleaving()
    {
        var writer = new FlatFileDeadLetterWriter(_filePath);

        var tasks = Enumerable.Range(0, 50).Select(i => writer.WriteAsync(MakeEntry(i), CancellationToken.None).AsTask());
        await Task.WhenAll(tasks);

        var lines = await File.ReadAllLinesAsync(_filePath);
        Assert.Equal(50, lines.Length);
        Assert.All(lines, line => JsonSerializer.Deserialize<DeadLetterEntry>(line));
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
