# ADR-001: IAsyncEnumerable\<T\> as the Reader Contract

**Status:** Accepted  
**Date:** 2026-06-28

## Context

The reader is the data ingress point for every batch step. The contract must support:
- Large or infinite data sets that cannot be loaded entirely into memory
- Natural backpressure (the engine controls the consumption rate)
- Integration with `CancellationToken` for cooperative cancellation
- Composability with LINQ and other async stream operators in .NET

Two alternatives were considered: `IAsyncEnumerable<T>` and a pull-based `ReadAsync()` returning `T?`.

## Decision

Use `IAsyncEnumerable<T>` as the reader contract:

```csharp
IAsyncEnumerable<TInput> ReadAsync(StepExecutionContext context, CancellationToken cancellationToken);
```

## Rationale

- **Built-in language support** — `await foreach` is idiomatic C# 8+; no custom iteration protocol needed.
- **Backpressure by design** — the consumer (`ChunkOrientedEngine`) drives the pace; the producer only runs when asked.
- **`CancellationToken` integration** — `[EnumeratorCancellation]` propagates the token into the iterator body automatically.
- **LINQ composability** — `System.Linq.Async` and custom extension methods work directly on `IAsyncEnumerable<T>`.
- **No buffering required** — items flow one at a time; memory usage is bounded by chunk size, not input size.

## Consequences

- Implementations must use `async` iterators (`yield return`) or wrap existing push-based sources.
- The engine never materializes the full input; readers that need random access must handle that internally.
- The `out` variance on `IItemReader<out TInput>` is intentional — it permits covariant reader assignments.
