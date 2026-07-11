using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.Hosting;
using Conveyor.Batch.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Conveyor.Batch.IntegrationTests.Repository;

/// <summary>
/// Shared test bodies for <see cref="EfCoreJobRepository"/>, run against both a real PostgreSQL
/// and a real SQL Server database (via Testcontainers) by
/// <see cref="EfCoreJobRepositoryPostgresTests"/> and <see cref="EfCoreJobRepositorySqlServerTests"/>.
/// Each test gets its own throwaway database on the shared container, created lazily on first use
/// and reused for any subsequent "reload from a new DbContext" step within that same test.
/// </summary>
public abstract class EfCoreJobRepositoryTestsBase
{
    private string? _connectionString;

    /// <summary>The Testcontainers-backed fixture for this provider.</summary>
    protected abstract ITestDatabaseFixture Fixture { get; }

    private async Task<ConveyorBatchDbContext> CreateContextAsync()
    {
        _connectionString ??= await Fixture.CreateFreshDatabaseAsync();

        var builder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
        Fixture.ConfigureProvider(builder, _connectionString);

        var context = new ConveyorBatchDbContext(builder.Options);
        await context.Database.MigrateAsync();
        return context;
    }

    private static JobParameters MakeParameters(params (string Key, string Value)[] pairs) =>
        new(pairs.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public async Task CreateJobInstance_Persists_AndIsRetrievable()
    {
        var parameters = MakeParameters(("a", "1"), ("b", "2"));

        await using var writeContext = await CreateContextAsync();
        var writeRepository = new EfCoreJobRepository(writeContext);
        var instance = await writeRepository.CreateJobInstanceAsync("job-instance-test", parameters);

        Assert.True(instance.Id > 0);

        await using var readContext = await CreateContextAsync();
        var entity = await readContext.JobInstances.FindAsync(instance.Id);

        Assert.NotNull(entity);
        Assert.Equal("job-instance-test", entity.JobName);

        var reloadedParameters = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ParametersJson)!;
        Assert.Equal(
            parameters.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal),
            reloadedParameters.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CreateJobExecution_Persists_AllFields()
    {
        var parameters = JobParameters.Empty;

        await using var writeContext = await CreateContextAsync();
        var writeRepository = new EfCoreJobRepository(writeContext);
        var instance = await writeRepository.CreateJobInstanceAsync("job-execution-test", parameters);
        var execution = await writeRepository.CreateJobExecutionAsync(instance, parameters);

        Assert.True(execution.Id > 0);

        await using var readContext = await CreateContextAsync();
        var readRepository = new EfCoreJobRepository(readContext);
        var reloaded = await readRepository.GetLastJobExecutionAsync("job-execution-test", parameters);

        Assert.NotNull(reloaded);
        Assert.Equal(BatchStatus.Starting, reloaded.Status);
        Assert.True(reloaded.StartTime <= DateTimeOffset.UtcNow);
        Assert.True(reloaded.StartTime >= DateTimeOffset.UtcNow.AddSeconds(-5));
        Assert.Null(reloaded.EndTime);
    }

    [Fact]
    public async Task UpdateJobExecution_Persists_StatusAndEndTime()
    {
        var parameters = JobParameters.Empty;

        await using var writeContext = await CreateContextAsync();
        var writeRepository = new EfCoreJobRepository(writeContext);
        var instance = await writeRepository.CreateJobInstanceAsync("job-update-test", parameters);
        var execution = await writeRepository.CreateJobExecutionAsync(instance, parameters);

        execution.Status = BatchStatus.Completed;
        execution.EndTime = DateTimeOffset.UtcNow;
        await writeRepository.UpdateJobExecutionAsync(execution);

        await using var readContext = await CreateContextAsync();
        var readRepository = new EfCoreJobRepository(readContext);
        var reloaded = await readRepository.GetLastJobExecutionAsync("job-update-test", parameters);

        Assert.NotNull(reloaded);
        Assert.Equal(BatchStatus.Completed, reloaded.Status);
        Assert.NotNull(reloaded.EndTime);
        Assert.True(Math.Abs((reloaded.EndTime!.Value - execution.EndTime!.Value).TotalSeconds) < 1);
    }

    [Fact]
    public async Task GetLastJobExecution_ReturnsLatest_ForSameParameters()
    {
        var parameters = MakeParameters(("run", "1"));

        await using var context = await CreateContextAsync();
        var repository = new EfCoreJobRepository(context);
        var instance = await repository.CreateJobInstanceAsync("latest-execution-test", parameters);

        JobExecution? highest = null;
        for (var i = 0; i < 3; i++)
            highest = await repository.CreateJobExecutionAsync(instance, parameters);

        var result = await repository.GetLastJobExecutionAsync("latest-execution-test", parameters);

        Assert.NotNull(result);
        Assert.NotNull(highest);
        Assert.Equal(highest!.Id, result.Id);
        Assert.True(result.Id >= highest.Id);
    }

    [Fact]
    public async Task GetLastJobExecution_ReturnsNull_ForDifferentParameters()
    {
        var paramsA = MakeParameters(("key", "A"));
        var paramsB = MakeParameters(("key", "B"));

        await using var context = await CreateContextAsync();
        var repository = new EfCoreJobRepository(context);
        var instance = await repository.CreateJobInstanceAsync("different-params-test", paramsA);
        await repository.CreateJobExecutionAsync(instance, paramsA);

        var result = await repository.GetLastJobExecutionAsync("different-params-test", paramsB);

        Assert.Null(result);
    }

    [Fact]
    public async Task JobParameters_Equality_IsOrderIndependent()
    {
        var creationOrderParameters = MakeParameters(("b", "2"), ("a", "1"));
        var queryOrderParameters = MakeParameters(("a", "1"), ("b", "2"));

        await using var context = await CreateContextAsync();
        var repository = new EfCoreJobRepository(context);
        var instance = await repository.CreateJobInstanceAsync("param-order-test", creationOrderParameters);
        var execution = await repository.CreateJobExecutionAsync(instance, creationOrderParameters);

        var result = await repository.GetLastJobExecutionAsync("param-order-test", queryOrderParameters);

        Assert.NotNull(result);
        Assert.Equal(execution.Id, result.Id);
    }

    [Fact]
    public async Task CreateStepExecution_Persists_AndLinksToJobExecution()
    {
        var parameters = JobParameters.Empty;

        await using var writeContext = await CreateContextAsync();
        var writeRepository = new EfCoreJobRepository(writeContext);
        var instance = await writeRepository.CreateJobInstanceAsync("step-link-test", parameters);
        var jobExecution = await writeRepository.CreateJobExecutionAsync(instance, parameters);
        var stepExecution = await writeRepository.CreateStepExecutionAsync(jobExecution, "the-step");

        await using var readContext = await CreateContextAsync();
        var entity = await readContext.StepExecutions.FindAsync(stepExecution.Id);

        Assert.NotNull(entity);
        Assert.Equal(jobExecution.Id, entity.JobExecutionId);
        Assert.Equal("the-step", entity.StepName);
    }

    [Fact]
    public async Task UpdateStepExecution_Persists_ExecutionContext()
    {
        var parameters = JobParameters.Empty;

        await using var writeContext = await CreateContextAsync();
        var writeRepository = new EfCoreJobRepository(writeContext);
        var instance = await writeRepository.CreateJobInstanceAsync("checkpoint-test", parameters);
        var jobExecution = await writeRepository.CreateJobExecutionAsync(instance, parameters);
        var stepExecution = await writeRepository.CreateStepExecutionAsync(jobExecution, "checkpoint-step");

        stepExecution.ExecutionContext.Put("offset", 42);
        await writeRepository.UpdateStepExecutionAsync(stepExecution);

        await using var readContext = await CreateContextAsync();
        var readRepository = new EfCoreJobRepository(readContext);
        var reloaded = await readRepository.GetLastStepExecutionAsync(jobExecution.Id, "checkpoint-step");

        Assert.NotNull(reloaded);
        Assert.Equal(42, reloaded.ExecutionContext.Get<int>("offset"));
    }

    [Fact]
    public async Task GetRunningJobExecution_ReturnsExecution_WhenStatusIsStarted()
    {
        var parameters = JobParameters.Empty;

        await using var context = await CreateContextAsync();
        var repository = new EfCoreJobRepository(context);
        var instance = await repository.CreateJobInstanceAsync("running-execution-test", parameters);
        var execution = await repository.CreateJobExecutionAsync(instance, parameters);

        execution.Status = BatchStatus.Started;
        await repository.UpdateJobExecutionAsync(execution);

        var running = await repository.GetRunningJobExecutionAsync("running-execution-test", parameters);
        Assert.NotNull(running);
        Assert.Equal(execution.Id, running.Id);

        execution.Status = BatchStatus.Completed;
        execution.EndTime = DateTimeOffset.UtcNow;
        await repository.UpdateJobExecutionAsync(execution);

        var noLongerRunning = await repository.GetRunningJobExecutionAsync("running-execution-test", parameters);
        Assert.Null(noLongerRunning);
    }

    [Fact]
    public async Task ConcurrentLaunch_SecondCall_Throws()
    {
        const string jobName = "concurrent-launch-test";
        var parameters = JobParameters.Empty;

        // Ensure the schema exists before either provider's launcher touches the database -
        // CreateContextAsync() both lazily creates the fresh database and migrates it.
        await using (await CreateContextAsync())
        {
        }

        await using var provider1 = BuildProvider();
        await using var provider2 = BuildProvider();

        var launcher1 = provider1.GetRequiredService<IJobLauncher>();
        var repository1 = provider1.GetRequiredService<IJobRepository>();
        var launcher2 = provider2.GetRequiredService<IJobLauncher>();

        var slowJob = new SlowJob(jobName, delayPerItem: TimeSpan.FromMilliseconds(200), itemCount: 5);
        var runTask = launcher1.RunAsync(slowJob, parameters, CancellationToken.None);

        JobExecution? running = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (running is null && DateTime.UtcNow < deadline)
        {
            running = await repository1.GetRunningJobExecutionAsync(jobName, parameters, CancellationToken.None);
            if (running is null)
                await Task.Delay(20);
        }

        Assert.NotNull(running);

        var secondJob = new SlowJob(jobName, delayPerItem: TimeSpan.FromMilliseconds(1), itemCount: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher2.RunAsync(secondJob, parameters, CancellationToken.None));

        var firstExecution = await runTask;
        Assert.Equal(BatchStatus.Completed, firstExecution.Status);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddConveyorBatch();
        services.AddDbContext<ConveyorBatchDbContext>(b => Fixture.ConfigureProvider(b, _connectionString!));
        services.AddScoped<IJobRepository, EfCoreJobRepository>();
        return services.BuildServiceProvider();
    }

    /// <summary>A minimal <see cref="IJob"/> that "runs" for roughly <c>delayPerItem * itemCount</c>, used to keep an execution in the <see cref="BatchStatus.Started"/> state long enough to observe it mid-flight.</summary>
    private sealed class SlowJob(string name, TimeSpan delayPerItem, int itemCount) : IJob
    {
        public string Name { get; } = name;

        public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
        {
            for (var i = 0; i < itemCount; i++)
                await Task.Delay(delayPerItem, cancellationToken);

            return new JobExecution { JobInstance = new JobInstance { JobName = Name }, Status = BatchStatus.Completed };
        }
    }
}

/// <summary>Runs <see cref="EfCoreJobRepositoryTestsBase"/> against a real PostgreSQL database.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class EfCoreJobRepositoryPostgresTests : EfCoreJobRepositoryTestsBase
{
    private readonly PostgresContainerFixture _fixture;

    public EfCoreJobRepositoryPostgresTests(PostgresContainerFixture fixture) => _fixture = fixture;

    protected override ITestDatabaseFixture Fixture => _fixture;
}

/// <summary>Runs <see cref="EfCoreJobRepositoryTestsBase"/> against a real SQL Server database.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class EfCoreJobRepositorySqlServerTests : EfCoreJobRepositoryTestsBase
{
    private readonly SqlServerContainerFixture _fixture;

    public EfCoreJobRepositorySqlServerTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    protected override ITestDatabaseFixture Fixture => _fixture;
}
