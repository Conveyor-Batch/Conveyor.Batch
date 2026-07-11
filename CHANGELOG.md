# Changelog

All notable changes to Conveyor.Batch are documented here.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning: [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.0-beta.4] — 2026-07-11

### Added
- **Graceful shutdown** — `GracefulShutdownOptions` on `StepBuilder`: a stop signal lets the
  current chunk finish processing and commit before the engine exits cleanly with
  `BatchStatus.Stopped`. Configurable `DrainTimeout` aborts if the drain stalls. (#14)
- **Job heartbeat** — `HeartbeatOptions` on `SimpleJobLauncher`: writes `LastHeartbeatAt` to
  the job execution record on a configurable interval. Heartbeat failures are swallowed and
  logged — they never abort the job. (#15)
- **XmlItemReader\<T\>** and **XmlItemWriter\<T\>** in `Conveyor.Batch.IO` — element-level XML
  streaming via `System.Xml.Linq`; `XmlItemReader` implements `IItemStream` for restartable
  reads. No external packages required. (#15)
- **Three new samples**: `CsvToDatabase` (skip policy + EF Core writer), `PartitionedProcessing`
  (RangePartitioner + 4 parallel workers over 10 000 rows), `RestartableJob` (failure and resume
  with checkpoint verification). (#15)
- **Integration test suite** (`Conveyor.Batch.IntegrationTests`) — Testcontainers-backed tests
  running against real PostgreSQL and SQL Server instances, covering: EF Core repository
  persistence, `JobParameters` order-independent equality regression, checkpoint round-trip,
  heartbeat persistence, concurrent launch guard, and restartability end-to-end. (#16)
- **`Conveyor.Batch.Dapper`** — `DapperItemReader<T>` with keyset/offset pagination, `IItemStream`
  for restartable reads, and a `connectionFactory` delegate so callers control connection
  lifetime. (#17, #20)
- **VitePress documentation site** — full docs at `docs/site/` with Getting Started guide, per-package
  API reference, feature guides (restartability, partitioning, conditional flow, graceful shutdown,
  heartbeat, dead-lettering, observability), and ADR index. Deployed to GitHub Pages via Actions. (#18)
- **`dotnet-batch` CLI tool** (`Conveyor.Batch.Tools`) — `dotnet batch jobs`, `dotnet batch executions
  <jobName>`, `dotnet batch steps <executionId>`, `dotnet batch rerun <executionId>`. Connects to
  any EF Core–backed job repository via `--connection` and `--provider`. (#19)

---

## [0.1.0-beta.3] — 2026-07

### Changed
- CI publish workflow now derives the NuGet package version directly from the release tag,
  removing the need for a manual version bump commit before each release.

---

## [0.1.0-beta.2] — 2026-07

### Added
- **EfCoreItemReader\<TContext, T, TKey\>** — keyset-paginated reader over any EF Core
  `DbContext`; implements `IItemStream` for restartable reads across process restarts.
- **EfCoreItemWriter\<TContext, T\>** — bulk-adds items to a `DbContext` and saves per chunk.
- **Dead-lettering** — `IDeadLetterWriter<T>` abstraction with three built-in implementations:
  `JsonDeadLetterWriter`, `CsvDeadLetterWriter`, and `ChannelDeadLetterWriter`. Wire up via
  `DeadLetterChunkListener<T>` so poison items are routed to an inspectable sink rather than
  silently dropped.

### Fixed
- **`JobParameters` value equality** — `record struct` auto-generated equality used reference
  equality on `IReadOnlyDictionary`, causing `GetLastJobExecutionAsync` to never find a prior
  execution. Overrode `Equals`/`GetHashCode` with sorted key-value comparison.
- **Concurrent launch guard** — `SimpleJobLauncher` now calls `GetRunningJobExecutionAsync`
  before creating a new execution and throws `InvalidOperationException` if the same job is
  already running with the same parameters.

### Infrastructure
- CI enforces ≥ 80% line coverage on `Conveyor.Batch` core package.

---

## [0.1.0-beta.1] — 2026-07

### Added
- **Restartability** — `IItemStream` lifecycle (`OpenAsync` / `UpdateAsync` / `CloseAsync`) and
  `BatchExecutionContext` (typed key-value store serialized via `System.Text.Json`). The
  `ChunkOrientedEngine` calls `UpdateAsync` and persists the step execution after every committed
  chunk. On re-launch with the same `JobParameters`, the engine reloads the checkpoint and the
  reader resumes from the last committed position.
- **Partitioning** — `IPartitioner`, `PartitionStep`, `PartitionStepBuilder`, `RangePartitioner`,
  `LocalPartitionHandler` (parallel execution via `SemaphoreSlim`). Splits large datasets into
  named partitions and fans them out to worker steps.
- **Concurrent chunk engine** — `ConcurrentChunkOrientedEngine<TInput, TOutput>` using
  `System.Threading.Channels`: producer reads items into a channel, N processor workers consume
  in parallel, a chunk assembler batches results, writer commits sequentially. Output order is
  non-deterministic by design.
- **Conditional job flow** — `FluentJobBuilder` with `.Start().On("STATUS").To()/.Fail()/.Stop()/.End()` DSL. Graph is validated on `Build()`: unreachable steps and missing terminal rules are detected.
- **Composite patterns** — `CompositeItemProcessor<T>` (homogeneous chain) and
  `ProcessorChain<TIn, TMid, TOut>` (two-step typed chain); `CompositeItemWriter<T>` (fan-out
  to multiple writers).
- **OpenTelemetry instrumentation** — `ActivitySource("Conveyor.Batch")` and
  `Meter("Conveyor.Batch")` with instruments: `JobsCompleted`, `JobsFailed`, `JobDuration`,
  `ChunksCommitted`, `ChunkSize`, `ItemsRead`, `ItemsWritten`, `ItemsSkipped`. Zero extra
  NuGet packages — uses inbox `System.Diagnostics` APIs.
- **`Conveyor.Batch.Http`** — `HttpPaginatedItemReader<T>` with configurable page size, URL
  factory, response deserializer, and stop predicate.
- **`Conveyor.Batch.Testing`** — `InMemoryItemReader<T>`, `InMemoryItemWriter<T>`,
  `IdentityProcessor<T>`, `FuncProcessor<TIn, TOut>`, `AlwaysSkipPolicy`.

---

## [0.1.0-alpha.1] — 2026-06

Initial pre-release. Establishes the full public API surface and package structure.

### Added
- **Core abstractions** — `IJob`, `IStep`, `ITasklet`, `IItemReader<T>`, `IItemProcessor<TIn, TOut>`,
  `IItemWriter<T>`, `IJobRepository`, `IJobLauncher`, `IJobParameters`.
- **Chunk-oriented engine** — `ChunkOrientedEngine<TInput, TOutput>` with configurable chunk
  size, full `CancellationToken` propagation, skip policy integration, Polly v8 retry adapter,
  and chunk listener hooks (`BeforeWrite`, `AfterWrite`, `OnSkip`).
- **`InMemoryJobRepository`** — thread-safe in-memory repository for unit testing.
- **`JobBuilder`** and **`StepBuilder`** — fluent builders for constructing jobs and steps.
- **`SimpleJobLauncher`** — default `IJobLauncher` implementation.
- **`Conveyor.Batch.EntityFrameworkCore`** — `EfCoreJobRepository` with EF Core migrations for
  PostgreSQL, SQL Server, and SQLite. Multi-provider migration support via `ADR-005`.
- **`Conveyor.Batch.IO`** — `FlatFileItemReader<T>` / `FlatFileItemWriter<T>`,
  `JsonItemReader<T>` / `JsonItemWriter<T>`.
- **`Conveyor.Batch.Hosting`** — `services.AddConveyorBatch()` DI registration,
  `IHostedService` / Worker Service integration.
- **Architecture Decision Records** — ADR-001 through ADR-004 covering async enumerable reader
  contract, EF Core persistence, Polly adapter pattern, and Channels chunk transport.
- **CI pipeline** — multi-OS (Linux, Windows, macOS) × multi-TFM (.NET 8/9/10) matrix.
- **BenchmarkDotNet baseline** — throughput baseline for the sequential chunk engine.
- **`GettingStarted` sample** — verified on CI.
- OSS hygiene: MIT license, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md.

[Unreleased]: https://github.com/Conveyor-Batch/Conveyor.Batch/compare/v0.1.0-beta.4...HEAD
[0.1.0-beta.4]: https://github.com/Conveyor-Batch/Conveyor.Batch/compare/0.1.0-beta.3...v0.1.0-beta.4
[0.1.0-beta.3]: https://github.com/Conveyor-Batch/Conveyor.Batch/compare/v0.1.0-beta.2...0.1.0-beta.3
[0.1.0-beta.2]: https://github.com/Conveyor-Batch/Conveyor.Batch/compare/v0.1.0-alpha.1...v0.1.0-beta.2
[0.1.0-beta.1]: https://github.com/Conveyor-Batch/Conveyor.Batch/compare/v0.1.0-alpha.1...v0.1.0-beta.2
[0.1.0-alpha.1]: https://github.com/Conveyor-Batch/Conveyor.Batch/releases/tag/v0.1.0-alpha.1
