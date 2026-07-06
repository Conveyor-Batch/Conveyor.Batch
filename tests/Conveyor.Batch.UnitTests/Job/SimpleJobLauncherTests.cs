using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;

namespace Conveyor.Batch.UnitTests.Job;

public sealed class SimpleJobLauncherTests
{
    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private sealed class FakeJob(string name, BatchStatus status = BatchStatus.Completed) : IJob
    {
        public string Name { get; } = name;

        public int ExecuteCallCount { get; private set; }

        public Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
        {
            ExecuteCallCount++;
            return Task.FromResult(new JobExecution { Status = status });
        }
    }

    private sealed class FakeJobLock(bool isAcquired) : IJobLock
    {
        public bool IsAcquired { get; } = isAcquired;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeJobLockProvider(bool isAcquired) : IJobLockProvider
    {
        public FakeJobLock? LastLock { get; private set; }

        public Task<IJobLock> TryAcquireAsync(
            string jobName,
            JobParameters parameters,
            CancellationToken cancellationToken)
        {
            LastLock = new FakeJobLock(isAcquired);
            return Task.FromResult<IJobLock>(LastLock);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlreadyRunning_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryJobRepository();
        var job = new FakeJob("job-already-running");

        var instance = await repository.CreateJobInstanceAsync(job.Name, JobParameters.Empty);
        var runningExecution = await repository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        runningExecution.Status = BatchStatus.Started;
        await repository.UpdateJobExecutionAsync(runningExecution);

        var launcher = new SimpleJobLauncher(repository);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None));

        Assert.Contains(runningExecution.Id.ToString(), ex.Message);
        Assert.Equal(0, job.ExecuteCallCount);
    }

    [Fact]
    public async Task CompletedExecution_AllowsRelaunch()
    {
        var repository = new InMemoryJobRepository();
        var job = new FakeJob("job-completed");

        var instance = await repository.CreateJobInstanceAsync(job.Name, JobParameters.Empty);
        var priorExecution = await repository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        priorExecution.Status = BatchStatus.Completed;
        await repository.UpdateJobExecutionAsync(priorExecution);

        var launcher = new SimpleJobLauncher(repository);

        var execution = await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(1, job.ExecuteCallCount);
    }

    [Fact]
    public async Task DifferentParameters_AllowsConcurrentExecution()
    {
        var repository = new InMemoryJobRepository();
        var job = new FakeJob("job-different-params");
        var runningParameters = new JobParameters(new Dictionary<string, string> { ["file"] = "a" });
        var newParameters = new JobParameters(new Dictionary<string, string> { ["file"] = "b" });

        var instance = await repository.CreateJobInstanceAsync(job.Name, runningParameters);
        var runningExecution = await repository.CreateJobExecutionAsync(instance, runningParameters);
        runningExecution.Status = BatchStatus.Started;
        await repository.UpdateJobExecutionAsync(runningExecution);

        var launcher = new SimpleJobLauncher(repository);

        var execution = await launcher.RunAsync(job, newParameters, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(1, job.ExecuteCallCount);
    }

    [Fact]
    public async Task LockNotAcquired_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryJobRepository();
        var job = new FakeJob("job-lock-not-acquired");
        var lockProvider = new FakeJobLockProvider(isAcquired: false);
        var launcher = new SimpleJobLauncher(repository, lockProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None));

        Assert.Equal(0, job.ExecuteCallCount);
    }

    [Fact]
    public async Task LockAcquired_ReleasedAfterJobCompletes()
    {
        var repository = new InMemoryJobRepository();
        var job = new FakeJob("job-lock-released");
        var lockProvider = new FakeJobLockProvider(isAcquired: true);
        var launcher = new SimpleJobLauncher(repository, lockProvider);

        await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);

        Assert.NotNull(lockProvider.LastLock);
        Assert.True(lockProvider.LastLock!.Disposed);
    }
}
