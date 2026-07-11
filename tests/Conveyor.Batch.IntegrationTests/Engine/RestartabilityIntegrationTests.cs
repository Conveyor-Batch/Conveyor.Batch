using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.IntegrationTests.Fixtures;
using Conveyor.Batch.Testing;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.IntegrationTests.Engine;

/// <summary>
/// Proves that a checkpointing <see cref="IItemStream"/> reader can resume a
/// <see cref="ChunkOrientedEngine{TInput,TOutput}"/> run from its last committed chunk after a
/// failure, and that the checkpoint is persisted after every committed chunk (not only once at
/// the end) - both backed by a real PostgreSQL database via <see cref="EfCoreJobRepository"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class RestartabilityIntegrationTests
{
    private readonly PostgresContainerFixture _fixture;
    private string? _connectionString;

    public RestartabilityIntegrationTests(PostgresContainerFixture fixture) => _fixture = fixture;

    private async Task<ConveyorBatchDbContext> CreateContextAsync()
    {
        _connectionString ??= await _fixture.CreateFreshDatabaseAsync();

        var builder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
        _fixture.ConfigureProvider(builder, _connectionString);

        var context = new ConveyorBatchDbContext(builder.Options);
        await context.Database.MigrateAsync();
        return context;
    }

    [Fact]
    public async Task RestartableReader_ResumesFromCheckpoint_AfterFailure()
    {
        await using var context = await CreateContextAsync();
        var repository = new EfCoreJobRepository(context);
        var instance = await repository.CreateJobInstanceAsync("restart-engine-test", JobParameters.Empty);

        // Run 1: fails on item 11, one item past the second committed chunk (5,5).
        var jobExecution1 = await repository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        var stepExecution1 = await repository.CreateStepExecutionAsync(jobExecution1, "restart-step");
        var stepContext1 = new StepExecutionContext(stepExecution1);

        var reader1 = new RestartableCountingReader(Enumerable.Range(1, 20));
        var writer1 = new InMemoryItemWriter<int>();
        var engine1 = new ChunkOrientedEngine<int, int>(
            reader1,
            new ThrowOnValueProcessor(throwOnValue: 11),
            writer1,
            chunkSize: 5,
            jobRepository: repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine1.ExecuteAsync(stepContext1, CancellationToken.None));

        Assert.Equal(Enumerable.Range(1, 10), writer1.AllItems);
        Assert.Equal(0, stepExecution1.SkipCount);
        Assert.Equal(10, stepExecution1.WriteCount);

        // Run 2: resume from the persisted checkpoint. New reader, new writer, and a
        // non-throwing processor - the "the transient problem is now fixed" restart convention;
        // reusing the value-11-throws processor would fail identically on resume.
        var previousStepExecution = await repository.GetLastStepExecutionAsync(jobExecution1.Id, "restart-step");
        Assert.NotNull(previousStepExecution);

        var jobExecution2 = await repository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        var stepExecution2 = await repository.CreateStepExecutionAsync(jobExecution2, "restart-step");
        stepExecution2.ExecutionContext = BatchExecutionContext.FromDictionary(
            new Dictionary<string, string>(previousStepExecution.ExecutionContext.ToDictionary()));
        var stepContext2 = new StepExecutionContext(stepExecution2);

        var reader2 = new RestartableCountingReader(Enumerable.Range(1, 20));
        var writer2 = new InMemoryItemWriter<int>();
        var engine2 = new ChunkOrientedEngine<int, int>(
            reader2,
            new IdentityProcessor(),
            writer2,
            chunkSize: 5,
            jobRepository: repository);

        await engine2.ExecuteAsync(stepContext2, CancellationToken.None);

        Assert.Equal(Enumerable.Range(11, 10), writer2.AllItems);

        var combined = writer1.AllItems.Concat(writer2.AllItems).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(1, 20), combined);
        Assert.Equal(20, combined.Distinct().Count());
    }

    [Fact]
    public async Task Checkpoint_IsPersistedAfterEachChunk_NotOnlyAtEnd()
    {
        await using var context = await CreateContextAsync();
        var countingRepository = new CountingJobRepository(new EfCoreJobRepository(context));

        var instance = await countingRepository.CreateJobInstanceAsync("checkpoint-frequency-test", JobParameters.Empty);
        var jobExecution = await countingRepository.CreateJobExecutionAsync(instance, JobParameters.Empty);

        var writer = new InMemoryItemWriter<int>();
        var step = new StepBuilder<int, int>(countingRepository)
            .Reader(new RestartableCountingReader(Enumerable.Range(1, 10)))
            .Processor(new IdentityProcessor())
            .Writer(writer)
            .ChunkSize(3)
            .Build("checkpoint-frequency-step");

        var stepExecution = await step.ExecuteAsync(jobExecution, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, stepExecution.Status);
        Assert.Equal(4, writer.Chunks.Count); // 10 items / chunk 3 -> 3,3,3,1
        Assert.True(
            countingRepository.UpdateStepExecutionCallCount >= 3,
            $"Expected UpdateStepExecutionAsync to be called at least once per committed chunk, but it was called {countingRepository.UpdateStepExecutionCallCount} time(s).");
    }

    // ── Fakes ──────────────────────────────────────────────────────────

    /// <summary>Restart-aware reader over a fixed list of items, checkpointing its index.</summary>
    private sealed class RestartableCountingReader(IEnumerable<int> items) : IItemReader<int>, IItemStream
    {
        private const string IndexKey = "RestartableCountingReader.index";
        private readonly List<int> _items = items.ToList();
        private int _skip;
        private int _index;

        public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken)
        {
            _skip = context.Get<int>(IndexKey);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken)
        {
            context.Put(IndexKey, _index);
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<int> ReadAsync(StepExecutionContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _index = _skip;
            for (var i = _skip; i < _items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                _index = i + 1;
                yield return _items[i];
            }
        }
    }

    private sealed class IdentityProcessor : IItemProcessor<int, int>
    {
        public ValueTask<int> ProcessAsync(int item, StepExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(item);
    }

    private sealed class ThrowOnValueProcessor(int throwOnValue) : IItemProcessor<int, int>
    {
        public ValueTask<int> ProcessAsync(int item, StepExecutionContext context, CancellationToken cancellationToken)
        {
            if (item == throwOnValue)
                throw new InvalidOperationException($"simulated failure on item {item}");
            return ValueTask.FromResult(item);
        }
    }

    /// <summary>Decorator counting calls to <see cref="UpdateStepExecutionAsync"/>, delegating everything else to <paramref name="inner"/>.</summary>
    private sealed class CountingJobRepository(IJobRepository inner) : IJobRepository
    {
        private int _updateStepExecutionCallCount;

        public int UpdateStepExecutionCallCount => _updateStepExecutionCallCount;

        public Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters) =>
            inner.CreateJobInstanceAsync(jobName, parameters);

        public Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters) =>
            inner.CreateJobExecutionAsync(instance, parameters);

        public Task UpdateJobExecutionAsync(JobExecution execution) =>
            inner.UpdateJobExecutionAsync(execution);

        public Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName) =>
            inner.CreateStepExecutionAsync(jobExecution, stepName);

        public Task UpdateStepExecutionAsync(StepExecution stepExecution)
        {
            Interlocked.Increment(ref _updateStepExecutionCallCount);
            return inner.UpdateStepExecutionAsync(stepExecution);
        }

        public Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters) =>
            inner.GetLastJobExecutionAsync(jobName, parameters);

        public Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance) =>
            inner.GetJobExecutionsAsync(instance);

        public Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName) =>
            inner.GetLastStepExecutionAsync(jobExecutionId, stepName);

        public Task<JobExecution?> GetRunningJobExecutionAsync(string jobName, JobParameters parameters, CancellationToken cancellationToken = default) =>
            inner.GetRunningJobExecutionAsync(jobName, parameters, cancellationToken);
    }
}
