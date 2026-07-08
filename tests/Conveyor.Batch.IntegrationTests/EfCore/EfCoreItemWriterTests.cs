using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Conveyor.Batch.IntegrationTests.EfCore;

public sealed class EfCoreItemWriterTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly IDbContextFactory<TestDbContext> _contextFactory;

    public EfCoreItemWriterTests()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
        _contextFactory = new PooledDbContextFactory<TestDbContext>(options);

        using var init = _contextFactory.CreateDbContext();
        init.Database.EnsureCreated();
    }

    private static StepExecutionContext NewContext() => new(new StepExecution { StepName = "test" });

    private static List<TestItem> MakeItems(int fromInclusive, int toInclusive) =>
        Enumerable.Range(fromInclusive, toInclusive - fromInclusive + 1)
            .Select(i => new TestItem { Id = i, Value = $"item-{i}" })
            .ToList();

    [Fact]
    public async Task WritesChunk_PersistedToDatabase()
    {
        var writer = new EfCoreItemWriter<TestDbContext, TestItem>(_contextFactory);

        await writer.WriteAsync(MakeItems(1, 10), NewContext(), CancellationToken.None);

        await using var verifyContext = _contextFactory.CreateDbContext();
        Assert.Equal(10, await verifyContext.TestItems.CountAsync());
    }

    [Fact]
    public async Task MultipleChunks_AllPersistedSeparately()
    {
        var reader = new InMemoryItemReader<TestItem>(MakeItems(1, 15));
        var processor = new IdentityProcessor<TestItem>();
        var writer = new EfCoreItemWriter<TestDbContext, TestItem>(_contextFactory);
        var engine = new ChunkOrientedEngine<TestItem, TestItem>(reader, processor, writer, chunkSize: 5);

        await engine.ExecuteAsync(NewContext(), CancellationToken.None);

        await using var verifyContext = _contextFactory.CreateDbContext();
        Assert.Equal(15, await verifyContext.TestItems.CountAsync());
    }

    [Fact]
    public async Task ClearChangeTracker_True_NoAccumulatedEntities()
    {
        var writer = new EfCoreItemWriter<TestDbContext, TestItem>(_contextFactory, clearChangeTrackerAfterChunk: true);

        for (var chunk = 0; chunk < 5; chunk++)
        {
            var items = MakeItems(chunk * 5 + 1, chunk * 5 + 5);
            await writer.WriteAsync(items, NewContext(), CancellationToken.None);
        }

        await using var verifyContext = _contextFactory.CreateDbContext();
        Assert.Equal(25, await verifyContext.TestItems.CountAsync());
    }

    [Fact]
    public async Task WriterThrows_ChunkNotPartiallyPersisted()
    {
        await using (var seedContext = _contextFactory.CreateDbContext())
        {
            seedContext.TestItems.Add(new TestItem { Id = 5, Value = "pre-existing" });
            await seedContext.SaveChangesAsync();
        }

        var writer = new EfCoreItemWriter<TestDbContext, TestItem>(_contextFactory);
        var chunk = MakeItems(1, 10); // Id 5 collides with the pre-existing row.

        await Assert.ThrowsAsync<DbUpdateException>(() => writer.WriteAsync(chunk, NewContext(), CancellationToken.None).AsTask());

        await using var verifyContext = _contextFactory.CreateDbContext();
        Assert.Equal(1, await verifyContext.TestItems.CountAsync());

        var survivingRow = await verifyContext.TestItems.SingleAsync();
        Assert.Equal(5, survivingRow.Id);
        Assert.Equal("pre-existing", survivingRow.Value);
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }
}
