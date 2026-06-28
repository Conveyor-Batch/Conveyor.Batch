namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Records the runtime state of a single job execution.
/// </summary>
public sealed class JobExecution
{
    /// <summary>Gets the unique identifier of this execution.</summary>
    public long Id { get; init; }

    /// <summary>Gets the job instance this execution belongs to.</summary>
    public JobInstance JobInstance { get; init; } = null!;

    /// <summary>Gets the parameters used for this execution.</summary>
    public JobParameters Parameters { get; init; }

    /// <summary>Gets or sets the current status of this execution.</summary>
    public BatchStatus Status { get; set; } = BatchStatus.Starting;

    /// <summary>Gets the UTC time at which this execution started.</summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the UTC time at which this execution ended.</summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Gets or sets the exception that caused this execution to fail, if any.</summary>
    public Exception? FailureException { get; set; }
}
