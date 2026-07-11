using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Conveyor.Batch.IntegrationTests.Fixtures;

/// <summary>
/// Owns a single PostgreSQL Testcontainers container shared across every test class in the
/// "Postgres" xunit collection - started once per test assembly run, not once per test class.
/// Each test gets its own throwaway database on that shared container via
/// <see cref="CreateFreshDatabaseAsync"/>.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime, ITestDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <inheritdoc />
    public async Task<string> CreateFreshDatabaseAsync()
    {
        var databaseName = $"conveyor_{Guid.NewGuid():N}";

        await using var adminConnection = new NpgsqlConnection(_container.GetConnectionString());
        await adminConnection.OpenAsync();
        await using (var command = adminConnection.CreateCommand())
        {
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    /// <inheritdoc />
    public void ConfigureProvider(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly("Conveyor.Batch.EntityFrameworkCore.Migrations.Npgsql"));
}
