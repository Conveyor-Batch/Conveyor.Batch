using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Partitioning;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IntegrationTests;

public sealed class PartitionedPipelineTests
{
    // ── Fakes ──────────────────────────────────────────────────────────

    /// <summary>Reads the inclusive integer range assigned to this partition via its execution context.</summary>
    private sealed class RangeAwareReader : IItemReader<int>
    {
        public async IAsyncEnumerable<int> ReadAsync(
            StepExecutionContext ctx,
            [EnumeratorCancellation] CancellationToken ct)
        {
            long min = ctx.StepExecution.JobExecution.ExecutionContext.Get<long>("partition.minValue");
            long max = ctx.StepExecution.JobExecution.ExecutionContext.Get<long>("partition.maxValue");

            for (long i = min; i <= max; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return (int)i;
            }
        }
    }

    private sealed class IdentityProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult<T?>(item);
    }

    private sealed class NoOpWriter<T> : IItemWriter<T>
    {
        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RangePartitioned_1To100_GridSize4_TotalWriteCountIs100()
    {
        var repository = new InMemoryJobRepository();
        var workerStep = new StepBuilder<int, int>(repository)
            .Reader(new RangeAwareReader())
            .Processor(new IdentityProcessor<int>())
            .Writer(new NoOpWriter<int>())
            .ChunkSize(10)
            .Build("range-worker");

        var partitioner = new RangePartitioner(1, 100);
        var handler = new LocalPartitionHandler(repository, maxDegreeOfParallelism: 4);

        var instance = await repository.CreateJobInstanceAsync("partitioned-job", JobParameters.Empty);
        var jobExecution = await repository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        var managerExecution = await repository.CreateStepExecutionAsync(jobExecution, "manager");

        IReadOnlyDictionary<string, BatchExecutionContext> partitions =
            new Dictionary<string, BatchExecutionContext>(partitioner.Partition(4));

        var results = await handler.HandleAsync(workerStep, managerExecution, partitions, CancellationToken.None);

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Equal(BatchStatus.Completed, r.Status));
        Assert.Equal(100, results.Sum(r => r.WriteCount));
    }

    [Fact]
    public async Task RangePartitioned_FullJobPipeline_CompletesSuccessfully()
    {
        var repository = new InMemoryJobRepository();
        var workerStep = new StepBuilder<int, int>(repository)
            .Reader(new RangeAwareReader())
            .Processor(new IdentityProcessor<int>())
            .Writer(new NoOpWriter<int>())
            .ChunkSize(10)
            .Build("range-worker");

        var partitionStep = new PartitionStepBuilder(repository)
            .Worker(workerStep)
            .Partitioner(new RangePartitioner(1, 100))
            .GridSize(4)
            .MaxDegreeOfParallelism(4)
            .Build("partitioned-range-step");

        var job = new JobBuilder("partitioned-job", repository).AddStep(partitionStep).Build();
        var jobExecution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, jobExecution.Status);
    }
}
