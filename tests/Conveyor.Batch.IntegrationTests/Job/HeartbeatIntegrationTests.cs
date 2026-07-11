using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.Hosting;
using Conveyor.Batch.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Conveyor.Batch.IntegrationTests.Job;

/// <summary>
/// Proves that <c>SimpleJobLauncher</c>'s heartbeat feature actually persists
/// <see cref="JobExecution.LastHeartbeatAt"/> to a real database, and that a transient failure on
/// a single heartbeat write does not abort the job - both against a real PostgreSQL database via
/// <see cref="EfCoreJobRepository"/>, reached only through the public DI surface since
/// <c>SimpleJobLauncher</c> is internal.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class HeartbeatIntegrationTests
{
    private readonly PostgresContainerFixture _fixture;

    public HeartbeatIntegrationTests(PostgresContainerFixture fixture) => _fixture = fixture;

    private ConveyorBatchDbContext BuildContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ConveyorBatchDbContext>();
        _fixture.ConfigureProvider(builder, connectionString);
        return new ConveyorBatchDbContext(builder.Options);
    }

    [Fact]
    public async Task Heartbeat_WritesLastHeartbeatAt_ToDatabase()
    {
        var connectionString = await _fixture.CreateFreshDatabaseAsync();
        await using (var migrateContext = BuildContext(connectionString))
            await migrateContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddConveyorBatch(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(100));
        services.AddDbContext<ConveyorBatchDbContext>(b => _fixture.ConfigureProvider(b, connectionString));
        services.AddScoped<IJobRepository, EfCoreJobRepository>();

        await using var provider = services.BuildServiceProvider();
        var launcher = provider.GetRequiredService<IJobLauncher>();

        var job = new SlowJob("heartbeat-write-test", totalDelay: TimeSpan.FromMilliseconds(500));
        var execution = await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);

        await using var verifyContext = BuildContext(connectionString);
        var verifyRepository = new EfCoreJobRepository(verifyContext);
        var reloaded = await verifyRepository.GetLastJobExecutionAsync("heartbeat-write-test", JobParameters.Empty);

        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.LastHeartbeatAt);
        Assert.NotNull(reloaded.EndTime);
        Assert.True(reloaded.LastHeartbeatAt >= reloaded.StartTime);
        Assert.True(reloaded.LastHeartbeatAt <= reloaded.EndTime.Value.AddSeconds(1));
    }

    [Fact]
    public async Task Heartbeat_DoesNotPreventJobCompletion_OnTransientFailure()
    {
        var connectionString = await _fixture.CreateFreshDatabaseAsync();
        await using (var migrateContext = BuildContext(connectionString))
            await migrateContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddConveyorBatch(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(20));
        services.AddDbContext<ConveyorBatchDbContext>(b => _fixture.ConfigureProvider(b, connectionString));
        services.AddScoped<IJobRepository>(sp =>
            new ThrowOnNthUpdateJobExecutionRepository(
                new EfCoreJobRepository(sp.GetRequiredService<ConveyorBatchDbContext>()),
                throwOnCallNumber: 2));

        await using var provider = services.BuildServiceProvider();
        var launcher = provider.GetRequiredService<IJobLauncher>();

        var job = new SlowJob("heartbeat-resilience-test", totalDelay: TimeSpan.FromMilliseconds(300));
        var execution = await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
    }

    // ── Fakes ──────────────────────────────────────────────────────────

    /// <summary>An <see cref="IJob"/> that sleeps for <paramref name="totalDelay"/> then completes.</summary>
    private sealed class SlowJob(string name, TimeSpan totalDelay) : IJob
    {
        public string Name { get; } = name;

        public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
        {
            await Task.Delay(totalDelay, cancellationToken);
            return new JobExecution { JobInstance = new JobInstance { JobName = Name }, Status = BatchStatus.Completed };
        }
    }

    /// <summary>
    /// Decorator that throws on the <paramref name="throwOnCallNumber"/>-th call to
    /// <see cref="UpdateJobExecutionAsync"/> (simulating a transient repository failure on one
    /// heartbeat tick), delegating everything else - and every other call - to <paramref name="inner"/>.
    /// </summary>
    private sealed class ThrowOnNthUpdateJobExecutionRepository(IJobRepository inner, int throwOnCallNumber) : IJobRepository
    {
        private int _updateJobExecutionCallCount;

        public Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters) =>
            inner.CreateJobInstanceAsync(jobName, parameters);

        public Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters) =>
            inner.CreateJobExecutionAsync(instance, parameters);

        public Task UpdateJobExecutionAsync(JobExecution execution)
        {
            if (Interlocked.Increment(ref _updateJobExecutionCallCount) == throwOnCallNumber)
                throw new InvalidOperationException("simulated transient heartbeat write failure");
            return inner.UpdateJobExecutionAsync(execution);
        }

        public Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName) =>
            inner.CreateStepExecutionAsync(jobExecution, stepName);

        public Task UpdateStepExecutionAsync(StepExecution stepExecution) =>
            inner.UpdateStepExecutionAsync(stepExecution);

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
