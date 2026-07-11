# Dead-Lettering

Skipping a bad record (see [Skip & Retry](/guide/skip-retry)) keeps a job running, but a silently discarded record is hard to investigate later. Dead-lettering routes skipped items to an inspectable sink — a file, a database table, or an in-memory list in tests — instead of dropping them.

## Wiring a dead-letter writer

`StepBuilder.DeadLetter(IDeadLetterWriter writer)` wires the writer in behind the scenes by wrapping it in a `DeadLetterChunkListener` (composing with any other listener you've registered):

```csharp
using Conveyor.Batch.Core.Listeners;

var deadLetterWriter = new InMemoryDeadLetterWriter(); // in-memory, no extra package needed

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(100)
    .SkipPolicy(skipPolicy)
    .DeadLetter(deadLetterWriter)
    .Build("process-orders");
```

For durable storage, use `FlatFileDeadLetterWriter` (appends newline-delimited JSON, from `Conveyor.Batch.IO`) or `EfCoreDeadLetterWriter` (persists to the `batch_` dead-letter table via `ConveyorBatchDbContext`, from `Conveyor.Batch.EntityFrameworkCore`) instead — see [IO API](/api/io) and [Repository API](/api/repository) for their constructors.

## What gets recorded

Each skipped item is captured as a `DeadLetterEntry`, with fields including the job and step name, the serialized item, the item's type name, the exception type/message/stack trace, the skip count at the time, and when it occurred — enough context to inspect or manually reprocess the item later.

::: tip When to use
Use whenever skipped items must remain inspectable or reprocessable rather than silently discarded — this is close to required for any pipeline handling financial or compliance-sensitive data.
:::
