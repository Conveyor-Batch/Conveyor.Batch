# Core Abstractions

The core abstractions live in `Conveyor.Batch.Abstractions` (except `JobParameters`, noted below) and form the public contract every other Conveyor.Batch package builds on.

## IJob

The top-level execution unit.

```csharp
public interface IJob
{
    string Name { get; }

    Task<JobExecution> ExecuteAsync(
        JobParameters parameters,
        CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `Name` | The job's name, used to correlate executions of the same job across runs. |
| `ExecuteAsync(parameters, cancellationToken)` | Runs the job with the given `parameters` and returns the resulting `JobExecution` once all steps have completed, failed, or stopped. |

## IStep

A single phase of a job — either chunk-oriented or a `ITasklet`.

```csharp
public interface IStep
{
    string Name { get; }

    Task<StepExecution> ExecuteAsync(
        JobExecution jobExecution,
        CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `Name` | The step's name, scoped to the job it belongs to. |
| `ExecuteAsync(jobExecution, cancellationToken)` | Runs the step against the given `jobExecution` and returns the resulting `StepExecution`. |

## ITasklet

A simple, non-chunk unit of work — for steps that don't fit the reader/processor/writer shape.

```csharp
public interface ITasklet
{
    ValueTask<RepeatStatus> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `ExecuteAsync(context, cancellationToken)` | Runs one unit of work and returns a `RepeatStatus` indicating whether the tasklet should run again or is finished. |

## IItemReader\<out TInput\>

Produces the input stream for a chunk-oriented step.

```csharp
public interface IItemReader<out TInput>
{
    IAsyncEnumerable<TInput> ReadAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `ReadAsync(context, cancellationToken)` | Returns an `IAsyncEnumerable<TInput>` the chunk engine consumes one item at a time — see [ADR-001](/adr/001) for why `IAsyncEnumerable<T>` was chosen as the contract. |

A reader that supports [restart checkpointing](/guide/restartability) additionally implements `IItemStream` (below).

## IItemProcessor\<in TInput, TOutput\>

Transforms one input item into an output item, or filters it out.

```csharp
public interface IItemProcessor<in TInput, TOutput>
{
    ValueTask<TOutput?> ProcessAsync(
        TInput item,
        StepExecutionContext context,
        CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `ProcessAsync(item, context, cancellationToken)` | Transforms `item` into a `TOutput`. Returning `null` filters the item out of the chunk entirely — it is not written and does not count as a skip. |

## IItemWriter\<in TOutput\>

Receives a committed chunk of processed items.

```csharp
public interface IItemWriter<in TOutput>
{
    ValueTask WriteAsync(
        IReadOnlyList<TOutput> items,
        StepExecutionContext context,
        CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `WriteAsync(items, context, cancellationToken)` | Persists or emits an entire chunk of `items` at once, once the chunk engine has accumulated `ChunkSize` processed items (or reached the end of the stream). |

## IItemStream

Optional restart/checkpoint contract, implemented alongside `IItemReader<T>` by readers that support resuming from a saved position. See [Restartability](/guide/restartability).

```csharp
public interface IItemStream
{
    ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken);
    ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken);
    ValueTask CloseAsync(CancellationToken cancellationToken);
}
```

| Member | Description |
|---|---|
| `OpenAsync(context, cancellationToken)` | Called when the step starts; restores any previously saved position from `context`. |
| `UpdateAsync(context, cancellationToken)` | Called after each committed chunk; saves the current position into `context`. |
| `CloseAsync(cancellationToken)` | Called when the step finishes or fails, for any cleanup. |

## IJobRepository

Persists job and step execution state so jobs can be tracked and restarted. See [Repository API](/api/repository) for the concrete implementations.

```csharp
public interface IJobRepository
{
    Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters);
    Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters);
    Task UpdateJobExecutionAsync(JobExecution execution);
    Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName);
    Task UpdateStepExecutionAsync(StepExecution stepExecution);
    Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters);
    Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance);
    Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName);
    Task<JobExecution?> GetRunningJobExecutionAsync(
        string jobName, JobParameters parameters, CancellationToken cancellationToken = default);
}
```

| Member | Description |
|---|---|
| `CreateJobInstanceAsync(jobName, parameters)` | Creates (or returns the existing) `JobInstance` identified by `jobName` + `parameters`. |
| `CreateJobExecutionAsync(instance, parameters)` | Starts a new `JobExecution` for the given `JobInstance`. |
| `UpdateJobExecutionAsync(execution)` | Persists changes to an existing `JobExecution` (status, timestamps, heartbeat, etc.). |
| `CreateStepExecutionAsync(jobExecution, stepName)` | Starts a new `StepExecution` for `stepName` within `jobExecution`. |
| `UpdateStepExecutionAsync(stepExecution)` | Persists changes to an existing `StepExecution` (counts, status, checkpoint context). |
| `GetLastJobExecutionAsync(jobName, parameters)` | Returns the most recent `JobExecution` for a job instance, or `null` if none exists — used to detect restarts. |
| `GetJobExecutionsAsync(instance)` | Returns every `JobExecution` recorded for a `JobInstance`. |
| `GetLastStepExecutionAsync(jobExecutionId, stepName)` | Returns the most recent `StepExecution` for `stepName` within a given job execution, or `null`. |
| `GetRunningJobExecutionAsync(jobName, parameters, cancellationToken)` | Returns a currently-running `JobExecution` for the same job instance, if one exists — used to prevent concurrent runs of the same job. |

## IJobLauncher

The entry point for triggering jobs, typically obtained via dependency injection (see [Hosting](/guide/hosting)).

```csharp
public interface IJobLauncher
{
    Task<JobExecution> RunAsync(
        IJob job,
        JobParameters parameters,
        CancellationToken cancellationToken = default);
}
```

| Member | Description |
|---|---|
| `RunAsync(job, parameters, cancellationToken)` | Runs `job` with `parameters`, applying launcher-level concerns such as heartbeat and single-run locking (see [Heartbeat](/guide/heartbeat)). |

## JobParameters

There is no separate `IJobParameters` interface in code — `JobParameters` is a `readonly record struct` (namespace `Conveyor.Batch.Core.Job`) that fills this role directly:

```csharp
public readonly record struct JobParameters(IReadOnlyDictionary<string, string> Values)
{
    public static readonly JobParameters Empty;

    public string? Get(string key);
}
```

| Member | Description |
|---|---|
| `Values` | The underlying key/value parameters. |
| `Empty` | A `JobParameters` with no values — use this when a job takes no parameters. |
| `Get(key)` | Returns the value for `key`, or `null` if it isn't present. |

Two `JobParameters` with the same key/value pairs are considered equal regardless of order — this equality is what lets `IJobRepository` detect that a new `ExecuteAsync` call is a restart of a prior failed execution rather than a brand-new run.
