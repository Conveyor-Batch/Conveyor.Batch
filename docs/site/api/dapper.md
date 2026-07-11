# Dapper

::: warning Not yet implemented
Dapper support is on the roadmap but has not shipped yet — there is no `Conveyor.Batch.Dapper` package and no `DapperItemReader` type in the current release. This page exists as a placeholder so the API reference doesn't have a dead link.
:::

Today, `Conveyor.Batch.EntityFrameworkCore` is the only persistence package, and it uses EF Core for the job repository and for item readers/writers (see [Repository API](/api/repository) and [ADR-002](/adr/002) for the rationale). A lighter-weight, Dapper-based repository and item reader/writer implementation is a candidate for a future `Conveyor.Batch.Dapper` package, for teams that want raw-SQL performance without pulling in EF Core.

Check the [ADRs](/adr/) and the project's GitHub repository for the current status of this package.
