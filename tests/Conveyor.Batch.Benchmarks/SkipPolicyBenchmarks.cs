using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.Benchmarks;

/// <summary>BenchmarkDotNet comparison of chunk engine throughput with and without a skip policy.</summary>
[MemoryDiagnoser]
public class SkipPolicyBenchmarks
{
    [Params(10_000)]
    public int ItemCount { get; set; }

    [Params(100)]
    public int ChunkSize { get; set; }

    /// <summary>Number of items expected to be skipped: 0 = no skips, 100 = 1% skip rate, 500 = 5% skip rate.</summary>
    [Params(0, 100, 500)]
    public int SkippableItemCount { get; set; }

    private ChunkOrientedEngine<int, int> _noSkipEngine = null!;
    private ChunkOrientedEngine<int, int> _withSkipEngine = null!;
    private StepExecutionContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _noSkipEngine = new ChunkOrientedEngine<int, int>(
            new RangeReader(ItemCount),
            new PassThroughProcessor(),
            new NullWriter(),
            ChunkSize,
            skipPolicy: null);

        var skipPolicy = new ExceptionClassifierSkipPolicy(
            new ExceptionClassifier().AddSkippable<InvalidOperationException>());

        _withSkipEngine = new ChunkOrientedEngine<int, int>(
            new RangeReader(ItemCount),
            new ThrowingProcessor(ItemCount, SkippableItemCount),
            new NullWriter(),
            ChunkSize,
            skipPolicy: skipPolicy);

        _context = new StepExecutionContext(new StepExecution
        {
            StepName = "benchmark",
            JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "bench-job" } }
        });
    }

    [Benchmark(Baseline = true)]
    public async Task NoSkipPolicy()
    {
        await _noSkipEngine.ExecuteAsync(_context, CancellationToken.None);
    }

    [Benchmark]
    public async Task WithSkipPolicy()
    {
        await _withSkipEngine.ExecuteAsync(_context, CancellationToken.None);
    }

    private sealed class RangeReader(int count) : IItemReader<int>
    {
        public async IAsyncEnumerable<int> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return i;
                await Task.CompletedTask;
            }
        }
    }

    private sealed class PassThroughProcessor : IItemProcessor<int, int>
    {
        public ValueTask<int> ProcessAsync(int item, StepExecutionContext ctx, CancellationToken ct) =>
            new(item);
    }

    /// <summary>Throws on every Nth item (N = itemCount / skippableItemCount) to hit the skip path.</summary>
    private sealed class ThrowingProcessor(int itemCount, int skippableItemCount) : IItemProcessor<int, int>
    {
        private readonly int _throwEveryNth = skippableItemCount > 0 ? itemCount / skippableItemCount : 0;

        public ValueTask<int> ProcessAsync(int item, StepExecutionContext ctx, CancellationToken ct)
        {
            if (_throwEveryNth > 0 && (item + 1) % _throwEveryNth == 0)
                throw new InvalidOperationException($"Simulated failure for item {item}.");

            return new ValueTask<int>(item);
        }
    }

    private sealed class NullWriter : IItemWriter<int>
    {
        public ValueTask WriteAsync(IReadOnlyList<int> items, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }
}
