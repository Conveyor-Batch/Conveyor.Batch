# Dapper

`Conveyor.Batch.Dapper` provides a Dapper-backed item reader for pulling rows off a raw SQL query, as a lighter-weight alternative to `EfCoreItemReader` when you don't need (or want) EF Core in the pipeline. It ships one type: `DapperItemReader<T>`.

## Install

```bash
dotnet add package Conveyor.Batch.Dapper
```

## DapperItemReader\<T\>

Namespace `Conveyor.Batch.Dapper`.

```csharp
public sealed class DapperItemReader<T> : IItemReader<T>, IItemStream
{
    public DapperItemReader(
        Func<IDbConnection> connectionFactory,
        string sql,
        object? parameters = null,
        string contextKey = "DapperItemReader.offset",
        ILogger<DapperItemReader<T>>? logger = null);
}
```

| Parameter | Description |
|---|---|
| `connectionFactory` | Factory used to open a fresh `IDbConnection` for each page fetch. The reader opens and closes a connection from this factory once per page — no connection is held open while downstream processing consumes the page. |
| `sql` | The query to execute. Must be written to support offset pagination via an `@Offset` parameter (and typically `@PageSize`), which the reader supplies automatically on every fetch. |
| `parameters` | Optional additional parameters for `sql`, merged with the reader's `@Offset`/`@PageSize` values on each fetch. |
| `contextKey` | The execution-context key the current offset is checkpointed under. |
| `logger` | Optional logger used to report per-page fetch diagnostics. |

`DapperItemReader<T>` implements `IItemStream`, so it supports [restart checkpointing](/guide/restartability) out of the box: it saves the current offset into the step's `BatchExecutionContext` after every committed chunk, and resumes from it on restart.

### Writing the SQL query

The query must use offset-based pagination and a stable `ORDER BY` (typically an ascending sort on a primary or unique key) — otherwise rows can be skipped or repeated as the underlying table changes between page fetches. The exact pagination syntax is database-specific, for example:

```sql
-- SQL Server
SELECT * FROM Orders ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY

-- PostgreSQL / SQLite
SELECT * FROM Orders ORDER BY Id LIMIT @PageSize OFFSET @Offset
```

### Example

```csharp
using Conveyor.Batch.Dapper;
using Microsoft.Data.SqlClient;

var reader = new DapperItemReader<Order>(
    connectionFactory: () => new SqlConnection(connectionString),
    sql: "SELECT Id, Product, Amount FROM Orders ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");

var step = new StepBuilder<Order, ProcessedOrder>(repository)
    .Reader(reader)
    .Processor(new OrderProcessor())
    .Writer(writer)
    .ChunkSize(500)
    .Build("import-orders");
```

::: tip When to use
Use `DapperItemReader<T>` when you want to read from a database via a hand-written SQL query without pulling EF Core into the pipeline — for example, reading from a database your application doesn't otherwise use EF Core against, or when a raw query is significantly simpler or faster than an equivalent LINQ query. For writing processed items back to a database, or for the job repository itself, EF Core remains the only supported option today — see [Repository API](/api/repository).
:::
