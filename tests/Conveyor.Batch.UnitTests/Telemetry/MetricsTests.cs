using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Policies;
using Conveyor.Batch.Telemetry;

namespace Conveyor.Batch.UnitTests.Telemetry;

public sealed class MetricsTests
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

    private sealed class ThrowingProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            throw new InvalidOperationException("bad item");
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

    private sealed class AlwaysSkipPolicy : ISkipPolicy
    {
        public bool ShouldSkip(Exception exception, long skipCount) => true;
    }

    private sealed class ThrowingJob(string name) : IJob
    {
        public string Name { get; } = name;

        public Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed record Measurement(string InstrumentName, double Value, KeyValuePair<string, object?>[] Tags)
    {
        public string? Tag(string key) => (string?)Tags.FirstOrDefault(t => t.Key == key).Value;
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Snapshots the recorded list under lock. Other tests running concurrently in the same
    /// process share the same static <see cref="ConveyorBatchTelemetry.Meter"/>, so this test's
    /// listener callback (guarded by the same lock) can still be appending to <paramref name="recorded"/>
    /// from another thread; enumerating it directly without a lock risks a concurrent-modification
    /// exception even though the eventual filter-by-unique-tag makes the extra entries harmless.
    /// </summary>
    private static List<Measurement> Snapshot(List<Measurement> recorded)
    {
        lock (recorded)
            return [.. recorded];
    }

    private static MeterListener AttachListener(List<Measurement> recorded)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ConveyorBatchTelemetry.MeterName)
                l.EnableMeasurementEvents(instrument);
        };

        void Record(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (recorded)
                recorded.Add(new Measurement(name, value, tags.ToArray()));
        }

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument.Name, value, tags));

        listener.Start();
        return listener;
    }

    private static async Task<JobExecution> RunSimpleJobAsync(
        string jobName, string stepName, int itemCount, int chunkSize,
        ISkipPolicy? skipPolicy = null, IItemWriter<int>? writer = null)
    {
        var repository = new InMemoryJobRepository();
        var builder = new StepBuilder<int, int>(repository)
            .Reader(new ListReader<int>(Enumerable.Range(1, itemCount)))
            .Processor(skipPolicy is null ? new IdentityProcessor<int>() : new ThrowingProcessor<int>())
            .Writer(writer ?? new NoOpWriter<int>())
            .ChunkSize(chunkSize);

        if (skipPolicy is not null)
            builder = builder.SkipPolicy(skipPolicy);

        var step = builder.Build(stepName);
        var job = new JobBuilder(jobName, repository).AddStep(step).Build();
        var launcher = new SimpleJobLauncher(repository);

        return await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ItemsRead_CountedCorrectly()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(UniqueName("job"), stepName, itemCount: 10, chunkSize: 10);

        var total = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.items.read" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);

        Assert.Equal(10, total);
    }

    [Fact]
    public async Task ItemsWritten_CountedCorrectly()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(UniqueName("job"), stepName, itemCount: 10, chunkSize: 5);

        var total = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.items.written" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);

        Assert.Equal(10, total);
    }

    [Fact]
    public async Task ItemsSkipped_CountedCorrectly()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(UniqueName("job"), stepName, itemCount: 5, chunkSize: 10, skipPolicy: new AlwaysSkipPolicy());

        var skipped = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.items.skipped" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);
        var written = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.items.written" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);

        Assert.Equal(5, skipped);
        Assert.Equal(0, written);
    }

    [Fact]
    public async Task ChunksCommitted_CountedCorrectly()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(UniqueName("job"), stepName, itemCount: 10, chunkSize: 3);

        var total = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.chunks.committed" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);

        Assert.Equal(4, total);
    }

    [Fact]
    public async Task JobCompleted_CounterIncremented()
    {
        var jobName = UniqueName("job");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        await RunSimpleJobAsync(jobName, UniqueName("step"), itemCount: 3, chunkSize: 10);

        var completed = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.jobs.completed" && m.Tag(ConveyorBatchTelemetry.JobNameTag) == jobName)
            .Sum(m => m.Value);
        var failed = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.jobs.failed" && m.Tag(ConveyorBatchTelemetry.JobNameTag) == jobName)
            .Sum(m => m.Value);

        Assert.Equal(1, completed);
        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task JobFailed_CounterIncremented()
    {
        var jobName = UniqueName("job");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        var repository = new InMemoryJobRepository();
        var launcher = new SimpleJobLauncher(repository);
        var job = new ThrowingJob(jobName);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None));

        var completed = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.jobs.completed" && m.Tag(ConveyorBatchTelemetry.JobNameTag) == jobName)
            .Sum(m => m.Value);
        var failed = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.jobs.failed" && m.Tag(ConveyorBatchTelemetry.JobNameTag) == jobName)
            .Sum(m => m.Value);

        Assert.Equal(0, completed);
        Assert.Equal(1, failed);
    }

    [Fact]
    public async Task ChunksCommitted_NotIncrementedOnWriterFailure()
    {
        var stepName = UniqueName("step");
        var recorded = new List<Measurement>();
        using var listener = AttachListener(recorded);

        var execution = await RunSimpleJobAsync(
            UniqueName("job"), stepName, itemCount: 5, chunkSize: 5, writer: new ThrowingWriter<int>());

        Assert.Equal(BatchStatus.Failed, execution.Status);

        var committed = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.chunks.committed" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);
        var written = Snapshot(recorded)
            .Where(m => m.InstrumentName == "conveyor.batch.items.written" && m.Tag(ConveyorBatchTelemetry.StepNameTag) == stepName)
            .Sum(m => m.Value);

        Assert.Equal(0, committed);
        Assert.Equal(0, written);
    }

    [Fact]
    public async Task ZeroCost_WhenNoListenerAttached()
    {
        var execution = await RunSimpleJobAsync(UniqueName("job"), UniqueName("step"), itemCount: 10, chunkSize: 4);

        Assert.Equal(BatchStatus.Completed, execution.Status);
    }
}
