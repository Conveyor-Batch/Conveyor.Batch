# Conditional Flow

Most jobs run their steps in a fixed sequence, which is what `JobBuilder` gives you. When a job needs to branch — running a different step depending on whether the previous one completed, failed, or was stopped — use `FluentJobBuilder` to describe the transition graph explicitly.

## Building a conditional flow

```csharp
using Conveyor.Batch.Core.Job.Flow;

var job = new FluentJobBuilder("etl-pipeline", repository)
    .Start(validateStep)
        .On("COMPLETED").To(importStep)
        .On("FAILED").To(notifyStep).End()
    .From(importStep)
        .On("COMPLETED").End()
        .On("FAILED").To(rollbackStep).Fail()
    .Build();

var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
```

- `Start(step)` designates the entry-point step.
- `On(status)` matches a step's exit status (`"COMPLETED"`, `"FAILED"`, `"STOPPED"`, or the wildcard `"*"`).
- Each transition ends with `To(nextStep)` (continue to another step), `End()` (finish the job successfully), `Fail()` (finish the job as failed), or `Stop()` (finish the job as stopped).
- `From(step)` starts a new set of transitions from a step already referenced elsewhere in the graph.

`Build()` validates the transition graph and produces an `IJob` you run the same way as any other job — `job.ExecuteAsync(parameters, cancellationToken)`.

::: tip When to use
Use `FluentJobBuilder` instead of `JobBuilder` when steps must branch based on completion status rather than always running in a fixed sequence — for example, routing to a notification or rollback step only when validation or import fails.
:::
