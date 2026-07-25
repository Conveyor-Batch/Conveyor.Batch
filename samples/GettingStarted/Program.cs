// Conveyor.Batch — Getting Started Sample
//
// This sample demonstrates the core chunk-oriented processing pipeline:
//   1. An in-memory reader yields 100 Order records.
//   2. A processor validates and enriches each order (skips invalid ones).
//   3. A writer prints each committed chunk to the console.
//
// Run with:  dotnet run --project samples/GettingStarted

using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

await GettingStarted.RunAsync();

// ---------------------------------------------------------------------------
// Domain model
// ---------------------------------------------------------------------------

record Order(int Id, string Product, decimal Amount);
record ProcessedOrder(int Id, string Product, decimal Amount, decimal Tax);

// ---------------------------------------------------------------------------
// Reader — yields 100 in-memory orders; order 42 is intentionally invalid
// ---------------------------------------------------------------------------

sealed class InMemoryOrderReader : IItemReader<Order>
{
    private static readonly IReadOnlyList<Order> Source =
        Enumerable.Range(1, 100)
                  .Select(i => new Order(
                      Id: i,
                      Product: $"Product-{i % 10 + 1}",
                      Amount: i == 42 ? -1m : i * 9.99m)) // order 42 is invalid
                  .ToList();

    public async IAsyncEnumerable<Order> ReadAsync(
        StepExecutionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var order in Source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield(); // simulate async I/O
            yield return order;
        }
    }
}

// ---------------------------------------------------------------------------
// Processor — validates orders and computes tax; returns null to filter/skip
// ---------------------------------------------------------------------------

sealed class OrderProcessor : IItemProcessor<Order, ProcessedOrder>
{
    private const decimal TaxRate = 0.08m;

    public ValueTask<ProcessedOrder?> ProcessAsync(
        Order item,
        StepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (item.Amount <= 0)
        {
            Console.WriteLine($"  [SKIP] Order {item.Id} has invalid amount {item.Amount:C} — filtered out");
            return ValueTask.FromResult<ProcessedOrder?>(null); // null = filter this item
        }

        var processed = new ProcessedOrder(
            Id: item.Id,
            Product: item.Product,
            Amount: item.Amount,
            Tax: Math.Round(item.Amount * TaxRate, 2));

        return ValueTask.FromResult<ProcessedOrder?>(processed);
    }
}

// ---------------------------------------------------------------------------
// Writer — receives a committed chunk and prints a summary line
// ---------------------------------------------------------------------------

sealed class ConsoleOrderWriter : IItemWriter<ProcessedOrder>
{
    public ValueTask WriteAsync(
        IReadOnlyList<ProcessedOrder> items,
        StepExecutionContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [WRITE] Chunk of {items.Count} order(s) committed " +
                          $"(total written so far: {context.WriteCount + items.Count})");

        foreach (var order in items)
        {
            Console.WriteLine($"    Order #{order.Id:D3}  {order.Product,-12}  " +
                              $"Amount: {order.Amount,9:C}  Tax: {order.Tax,7:C}");
        }

        return ValueTask.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Entry point — wire everything up and run the job
// ---------------------------------------------------------------------------

static class GettingStarted
{
public static async Task<int> RunAsync()
{
Console.WriteLine("=== Conveyor.Batch — Getting Started Sample ===");
Console.WriteLine();

// StepBuilder/JobBuilder own all job/step execution bookkeeping (creating and persisting
// job/step executions, tracking Status/EndTime/FailureException) — application code just
// wires up the reader/processor/writer and runs the resulting job.
var repository = new InMemoryJobRepository();

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(new InMemoryOrderReader())
    .Processor(new OrderProcessor())
    .Writer(new ConsoleOrderWriter())
    .ChunkSize(10) // commit every 10 items
    .Build("process-orders");

var job = new JobBuilder("order-processing-job", repository).AddStep(step).Build();

Console.WriteLine("Running step 'process-orders' with chunk size 10...");
Console.WriteLine();

var jobExecution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
var stepExecution = await repository.GetLastStepExecutionAsync(jobExecution.Id, "process-orders");

if (jobExecution.Status != BatchStatus.Completed)
{
    Console.WriteLine($"Job FAILED: {jobExecution.FailureException?.Message}");
    return 1;
}

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("=== Job Complete ===");
Console.WriteLine($"  Status  : {jobExecution.Status}");
Console.WriteLine($"  Read    : {stepExecution!.ReadCount}");
Console.WriteLine($"  Written : {stepExecution.WriteCount}");
Console.WriteLine($"  Skipped : {stepExecution.SkipCount}");
Console.WriteLine($"  Duration: {(jobExecution.EndTime!.Value - jobExecution.StartTime).TotalMilliseconds:F0} ms");

return 0;
} // RunAsync
} // GettingStarted
