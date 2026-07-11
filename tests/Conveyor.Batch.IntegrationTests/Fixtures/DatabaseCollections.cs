namespace Conveyor.Batch.IntegrationTests.Fixtures;

/// <summary>
/// xunit collection sharing one <see cref="PostgresContainerFixture"/> (and therefore one
/// PostgreSQL container) across every test class tagged <c>[Collection(Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(PostgresCollection.Name)]</c>.</summary>
    public const string Name = "Postgres";
}

/// <summary>
/// xunit collection sharing one <see cref="SqlServerContainerFixture"/> (and therefore one
/// SQL Server container) across every test class tagged <c>[Collection(Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(SqlServerCollection.Name)]</c>.</summary>
    public const string Name = "SqlServer";
}
