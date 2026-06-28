using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Benchmarks;

/// <summary>BenchmarkDotNet baseline for chunk engine throughput.</summary>
[MemoryDiagnoser]
public class ChunkEngineBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int ItemCount { get; set; }

    [Params(10, 100, 1_000)]
    public int ChunkSize { get; set; }

    private ChunkOrientedEngine<int, int> _engine = null!;
    private StepExecutionContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new ChunkOrientedEngine<int, int>(
            new RangeReader(ItemCount),
            new PassThroughProcessor<int>(),
            new NullWriter(),
            ChunkSize);

        _context = new StepExecutionContext(new StepExecution
        {
            StepName = "benchmark",
            JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "bench-job" } }
        });
    }

    [Benchmark]
    public async Task ChunkEngine()
    {
        await _engine.ExecuteAsync(_context, CancellationToken.None);
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
