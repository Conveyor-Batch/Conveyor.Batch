using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Conveyor.Batch.Telemetry;

/// <summary>
/// Central registry of the <see cref="ActivitySource"/> and <see cref="Meter"/> that
/// Conveyor.Batch uses to emit distributed tracing spans and metrics. Both types are inbox in
/// .NET 8 — Conveyor takes no dependency on the OpenTelemetry SDK itself. Consumers wire their
/// own OpenTelemetry exporters against <see cref="ActivitySourceName"/> and <see cref="MeterName"/>
/// in their own application startup; when no listener is attached, emitting a span or metric costs
/// a single null check.
/// </summary>
public static class ConveyorBatchTelemetry
{
    /// <summary>The registered name of <see cref="ActivitySource"/>.</summary>
    public const string ActivitySourceName = "Conveyor.Batch";

    /// <summary>The registered name of <see cref="Meter"/>.</summary>
    public const string MeterName = "Conveyor.Batch";

    internal const string JobActivityName = "conveyor.batch.job.execute";
    internal const string StepActivityName = "conveyor.batch.step.execute";
    internal const string ChunkActivityName = "conveyor.batch.chunk.commit";

    internal const string JobNameTag = "batch.job.name";
    internal const string JobExecutionIdTag = "batch.job.execution_id";
    internal const string JobStatusTag = "batch.job.status";
    internal const string StepNameTag = "batch.step.name";
    internal const string StepExecutionIdTag = "batch.step.execution_id";
    internal const string StepStatusTag = "batch.step.status";
    internal const string ChunkSizeTag = "batch.chunk.size";

    /// <summary>The shared <see cref="ActivitySource"/> used for all Conveyor.Batch distributed tracing spans.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "0.1.0");

    /// <summary>The shared <see cref="Meter"/> used for all Conveyor.Batch metrics instruments.</summary>
    public static readonly Meter Meter = new(MeterName, "0.1.0");

    internal static Counter<long> JobsCompleted { get; } =
        Meter.CreateCounter<long>("conveyor.batch.jobs.completed", description: "Number of job executions that reached Completed status.");

    internal static Counter<long> JobsFailed { get; } =
        Meter.CreateCounter<long>("conveyor.batch.jobs.failed", description: "Number of job executions that reached Failed status.");

    internal static Histogram<double> JobDuration { get; } =
        Meter.CreateHistogram<double>("conveyor.batch.job.duration", unit: "ms", description: "Elapsed milliseconds per job execution.");

    internal static Counter<long> ItemsRead { get; } =
        Meter.CreateCounter<long>("conveyor.batch.items.read", description: "Number of items read by a step.");

    internal static Counter<long> ItemsWritten { get; } =
        Meter.CreateCounter<long>("conveyor.batch.items.written", description: "Number of items written by a step.");

    internal static Counter<long> ItemsSkipped { get; } =
        Meter.CreateCounter<long>("conveyor.batch.items.skipped", description: "Number of items skipped by a step.");

    internal static Histogram<int> ChunkSize { get; } =
        Meter.CreateHistogram<int>("conveyor.batch.chunk.size", description: "Size of each committed chunk.");

    internal static Counter<long> ChunksCommitted { get; } =
        Meter.CreateCounter<long>("conveyor.batch.chunks.committed", description: "Number of chunks committed.");
}
