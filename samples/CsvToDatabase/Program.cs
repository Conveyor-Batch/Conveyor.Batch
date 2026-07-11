// Conveyor.Batch — CSV to Database Sample
//
// Demonstrates: FlatFileItemReader -> processor -> EfCoreItemWriter, with a skip policy that
// tolerates malformed CSV rows instead of aborting the whole job.
//
//   1. FlatFileItemReader<RawOrder> reads orders.csv. Its lineMapper is deliberately tolerant —
//      it never throws, even on a missing field — because the chunk engine's skip policy only
//      wraps processor.ProcessAsync, not the reader's enumeration.
//   2. OrderProcessor does the real decimal parsing and tax calculation; a malformed or missing
//      Amount field throws FormatException, which ExceptionClassifierSkipPolicy marks as
//      skippable, so the job skips that row and keeps going instead of aborting.
//   3. EfCoreItemWriter persists each committed chunk of ProcessedOrder rows to SQLite.
//
// Run with: dotnet run --project samples/CsvToDatabase

using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.IO.FlatFile;
using Conveyor.Batch.Policies;
using CsvToDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

Console.WriteLine("=== Conveyor.Batch — CSV to Database Sample ===");
Console.WriteLine();

var dbPath = Path.Combine(AppContext.BaseDirectory, "csv_to_database.db");
if (File.Exists(dbPath))
    File.Delete(dbPath);

var contextOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
var contextFactory = new PooledDbContextFactory<AppDbContext>(contextOptions);

await using (var initContext = await contextFactory.CreateDbContextAsync())
    await initContext.Database.EnsureCreatedAsync();

var csvPath = Path.Combine(AppContext.BaseDirectory, "orders.csv");

var reader = new FlatFileItemReader<RawOrder>(csvPath, MapLine, skipHeader: true);
var processor = new OrderProcessor();
var writer = new EfCoreItemWriter<AppDbContext, ProcessedOrder>(contextFactory);
var skipPolicy = new ExceptionClassifierSkipPolicy(new ExceptionClassifier().AddSkippable<FormatException>());

var jobRepository = new InMemoryJobRepository();

var step = new StepBuilder<RawOrder, ProcessedOrder>(jobRepository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(10)
    .SkipPolicy(skipPolicy)
    .Build("import-orders");

var job = new JobBuilder("csv-to-database", jobRepository).AddStep(step).Build();

var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
var stepExecution = await jobRepository.GetLastStepExecutionAsync(execution.Id, "import-orders");

Console.WriteLine($"Processed: {stepExecution!.WriteCount} orders");
Console.WriteLine($"Skipped:   {stepExecution.SkipCount} malformed rows");
Console.WriteLine($"Status:    {execution.Status}");

return execution.Status == BatchStatus.Completed ? 0 : 1;

// Never throws: a missing field is padded with an empty string, deferring real validation to
// OrderProcessor so malformed rows go through the skip policy instead of aborting the read loop.
static RawOrder MapLine(string line)
{
    var parts = line.Split(',');
    string Field(int i) => i < parts.Length ? parts[i] : string.Empty;
    return new RawOrder(Field(0), Field(1), Field(2));
}
