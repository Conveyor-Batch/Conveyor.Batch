# Architecture Decision Records

Key design decisions behind Conveyor.Batch, and why they were made.

| ADR | Decision |
|---|---|
| [ADR-001](/adr/001) | Use `IAsyncEnumerable<T>` as the reader contract, for backpressure, cancellation, and LINQ composability. |
| [ADR-002](/adr/002) | Use EF Core as the job repository persistence mechanism, as a separate optional package. |
| [ADR-003](/adr/003) | Define `IRetryPolicy` as an adapter interface; ship Polly v8 support as a separate future package rather than a hard dependency. |
| [ADR-004](/adr/004) | Use `System.Threading.Channels` for internal chunk transport in parallel and partitioned steps. |

Source ADR files live in [`docs/adr/`](https://github.com/Conveyor-Batch/Conveyor.Batch/tree/main/docs/adr) in the repository.
