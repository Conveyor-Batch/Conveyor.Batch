using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.IntegrationTests.Fixtures;

/// <summary>
/// Abstraction over a Testcontainers-backed database engine shared across an xunit collection,
/// letting tests create an isolated throwaway database per test and configure a
/// <see cref="ConveyorBatchDbContext"/> to point at it.
/// </summary>
public interface ITestDatabaseFixture
{
    /// <summary>
    /// Creates a brand-new, empty database on the shared container and returns a connection
    /// string pointed at it.
    /// </summary>
    Task<string> CreateFreshDatabaseAsync();

    /// <summary>
    /// Configures <paramref name="builder"/> to use this fixture's provider (and its dedicated
    /// migrations assembly) against <paramref name="connectionString"/>.
    /// </summary>
    void ConfigureProvider(DbContextOptionsBuilder builder, string connectionString);
}
