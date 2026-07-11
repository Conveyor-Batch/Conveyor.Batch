# Skip & Retry

Not every failure should abort a job. **Skip policies** let a step tolerate a bounded number of bad records without stopping. **Retry policies** let a step retry a transient failure — a network blip, a lock timeout — before giving up. The two are independent and can be combined on the same step.

## Skip: tolerating bad records

`ExceptionClassifier` marks exception types as skippable, and `ExceptionClassifierSkipPolicy` wraps a classifier as an `ISkipPolicy`:

```csharp
using Conveyor.Batch.Policies;

var classifier = new ExceptionClassifier()
    .AddSkippable<FormatException>()
    .AddSkippable<ValidationException>();

var skipPolicy = new ExceptionClassifierSkipPolicy(classifier);

var step = new StepBuilder<RawOrder, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(processor)   // throws FormatException on a malformed row
    .Writer(writer)
    .ChunkSize(10)
    .SkipPolicy(skipPolicy)
    .Build("import-orders");
```

When the processor throws an exception the classifier marks as skippable, the engine increments the step's skip count, notifies any registered `IChunkListener.OnSkipAsync`, and continues with the next item instead of aborting the job. This is exactly the pattern used in the [`CsvToDatabase` sample](https://github.com/Conveyor-Batch/Conveyor.Batch/blob/main/samples/CsvToDatabase/Program.cs), where a deliberately tolerant `lineMapper` defers real validation to the processor so malformed rows go through the skip policy instead of aborting the read loop.

## Retry: tolerating transient failures

Retry is defined behind an adapter interface, `IRetryPolicy`, so the core package has no hard dependency on any particular retry library:

```csharp
public interface IRetryPolicy
{
    ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken);
}
```

Wire an implementation via `StepBuilder.RetryPolicy(IRetryPolicy policy)`. Today, that means bringing your own implementation — for example, a minimal fixed-attempt retry loop:

```csharp
sealed class FixedAttemptRetryPolicy(int maxAttempts) : IRetryPolicy
{
    public async ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation(cancellationToken);
                return;
            }
            catch when (attempt < maxAttempts)
            {
                // swallow and retry
            }
        }
    }
}
```

Per [ADR-003](/adr/003), first-class Polly v8 integration is planned as a separate `Conveyor.Batch.Polly` package that wraps `ResiliencePipeline` — it has not shipped yet, so there is no `PollyRetryPolicy` type in the current release. Teams that already use Polly can wrap their existing `ResiliencePipeline` in a small `IRetryPolicy` adapter like the one above until that package ships.

::: tip When to use
Use skip policies for known-bad, isolated records that will never succeed no matter how many times you retry them. Reach for retry only around transient failures — not for permanently invalid data, which a retry loop will just fail on repeatedly.
:::
