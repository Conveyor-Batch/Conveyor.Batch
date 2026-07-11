using System.Data;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Dapper;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Conveyor.Batch.UnitTests.IO;

public sealed class DapperItemReaderTests : IDisposable
{
    private const string ContextKey = "DapperItemReader.offset";

    private readonly string _connectionString =
        $"Data Source=file:{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _anchor;

    public DapperItemReaderTests()
    {
        _anchor = new SqliteConnection(_connectionString);
        _anchor.Open();
        _anchor.Execute("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NOT NULL)");
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
    }

    private static StepExecutionContext NewContext() => new(new StepExecution { StepName = "test" });

    private void SeedItems(int count, string category = "A")
    {
        var rows = Enumerable.Range(1, count)
            .Select(i => new Item { Id = i, Name = $"item{i}", Category = category });
        _anchor.Execute("INSERT INTO Items (Id, Name, Category) VALUES (@Id, @Name, @Category)", rows);
    }

    private Func<IDbConnection> ConnectionFactory => () => new SqliteConnection(_connectionString);

    [Fact]
    public async Task ReadsAllRows_AcrossMultiplePages()
    {
        SeedItems(2500);

        var connectionsOpened = 0;
        Func<IDbConnection> countingFactory = () =>
        {
            connectionsOpened++;
            return ConnectionFactory();
        };

        var reader = new DapperItemReader<Item>(
            countingFactory,
            "SELECT Id, Name, Category FROM Items ORDER BY Id LIMIT @PageSize OFFSET @Offset");
        await reader.OpenAsync(new BatchExecutionContext(), CancellationToken.None);

        var items = new List<Item>();
        await foreach (var item in reader.ReadAsync(NewContext(), CancellationToken.None))
            items.Add(item);

        Assert.Equal(3, connectionsOpened);
        Assert.Equal(2500, items.Count);
        Assert.Equal(Enumerable.Range(1, 2500), items.Select(i => i.Id));
    }

    [Fact]
    public async Task EmptyTable_YieldsNoItems()
    {
        var reader = new DapperItemReader<Item>(
            ConnectionFactory,
            "SELECT Id, Name, Category FROM Items ORDER BY Id LIMIT @PageSize OFFSET @Offset");
        await reader.OpenAsync(new BatchExecutionContext(), CancellationToken.None);

        var items = new List<Item>();
        await foreach (var item in reader.ReadAsync(NewContext(), CancellationToken.None))
            items.Add(item);

        Assert.Empty(items);
    }

    [Fact]
    public async Task Restart_OpenAsyncWithSavedOffset_SkipsAlreadyConsumedRows()
    {
        SeedItems(2500);

        var executionContext = new BatchExecutionContext();
        executionContext.Put(ContextKey, 1000);

        var reader = new DapperItemReader<Item>(
            ConnectionFactory,
            "SELECT Id, Name, Category FROM Items ORDER BY Id LIMIT @PageSize OFFSET @Offset",
            contextKey: ContextKey);
        await reader.OpenAsync(executionContext, CancellationToken.None);

        var items = new List<Item>();
        await foreach (var item in reader.ReadAsync(NewContext(), CancellationToken.None))
            items.Add(item);

        Assert.Equal(1500, items.Count);
        Assert.Equal(Enumerable.Range(1001, 1500), items.Select(i => i.Id));
    }

    [Fact]
    public async Task UpdateAsync_PersistsOffset_AfterEachPage()
    {
        SeedItems(2500);

        var reader = new DapperItemReader<Item>(
            ConnectionFactory,
            "SELECT Id, Name, Category FROM Items ORDER BY Id LIMIT @PageSize OFFSET @Offset",
            contextKey: ContextKey);
        var executionContext = new BatchExecutionContext();
        await reader.OpenAsync(executionContext, CancellationToken.None);

        var consumed = 0;
        await foreach (var _ in reader.ReadAsync(NewContext(), CancellationToken.None))
        {
            consumed++;
            if (consumed == 1000)
                break;
        }
        await reader.UpdateAsync(executionContext, CancellationToken.None);

        Assert.Equal(1000, executionContext.Get<int>(ContextKey));
    }

    [Fact]
    public async Task ExtraParameters_PassThroughCorrectly()
    {
        SeedItems(1500, category: "A");
        _anchor.Execute(
            "INSERT INTO Items (Id, Name, Category) VALUES (@Id, @Name, @Category)",
            Enumerable.Range(1501, 500).Select(i => new Item { Id = i, Name = $"item{i}", Category = "B" }));

        var reader = new DapperItemReader<Item>(
            ConnectionFactory,
            "SELECT Id, Name, Category FROM Items WHERE Category = @Category ORDER BY Id LIMIT @PageSize OFFSET @Offset",
            parameters: new { Category = "B" });
        await reader.OpenAsync(new BatchExecutionContext(), CancellationToken.None);

        var items = new List<Item>();
        await foreach (var item in reader.ReadAsync(NewContext(), CancellationToken.None))
            items.Add(item);

        Assert.Equal(500, items.Count);
        Assert.All(items, i => Assert.Equal("B", i.Category));
        Assert.Equal(Enumerable.Range(1501, 500), items.Select(i => i.Id));
    }

    public void Dispose() => _anchor.Dispose();
}
