# Core Concepts

Conveyor.Batch organizes work into **jobs** made of **steps**, and each chunk-oriented step drives items through a **reader → processor → writer** loop, committing a chunk at a time. Understanding this vocabulary — Job, Step, Chunk, and Execution — makes every other guide page easier to follow.

## Chunk-oriented processing

The engine reads items one at a time from the reader, passes each through the processor, accumulates the results into a chunk, and writes the whole chunk at once when the chunk size is reached. Remaining items are flushed at the end of the stream.

```
Reader ──► Processor ──► [accumulate] ──► Writer (per chunk)
              │
              └──► null = filter item out
```

## Job & Step model

```
Job
 └── Step 1  (chunk-oriented or tasklet)
 └── Step 2
 └── Step N
```

- `IJob` is the top-level execution unit — `ExecuteAsync(JobParameters, CancellationToken)` returns a `JobExecution`.
- `IStep` is a single phase of a job — `ExecuteAsync(JobExecution, CancellationToken)` returns a `StepExecution`. A step is either chunk-oriented (`IItemReader`/`IItemProcessor`/`IItemWriter`) or a `ITasklet` for simple, non-chunk work.
- Each step records its own `StepExecution` (read/write/skip counts, status, timestamps). Each job records a `JobExecution`. Both are persisted via `IJobRepository`.

## A minimal reader/processor/writer trio

```csharp
using Conveyor.Batch.Abstractions;

sealed class NumberReader : IItemReader<int>
{
    public async IAsyncEnumerable<int> ReadAsync(
        StepExecutionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var n in Enumerable.Range(1, 20))
            yield return n;
    }
}

sealed class DoublingProcessor : IItemProcessor<int, int>
{
    public ValueTask<int?> ProcessAsync(int item, StepExecutionContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult<int?>(item * 2);
}

sealed class SumWriter : IItemWriter<int>
{
    public ValueTask WriteAsync(IReadOnlyList<int> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Chunk sum: {items.Sum()}");
        return ValueTask.CompletedTask;
    }
}
```

Wire this trio together with `StepBuilder<TInput, TOutput>` and `JobBuilder` exactly as in [Getting Started](/guide/getting-started).

::: tip When to use
Read this before any other guide page — it defines the vocabulary (Job, Step, Chunk, Execution) used throughout the rest of the documentation.
:::
