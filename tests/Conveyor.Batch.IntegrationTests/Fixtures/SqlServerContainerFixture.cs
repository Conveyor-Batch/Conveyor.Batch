using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Conveyor.Batch.IntegrationTests.Fixtures;

/// <summary>
/// Owns a single SQL Server Testcontainers container shared across every test class in the
/// "SqlServer" xunit collection - started once per test assembly run, not once per test class.
/// Each test gets its own throwaway database on that shared container via
/// <see cref="CreateFreshDatabaseAsync"/>.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime, ITestDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <inheritdoc />
    public async Task<string> CreateFreshDatabaseAsync()
    {
        var databaseName = $"conveyor_{Guid.NewGuid():N}";

        await using var adminConnection = new SqlConnection(_container.GetConnectionString());
        await adminConnection.OpenAsync();
        await using (var command = adminConnection.CreateCommand())
        {
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }

    /// <inheritdoc />
    public void ConfigureProvider(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, sqlServer =>
            sqlServer.MigrationsAssembly("Conveyor.Batch.EntityFrameworkCore.Migrations.SqlServer"));
}
