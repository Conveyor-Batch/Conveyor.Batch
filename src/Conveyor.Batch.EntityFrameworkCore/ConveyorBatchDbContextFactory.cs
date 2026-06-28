using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// Design-time factory for ConveyorBatchDbContext, used by EF Core tools (dotnet ef migrations add).
/// </summary>
public sealed class ConveyorBatchDbContextFactory : IDesignTimeDbContextFactory<ConveyorBatchDbContext>
{
    /// <inheritdoc />
    public ConveyorBatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
        // Uses SQLite by default for design-time tooling; override at runtime via DI
        optionsBuilder.UseSqlite("Data Source=conveyor_batch_designtime.db");
        return new ConveyorBatchDbContext(optionsBuilder.Options);
    }
}
