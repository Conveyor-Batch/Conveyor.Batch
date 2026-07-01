using Conveyor.Batch.Core.Job;
using Conveyor.Batch.EntityFrameworkCore;
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
