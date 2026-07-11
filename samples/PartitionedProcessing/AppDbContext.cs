using Microsoft.EntityFrameworkCore;

namespace PartitionedProcessing;

/// <summary>SQLite context owning the sample's <see cref="SourceItem"/> and <see cref="ProcessedItem"/> tables.</summary>
sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SourceItem> SourceItems => Set<SourceItem>();
    public DbSet<ProcessedItem> ProcessedItems => Set<ProcessedItem>();
}
