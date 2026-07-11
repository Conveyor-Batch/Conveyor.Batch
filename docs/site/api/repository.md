# Repository

Job and step execution state is persisted through `IJobRepository` — see its full signature on the [Core API page](/api/core#ijobrepository). Two implementations ship today: an in-memory one for testing, and an EF Core–backed one for production, restartable jobs.

## InMemoryJobRepository

Namespace `Conveyor.Batch.Core.Repository` — ships in the core `Conveyor.Batch` package (no separate package needed).

```csharp
public sealed class InMemoryJobRepository : IJobRepository
{
    public InMemoryJobRepository();
}
```

No constructor parameters. State lives only for the lifetime of the process — use this for tests and simple, single-run jobs. It does **not** provide durable checkpoints across process restarts; for that, use `EfCoreJobRepository`.

## EfCoreJobRepository

Namespace `Conveyor.Batch.EntityFrameworkCore`.

```csharp
public sealed class EfCoreJobRepository : IJobRepository, IDisposable
{
    public EfCoreJobRepository(ConveyorBatchDbContext dbContext);
}
```

| Parameter | Description |
|---|---|
| `dbContext` | The `ConveyorBatchDbContext` used to persist job/step execution state. |

Persists every `JobInstance`, `JobExecution`, and `StepExecution` (including a reader's restart checkpoint, stored in the step's execution context) to the database backing `dbContext` — see [ADR-002](/adr/002) for why EF Core was chosen. Supports PostgreSQL, SQL Server, and SQLite via the corresponding EF Core provider package.

## ConveyorBatchDbContext

```csharp
public class ConveyorBatchDbContext : DbContext
{
    public ConveyorBatchDbContext(DbContextOptions<ConveyorBatchDbContext> options);

    public DbSet<JobInstanceEntity> JobInstances { get; }
    public DbSet<JobExecutionEntity> JobExecutions { get; }
    public DbSet<StepExecutionEntity> StepExecutions { get; }
    public DbSet<JobLockEntity> JobLocks { get; }
    public DbSet<DeadLetterEntryEntity> DeadLetterEntries { get; }
}
```

Tables are prefixed `batch_`. Run `dotnet ef database update` (or `Database.MigrateAsync()`) after registering your provider to create the schema.

## EfCoreItemReader\<TContext, TEntity, TKey\>

An `IItemReader<TEntity>` that also implements `IItemStream`, reading rows from an EF Core query with restartable, keyset-based pagination.

```csharp
public sealed class EfCoreItemReader<TContext, TEntity, TKey> : IItemReader<TEntity>, IItemStream
    where TContext : DbContext
    where TEntity : class
    where TKey : IComparable<TKey>
{
    public EfCoreItemReader(
        IDbContextFactory<TContext> contextFactory,
        Func<TContext, IQueryable<TEntity>> queryBuilder,
        Func<TEntity, TKey> keySelector,
        string contextKey = "EfCoreItemReader.lastKey");
}
```

| Parameter | Description |
|---|---|
| `contextFactory` | Factory used to create a fresh `TContext` per page of results. |
| `queryBuilder` | Builds the base query against `TContext` (filters, includes, ordering). |
| `keySelector` | Selects the key `EfCoreItemReader` uses for keyset pagination and checkpointing. |
| `contextKey` | The execution-context key the current position is checkpointed under. |

## EfCoreItemWriter\<TContext, TEntity\>

```csharp
public sealed class EfCoreItemWriter<TContext, TEntity> : IItemWriter<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    public EfCoreItemWriter(
        IDbContextFactory<TContext> contextFactory,
        bool clearChangeTrackerAfterChunk = true);
}
```

| Parameter | Description |
|---|---|
| `contextFactory` | Factory used to create a `TContext` for writing each chunk. |
| `clearChangeTrackerAfterChunk` | When `true` (default), clears the change tracker after each chunk is saved to keep memory bounded on long-running steps. |

## EfCoreDeadLetterWriter

An `IDeadLetterWriter` that persists dead-lettered items to the database. See [Dead-Lettering](/guide/dead-lettering).

```csharp
public sealed class EfCoreDeadLetterWriter : IDeadLetterWriter
{
    public EfCoreDeadLetterWriter(ConveyorBatchDbContext dbContext);
}
```

| Parameter | Description |
|---|---|
| `dbContext` | The `ConveyorBatchDbContext` whose `DeadLetterEntries` table entries are written to. |
