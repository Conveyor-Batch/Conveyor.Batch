using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Benchmarks;

/// <summary>BenchmarkDotNet comparison of sequential vs. concurrent chunk engine throughput.</summary>
[MemoryDiagnoser]
public class ConcurrentChunkEngineBenchmarks
{
    [Params(10_000, 100_000)]
    public int ItemCount { get; set; }

    [Params(100)]
    public int ChunkSize { get; set; }

    [Params(2, 4, 8)]
    public int MaxDegreeOfParallelism { get; set; }

    private ChunkOrientedEngine<int, int> _sequentialEngine = null!;
    private ConcurrentChunkOrientedEngine<int, int> _concurrentEngine = null!;
    private StepExecutionContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sequentialEngine = new ChunkOrientedEngine<int, int>(
            new RangeReader(ItemCount),
            new PassThroughProcessor<int>(),
            new NullWriter(),
            ChunkSize);

        _concurrentEngine = new ConcurrentChunkOrientedEngine<int, int>(
            new RangeReader(ItemCount),
            new PassThroughProcessor<int>(),
            new NullWriter(),
            ChunkSize,
            MaxDegreeOfParallelism);

        _context = new StepExecutionContext(new StepExecution
        {
            StepName = "benchmark",
            JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "bench-job" } }
        });
    }

    [Benchmark(Baseline = true)]
    public async Task Sequential()
    {
        await _sequentialEngine.ExecuteAsync(_context, CancellationToken.None);
    }

    [Benchmark]
    public async Task Concurrent()
    {
        await _concurrentEngine.ExecuteAsync(_context, CancellationToken.None);
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

    private sealed class PassThroughProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            new(item);
    }

    private sealed class NullWriter : IItemWriter<int>
    {
        public ValueTask WriteAsync(IReadOnlyList<int> items, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }
}
