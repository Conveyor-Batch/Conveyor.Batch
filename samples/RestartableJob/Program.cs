// Conveyor.Batch — Restartable Job Sample
//
// Demonstrates: restartability — a job that fails mid-run and resumes from its last checkpoint,
// with no duplicate processing and no gaps.
//
//   1. FlatFileItemReader<InputRow> implements IItemStream, so its read position checkpoints
//      into the step's execution context after every committed chunk.
//   2. FlakyProcessor deliberately throws on row 51, but only on the very first attempt (guarded
//      by StepExecution.IsRestart) — simulating a crash partway through the job.
//   3. EfCoreJobRepository persists job/step state, including the reader's checkpoint, to a real
//      SQLite database, so it survives across the two ExecuteAsync calls below.
//   4. Calling job.ExecuteAsync a second time with the SAME JobParameters is all it takes:
//      SequentialJob/ChunkOrientedStep detect the prior failed execution and resume the reader
//      from its checkpoint automatically — no manual checkpoint plumbing required.
//
// Run with: dotnet run --project samples/RestartableJob

using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.IO.FlatFile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RestartableJob;

Console.WriteLine("=== Conveyor.Batch — Restartable Job Sample ===");
Console.WriteLine();

// Job/step tracking state (including the reader's restart checkpoint) — a real SQLite database,
// so it genuinely persists across the two runs below.
var jobDbPath = Path.Combine(AppContext.BaseDirectory, "restartable_job.db");
if (File.Exists(jobDbPath))
    File.Delete(jobDbPath);

var jobDbOptions = new DbContextOptionsBuilder<ConveyorBatchDbContext>()
    .UseSqlite($"Data Source={jobDbPath}")
    .Options;
await using var jobDbContext = new ConveyorBatchDbContext(jobDbOptions);
await jobDbContext.Database.MigrateAsync();

var jobRepository = new EfCoreJobRepository(jobDbContext);

// The sample's own output table.
var outputDbPath = Path.Combine(AppContext.BaseDirectory, "restartable_job_output.db");
if (File.Exists(outputDbPath))
    File.Delete(outputDbPath);

var outputDbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={outputDbPath}")
    .Options;
var outputContextFactory = new PooledDbContextFactory<AppDbContext>(outputDbOptions);

await using (var initContext = await outputContextFactory.CreateDbContextAsync())
    await initContext.Database.EnsureCreatedAsync();

var inputPath = Path.Combine(AppContext.BaseDirectory, "input.csv");

var reader = new FlatFileItemReader<InputRow>(inputPath, MapLine, skipHeader: true);
var processor = new FlakyProcessor();
var writer = new EfCoreItemWriter<AppDbContext, OutputRecord>(outputContextFactory);

var step = new StepBuilder<InputRow, OutputRecord>(jobRepository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(10)
    .Build("process-rows");

var job = new JobBuilder("restartable-job", jobRepository).AddStep(step).Build();

// The SAME JobParameters value is used for both runs below — that equality is exactly what lets
// the framework detect run 2 as a restart of run 1's failed execution.
var parameters = JobParameters.Empty;

var firstExecution = await job.ExecuteAsync(parameters, CancellationToken.None);
var firstStepExecution = await jobRepository.GetLastStepExecutionAsync(firstExecution.Id, "process-rows");

Console.WriteLine("Run 1 failed at row 51. Checkpoint saved.");
Console.WriteLine($"Rows written before failure: {firstStepExecution!.WriteCount}");
Console.WriteLine();

var secondExecution = await job.ExecuteAsync(parameters, CancellationToken.None);
var secondStepExecution = await jobRepository.GetLastStepExecutionAsync(secondExecution.Id, "process-rows");

Console.WriteLine("Run 2 resuming from checkpoint...");
Console.WriteLine($"Rows written in run 2: {secondStepExecution!.WriteCount}");
Console.WriteLine($"Total rows written across both runs: {firstStepExecution.WriteCount + secondStepExecution.WriteCount}");
Console.WriteLine($"Status: {secondExecution.Status}");

return secondExecution.Status == BatchStatus.Completed ? 0 : 1;

static InputRow MapLine(string line)
{
    var parts = line.Split(',');
    string Field(int i) => i < parts.Length ? parts[i] : string.Empty;
    return new InputRow(Field(0), Field(1));
}
