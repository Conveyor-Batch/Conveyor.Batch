using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.UnitTests.Job;

public sealed class HeartbeatTests
{
    /// <summary>
    /// SimpleJobLauncher.RunAsync always calls UpdateJobExecutionAsync exactly twice per run
    /// regardless of heartbeat — once after Status=Started, once in the terminal `finally`. This
    /// baseline is pre-existing and unrelated to heartbeat; tests below assert against it by name
    /// rather than a bare literal, so it's clear which calls are "the baseline" vs. "a heartbeat tick".
    /// </summary>
    private const int BaselineUpdateCallCount = 2;

    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private sealed class DelayJob(int delayMilliseconds) : IJob
    {
        public string Name => "delay-job";

        public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
        {
            await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            return new JobExecution { JobInstance = new JobInstance { JobName = Name }, Status = BatchStatus.Completed };
        }
    }

    /// <summary>
    /// Wraps an inner <see cref="IJobRepository"/> and counts <see cref="UpdateJobExecutionAsync"/>
    /// calls, optionally throwing on specific (1-based) call numbers to simulate a transient
    /// heartbeat write failure — same decorator shape as the <c>SpyJobRepository</c> used by the
    /// graceful-shutdown tests, just instrumenting a different method.
    /// </summary>
    private sealed class HeartbeatSpyRepository(IJobRepository inner) : IJobRepository
    {
        private int _updateJobExecutionCallCount;

        public int UpdateJobExecutionCallCount => _updateJobExecutionCallCount;

        public Func<int, bool>? ThrowOnCall { get; set; }

        public Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters) =>
            inner.CreateJobInstanceAsync(jobName, parameters);

        public Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters) =>
            inner.CreateJobExecutionAsync(instance, parameters);

        public async Task UpdateJobExecutionAsync(JobExecution execution)
        {
            var callNumber = Interlocked.Increment(ref _updateJobExecutionCallCount);
            if (ThrowOnCall?.Invoke(callNumber) == true)
                throw new InvalidOperationException("simulated heartbeat failure");

            await inner.UpdateJobExecutionAsync(execution).ConfigureAwait(false);
        }

        public Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName) =>
            inner.CreateStepExecutionAsync(jobExecution, stepName);

        public Task UpdateStepExecutionAsync(StepExecution stepExecution) =>
            inner.UpdateStepExecutionAsync(stepExecution);

        public Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters) =>
            inner.GetLastJobExecutionAsync(jobName, parameters);

        public Task<JobExecution?> GetRunningJobExecutionAsync(
            string jobName, JobParameters parameters, CancellationToken cancellationToken = default) =>
            inner.GetRunningJobExecutionAsync(jobName, parameters, cancellationToken);

        public Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance) =>
            inner.GetJobExecutionsAsync(instance);

        public Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName) =>
            inner.GetLastStepExecutionAsync(jobExecutionId, stepName);
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HeartbeatWritten_DuringLongRunningJob()
    {
        var repository = new InMemoryJobRepository();
        var launcher = new SimpleJobLauncher(repository, heartbeat: new HeartbeatOptions { Interval = TimeSpan.FromMilliseconds(100) });

        var execution = await launcher.RunAsync(new DelayJob(500), JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.NotNull(execution.LastHeartbeatAt);
        Assert.True((DateTimeOffset.UtcNow - execution.LastHeartbeatAt!.Value).Duration() < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task HeartbeatTicks_MultipleTimesForLongJob()
    {
        var spy = new HeartbeatSpyRepository(new InMemoryJobRepository());
        var launcher = new SimpleJobLauncher(spy, heartbeat: new HeartbeatOptions { Interval = TimeSpan.FromMilliseconds(100) });

        await launcher.RunAsync(new DelayJob(600), JobParameters.Empty, CancellationToken.None);

        // A 600ms run at a 100ms interval should add several heartbeat ticks on top of the baseline.
        Assert.True(
            spy.UpdateJobExecutionCallCount > BaselineUpdateCallCount + 1,
            $"Expected more than {BaselineUpdateCallCount + 1} UpdateJobExecutionAsync calls, got {spy.UpdateJobExecutionCallCount}");
    }

    [Fact]
    public async Task HeartbeatDisabled_NoExtraUpdates()
    {
        var spy = new HeartbeatSpyRepository(new InMemoryJobRepository());
        var launcher = new SimpleJobLauncher(spy); // heartbeat: null (default) — disabled

        await launcher.RunAsync(new DelayJob(300), JobParameters.Empty, CancellationToken.None);

        // With heartbeat disabled, no calls beyond BaselineUpdateCallCount should occur.
        Assert.Equal(BaselineUpdateCallCount, spy.UpdateJobExecutionCallCount);
    }

    [Fact]
    public async Task HeartbeatFailure_DoesNotAbortJob()
    {
        var spy = new HeartbeatSpyRepository(new InMemoryJobRepository())
        {
            ThrowOnCall = callNumber => callNumber == 2 // the first heartbeat tick (call #1 is the "Started" write)
        };
        var launcher = new SimpleJobLauncher(spy, heartbeat: new HeartbeatOptions { Interval = TimeSpan.FromMilliseconds(50) });

        var execution = await launcher.RunAsync(new DelayJob(400), JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.True(
            spy.UpdateJobExecutionCallCount > BaselineUpdateCallCount + 1,
            "Expected heartbeat ticks to continue after the injected failure on tick #1.");
    }

    [Fact]
    public async Task HeartbeatStopped_AfterJobCompletes()
    {
        var spy = new HeartbeatSpyRepository(new InMemoryJobRepository());
        var launcher = new SimpleJobLauncher(spy, heartbeat: new HeartbeatOptions { Interval = TimeSpan.FromMilliseconds(50) });

        await launcher.RunAsync(new DelayJob(200), JobParameters.Empty, CancellationToken.None);
        var countAfterReturn = spy.UpdateJobExecutionCallCount;

        await Task.Delay(300);

        Assert.Equal(countAfterReturn, spy.UpdateJobExecutionCallCount);
    }
}
