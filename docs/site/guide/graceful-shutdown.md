# Graceful Shutdown

When a process receives a stop signal — a container orchestrator sending SIGTERM, a manual cancellation — you generally don't want to abandon the chunk currently in flight. Graceful shutdown gives the engine a drain window to finish the current chunk, commit it, and persist a checkpoint before exiting.

## Configuring a drain timeout

```csharp
using Conveyor.Batch.Core.Engine;

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(100)
    .GracefulShutdown(new GracefulShutdownOptions { DrainTimeout = TimeSpan.FromSeconds(30) })
    .Build("process-orders");
```

`GracefulShutdownOptions.DrainTimeout` defaults to 30 seconds (`GracefulShutdownOptions.Default`) if you call `.GracefulShutdown()` with no arguments. When a stop signal arrives, the engine stops reading new items but finishes processing the items already read, commits that final chunk, and persists a checkpoint before exiting cleanly with `BatchStatus.Stopped` — as long as the drain completes within `DrainTimeout`.

::: tip When to use
Use in any hosted or long-running deployment (containers, Kubernetes, Worker Services) where the process can be asked to stop mid-chunk and you need the in-flight chunk to finish and checkpoint before exit, rather than being cut off mid-write.
:::
