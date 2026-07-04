using System.Diagnostics;
using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Telemetry;

namespace Conveyor.Batch.UnitTests.Telemetry;

public sealed class ActivityTests
{
    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
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

    private sealed class NoOpWriter<T> : IItemWriter<T>
    {
        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowingWriter<T> : IItemWriter<T>
    {
        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct) =>
            throw new InvalidOperationException("write failed");
    }

    private sealed class ThrowingJob(string name) : IJob
    {
        public string Name { get; } = name;

        public Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Snapshots the recorded list under lock. Other tests running concurrently in the same
    /// process share the same static <see cref="ConveyorBatchTelemetry.ActivitySource"/>, so this
    /// test's listener callback (guarded by the same lock) can still be appending to
    /// <paramref name="recorded"/> from another thread; enumerating it directly without a lock
    /// risks a concurrent-modification exception even though the eventual filter-by-unique-tag
    /// makes the extra entries harmless.
    /// </summary>
    private static List<Activity> Snapshot(List<Activity> recorded)
    {
        lock (recorded)
            return [.. recorded];
    }

    private static ActivityListener AttachListener(List<Activity> recorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConveyorBatchTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (recorded)
                    recorded.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task<JobExecution> RunSimpleJobAsync(
        string jobName, string stepName, int itemCount, int chunkSize, IItemWriter<int>? writer = null)
    {
        var repository = new InMemoryJobRepository();
        var step = new StepBuilder<int, int>(repository)
            .Reader(new ListReader<int>(Enumerable.Range(1, itemCount)))
            .Processor(new IdentityProcessor<int>())
            .Writer(writer ?? new NoOpWriter<int>())
            .ChunkSize(chunkSize)
            .Build(stepName);
        var job = new JobBuilder(jobName, repository).AddStep(step).Build();
        var launcher = new SimpleJobLauncher(repository);

        return await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task JobActivity_StartedAndStopped()
    {
        var jobName = UniqueName("job");
        var recorded = new List<Activity>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(jobName, UniqueName("step"), itemCount: 3, chunkSize: 10);

        var jobActivities = Snapshot(recorded)
            .Where(a => a.OperationName == ConveyorBatchTelemetry.JobActivityName
                && (string?)a.GetTagItem(ConveyorBatchTelemetry.JobNameTag) == jobName)
            .ToList();

        var activity = Assert.Single(jobActivities);
        Assert.Equal(jobName, activity.GetTagItem(ConveyorBatchTelemetry.JobNameTag));
        Assert.True(activity.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task JobActivity_FailedJob_StatusSetToError()
    {
        var jobName = UniqueName("job");
        var recorded = new List<Activity>();
        using var listener = AttachListener(recorded);

        var repository = new InMemoryJobRepository();
        var launcher = new SimpleJobLauncher(repository);
        var job = new ThrowingJob(jobName);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None));

        var activity = Snapshot(recorded).Single(a => a.OperationName == ConveyorBatchTelemetry.JobActivityName
            && (string?)a.GetTagItem(ConveyorBatchTelemetry.JobNameTag) == jobName);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task ChunkActivity_OnePerCommit()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Activity>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(UniqueName("job"), stepName, itemCount: 10, chunkSize: 5);

        var chunkActivities = Snapshot(recorded)
            .Where(a => a.OperationName == ConveyorBatchTelemetry.ChunkActivityName
                && (string?)a.GetTagItem(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .ToList();

        Assert.Equal(2, chunkActivities.Count);
    }

    [Fact]
    public async Task ChunkActivity_SizeTagCorrect()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Activity>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(UniqueName("job"), stepName, itemCount: 10, chunkSize: 3);

        var sizes = Snapshot(recorded)
            .Where(a => a.OperationName == ConveyorBatchTelemetry.ChunkActivityName
                && (string?)a.GetTagItem(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Select(a => (int)a.GetTagItem(ConveyorBatchTelemetry.ChunkSizeTag)!)
            .ToList();

        Assert.Equal([3, 3, 3, 1], sizes);
    }

    [Fact]
    public async Task Activities_AreParented()
    {
        var jobName = UniqueName("job");
        var stepName = UniqueName("step");
        var recorded = new List<Activity>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(jobName, stepName, itemCount: 2, chunkSize: 10);

        var jobActivity = Snapshot(recorded).Single(a => a.OperationName == ConveyorBatchTelemetry.JobActivityName
            && (string?)a.GetTagItem(ConveyorBatchTelemetry.JobNameTag) == jobName);
        var stepActivity = Snapshot(recorded).Single(a => a.OperationName == ConveyorBatchTelemetry.StepActivityName
            && (string?)a.GetTagItem(ConveyorBatchTelemetry.StepNameTag) == stepName);
        var chunkActivity = Snapshot(recorded).Single(a => a.OperationName == ConveyorBatchTelemetry.ChunkActivityName
            && (string?)a.GetTagItem(ConveyorBatchTelemetry.StepNameTag) == stepName);

        Assert.Same(jobActivity, stepActivity.Parent);
        Assert.Same(stepActivity, chunkActivity.Parent);
    }

    [Fact]
    public async Task ChunkActivity_WriterThrows_StillStoppedWithDuration()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Activity>();
        using var listener = AttachListener(recorded);

        var execution = await RunSimpleJobAsync(
            UniqueName("job"), stepName, itemCount: 5, chunkSize: 5, writer: new ThrowingWriter<int>());

        Assert.Equal(BatchStatus.Failed, execution.Status);

        var chunkActivity = Snapshot(recorded).Single(a => a.OperationName == ConveyorBatchTelemetry.ChunkActivityName
            && (string?)a.GetTagItem(ConveyorBatchTelemetry.StepNameTag) == stepName);

        Assert.True(chunkActivity.Duration > TimeSpan.Zero);
    }
}
