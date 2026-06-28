# ADR-002: EF Core as the Job Repository Persistence Mechanism

**Status:** Accepted  
**Date:** 2026-06-28

## Context

The job repository must persist execution state (job instances, executions, step executions) so that jobs can be restarted after failure. Several persistence strategies were evaluated:
- Raw ADO.NET with hand-written SQL
- Dapper (micro-ORM)
- EF Core (full ORM)

## Decision

Use EF Core 8+ as the persistence mechanism for `Conveyor.Batch.EntityFrameworkCore`, a separate optional package.

## Rationale

- **Portability** — EF Core supports PostgreSQL, SQL Server, SQLite, and others via provider packages; one implementation serves all.
- **Migration support** — EF Core Migrations manage schema evolution without hand-written DDL, which is critical for a framework where the schema is owned by the library.
- **Ecosystem fit** — Most .NET applications already depend on EF Core; adding this package costs nothing in terms of new transitive dependencies.
- **Separation of concerns** — The core `Conveyor.Batch` package has zero persistence dependencies; EF Core is an opt-in add-on.

## Consequences

- `InMemoryJobRepository` remains in the core package for zero-dependency testing.
- The EF Core package targets the same multi-TFM as the core (`net8.0;net9.0`).
- Consumer applications must call `AddDbContext<ConveyorBatchDbContext>()` and run `dotnet ef database update`.
- Schema changes require a migration; the library ships a `ModelSnapshot` and migration history.
