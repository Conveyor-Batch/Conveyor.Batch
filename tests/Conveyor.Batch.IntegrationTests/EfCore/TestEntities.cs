using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.IntegrationTests.EfCore;

/// <summary>Minimal source entity used by the EF Core reader/writer integration tests.</summary>
public sealed class TestItem
{
    public long Id { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>Same shape as <see cref="TestItem"/>, mapped to a separate table, used as a write destination.</summary>
public sealed class ProcessedItem
{
    public long Id { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>Test-only application <see cref="DbContext"/> (not <c>ConveyorBatchDbContext</c>) exercising a generic <c>TContext</c>.</summary>
public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<TestItem> TestItems => Set<TestItem>();

    public DbSet<ProcessedItem> ProcessedItems => Set<ProcessedItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TestItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<ProcessedItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });
    }
}

/// <summary>
/// Wraps a real <see cref="IDbContextFactory{TContext}"/> and counts how many contexts it has
/// created, so tests can prove multiple pages really do use fresh contexts even though the
/// reader's internal page size is a private implementation detail.
/// </summary>
public sealed class CountingDbContextFactory : IDbContextFactory<TestDbContext>
{
    private readonly IDbContextFactory<TestDbContext> _inner;
    private int _createdCount;

    public CountingDbContextFactory(IDbContextFactory<TestDbContext> inner)
    {
        _inner = inner;
    }

    public int CreatedCount => _createdCount;

    public TestDbContext CreateDbContext()
    {
        Interlocked.Increment(ref _createdCount);
        return _inner.CreateDbContext();
    }
}

/// <summary>An <see cref="IItemReader{T}"/> over an in-memory sequence, for tests that isolate writer behavior.</summary>
public sealed class InMemoryItemReader<T> : IItemReader<T>
{
    private readonly IReadOnlyList<T> _items;

    public InMemoryItemReader(IReadOnlyList<T> items)
    {
        _items = items;
    }

    public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}

/// <summary>An <see cref="IItemProcessor{T,T}"/> that passes items through unchanged.</summary>
public sealed class IdentityProcessor<T> : IItemProcessor<T, T>
{
    public ValueTask<T?> ProcessAsync(T item, StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<T?>(item);
}

/// <summary>Maps a <see cref="TestItem"/> to a <see cref="ProcessedItem"/> with the same key/value.</summary>
public sealed class TestItemToProcessedItemProcessor : IItemProcessor<TestItem, ProcessedItem>
{
    public ValueTask<ProcessedItem?> ProcessAsync(TestItem item, StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ProcessedItem?>(new ProcessedItem { Id = item.Id, Value = item.Value });
}

/// <summary>An <see cref="IItemWriter{T}"/> that records each committed chunk in memory instead of persisting it.</summary>
public sealed class RecordingItemWriter<T> : IItemWriter<T>
{
    private readonly List<IReadOnlyList<T>> _chunks = new();

    public IReadOnlyList<IReadOnlyList<T>> Chunks => _chunks;

    public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        _chunks.Add(items.ToList());
        return ValueTask.CompletedTask;
    }
}
