using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.Listeners;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Conveyor.Batch.IntegrationTests.EfCore;

public sealed class EfCoreReaderWriterPipelineTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly IDbContextFactory<TestDbContext> _contextFactory;

    public EfCoreReaderWriterPipelineTests()
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

    private EfCoreItemReader<TestDbContext, TestItem, long> CreateReader() =>
        new(_contextFactory, ctx => ctx.TestItems.OrderBy(x => x.Id), x => x.Id);

    [Fact]
    public async Task FullPipeline_ReadFromDb_WriteToDb()
    {
        await SeedAsync(1, 50);

        var repository = new InMemoryJobRepository();
        var step = new StepBuilder<TestItem, ProcessedItem>(repository)
            .Reader(CreateReader())
            .Processor(new TestItemToProcessedItemProcessor())
            .Writer(new EfCoreItemWriter<TestDbContext, ProcessedItem>(_contextFactory))
            .ChunkSize(10)
            .Build("step");

        var job = new JobBuilder("job", repository).AddStep(step).Build();
        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);

        await using var verifyContext = _contextFactory.CreateDbContext();
        Assert.Equal(50, await verifyContext.ProcessedItems.CountAsync());
    }

    [Fact]
    public async Task RestartablePipeline_ResumesCorrectly()
    {
        await SeedAsync(1, 50);

        var repository = new InMemoryJobRepository();

        // Run 1: cancel after the 2nd committed chunk (20 items) to simulate a mid-run failure.
        using var cts = new CancellationTokenSource();
        var cancelListener = new CancelAfterWritesListener(cts, cancelAfterWrites: 2);

        var step1 = new StepBuilder<TestItem, ProcessedItem>(repository)
            .Reader(CreateReader())
            .Processor(new TestItemToProcessedItemProcessor())
            .Writer(new EfCoreItemWriter<TestDbContext, ProcessedItem>(_contextFactory))
            .ChunkSize(10)
            .Listener(cancelListener)
            .Build("step");

        var job1 = new JobBuilder("job", repository).AddStep(step1).Build();
        var execution1 = await job1.ExecuteAsync(JobParameters.Empty, cts.Token);

        Assert.Equal(BatchStatus.Failed, execution1.Status);

        await using (var verifyAfterRun1 = _contextFactory.CreateDbContext())
            Assert.Equal(20, await verifyAfterRun1.ProcessedItems.CountAsync());

        // Run 2: fresh reader/writer/step instances, same job name and parameters, so the job
        // builder detects the prior failed execution and resumes from its saved checkpoint.
        var step2 = new StepBuilder<TestItem, ProcessedItem>(repository)
            .Reader(CreateReader())
            .Processor(new TestItemToProcessedItemProcessor())
            .Writer(new EfCoreItemWriter<TestDbContext, ProcessedItem>(_contextFactory))
            .ChunkSize(10)
            .Build("step");

        var job2 = new JobBuilder("job", repository).AddStep(step2).Build();
        var execution2 = await job2.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution2.Status);

        await using var verifyContext = _contextFactory.CreateDbContext();
        var processedIds = await verifyContext.ProcessedItems.Select(p => p.Id).OrderBy(id => id).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), processedIds);
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }

    /// <summary>Test double that cancels a linked token after a fixed number of committed chunks.</summary>
    private sealed class CancelAfterWritesListener : IChunkListener
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _cancelAfterWrites;
        private int _writeCount;

        public CancelAfterWritesListener(CancellationTokenSource cts, int cancelAfterWrites)
        {
            _cts = cts;
            _cancelAfterWrites = cancelAfterWrites;
        }

        public ValueTask BeforeChunkAsync(StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask AfterChunkAsync(StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask OnChunkErrorAsync(StepExecutionContext context, Exception exception, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask BeforeWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask AfterWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _writeCount) >= _cancelAfterWrites)
                _cts.Cancel();

            return ValueTask.CompletedTask;
        }

        public ValueTask OnSkipAsync<TInput>(TInput item, Exception exception, StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
