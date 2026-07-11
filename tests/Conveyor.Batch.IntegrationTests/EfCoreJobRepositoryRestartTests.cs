using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.Policies;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.IntegrationTests;

/// <summary>
/// Integration tests for the checkpoint (<c>ExecutionContext</c>) persistence path added to
/// <see cref="EfCoreJobRepository"/> for restartability. This is the first test coverage
/// <see cref="EfCoreJobRepository"/> has in this repo, so it is deliberately scoped to just the
/// new checkpoint round-trip rather than a general repository test suite.
/// </summary>
public sealed class EfCoreJobRepositoryRestartTests : IDisposable
{
    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class ThrowOnValueProcessor<T>(T throwOn) : IItemProcessor<T, T> where T : IEquatable<T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct)
        {
            if (item.Equals(throwOn))
                throw new InvalidOperationException($"simulated failure on {item}");
            return ValueTask.FromResult<T?>(item);
        }
    }

    private sealed class CountingWriter<T> : IItemWriter<T>
    {
        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    private sealed class AlwaysSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => true;
    }

    private readonly string _dbPath;
    private readonly ConveyorBatchDbContext _dbContext;
    private readonly EfCoreJobRepository _repository;

    public EfCoreJobRepositoryRestartTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"conveyor_batch_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<ConveyorBatchDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbContext = new ConveyorBatchDbContext(options);
        _dbContext.Database.Migrate();
        _repository = new EfCoreJobRepository(_dbContext);
    }

    [Fact]
    public async Task Checkpoint_RoundTripsThroughDatabase()
    {
        var instance = await _repository.CreateJobInstanceAsync("job", JobParameters.Empty);
        var jobExecution = await _repository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        var stepExecution = await _repository.CreateStepExecutionAsync(jobExecution, "step");

        stepExecution.ExecutionContext.Put("FlatFileItemReader.currentLine", 42);
        stepExecution.ExecutionContext.Put("someKey", "someValue");
        await _repository.UpdateStepExecutionAsync(stepExecution);

        var reloaded = await _repository.GetLastStepExecutionAsync(jobExecution.Id, "step");

        Assert.NotNull(reloaded);
        Assert.Equal(42, reloaded.ExecutionContext.Get<int>("FlatFileItemReader.currentLine"));
        Assert.Equal("someValue", reloaded.ExecutionContext.Get<string>("someKey"));
    }

    [Fact]
    public async Task Counters_RoundTripThroughDatabase_AfterRealStepExecution()
    {
        // Item 3 is skipped (via AlwaysSkipPolicy), the rest are read and written — exercises
        // ReadCount/WriteCount/SkipCount through the real chunk engine rather than setting them
        // directly, so this also guards the ToStepExecution mapping used by GetLastStepExecutionAsync.
        var reader = new ListReader<int>([1, 2, 3, 4, 5]);
        var processor = new ThrowOnValueProcessor<int>(throwOn: 3);
        var writer = new CountingWriter<int>();

        var step = new StepBuilder<int, int>(_repository)
            .Reader(reader)
            .Processor(processor)
            .Writer(writer)
            .ChunkSize(10)
            .SkipPolicy(new AlwaysSkipPolicy())
            .Build("counting-step");

        var instance = await _repository.CreateJobInstanceAsync("job", JobParameters.Empty);
        var jobExecution = await _repository.CreateJobExecutionAsync(instance, JobParameters.Empty);

        var stepExecution = await step.ExecuteAsync(jobExecution, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, stepExecution.Status);
        Assert.Equal(5, stepExecution.ReadCount);
        Assert.Equal(4, stepExecution.WriteCount);
        Assert.Equal(1, stepExecution.SkipCount);

        var reloaded = await _repository.GetLastStepExecutionAsync(jobExecution.Id, "counting-step");

        Assert.NotNull(reloaded);
        Assert.Equal(stepExecution.ReadCount, reloaded.ReadCount);
        Assert.Equal(stepExecution.WriteCount, reloaded.WriteCount);
        Assert.Equal(stepExecution.SkipCount, reloaded.SkipCount);
    }

    [Fact]
    public async Task GetLastStepExecutionAsync_NoMatch_ReturnsNull()
    {
        var instance = await _repository.CreateJobInstanceAsync("job", JobParameters.Empty);
        var jobExecution = await _repository.CreateJobExecutionAsync(instance, JobParameters.Empty);

        var result = await _repository.GetLastStepExecutionAsync(jobExecution.Id, "nonexistent-step");

        Assert.Null(result);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
