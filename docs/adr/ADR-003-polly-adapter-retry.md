# ADR-003: Polly v8 Adapter Pattern for Retry (Not a Hard Dependency)

**Status:** Accepted  
**Date:** 2026-06-28

## Context

Retry behavior is essential for resilient batch processing, but the choice of retry library should not be forced on consumers. Options considered:
- Embed Polly as a hard dependency in `Conveyor.Batch`
- Ship a minimal built-in retry loop
- Define an `IRetryPolicy` adapter interface; ship a Polly implementation separately

## Decision

Define `IRetryPolicy` in the core package as an adapter interface. Provide a `Conveyor.Batch.Polly` package (future) that wraps Polly v8's `ResiliencePipeline`.

```csharp
public interface IRetryPolicy
{
    ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken);
}
```

## Rationale

- **Zero forced dependencies** — `Conveyor.Batch` has no NuGet dependencies beyond the BCL; consumers bring their own resilience library.
- **Testability** — `IRetryPolicy` is easy to mock or fake without Polly being present in test projects.
- **Flexibility** — Teams with existing Polly pipelines can wrap them directly; teams that prefer Microsoft.Extensions.Resilience or custom logic are not blocked.
- **Polly v8 alignment** — When the adapter package ships, it wraps `ResiliencePipeline<T>`, which is the idiomatic Polly v8 API.

## Consequences

- Retry is opt-in; passing `null` for `IRetryPolicy` disables retry (no retry happens).
- The `ChunkOrientedEngine` calls `IRetryPolicy.ExecuteAsync` only for processor invocations; write retries are out of scope for now.
- A future `Conveyor.Batch.Polly` package will provide `PollyRetryPolicy : IRetryPolicy` wrapping `ResiliencePipeline`.
