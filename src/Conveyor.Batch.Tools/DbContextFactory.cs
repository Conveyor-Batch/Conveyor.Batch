using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace Conveyor.Batch.Tools;

/// <summary>
/// Owns a <see cref="ConveyorBatchDbContext"/> together with any ADO.NET connection the factory
/// had to open and keep alive itself (only needed for SQLite — see <see cref="DbContextFactory"/>).
/// </summary>
internal sealed record DbContextHandle(ConveyorBatchDbContext Context, SqliteConnection? OwnedConnection) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync().ConfigureAwait(false);

        if (OwnedConnection is not null)
            await OwnedConnection.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Builds a <see cref="ConveyorBatchDbContext"/> directly from a connection string and provider
/// name, with no host or DI container involved.
/// </summary>
internal static class DbContextFactory
{
    /// <summary>
    /// Attempts to build and initialize a <see cref="ConveyorBatchDbContext"/> for the given
    /// provider. Prints a formatted error and returns <see langword="null"/> on any failure
    /// (unreachable server, bad connection string, unknown provider) so callers can uniformly
    /// exit with code 1.
    /// </summary>
    internal static async Task<DbContextHandle?> TryCreateAsync(
        string connectionString,
        string provider,
        CancellationToken cancellationToken)
    {
        SqliteConnection? ownedConnection = null;

        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
            var isSqlite = false;

            switch (provider.ToLowerInvariant())
            {
                case "postgres":
                    optionsBuilder.UseNpgsql(connectionString);
                    break;

                case "sqlserver":
                    optionsBuilder.UseSqlServer(connectionString);
                    break;

                case "sqlite":
                    // A "Data Source=:memory:" connection only exists for as long as a single
                    // ADO.NET connection to it stays open, so we own and hold that connection
                    // open for the lifetime of the returned handle instead of letting EF Core
                    // open/close an implicit connection per operation.
                    ownedConnection = new SqliteConnection(connectionString);
                    await ownedConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    optionsBuilder.UseSqlite(ownedConnection);
                    isSqlite = true;
                    break;

                default:
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]Error:[/] Unknown provider '{provider}'. Expected 'postgres', 'sqlserver', or 'sqlite'.");
                    return null;
            }

            var context = new ConveyorBatchDbContext(optionsBuilder.Options);

            // Only SQLite needs EnsureCreated here (a fresh :memory: or file db has no schema
            // yet). For Postgres/SQL Server, schema is expected to already exist via the
            // dedicated Migrations projects (see ADR-005) — silently EnsureCreated-ing a
            // genuinely empty production/staging database would mask a misconfigured
            // --connection as "no jobs found" instead of a clear error, and would create a
            // schema outside of migration tracking.
            if (isSqlite)
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            return new DbContextHandle(context, ownedConnection);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");

            if (ownedConnection is not null)
                await ownedConnection.DisposeAsync().ConfigureAwait(false);

            return null;
        }
    }
}
