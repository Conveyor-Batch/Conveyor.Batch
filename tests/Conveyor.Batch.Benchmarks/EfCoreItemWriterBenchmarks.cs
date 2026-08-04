using BenchmarkDotNet.Attributes;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Conveyor.Batch.Benchmarks;

/// <summary>
/// Baseline before bulk write optimization. Do not delete — needed for regression comparison.
/// Measures <see cref="EfCoreItemWriter{TContext,TEntity}"/>'s per-chunk write cost against
/// SQLite in-memory, calling <c>WriteAsync</c> directly (no engine involved).
/// </summary>
[MemoryDiagnoser]
public class EfCoreItemWriterBenchmarks
{
    [Params(100, 1_000, 5_000)]
    public int ChunkSize { get; set; }

    private SqliteConnection _connection = null!;
    private IDbContextFactory<BenchmarkDbContext> _contextFactory = null!;
    private EfCoreItemWriter<BenchmarkDbContext, BenchmarkRow> _writer = null!;
    private StepExecutionContext _context = null!;
    private IReadOnlyList<BenchmarkRow> _items = null!;

    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite(_connection)
            .Options;

        _contextFactory = new PooledDbContextFactory<BenchmarkDbContext>(options);

        await using (var initContext = await _contextFactory.CreateDbContextAsync())
        {
            await initContext.Database.EnsureCreatedAsync();
        }

        _writer = new EfCoreItemWriter<BenchmarkDbContext, BenchmarkRow>(_contextFactory);

        _context = new StepExecutionContext(new StepExecution
        {
            StepName = "benchmark",
            JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "bench-job" } }
        });
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var context = _contextFactory.CreateDbContext();
        context.BenchmarkRows.ExecuteDelete();

        _items = Enumerable.Range(1, ChunkSize)
            .Select(i => new BenchmarkRow { Id = i, Name = $"item-{i}", Value = i * 1.5m })
            .ToList();
    }

    /// <summary>Baseline before bulk write optimization. Do not delete — needed for regression comparison.</summary>
    [Benchmark]
    public async Task WriteChunk()
    {
        await _writer.WriteAsync(_items, _context, CancellationToken.None);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _connection.Dispose();
    }

    private sealed class BenchmarkRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Value { get; set; }
    }

    private sealed class BenchmarkDbContext : DbContext
    {
        public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : base(options)
        {
        }

        public DbSet<BenchmarkRow> BenchmarkRows => Set<BenchmarkRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BenchmarkRow>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
            });
        }
    }
}
