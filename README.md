# Conveyor.Batch

**Reliable batch processing for .NET 8+**

[![CI](https://github.com/Conveyor-Batch/Conveyor.Batch/actions/workflows/ci.yml/badge.svg)](https://github.com/Conveyor-Batch/Conveyor.Batch/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.svg?label=nuget)](https://www.nuget.org/packages/Conveyor.Batch)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Conveyor.Batch is a production-grade, open-source batch processing framework for .NET — the Spring Batch equivalent for the .NET ecosystem. It provides chunk-oriented processing, job repositories, restartability, skip/retry policies, and partitioning as first-class citizens.

---

## Why Conveyor.Batch?

No existing .NET library provides all of these together:

- **Chunk-oriented processing** — read → process → write in configurable commit intervals
- **Restartability** — jobs resume from the last committed checkpoint after a failure, with no duplicate processing
- **Partitioning** — split large datasets and process partitions in parallel with `LocalPartitionHandler`
- **Concurrent chunk engine** — pipeline parallelism via `System.Threading.Channels` for CPU-bound workloads
- **Conditional job flow** — branch between steps based on exit status with `FluentJobBuilder`
- **Skip & retry policies** — handle bad records and transient failures without aborting the job
- **Dead-lettering** — poison items are routed to an inspectable dead-letter sink, not silently dropped
- **Graceful shutdown** — stop token lets the current chunk finish before the process exits
- **Heartbeat** — long-running jobs write `LastHeartbeatAt` on a configurable interval for liveness monitoring
- **Observable** — OpenTelemetry-native via `ActivitySource` and `Metrics`; no extra packages required
- **Composable** — use only the packages you need, no forced dependencies
- **Idiomatic .NET** — not a Java port; feels native to C# developers

---

## Packages

| Package | Description | NuGet |
|---|---|---|
| `Conveyor.Batch` | Core abstractions + chunk engine | [![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.svg)](https://www.nuget.org/packages/Conveyor.Batch) |
| `Conveyor.Batch.EntityFrameworkCore` | Persistent EF Core job repository | [![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Conveyor.Batch.EntityFrameworkCore) |
| `Conveyor.Batch.IO` | Flat-file, JSON, and XML readers/writers | [![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.IO.svg)](https://www.nuget.org/packages/Conveyor.Batch.IO) |
| `Conveyor.Batch.Http` | Paginated HTTP item reader | [![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.Http.svg)](https://www.nuget.org/packages/Conveyor.Batch.Http) |
| `Conveyor.Batch.Hosting` | `IHostedService` / Worker Service integration | [![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.Hosting.svg)](https://www.nuget.org/packages/Conveyor.Batch.Hosting) |
| `Conveyor.Batch.Testing` | Test builders and helpers | [![NuGet](https://img.shields.io/nuget/vpre/Conveyor.Batch.Testing.svg)](https://www.nuget.org/packages/Conveyor.Batch.Testing) |

---

## Quick Start

### 1. Install

```bash
dotnet add package Conveyor.Batch
dotnet add package Conveyor.Batch.Hosting   # optional: Worker Service integration
dotnet add package Conveyor.Batch.IO        # optional: flat-file / JSON IO
```

### 2. Define your pipeline

```csharp
// Reader — async stream of input items
sealed class CsvOrderReader(string filePath) : IItemReader<Order>
{
    public IAsyncEnumerable<Order> ReadAsync(StepExecutionContext ctx, CancellationToken ct)
        => new FlatFileItemReader<Order>(filePath, line =>
        {
            var parts = line.Split(',');
            return new Order(int.Parse(parts[0]), parts[1], decimal.Parse(parts[2]));
        }).ReadAsync(ctx, ct);
}

// Processor — transform one item, return null to filter it out
sealed class OrderProcessor : IItemProcessor<Order, ProcessedOrder>
{
    public ValueTask<ProcessedOrder?> ProcessAsync(Order item, StepExecutionContext ctx, CancellationToken ct)
    {
        if (item.Amount <= 0) return ValueTask.FromResult<ProcessedOrder?>(null); // skip

        return ValueTask.FromResult<ProcessedOrder?>(
            new ProcessedOrder(item.Id, item.Product, item.Amount, item.Amount * 0.08m));
    }
}

// Writer — receives a committed chunk
sealed class DatabaseOrderWriter(AppDbContext db) : IItemWriter<ProcessedOrder>
{
    public async ValueTask WriteAsync(IReadOnlyList<ProcessedOrder> items, StepExecutionContext ctx, CancellationToken ct)
    {
        db.ProcessedOrders.AddRange(items.Select(o => new ProcessedOrderRow(o)));
        await db.SaveChangesAsync(ct);
    }
}
```

### 3. Wire it up with the builder API

```csharp
var repository = new InMemoryJobRepository(); // or EfCoreJobRepository for persistence

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(new CsvOrderReader("orders.csv"))
    .Processor(new OrderProcessor())
    .Writer(new DatabaseOrderWriter(db))
    .ChunkSize(100)
    .SkipPolicy(new ExceptionClassifier().AddSkippable<FormatException>())
    .Build("process-orders");

var job = new JobBuilder("import-orders", repository)
    .AddStep(step)
    .Build();

var launcher = new SimpleJobLauncher(repository);
var execution = await launcher.RunAsync(job, JobParameters.Empty);

Console.WriteLine($"Status: {execution.Status}"); // Completed
```

### 4. Or integrate with Worker Services

```csharp
// Program.cs
builder.Services
    .AddConveyorBatch()                  // registers IJobRepository + IJobLauncher
    .AddBatchJob<OrderImportJob>();      // registers job + IHostedService

// OrderImportJob.cs
sealed class OrderImportJob(IJobRepository repository) : IJob
{
    public string Name => "order-import";

    public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken ct)
    {
        var step = new StepBuilder<Order, ProcessedOrder>(repository)
            .Reader(new CsvOrderReader(parameters.Get("file")!))
            .Processor(new OrderProcessor())
            .Writer(new DatabaseOrderWriter(...))
            .ChunkSize(500)
            .Build("process-orders");

        return await new JobBuilder(Name, repository)
            .AddStep(step)
            .Build()
            .ExecuteAsync(parameters, ct);
    }
}
```

---

## Core Concepts

### Chunk-Oriented Processing

The engine reads items one at a time from the reader, passes each through the processor, accumulates them into a chunk, and writes the whole chunk at once when the chunk size is reached. Remaining items are flushed at the end of the stream.

```
Reader ──► Processor ──► [accumulate] ──► Writer (per chunk)
              │
              └──► null = filter item out
```

### Job & Step Model

```
Job
 └── Step 1  (chunk-oriented or tasklet)
 └── Step 2
 └── Step N
```

Each step records its own `StepExecution` (read/write/skip counts, status, timestamps). Each job records a `JobExecution`. Both are persisted via `IJobRepository`.

### Skip & Retry Policies

```csharp
// Skip bad records up to a limit
var classifier = new ExceptionClassifier()
    .AddSkippable<FormatException>()
    .AddSkippable<ValidationException>();

var skipPolicy = new ClassifierSkipPolicy(classifier, skipLimit: 10);

// Retry transient failures (bring your own Polly pipeline)
var retryPolicy = new PollyRetryPolicy(
    Pipeline.Create().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 }));
```

### Flat-File, JSON, and XML IO

```csharp
// Read a CSV
var reader = new FlatFileItemReader<Product>(
    filePath: "products.csv",
    lineMapper: line => { var p = line.Split(','); return new Product(p[0], decimal.Parse(p[1])); },
    skipHeader: true);

// Write JSON output
var writer = new JsonItemWriter<Product>("output.json");

// Read / write XML
var reader = new XmlItemReader<Product>(
    filePath: "products.xml",
    elementName: "Product",
    elementMapper: el => new Product(el.Element("Name")!.Value, decimal.Parse(el.Element("Price")!.Value)));

var writer = new XmlItemWriter<Product>(
    filePath: "output.xml",
    rootElementName: "Products",
    itemElementName: "Product",
    elementMapper: p => new XElement("Product",
        new XElement("Name", p.Name),
        new XElement("Price", p.Price)));
```

### Restartability

Jobs that fail mid-run resume from the last committed chunk. No duplicate processing, no gaps.

```csharp
// Reader implements IItemStream — saves its position to ExecutionContext after each chunk
sealed class RestartableCsvReader(string filePath) : IItemReader<Order>, IItemStream
{
    private int _currentIndex;

    public async ValueTask OpenAsync(BatchExecutionContext ctx, CancellationToken ct)
        => _currentIndex = ctx.Get<int>("reader.offset");

    public async ValueTask UpdateAsync(BatchExecutionContext ctx, CancellationToken ct)
        => ctx.Put("reader.offset", _currentIndex);

    public ValueTask CloseAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public async IAsyncEnumerable<Order> ReadAsync(StepExecutionContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        foreach (var line in lines.Skip(_currentIndex))
        {
            _currentIndex++;
            yield return Order.Parse(line);
        }
    }
}

// Use EfCoreJobRepository so the checkpoint survives process restarts
var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(new RestartableCsvReader("orders.csv"))
    .Processor(new OrderProcessor())
    .Writer(new DatabaseWriter(db))
    .ChunkSize(100)
    .Build("process-orders");
```

When the job is re-launched with the same `JobParameters`, Conveyor.Batch detects the prior failed execution, loads the saved checkpoint, and the reader skips the already-processed records automatically.

### Partitioning

Split a large dataset and process partitions in parallel.

```csharp
// Divide rows 1–1,000,000 into 8 partitions processed concurrently
var partitionStep = new PartitionStepBuilder<long>(repository)
    .Partitioner(new RangePartitioner(1, 1_000_000, gridSize: 8))
    .WorkerStep((ctx, partition) =>
        new StepBuilder<SourceRow, ProcessedRow>(repository)
            .Reader(new EfCoreItemReader<AppDbContext, SourceRow, long>(
                db, q => q.Where(r => r.Id >= partition.MinValue && r.Id <= partition.MaxValue)))
            .Processor(new RowProcessor())
            .Writer(new EfCoreItemWriter<AppDbContext, ProcessedRow>(db))
            .ChunkSize(500)
            .Build($"partition-{partition.Name}"))
    .Handler(new LocalPartitionHandler(maxDegreeOfParallelism: 8))
    .Build("partition-step");
```

### Conditional Job Flow

Branch between steps based on exit status using the fluent builder.

```csharp
var job = new FluentJobBuilder("etl-pipeline", repository)
    .Start(validateStep)
        .On("COMPLETED").To(importStep)
        .On("FAILED").To(notifyStep).End()
    .From(importStep)
        .On("COMPLETED").End()
        .On("FAILED").To(rollbackStep).Fail()
    .Build();
```

### Graceful Shutdown

Configure a drain window so the current chunk finishes before the process exits.

```csharp
var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(100)
    .GracefulShutdown(new GracefulShutdownOptions { DrainTimeout = TimeSpan.FromSeconds(30) })
    .Build("process-orders");
```

When a stop signal arrives, the engine finishes processing the items already read, commits the chunk, and persists a checkpoint before exiting cleanly with `BatchStatus.Stopped`.

### Heartbeat

Monitor long-running jobs by checking `LastHeartbeatAt`. Alert if it goes stale.

```csharp
var launcher = new SimpleJobLauncher(
    repository,
    heartbeat: new HeartbeatOptions { Interval = TimeSpan.FromSeconds(30) });
```

The launcher updates `JobExecution.LastHeartbeatAt` in the repository every 30 seconds. Heartbeat failures are swallowed and logged — they never abort the job.

### Dead-Lettering

Poison items are routed to an inspectable sink rather than silently dropped.

```csharp
// Write failed items to a JSON file for later inspection
var deadLetterWriter = new JsonDeadLetterWriter<Order>("dead-letters.json");

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(100)
    .ChunkListener(new DeadLetterChunkListener<Order>(deadLetterWriter))
    .Build("process-orders");
```

---

## Samples

| Sample | What it demonstrates |
|---|---|
| [`GettingStarted`](samples/GettingStarted/) | Minimal reader → processor → writer pipeline |
| [`CsvToDatabase`](samples/CsvToDatabase/) | `FlatFileItemReader` + EF Core writer + skip policy for malformed rows |
| [`PartitionedProcessing`](samples/PartitionedProcessing/) | `RangePartitioner` + `LocalPartitionHandler` processing 10 000 rows across 4 parallel workers |
| [`RestartableJob`](samples/RestartableJob/) | Job that fails mid-run and resumes from checkpoint with no duplicate processing |

Run any sample with:

```bash
dotnet run --project samples/CsvToDatabase
```

---

## Architecture

Conveyor.Batch follows a strict layered architecture:

```
Conveyor.Batch                      ← zero dependencies: abstractions + chunk engine
                                      + sequential + concurrent engines, partitioning,
                                      skip/retry/dead-letter policies, graceful shutdown,
                                      heartbeat, FluentJobBuilder, InMemoryJobRepository
Conveyor.Batch.EntityFrameworkCore  ← optional: persistent job repository (PostgreSQL,
                                      SQL Server, SQLite) + EF Core item reader/writer
Conveyor.Batch.IO                   ← optional: flat-file, JSON, XML readers & writers
Conveyor.Batch.Http                 ← optional: paginated HTTP item reader
Conveyor.Batch.Hosting              ← optional: IHostedService + DI extensions
Conveyor.Batch.Testing              ← optional: InMemoryItemReader/Writer, FuncProcessor,
                                      AlwaysSkipPolicy, and other test helpers
```

Key decisions are documented in [Architecture Decision Records](/docs/adr/):
- [ADR-001](docs/adr/ADR-001-async-enumerable-reader.md) — `IAsyncEnumerable<T>` as the reader contract
- [ADR-002](docs/adr/ADR-002-efcore-job-repository.md) — EF Core for job repository persistence
- [ADR-003](docs/adr/ADR-003-polly-adapter-retry.md) — Polly v8 adapter pattern for retry
- [ADR-004](docs/adr/ADR-004-channels-chunk-transport.md) — `System.Threading.Channels` for internal chunk transport

---

## Requirements

- .NET 8, .NET 9, or .NET 10

---

## Building from Source

```bash
git clone https://github.com/Conveyor-Batch/Conveyor.Batch.git
cd Conveyor.Batch

dotnet build ConveyorBatch.slnx
dotnet test ConveyorBatch.slnx --framework net10.0
```

Run the getting-started sample:

```bash
dotnet run --project samples/GettingStarted
```

---

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

- **Bug reports** → [open an issue](https://github.com/Conveyor-Batch/Conveyor.Batch/issues)
- **Feature requests** → open an issue with the `enhancement` label
- **Security vulnerabilities** → see [SECURITY.md](SECURITY.md)

---

## License

MIT — see [LICENSE](LICENSE) for details.
