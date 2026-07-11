using Microsoft.EntityFrameworkCore;

namespace CsvToDatabase;

/// <summary>SQLite context owning the sample's own <see cref="ProcessedOrder"/> table.</summary>
sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedOrder> ProcessedOrders => Set<ProcessedOrder>();
}
