# Getting Started

Every Conveyor.Batch pipeline is built from three pieces — a reader, a processor, and a writer — wired together into a step, and a step is wired into a job. This page walks through the smallest possible pipeline: an in-memory reader, a processor that validates and enriches each item, and a writer that commits each chunk. It's the fastest way to see the chunk-oriented engine run end to end.

## Install

```bash
dotnet add package Conveyor.Batch
```

## Write the pipeline

```csharp
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

record Order(int Id, string Product, decimal Amount);
record ProcessedOrder(int Id, string Product, decimal Amount, decimal Tax);

sealed class InMemoryOrderReader : IItemReader<Order>
{
    public async IAsyncEnumerable<Order> ReadAsync(
        StepExecutionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var i in Enumerable.Range(1, 100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new Order(i, $"Product-{i % 10 + 1}", i * 9.99m);
        }
    }
}

sealed class OrderProcessor : IItemProcessor<Order, ProcessedOrder>
{
    public ValueTask<ProcessedOrder?> ProcessAsync(
        Order item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        if (item.Amount <= 0)
            return ValueTask.FromResult<ProcessedOrder?>(null); // null = filter/skip this item

        return ValueTask.FromResult<ProcessedOrder?>(
            new ProcessedOrder(item.Id, item.Product, item.Amount, Math.Round(item.Amount * 0.08m, 2)));
    }
}

sealed class ConsoleOrderWriter : IItemWriter<ProcessedOrder>
{
    public ValueTask WriteAsync(
        IReadOnlyList<ProcessedOrder> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Committed chunk of {items.Count} order(s)");
        return ValueTask.CompletedTask;
    }
}
```

## Wire it up and run it

```csharp
var repository = new InMemoryJobRepository();

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(new InMemoryOrderReader())
    .Processor(new OrderProcessor())
    .Writer(new ConsoleOrderWriter())
    .ChunkSize(10)
    .Build("process-orders");

var job = new JobBuilder("order-processing-job", repository)
    .AddStep(step)
    .Build();

var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

Console.WriteLine($"Status: {execution.Status}"); // Completed
```

`JobBuilder` produces an `IJob`, and calling `ExecuteAsync` directly on it is enough to run the job — no separate launcher is required for simple, imperative scenarios. See [Hosting](/guide/hosting) for wiring a job into a Worker Service via dependency injection instead.

::: tip When to use
Use this as your first Conveyor.Batch pipeline when you need a simple, in-process read → process → write job with no persistence or restart requirements. For jobs that must survive a process restart, see [Restartability](/guide/restartability).
:::
