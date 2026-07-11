# Testing

`Conveyor.Batch.Testing` provides small, dependency-free helpers for unit testing pipelines — in-memory readers/writers and trivial processor/policy implementations.

## InMemoryItemReader\<T\>

```csharp
public sealed class InMemoryItemReader<T> : IItemReader<T>
{
    public InMemoryItemReader(IEnumerable<T> items);
}
```

Yields the given `items` as the reader's `IAsyncEnumerable<T>`.

## InMemoryItemWriter\<T\>

```csharp
public sealed class InMemoryItemWriter<T> : IItemWriter<T>
{
    public InMemoryItemWriter();

    public IReadOnlyList<IReadOnlyList<T>> Chunks { get; }
    public IEnumerable<T> AllItems { get; }
}
```

No constructor parameters. `Chunks` captures every committed chunk in the order it was written, so tests can assert on chunk boundaries; `AllItems` flattens every written item across all chunks.

## FuncProcessor\<TIn, TOut\>

```csharp
public sealed class FuncProcessor<TIn, TOut> : IItemProcessor<TIn, TOut>
{
    public FuncProcessor(Func<TIn, StepExecutionContext, CancellationToken, ValueTask<TOut?>> processAsync);
    public FuncProcessor(Func<TIn, StepExecutionContext, CancellationToken, TOut?> process);
}
```

Wraps an inline delegate as an `IItemProcessor<TIn, TOut>`, so a test doesn't need a dedicated processor class. The async overload takes a `ValueTask<TOut?>`-returning delegate; the sync overload takes a plain `TOut?`-returning delegate.

## IdentityProcessor\<T\>

```csharp
public sealed class IdentityProcessor<T> : IItemProcessor<T, T>
{
    public IdentityProcessor();
}
```

No constructor parameters. Passes every item through unchanged — useful when a test only needs to exercise the reader/writer path.

## AlwaysSkipPolicy

```csharp
public sealed class AlwaysSkipPolicy : ISkipPolicy
{
    public AlwaysSkipPolicy();
}
```

No constructor parameters. `ShouldSkip` always returns `true` — useful for testing that a step's skip-handling and dead-lettering paths behave correctly under worst-case conditions.

## Worked example

```csharp
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Testing;

var reader = new InMemoryItemReader<int>(Enumerable.Range(1, 5));
var processor = new FuncProcessor<int, int>((item, ctx, ct) => item * 2);
var writer = new InMemoryItemWriter<int>();

var repository = new InMemoryJobRepository();

var step = new StepBuilder<int, int>(repository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(2)
    .Build("double-numbers");

var job = new JobBuilder("double-numbers-job", repository).AddStep(step).Build();
await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

// Assert.Equal(new[] { 1, 2, 3, 4, 5 }.Select(i => i * 2), writer.AllItems);
// Assert.Equal(3, writer.Chunks.Count); // chunks of 2, 2, 1
```
