using Conveyor.Batch.Abstractions;
using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="EfCoreDeadLetterWriter"/>, in particular that concurrent
/// writes against the shared <see cref="ConveyorBatchDbContext"/> are serialized correctly
/// rather than corrupting the context (the scenario a step with
/// <c>DegreeOfParallelism &gt; 1</c> and <c>.DeadLetter(...)</c> configured would hit).
/// </summary>
public sealed class EfCoreDeadLetterWriterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ConveyorBatchDbContext _dbContext;
    private readonly EfCoreDeadLetterWriter _writer;

    public EfCoreDeadLetterWriterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"conveyor_batch_dlq_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<ConveyorBatchDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbContext = new ConveyorBatchDbContext(options);
        _dbContext.Database.Migrate();
        _writer = new EfCoreDeadLetterWriter(_dbContext);
    }

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
    public async Task WriteAsync_PersistsEntryToDatabase()
    {
        await _writer.WriteAsync(MakeEntry(0), CancellationToken.None);

        var persisted = Assert.Single(_dbContext.DeadLetterEntries);
        Assert.Equal("job", persisted.JobName);
        Assert.Equal("step", persisted.StepName);
        Assert.Equal("bad", persisted.ExceptionMessage);
    }

    [Fact]
    public async Task ConcurrentWrites_AllEntriesPersisted()
    {
        var tasks = Enumerable.Range(0, 20).Select(i => _writer.WriteAsync(MakeEntry(i), CancellationToken.None).AsTask());

        await Task.WhenAll(tasks);

        Assert.Equal(20, await _dbContext.DeadLetterEntries.CountAsync());
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
