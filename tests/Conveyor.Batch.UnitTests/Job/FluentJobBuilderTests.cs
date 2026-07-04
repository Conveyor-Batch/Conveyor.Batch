using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Job.Flow;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.UnitTests.Job;

public sealed class FluentJobBuilderTests
{
    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private sealed class FakeStep : IStep
    {
        private readonly BatchStatus[] _statuses;
        private int _callCount;

        public string Name { get; }
        public List<string> ExecutionLog { get; }

        public FakeStep(string name, List<string> executionLog, params BatchStatus[] statuses)
        {
            Name = name;
            ExecutionLog = executionLog;
            _statuses = statuses.Length > 0 ? statuses : [BatchStatus.Completed];
        }

        public Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
        {
            ExecutionLog.Add(Name);
            cancellationToken.ThrowIfCancellationRequested();

            var status = _statuses[Math.Min(_callCount, _statuses.Length - 1)];
            _callCount++;

            var stepExecution = new StepExecution
            {
                StepName = Name,
                JobExecution = jobExecution,
                Status = status
            };
            return Task.FromResult(stepExecution);
        }
    }

    private sealed class CancellingStep(string name, List<string> executionLog, CancellationTokenSource gate) : IStep
    {
        public string Name { get; } = name;

        public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
        {
            executionLog.Add(Name);
            await gate.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        }
    }

    private static IJobRepository NewRepository() => new InMemoryJobRepository();

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinearFlow_AllStepsComplete_JobCompletes()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);
        var stepB = new FakeStep("stepB", log, BatchStatus.Completed);
        var stepC = new FakeStep("stepC", log, BatchStatus.Completed);

        var job = new FluentJobBuilder("linear-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").To(stepB)
            .From(stepB)
                .On("COMPLETED").To(stepC)
            .From(stepC)
                .On("*").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(["stepA", "stepB", "stepC"], log);
    }

    [Fact]
    public async Task BranchOnFailure_FailedStep_RoutesToErrorStep()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);
        var stepB = new FakeStep("stepB", log, BatchStatus.Failed);
        var errorStep = new FakeStep("errorStep", log, BatchStatus.Completed);
        var notifyStep = new FakeStep("notifyStep", log, BatchStatus.Completed);

        var job = new FluentJobBuilder("branch-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").To(stepB)
            .From(stepB)
                .On("COMPLETED").To(notifyStep)
                .On("FAILED").To(errorStep)
            .From(notifyStep)
                .On("*").End()
            .From(errorStep)
                .On("*").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(["stepA", "stepB", "errorStep"], log);
        Assert.Equal(BatchStatus.Completed, execution.Status);
    }

    [Fact]
    public async Task WildcardTransition_MatchesUnhandledStatus()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Stopped);
        var stepB = new FakeStep("stepB", log, BatchStatus.Completed);

        var job = new FluentJobBuilder("wildcard-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").To(stepB)
                .On("*").End()
            .From(stepB)
                .On("*").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(["stepA"], log);
        Assert.Equal(BatchStatus.Stopped, execution.Status);
    }

    [Fact]
    public async Task ExactMatchPriority_OverWildcard()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Failed);
        var errorStep = new FakeStep("errorStep", log, BatchStatus.Completed);

        var job = new FluentJobBuilder("priority-job", NewRepository())
            .Start(stepA)
                .On("FAILED").To(errorStep)
                .On("*").End()
            .From(errorStep)
                .On("*").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(["stepA", "errorStep"], log);
        Assert.Equal(BatchStatus.Completed, execution.Status);
    }

    [Fact]
    public async Task FailAction_SetsJobStatusFailed()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Failed);

        var job = new FluentJobBuilder("fail-job", NewRepository())
            .Start(stepA)
                .On("FAILED").Fail()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Failed, execution.Status);
    }

    [Fact]
    public async Task StopAction_SetsJobStatusStopped()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);

        var job = new FluentJobBuilder("stop-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").Stop()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Stopped, execution.Status);
    }

    [Fact]
    public async Task NoMatchingRule_ThrowsInvalidOperationException()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Stopped);

        var job = new FluentJobBuilder("no-match-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").End()
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            job.ExecuteAsync(JobParameters.Empty, CancellationToken.None));
    }

    [Fact]
    public void Build_UnreachableStep_ThrowsInvalidOperationException()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);
        var stepB = new FakeStep("stepB", log, BatchStatus.Completed);
        var stepC = new FakeStep("stepC", log, BatchStatus.Completed);

        var builder = new FluentJobBuilder("unreachable-job", NewRepository())
            .Start(stepA)
                .On("*").End()
            .From(stepB)
                .On("*").End()
            .From(stepC)
                .On("*").End();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_NoTerminalRule_ThrowsInvalidOperationException()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);
        var stepB = new FakeStep("stepB", log, BatchStatus.Completed);

        var builder = new FluentJobBuilder("no-terminal-job", NewRepository())
            .Start(stepA)
                .On("*").To(stepB)
            .From(stepB)
                .On("*").To(stepA);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public async Task RetryLoop_StepRetriedOnFailure()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Failed, BatchStatus.Completed);

        var job = new FluentJobBuilder("retry-job", NewRepository())
            .Start(stepA)
                .On("FAILED").To(stepA)
                .On("COMPLETED").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(2, log.Count(name => name == "stepA"));
        Assert.Equal(BatchStatus.Completed, execution.Status);
    }

    [Fact]
    public async Task CancellationToken_PropagatedToSteps()
    {
        var log = new List<string>();
        using var cts = new CancellationTokenSource();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);
        var stepB = new CancellingStep("stepB", log, cts);

        var job = new FluentJobBuilder("cancel-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").To(stepB)
            .From(stepB)
                .On("*").End()
            .Build();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            job.ExecuteAsync(JobParameters.Empty, cts.Token));
    }

    [Fact]
    public async Task SingleStep_LinearJob_Works()
    {
        var log = new List<string>();
        var stepA = new FakeStep("stepA", log, BatchStatus.Completed);

        var job = new FluentJobBuilder("single-step-job", NewRepository())
            .Start(stepA)
                .On("COMPLETED").End()
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(["stepA"], log);
    }
}
