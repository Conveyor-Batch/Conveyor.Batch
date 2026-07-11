# Chunk Engine

The chunk engine is the heart of a chunk-oriented step. `ChunkOrientedEngine` runs items through the reader/processor/writer loop sequentially; when a step's `DegreeOfParallelism` is set above 1, `StepBuilder` switches to `ConcurrentChunkOrientedEngine`, which pipelines processing across multiple workers using `System.Threading.Channels` internally (see [ADR-004](/adr/004)) while still committing chunks through the writer.

## Configuring chunk size and parallelism

```csharp
using Conveyor.Batch.Core.Step;

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(100)              // commit every 100 processed items
    .DegreeOfParallelism(4)      // >1 switches to the concurrent engine
    .Build("process-orders");
```

`ChunkSize` controls the commit interval — how many processed items accumulate before `IItemWriter.WriteAsync` is called. Smaller chunks mean more frequent, smaller writes and a smaller unit of re-work on restart; larger chunks reduce write overhead but increase memory use and the amount of re-processed work after a failure.

## Chunk listener hooks

`IChunkListener` lets you observe or intervene at each stage of the chunk loop:

```csharp
public interface IChunkListener
{
    ValueTask BeforeChunkAsync(StepExecutionContext context, CancellationToken cancellationToken);
    ValueTask AfterChunkAsync(StepExecutionContext context, CancellationToken cancellationToken);
    ValueTask OnChunkErrorAsync(StepExecutionContext context, Exception exception, CancellationToken cancellationToken);
    ValueTask BeforeWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken);
    ValueTask AfterWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken);
    ValueTask OnSkipAsync<TInput>(TInput item, Exception exception, StepExecutionContext context, CancellationToken cancellationToken);
}
```

Register a listener with `StepBuilder.Listener(IChunkListener listener)`. [Dead-Lettering](/guide/dead-lettering) is built on top of this same hook via `DeadLetterChunkListener`.

::: tip When to use
Tune `ChunkSize` for commit-interval trade-offs; set `DegreeOfParallelism` above 1 only for CPU-bound processors, since it switches to the concurrent engine and adds coordination overhead that isn't worth it for I/O-bound work.
:::
