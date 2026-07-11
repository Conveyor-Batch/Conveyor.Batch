using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Conveyor.Batch.EntityFrameworkCore.Migrations.Npgsql;

/// <summary>
/// Design-time factory used only by <c>dotnet ef migrations add</c> to scaffold PostgreSQL
/// migrations for <see cref="ConveyorBatchDbContext"/> against this package's own migrations
/// assembly. Never opens a real connection.
/// </summary>
public sealed class NpgsqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConveyorBatchDbContext>
{
    /// <inheritdoc />
    public ConveyorBatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=conveyor_batch_designtime;Username=designtime;Password=designtime",
            npgsql => npgsql.MigrationsAssembly(typeof(NpgsqlDesignTimeDbContextFactory).Assembly.GetName().Name));
        return new ConveyorBatchDbContext(optionsBuilder.Options);
    }
}
