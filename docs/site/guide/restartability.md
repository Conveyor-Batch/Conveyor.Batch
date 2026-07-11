# Restartability

Jobs that fail mid-run resume from the last committed chunk instead of starting over — no duplicate processing, no gaps. A reader opts into this by implementing `IItemStream` alongside `IItemReader<T>`: it saves its position into the execution context after every committed chunk, and restores it when the step restarts.

## Implementing a restartable reader

```csharp
using Conveyor.Batch.Abstractions;

sealed class RestartableCsvReader(string filePath) : IItemReader<Order>, IItemStream
{
    private int _currentIndex;

    public async ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken)
        => _currentIndex = context.Get<int>("reader.offset");

    public async ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken)
        => context.Put("reader.offset", _currentIndex);

    public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public async IAsyncEnumerable<Order> ReadAsync(
        StepExecutionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        foreach (var line in lines.Skip(_currentIndex))
        {
            _currentIndex++;
            yield return Order.Parse(line);
        }
    }
}
```

- `OpenAsync` is called when the step starts — it reads any previously saved offset out of the `BatchExecutionContext`.
- `UpdateAsync` is called after each committed chunk — it persists the current offset.
- `CloseAsync` is called when the step finishes or fails.

## Persisting the checkpoint across process restarts

For the checkpoint to survive a process crash or restart, use `EfCoreJobRepository` (see [Repository API](/api/repository)) instead of `InMemoryJobRepository`, which only lives for the duration of the process:

```csharp
var step = new StepBuilder<Order, ProcessedOrder>(jobRepository) // jobRepository is an EfCoreJobRepository
    .Reader(new RestartableCsvReader("orders.csv"))
    .Processor(new OrderProcessor())
    .Writer(new DatabaseWriter(db))
    .ChunkSize(100)
    .Build("process-orders");

var job = new JobBuilder("restartable-job", jobRepository).AddStep(step).Build();

// Re-launching with the SAME JobParameters is all it takes: Conveyor.Batch detects the
// prior failed execution, loads the saved checkpoint, and the reader resumes automatically.
var firstExecution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
var secondExecution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
```

When the job is re-launched with the same `JobParameters`, Conveyor.Batch detects the prior failed execution, loads the saved checkpoint, and the reader skips the already-processed records automatically.

::: tip When to use
Use whenever a job's reader can fail or the process can be killed mid-run and you cannot tolerate re-processing already-committed items — long file imports, large database migrations, or any job that touches external systems.
:::
