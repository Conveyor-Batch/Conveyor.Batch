# Hosting

`Conveyor.Batch.Hosting` integrates a job with the generic host as an `IHostedService`, so it runs automatically at application startup instead of being launched imperatively from `Main`. This is the natural fit for Worker Service deployments and containers.

## Registering a job

```csharp
using Conveyor.Batch.Hosting;

// Program.cs
builder.Services
    .AddConveyorBatch()                  // registers IJobRepository + IJobLauncher
    .AddBatchJob<OrderImportJob>();      // registers the job + a hosted service that runs it

// OrderImportJob.cs
sealed class OrderImportJob(IJobRepository repository, AppDbContext db) : IJob
{
    public string Name => "order-import";

    public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
    {
        var step = new StepBuilder<Order, ProcessedOrder>(repository)
            .Reader(new CsvOrderReader(parameters.Get("file")!))
            .Processor(new OrderProcessor())
            .Writer(new DatabaseOrderWriter(db)) // your own IItemWriter<ProcessedOrder>
            .ChunkSize(500)
            .Build("process-orders");

        return await new JobBuilder(Name, repository)
            .AddStep(step)
            .Build()
            .ExecuteAsync(parameters, cancellationToken);
    }
}
```

- `AddConveyorBatch()` registers `InMemoryJobRepository` as `IJobRepository` and the built-in `IJobLauncher` — use the `AddConveyorBatch<TRepository>()` overload to register a custom repository (for example `EfCoreJobRepository`) instead.
- `AddBatchJob<TJob>()` registers `TJob` as `IJob` and adds `BatchJobHostedService`, which runs the job when the host starts and cancels/drains it when the host stops.
- `BatchJobHostedService.ShutdownTimeout` (default 30 seconds) bounds how long the hosted service waits for the job to drain on shutdown — configure it with the `AddBatchJob<TJob>(Action<BatchJobHostedService> configure)` overload.

::: tip When to use
Use for containerized or Worker Service deployments where the batch job should run as part of the app's hosted lifecycle rather than being launched imperatively — the host handles startup and graceful shutdown for you (see also [Graceful Shutdown](/guide/graceful-shutdown)).
:::
