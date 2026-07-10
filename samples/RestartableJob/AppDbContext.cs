using Microsoft.EntityFrameworkCore;

namespace RestartableJob;

/// <summary>SQLite context owning the sample's own <see cref="OutputRecord"/> table.</summary>
sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<OutputRecord> OutputRecords => Set<OutputRecord>();
}
