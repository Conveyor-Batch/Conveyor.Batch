using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Conveyor.Batch.EntityFrameworkCore.Migrations.SqlServer;

/// <summary>
/// Design-time factory used only by <c>dotnet ef migrations add</c> to scaffold SQL Server
/// migrations for <see cref="ConveyorBatchDbContext"/> against this package's own migrations
/// assembly. Never opens a real connection.
/// </summary>
public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConveyorBatchDbContext>
{
    /// <inheritdoc />
    public ConveyorBatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=conveyor_batch_designtime;User Id=designtime;Password=designtime;TrustServerCertificate=true",
            sqlServer => sqlServer.MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.GetName().Name));
        return new ConveyorBatchDbContext(optionsBuilder.Options);
    }
}
