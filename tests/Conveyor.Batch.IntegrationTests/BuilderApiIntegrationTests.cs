using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.IntegrationTests;

/// <summary>
/// End-to-end tests for the builder API: JobBuilder + StepBuilder.
/// SimpleJobLauncher is internal, so jobs are launched via IJob.ExecuteAsync directly.
/// </summary>
public sealed class BuilderApiIntegrationTests
{
    // ── Fakes ──────────────────────────────────────────────────────────

    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(
            StepExecutionContext ctx,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class IdentityProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult<T?>(item);
    }

    private sealed class ThrowingProcessor<T>(Exception ex) : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            throw ex;
    }

    private sealed class CountingWriter<T> : IItemWriter<T>
    {
        public int TotalWritten { get; private set; }

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            TotalWritten += items.Count;
            return ValueTask.CompletedTask;
        }
    }

    private static IStep BuildStep<T>(
        string name,
        IJobRepository repo,
        IItemReader<T> reader,
        IItemProcessor<T, T> processor,
        IItemWriter<T> writer,
        int chunkSize = 10,
        ISkipPolicy? skipPolicy = null) =>
        new StepBuilder<T, T>(repo)
            .Reader(reader)
            .Processor(processor)
            .Writer(writer)
            .ChunkSize(chunkSize)
            .SkipPolicy(skipPolicy ?? new NeverSkipPolicy())
            .Build(name);

    private sealed class NeverSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => false;
    }

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleStepJob_HappyPath_StatusIsCompleted()
    {
        var repo = new InMemoryJobRepository();
        var writer = new CountingWriter<int>();

        var step = BuildStep(
            "process-step",
            repo,
            new ListReader<int>(Enumerable.Range(0, 100)),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10);

        var job = new JobBuilder("my-job", repo)
            .AddStep(step)
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(100, writer.TotalWritten);
        Assert.NotNull(execution.EndTime);
        Assert.Null(execution.FailureException);
    }

    [Fact]
    public async Task MultiStepJob_TwoStepsInSequence_BothComplete()
    {
        var repo = new InMemoryJobRepository();
        var writer1 = new CountingWriter<string>();
        var writer2 = new CountingWriter<string>();

        var step1 = BuildStep(
            "step-one",
            repo,
            new ListReader<string>(["a", "b", "c"]),
            new IdentityProcessor<string>(),
            writer1);

        var step2 = BuildStep(
            "step-two",
            repo,
            new ListReader<string>(["x", "y"]),
            new IdentityProcessor<string>(),
            writer2);

        var job = new JobBuilder("two-step-job", repo)
            .AddStep(step1)
            .AddStep(step2)
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.Equal(3, writer1.TotalWritten);
        Assert.Equal(2, writer2.TotalWritten);
    }

    [Fact]
    public async Task StepFailure_NonSkippableException_JobStatusIsFailed()
    {
        var repo = new InMemoryJobRepository();
        var writer = new CountingWriter<int>();

        var step = BuildStep(
            "failing-step",
            repo,
            new ListReader<int>([1, 2, 3]),
            new ThrowingProcessor<int>(new InvalidOperationException("Boom")),
            writer);

        var job = new JobBuilder("failing-job", repo)
            .AddStep(step)
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Failed, execution.Status);
        Assert.NotNull(execution.FailureException);
        Assert.Equal(0, writer.TotalWritten);
    }

    [Fact]
    public async Task MultiStepJob_FirstStepFails_SecondStepNeverRuns()
    {
        var repo = new InMemoryJobRepository();
        var writer1 = new CountingWriter<int>();
        var writer2 = new CountingWriter<int>();

        var step1 = BuildStep(
            "bad-step",
            repo,
            new ListReader<int>([1]),
            new ThrowingProcessor<int>(new Exception("Step 1 failed")),
            writer1);

        var step2 = BuildStep(
            "good-step",
            repo,
            new ListReader<int>([1, 2, 3]),
            new IdentityProcessor<int>(),
            writer2);

        var job = new JobBuilder("multi-fail-job", repo)
            .AddStep(step1)
            .AddStep(step2)
            .Build();

        var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Failed, execution.Status);
        Assert.Equal(0, writer2.TotalWritten);
    }

    [Fact]
    public async Task JobExecution_PersistedToRepository_CanBeRetrieved()
    {
        var repo = new InMemoryJobRepository();
        var writer = new CountingWriter<int>();

        var step = BuildStep(
            "step",
            repo,
            new ListReader<int>(Enumerable.Range(0, 20)),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 5);

        var job = new JobBuilder("retrieval-job", repo)
            .AddStep(step)
            .Build();

        var parameters = new JobParameters(new Dictionary<string, string> { ["version"] = "1" });
        await job.ExecuteAsync(parameters, CancellationToken.None);

        var last = await repo.GetLastJobExecutionAsync("retrieval-job", parameters);
        Assert.NotNull(last);
        Assert.Equal(BatchStatus.Completed, last.Status);
        Assert.Equal(20, writer.TotalWritten);
    }
}
