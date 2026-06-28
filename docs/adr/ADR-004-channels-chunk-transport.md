# ADR-004: System.Threading.Channels for Internal Chunk Transport

**Status:** Accepted  
**Date:** 2026-06-28

## Context

Future partitioned and parallel step implementations will require a mechanism to pass chunks between producer and consumer stages within a pipeline. Options considered:
- `BlockingCollection<T>` — older, blocking API not suited for async code
- `System.Threading.Channels` — modern, async-native, built into the BCL since .NET Core 3.0
- Third-party dataflow libraries (TPL Dataflow, etc.)

## Decision

Use `System.Threading.Channels` (`Channel<T>`) for internal chunk transport in parallel and partitioned step implementations.

## Rationale

- **BCL-native** — no additional NuGet dependency; available in all target frameworks.
- **Async-first** — `ChannelReader<T>` and `ChannelWriter<T>` expose `ReadAsync`/`WriteAsync` and integrate naturally with `IAsyncEnumerable<T>`.
- **Backpressure control** — bounded channels provide natural flow control between fast readers and slow writers.
- **Well-understood semantics** — familiar to .NET developers; documented, tested, and maintained by Microsoft.

## Consequences

- The current `ChunkOrientedEngine` does not use channels (single-threaded, sequential); channels are introduced when partitioned steps are implemented.
- Channel configuration (bounded vs. unbounded, capacity) will be exposed via builder options.
- TPL Dataflow is explicitly excluded to avoid an optional NuGet dependency in the core package.
