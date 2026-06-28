# Conveyor.Batch — Agent Working Context

## Project Summary
**Conveyor.Batch** is a production-grade, open-source batch processing framework for .NET 8+,
equivalent to Spring Batch in the Java ecosystem. Fills a genuine gap: no existing .NET library
provides chunk-oriented processing, job repositories, restartability, skip/retry policies, and
partitioning as first-class citizens.

- **GitHub org**: conveyor-dotnet/conveyor.batch
- **NuGet root**: Conveyor.Batch
- **Tagline**: "Reliable batch processing for .NET 8+"
- **License**: MIT

---

## Guiding Principles
- **Correctness first** — a batch framework that loses data or silently skips records is worse than useless
- **Idiomatic .NET** — not a Java port, feels native to C# developers
- **Zero magic** — explicit over convention, debuggable, traceable
- **Composable** — use only what you need, no forced dependencies
- **Observable** — OpenTelemetry native, not bolted on

---

## Tech Stack
| Concern | Choice |
|---|---|
| Target frameworks | .NET 8 + .NET 9 (multi-TFM) |
| Async model | `IAsyncEnumerable<T>` + `System.Threading.Channels` |
| Retry | Polly v8 (adapter pattern, NOT a hard dependency) |
| Persistence | EF Core 8 (PostgreSQL, SQL Server, SQLite) |
| Serialization | `System.Text.Json` |
| Observability | `System.Diagnostics.ActivitySource` + `System.Diagnostics.Metrics` |
| Testing | xUnit + Testcontainers + BenchmarkDotNet |
| CI | GitHub Actions (multi-OS matrix: Linux, Windows, macOS) |

---

## Solution Structure
```
conveyor.batch/
├── src/
│   ├── Conveyor.Batch/
│   │   ├── Abstractions/
│   │   │   ├── IJob.cs
│   │   │   ├── IStep.cs
│   │   │   ├── ITasklet.cs
│   │   │   ├── IItemReader.cs
│   │   │   ├── IItemProcessor.cs
│   │   │   ├── IItemWriter.cs
│   │   │   ├── IJobRepository.cs
│   │   │   ├── IJobLauncher.cs
│   │   │   └── IJobParameters.cs
│   │   ├── Core/
│   │   │   ├── Engine/
│   │   │   │   ├── ChunkOrientedEngine.cs
│   │   │   │   └── TaskletEngine.cs
│   │   │   ├── Job/
│   │   │   │   ├── JobBuilder.cs
│   │   │   │   ├── JobInstance.cs
│   │   │   │   └── JobExecution.cs
│   │   │   ├── Step/
│   │   │   │   ├── StepBuilder.cs
│   │   │   │   ├── StepExecution.cs
│   │   │   │   └── StepExecutionContext.cs
│   │   │   └── Repository/
│   │   │       └── InMemoryJobRepository.cs
│   │   ├── Policies/
│   │   │   ├── ISkipPolicy.cs
│   │   │   ├── IRetryPolicy.cs
│   │   │   └── ExceptionClassifier.cs
│   │   └── Listeners/
│   │       ├── IJobExecutionListener.cs
│   │       ├── IStepExecutionListener.cs
│   │       └── IChunkListener.cs
│   ├── Conveyor.Batch.EntityFrameworkCore/
│   ├── Conveyor.Batch.IO/
│   ├── Conveyor.Batch.Http/
│   ├── Conveyor.Batch.Hosting/
│   └── Conveyor.Batch.Testing/
├── tests/
│   ├── Conveyor.Batch.UnitTests/
│   ├── Conveyor.Batch.IntegrationTests/
│   └── Conveyor.Batch.Benchmarks/
├── samples/
│   ├── GettingStarted/
│   ├── CsvToDatabase/
│   ├── PartitionedProcessing/
│   └── RestartableJob/
├── docs/
│   └── adr/
├── .github/
│   └── workflows/
├── ConveyorBatch.sln
├── README.md
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── SECURITY.md
└── LICENSE (MIT)
```

---

## NuGet Package Structure
| Package | Purpose |
|---|---|
| `Conveyor.Batch` | Core abstractions + chunk engine |
| `Conveyor.Batch.EntityFrameworkCore` | Persistent EF Core job repository |
| `Conveyor.Batch.IO` | FlatFile, JSON, XML readers/writers |
| `Conveyor.Batch.Http` | Paginated HTTP reader |
| `Conveyor.Batch.Hosting` | IHostedService / Worker Service integration |
| `Conveyor.Batch.Tools` | dotnet batch CLI tool |
| `Conveyor.Batch.Testing` | Test builders and mock helpers |

---

## Phase 0 Objectives (Current Work)

Tasks in dependency order — agents should claim a task before starting it:

1. **[SCAFFOLD]** Scaffold the full solution and project structure using `dotnet` CLI
2. **[ADR]** Write Architecture Decision Records in `/docs/adr/`:
   - ADR-001: `IAsyncEnumerable<T>` as the reader contract
   - ADR-002: EF Core as the job repository persistence mechanism
   - ADR-003: Polly v8 adapter pattern for retry (not a hard dependency)
   - ADR-004: `System.Threading.Channels` for internal chunk transport
3. **[ABSTRACTIONS]** Implement all core abstractions in `Conveyor.Batch/Abstractions/` with full XML docs
4. **[ENGINE]** Implement `ChunkOrientedEngine` (depends on ABSTRACTIONS):
   - Configurable chunk size (commit interval)
   - Full `CancellationToken` propagation
   - `IAsyncEnumerable<T>` reader contract
   - Chunk listener hooks (`BeforeChunk`, `AfterChunk`, `OnChunkError`)
   - Skip policy integration
   - Retry policy integration (via Polly adapter)
5. **[REPOSITORY]** Implement `InMemoryJobRepository` for testing
6. **[TESTS]** Write full xUnit unit test coverage for the chunk engine (depends on ENGINE):
   - Happy path: read → process → write
   - Skip behavior: skippable exceptions do not abort the job
   - Retry behavior: retryable exceptions are retried up to limit
   - Cancellation: `CancellationToken` respected mid-chunk
   - Empty input: zero items processed, job completes successfully
7. **[BENCHMARK]** BenchmarkDotNet baseline for chunk engine throughput
8. **[CI]** GitHub Actions CI pipeline (multi-OS matrix: Linux, Windows, macOS)

---

## Core Interface Contracts
**THESE ARE THE PUBLIC API SURFACE — DO NOT MODIFY WITHOUT AN ADR.**
Treat any change as a major architectural decision.

```csharp
// Reader — async stream of input items
public interface IItemReader<out TInput>
{
    IAsyncEnumerable<TInput> ReadAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken);
}

// Processor — transforms one item, can return null to filter/skip
public interface IItemProcessor<in TInput, TOutput>
{
    ValueTask<TOutput?> ProcessAsync(
        TInput item,
        StepExecutionContext context,
        CancellationToken cancellationToken);
}

// Writer — receives a committed chunk of processed items
public interface IItemWriter<in TOutput>
{
    ValueTask WriteAsync(
        IReadOnlyList<TOutput> items,
        StepExecutionContext context,
        CancellationToken cancellationToken);
}

// Job — top-level execution unit
public interface IJob
{
    string Name { get; }
    Task<JobExecution> ExecuteAsync(
        JobParameters parameters,
        CancellationToken cancellationToken);
}

// Step — single phase of a job
public interface IStep
{
    string Name { get; }
    Task<StepExecution> ExecuteAsync(
        JobExecution jobExecution,
        CancellationToken cancellationToken);
}

// Tasklet — simple non-chunk unit of work
public interface ITasklet
{
    ValueTask<RepeatStatus> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken);
}

// Job repository — persistence of execution state
public interface IJobRepository
{
    Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters);
    Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters);
    Task UpdateJobExecutionAsync(JobExecution execution);
    Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName);
    Task UpdateStepExecutionAsync(StepExecution stepExecution);
    Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters);
    Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance);
}

// Job launcher — entry point for triggering jobs
public interface IJobLauncher
{
    Task<JobExecution> RunAsync(
        IJob job,
        JobParameters parameters,
        CancellationToken cancellationToken = default);
}
```

---

## Chunk Engine Behavior (Canonical Pseudocode)
**Implement this exactly.** This is the heart of the framework.

```
FUNCTION ExecuteChunk(reader, processor, writer, chunkSize, context, ct):
  items = []

  FOREACH item IN reader.ReadAsync(context, ct):
    TRY:
      processed = AWAIT processor.ProcessAsync(item, context, ct)
      IF processed IS NOT NULL:
        items.Add(processed)
    CATCH skippable exception:
      context.IncrementSkipCount()
      NOTIFY chunkListener.OnSkip(item, exception)
      CONTINUE
    CATCH retryable exception:
      RETRY with Polly policy

    IF items.Count >= chunkSize:
      NOTIFY chunkListener.BeforeWrite(items)
      AWAIT writer.WriteAsync(items, context, ct)
      NOTIFY chunkListener.AfterWrite(items)
      context.IncrementWriteCount(items.Count)
      items.Clear()

  IF items.Count > 0:  // flush remaining
    AWAIT writer.WriteAsync(items, context, ct)
    context.IncrementWriteCount(items.Count)
```

---

## Quality Gates (Non-Negotiable, Enforced in CI)
- Code coverage ≥ 80%
- Zero warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- All public APIs have XML documentation comments
- BenchmarkDotNet baseline committed before any optimization work

---

## Coding Conventions
- Namespace root: `Conveyor.Batch`
- All async methods return `Task`, `ValueTask`, or `IAsyncEnumerable` — no sync-over-async
- `CancellationToken` is always the last parameter
- No `throw new NotImplementedException()` in committed code
- Internal types use `internal sealed` where appropriate
- Use `readonly record struct` for value objects (e.g., `JobParameters`)

---

## Agent Coordination Notes
- This file is the ground truth. If it conflicts with anything else, this wins.
- Before implementing a component, verify the interface contract above exactly matches what you implement.
- The `Abstractions/` layer has zero dependencies on `Core/` — never reverse this.
- `InMemoryJobRepository` lives in `Conveyor.Batch` (not a separate package) for zero-dependency testing.
- Do not add NuGet packages not listed in the tech stack without a new ADR.
