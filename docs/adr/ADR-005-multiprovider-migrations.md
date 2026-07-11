# ADR-005: Separate Migrations Assemblies per EF Core Provider

**Status:** Accepted
**Date:** 2026-07-11

## Context

ADR-002 established EF Core as the job repository persistence mechanism and named PostgreSQL,
SQL Server, and SQLite as the target providers, but only SQLite migrations were ever scaffolded.
Every migration under `Conveyor.Batch.EntityFrameworkCore/Migrations/` was generated with the
SQLite provider active, so their `Up()` methods hard-code SQLite type strings
(`type: "INTEGER"`, `type: "TEXT"`) and a SQLite-only `Sqlite:Autoincrement` annotation. Applying
these migrations verbatim against a different provider is broken, not just suboptimal:

- **SQL Server** rejects a unique index over a column typed `TEXT` (the `AddJobLocks` migration
  creates exactly that on `ParametersJson`), so `Database.MigrateAsync()` throws.
- **PostgreSQL** accepts `INTEGER`/`TEXT` as valid type names, but every `long` primary/foreign
  key ends up a 32-bit `integer` with no identity/serial generator (the `Sqlite:Autoincrement`
  annotation has no Postgres equivalent), so any insert relying on a DB-generated `Id` fails a
  NOT NULL constraint.

The same `ConveyorBatchDbContext` and model need to support genuinely different, provider-native
migrations without conflating them.

## Decision

Ship each non-SQLite provider's migrations in its own package, built as a small class library
that references `Conveyor.Batch.EntityFrameworkCore` and the provider's EF Core package:

- `Conveyor.Batch.EntityFrameworkCore.Migrations.Npgsql`
- `Conveyor.Batch.EntityFrameworkCore.Migrations.SqlServer`

Each package contains only a `Migrations/` folder (scaffolded via `dotnet ef migrations add`
against that provider) and a throwaway `IDesignTimeDbContextFactory` used solely for scaffolding.
Consumers select the matching migrations assembly when configuring their provider:

```csharp
services.AddDbContext<ConveyorBatchDbContext>(o =>
    o.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly("Conveyor.Batch.EntityFrameworkCore.Migrations.Npgsql")));
```

EF Core scopes migration discovery for a `DbContext` to whichever assembly `MigrationsAssembly()`
points at (or the context's own assembly, by default), so three independent `Up()`
implementations — SQLite (unchanged, in the core EF Core package), Postgres, and SQL Server — can
coexist for the same `ConveyorBatchDbContext` without collision.

SQLite keeps its existing migrations in `Conveyor.Batch.EntityFrameworkCore` itself with no
`MigrationsAssembly` override, so nothing changes for existing consumers of that provider.

## Rationale

- **Correctness over convenience** — a shared, hand-waved migration set silently produces wrong
  schema (undersized integer keys, no identity generation) or outright fails (SQL Server's TEXT
  index restriction). Provider-native migrations, scaffolded per provider, avoid both.
- **No forced new dependencies for SQLite users** — `Conveyor.Batch.EntityFrameworkCore` does not
  gain a Postgres or SQL Server package reference; those only load if a consumer opts into the
  corresponding migrations package.
- **Standard EF Core pattern** — `MigrationsAssembly` is the documented mechanism for a single
  `DbContext` to support multiple providers with independent migration histories.

## Consequences

- Every future model change requires a matching migration added in **three** places: the core
  package (SQLite) and both new migrations packages (Postgres, SQL Server). This is a real
  maintenance cost that must be part of the review checklist for any entity/schema change.
- Two new shippable NuGet packages exist that are not yet listed in CLAUDE.md's "NuGet Package
  Structure" table; that table should be updated as a documentation follow-up.
- Consumers targeting Postgres or SQL Server must reference the matching migrations package and
  set `MigrationsAssembly` explicitly — this is a one-time setup step, documented in each
  package's usage.
