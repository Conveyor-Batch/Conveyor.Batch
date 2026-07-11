# Heartbeat

Long-running jobs need a way to signal they're still alive. When heartbeat is enabled, the launcher updates `JobExecution.LastHeartbeatAt` in the repository on a configurable interval — an external monitor or alerting system can watch that timestamp and flag a job as stuck if it goes stale.

## Enabling heartbeat via dependency injection

The `IJobLauncher` that ships with Conveyor.Batch is registered through `AddConveyorBatch`, which is where the heartbeat interval is configured:

```csharp
using Conveyor.Batch.Hosting;

builder.Services.AddConveyorBatch(options =>
{
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
});
```

With heartbeat configured, the registered `IJobLauncher` updates `JobExecution.LastHeartbeatAt` in the repository every `HeartbeatInterval`, on a background loop independent of the caller's cancellation token. Heartbeat failures are swallowed and logged — they never abort the job.

::: tip When to use
Monitor `JobExecution.LastHeartbeatAt` from an external process or alerting system for any job expected to run longer than a few minutes, so a hung job can be detected even though it hasn't technically failed.
:::
