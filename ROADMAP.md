# Conveyor.Batch — Public Roadmap

This is the living roadmap for Conveyor.Batch. Updated after each phase milestone.
Comment with questions, concerns, or priorities.

---

## Phase 0 — Foundation `[Complete]`

**Goal:** Lock the architecture before the community sees it.

- [x] Repository scaffold + OSS hygiene (LICENSE, CONTRIBUTING, SECURITY)
- [x] Architecture Decision Records (ADR-001 through ADR-004)
- [x] Core abstractions (IJob, IStep, IItemReader, IItemProcessor, IItemWriter)
- [x] Chunk-oriented engine (read → process → write loop)
- [x] InMemoryJobRepository (for testing)
- [x] Skip policy + ExceptionClassifier
- [x] Polly v8 retry adapter
- [x] CI pipeline (multi-OS, multi-TFM: .NET 8/9/10)
- [x] Unit test coverage ≥ 80%
- [x] BenchmarkDotNet baseline committed
- [x] GettingStarted sample verified on CI

---

## Phase 1 — Alpha `[Complete]`

**Goal:** First NuGet publish. External developers can build real jobs.

- [x] EF Core job repository (PostgreSQL, SQL Server, SQLite)
- [x] EfCoreItemReader / EfCoreItemWriter
- [x] FlatFileItemReader / FlatFileItemWriter
- [x] JsonItemReader / JsonItemWriter
- [x] XmlItemReader / XmlItemWriter
- [x] HttpPaginatedItemReader (`Conveyor.Batch.Http`)
- [x] CompositeItemProcessor (homogeneous chain + typed two-step chain)
- [x] CompositeItemWriter (fan-out to multiple writers)
- [x] IHostedService / Worker Service integration
- [x] `services.AddConveyorBatch()` DI extensions
- [x] Restartability — resume from last checkpoint (BatchExecutionContext, IItemStream)
- [x] Conditional job flow (FluentJobBuilder — `.On("COMPLETED").To()/.Fail()/.Stop()/.End()`)
- [x] Conveyor.Batch.Testing (InMemoryItemReader/Writer, FuncProcessor, AlwaysSkipPolicy)
- [x] First NuGet pre-release publish (`0.1.0-beta.3`)
- [ ] Documentation site (VitePress on GitHub Pages)

---

## Phase 2 — Beta `[Complete]`

**Goal:** Harden under community feedback. Close remaining production gaps.

**Shipped ahead of schedule (originally Phase 2–3):**
- [x] Local partitioning (IPartitioner, PartitionStep, RangePartitioner, LocalPartitionHandler)
- [x] Remote partitioning interface (IPartitionHandler — extension point for distributed handlers)
- [x] Multi-threaded step execution (ConcurrentChunkOrientedEngine via System.Threading.Channels)
- [x] OpenTelemetry instrumentation (ActivitySource + Metrics: job/step/chunk duration, items read/written/skipped)
- [x] Graceful shutdown (drain current chunk, commit, stop cleanly — GracefulShutdownOptions)
- [x] Long-running job heartbeat (LastHeartbeatAt, configurable interval)
- [x] Dead-lettering (IDeadLetterWriter + DeadLetterChunkListener — JSON, CSV, and channel implementations)
- [x] Distributed job locking (IJobLockProvider + EfCoreJobLockProvider — prevents duplicate concurrent launches)
- [x] JobParameters value-equality fix (order-independent key comparison)
- [x] Samples: CsvToDatabase, PartitionedProcessing, RestartableJob
- [x] Integration test suite (Testcontainers — PostgreSQL + SQL Server)

- [x] DapperItemReader (`Conveyor.Batch.Dapper`)
- [x] `dotnet batch` CLI tool (`Conveyor.Batch.Tools`)
- [x] Documentation site (VitePress on GitHub Pages)
- [ ] GitHub Discussions open to community
- [ ] Discord server

---

## Phase 3 — RC & v1.0 `[In Progress]`

**Goal:** API frozen. Production confidence. Stable release.

- [ ] API freeze + semantic versioning policy published
- [ ] BenchmarkDotNet performance suite (full regression baseline)
- [ ] Bulk write optimization (TVP for SQL Server, COPY for PostgreSQL)
- [ ] Chaos testing suite
- [ ] Security hardening (parameter sanitization, log masking)
- [ ] Full production deployment guide
- [ ] CHANGELOG.md + automated GitHub Release notes on tag push
- [ ] v1.0 NuGet stable release

---

## Phase 4 — Ecosystem `[Planned: Months 9–16]`

**Goal:** Extensions, admin UI, community governance.

- [ ] Conveyor.Batch.Kafka
- [ ] Conveyor.Batch.AzureBlob / Conveyor.Batch.S3
- [ ] Conveyor.Batch.AzureServiceBus
- [ ] Quartz.NET + Hangfire integration
- [ ] Blazor admin dashboard (job monitoring, restart from UI)
- [ ] Core maintainer team established
- [ ] GitHub Sponsors / Open Collective

---

## What's not on the roadmap (yet)

- SSIS migration tooling
- Azure Data Factory integration
- GUI job designer

If you need something not listed, open a thread in
[Ideas & Feature Requests](https://github.com/Conveyor-Batch/Conveyor.Batch/discussions).

---

## Changelog

| Date | Change |
|---|---|
| July 2026 | Phase 2 marked complete — `0.1.0-beta.4` ships Conveyor.Batch.Dapper, VitePress docs site, dotnet-batch CLI |
| July 2026 | Phase 2 update: integration test suite (Testcontainers — PostgreSQL + SQL Server) shipped |
| July 2026 | Phase 2 update: dead-lettering, distributed locking, graceful shutdown, heartbeat, JobParameters fix, XmlItemReader/Writer, 3 new samples all shipped — all originally planned for Phase 2–3 |
| July 2026 | Phase 2 update: partitioning, ConcurrentChunkOrientedEngine, OpenTelemetry shipped |
| July 2026 | Phase 1 marked complete: restartability, FluentJobBuilder, EfCoreItemReader/Writer, Http + Testing packages all shipped; version track moved to beta |
| July 2026 | Phase 1 update: EF Core repo, FlatFile/JSON IO, Hosting, DI, CompositeItemProcessor/Writer, NuGet beta publish |
| July 2026 | Phase 0 marked complete |
| June 2026 | Initial roadmap published |

*Last updated: July 2026 — Phase 2 complete (`0.1.0-beta.4`), Phase 3 in progress*
