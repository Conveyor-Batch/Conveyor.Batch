using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Conveyor.Batch.IntegrationTests.EfCore;

public sealed class EfCoreItemReaderTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly IDbContextFactory<TestDbContext> _contextFactory;

    public EfCoreItemReaderTests()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
        _contextFactory = new PooledDbContextFactory<TestDbContext>(options);

        using var init = _contextFactory.CreateDbContext();
        init.Database.EnsureCreated();
    }

    private async Task SeedAsync(int fromInclusive, int toInclusive)
    {
        await using var context = _contextFactory.CreateDbContext();
        for (var i = fromInclusive; i <= toInclusive; i++)
            context.TestItems.Add(new TestItem { Id = i, Value = $"item-{i}" });
        await context.SaveChangesAsync();
    }

    private EfCoreItemReader<TestDbContext, TestItem, long> CreateReader(IDbContextFactory<TestDbContext>? factory = null) =>
        new(factory ?? _contextFactory, ctx => ctx.TestItems.OrderBy(x => x.Id), x => x.Id);

    [Fact]
    public async Task ReadsAllItems_InKeyOrder()
    {
        await SeedAsync(1, 20);

        var reader = CreateReader();
        var stepExecution = new StepExecution { StepName = "test" };
        var context = new StepExecutionContext(stepExecution);
        await reader.OpenAsync(stepExecution.ExecutionContext, CancellationToken.None);

        var results = new List<TestItem>();
        await foreach (var item in reader.ReadAsync(context, CancellationToken.None))
            results.Add(item);

        Assert.Equal(20, results.Count);
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), results.Select(x => x.Id));
    }

    [Fact]
    public async Task EmptyTable_NoItemsReturned()
    {
        var reader = CreateReader();
        var stepExecution = new StepExecution { StepName = "test" };
        var context = new StepExecutionContext(stepExecution);
        await reader.OpenAsync(stepExecution.ExecutionContext, CancellationToken.None);

        var results = new List<TestItem>();
        await foreach (var item in reader.ReadAsync(context, CancellationToken.None))
            results.Add(item);

        Assert.Empty(results);
    }

    [Fact]
    public async Task LargeDataset_StreamsWithoutMaterializingAll()
    {
        await SeedAsync(1, 1000);

        var reader = CreateReader();
        var processor = new TestItemToProcessedItemProcessor();
        var writer = new RecordingItemWriter<ProcessedItem>();
        var engine = new ChunkOrientedEngine<TestItem, ProcessedItem>(reader, processor, writer, chunkSize: 100);

        var stepExecution = new StepExecution { StepName = "test" };
        var context = new StepExecutionContext(stepExecution);
        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(10, writer.Chunks.Count);
        Assert.All(writer.Chunks, chunk => Assert.Equal(100, chunk.Count));
        Assert.Equal(1000, writer.Chunks.Sum(c => c.Count));
    }

    [Fact]
    public async Task Restart_ResumesFromSavedKey()
    {
        await SeedAsync(1, 20);

        var readerA = CreateReader();
        var stepExecutionA = new StepExecution { StepName = "test" };
        var contextA = new StepExecutionContext(stepExecutionA);
        await readerA.OpenAsync(stepExecutionA.ExecutionContext, CancellationToken.None);

        var firstChunk = new List<TestItem>();
        await foreach (var item in readerA.ReadAsync(contextA, CancellationToken.None))
        {
            firstChunk.Add(item);
            if (firstChunk.Count == 10)
                break;
        }

        await readerA.UpdateAsync(stepExecutionA.ExecutionContext, CancellationToken.None);

        Assert.Equal(10, firstChunk.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), firstChunk.Select(x => x.Id));

        var savedContext = BatchExecutionContext.FromDictionary(
            new Dictionary<string, string>(stepExecutionA.ExecutionContext.ToDictionary()));

        var readerB = CreateReader();
        var stepExecutionB = new StepExecution { StepName = "test" };
        await readerB.OpenAsync(savedContext, CancellationToken.None);
        var contextB = new StepExecutionContext(stepExecutionB);

        var secondRun = new List<TestItem>();
        await foreach (var item in readerB.ReadAsync(contextB, CancellationToken.None))
            secondRun.Add(item);

        Assert.Equal(10, secondRun.Count);
        Assert.Equal(Enumerable.Range(11, 10).Select(i => (long)i), secondRun.Select(x => x.Id));
    }

    [Fact]
    public async Task AsNoTracking_ContextDisposedAfterEachPage()
    {
        await SeedAsync(1, 1000);

        var countingFactory = new CountingDbContextFactory(_contextFactory);
        var reader = CreateReader(countingFactory);
        var stepExecution = new StepExecution { StepName = "test" };
        var context = new StepExecutionContext(stepExecution);
        await reader.OpenAsync(stepExecution.ExecutionContext, CancellationToken.None);

        var count = 0;
        await foreach (var item in reader.ReadAsync(context, CancellationToken.None))
            count++;

        Assert.Equal(1000, count);
        Assert.True(countingFactory.CreatedCount > 1, "Expected reading 1000 rows to span more than one page/context.");

        await using var verifyContext = _contextFactory.CreateDbContext();
        Assert.Equal(1000, await verifyContext.TestItems.CountAsync());
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }
}
