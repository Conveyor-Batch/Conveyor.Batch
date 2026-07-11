// Conveyor.Batch — Partitioned Processing Sample
//
// Demonstrates: RangePartitioner + LocalPartitionHandler processing a large numeric dataset in
// parallel.
//
//   1. 10,000 SourceItems are seeded into SQLite with random Values.
//   2. RangePartitioner divides [1, 10_000] into 4 equal partitions.
//   3. PartitionStepBuilder runs the SAME worker step once per partition (up to 4 concurrently).
//      PartitionRangeItemReader reads each partition's assigned [min,max] range off the cloned
//      JobExecution LocalPartitionHandler hands to the worker, and filters an EfCoreItemReader
//      query to it.
//   4. Each worker computes Result = sqrt(Value) and writes ProcessedItem rows via EfCoreItemWriter.
//
// Run with: dotnet run --project samples/PartitionedProcessing

using System.Diagnostics;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Partitioning;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PartitionedProcessing;

Console.WriteLine("=== Conveyor.Batch — Partitioned Processing Sample ===");
Console.WriteLine();

const int itemCount = 10_000;
const int gridSize = 4;

var dbPath = Path.Combine(AppContext.BaseDirectory, "partitioned_processing.db");
if (File.Exists(dbPath))
    File.Delete(dbPath);

var contextOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
var contextFactory = new PooledDbContextFactory<AppDbContext>(contextOptions);

await using (var seedContext = await contextFactory.CreateDbContextAsync())
{
    await seedContext.Database.EnsureCreatedAsync();

    var random = new Random(42);
    for (var id = 1L; id <= itemCount; id++)
        seedContext.SourceItems.Add(new SourceItem { Id = id, Value = random.NextDouble() * 1000 });

    await seedContext.SaveChangesAsync();
}

var reader = new PartitionRangeItemReader<AppDbContext, SourceItem, long>(
    contextFactory,
    (ctx, min, max) => ctx.SourceItems.Where(s => s.Id >= min && s.Id <= max).OrderBy(s => s.Id),
    s => s.Id);
var processor = new SquareRootProcessor();
var writer = new EfCoreItemWriter<AppDbContext, ProcessedItem>(contextFactory);

var jobRepository = new InMemoryJobRepository();

var workerStep = new StepBuilder<SourceItem, ProcessedItem>(jobRepository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(250)
    .Build("range-worker");

var partitionStep = new PartitionStepBuilder(jobRepository)
    .Worker(workerStep)
    .Partitioner(new RangePartitioner(1, itemCount))
    .GridSize(gridSize)
    .MaxDegreeOfParallelism(gridSize)
    .Build("partitioned-step");

var job = new JobBuilder("partitioned-processing", jobRepository).AddStep(partitionStep).Build();

var stopwatch = Stopwatch.StartNew();
var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
stopwatch.Stop();

await using var verifyContext = await contextFactory.CreateDbContextAsync();
var totalProcessed = await verifyContext.ProcessedItems.CountAsync();

Console.WriteLine($"Partitions: {gridSize}");
Console.WriteLine($"Total processed: {totalProcessed} items");
Console.WriteLine($"Elapsed: {stopwatch.ElapsedMilliseconds}ms");
Console.WriteLine($"Status: {execution.Status}");

return execution.Status == BatchStatus.Completed ? 0 : 1;

sealed class SquareRootProcessor : IItemProcessor<SourceItem, ProcessedItem>
{
    public ValueTask<ProcessedItem?> ProcessAsync(SourceItem item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        var result = new ProcessedItem { SourceId = item.Id, Result = Math.Sqrt(item.Value) };
        return ValueTask.FromResult<ProcessedItem?>(result);
    }
}
