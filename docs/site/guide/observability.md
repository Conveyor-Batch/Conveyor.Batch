# Observability

Conveyor.Batch emits OpenTelemetry traces and metrics natively via `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics` — no Conveyor.Batch-specific telemetry package is needed, just standard OpenTelemetry wiring in your host.

## Wiring OpenTelemetry

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Conveyor.Batch"))
    .WithMetrics(metrics => metrics.AddMeter("Conveyor.Batch"));
```

Both the `ActivitySource` and the `Meter` are named `"Conveyor.Batch"`.

## Activities

| Activity name | Emitted by | Key tags |
|---|---|---|
| `conveyor.batch.job.execute` | Job launcher | `batch.job.name`, `batch.job.execution_id`, `batch.job.status` |
| `conveyor.batch.step.execute` | Step execution | `batch.step.name`, `batch.step.execution_id`, `batch.step.status` |
| `conveyor.batch.chunk.commit` | Chunk engine, per committed chunk | `batch.chunk.size` |

## Metrics

| Instrument | Type | Description |
|---|---|---|
| `conveyor.batch.jobs.completed` | Counter\<long\> | Jobs that completed successfully |
| `conveyor.batch.jobs.failed` | Counter\<long\> | Jobs that failed |
| `conveyor.batch.job.duration` | Histogram\<double\> | Job duration, milliseconds |
| `conveyor.batch.items.read` | Counter\<long\> | Items read across all steps |
| `conveyor.batch.items.written` | Counter\<long\> | Items written across all steps |
| `conveyor.batch.items.skipped` | Counter\<long\> | Items skipped via a skip policy |
| `conveyor.batch.chunk.size` | Histogram\<int\> | Size of each committed chunk |
| `conveyor.batch.chunks.committed` | Counter\<long\> | Chunks committed across all steps |

::: tip When to use
Wire this in any production deployment to get job- and step-level spans plus chunk-level metrics without adding any extra Conveyor.Batch package — it composes with whatever OpenTelemetry exporter (OTLP, Prometheus, Application Insights, etc.) your host already uses.
:::
