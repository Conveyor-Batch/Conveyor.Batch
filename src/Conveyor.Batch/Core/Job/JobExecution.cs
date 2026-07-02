using Conveyor.Batch.Abstractions;

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

    /// <summary>
    /// Gets or sets the Id of the prior <see cref="JobExecution"/> this execution is resuming,
    /// or <see langword="null"/> if this is a fresh (non-restart) run. This is an in-process-only
    /// signal: it is produced and consumed entirely within a single job run (set once the new
    /// execution is created, read immediately by each step as it starts), so it never needs to
    /// survive a process boundary and is intentionally not persisted via <c>IJobRepository</c>.
    /// </summary>
    public long? RestartedFromExecutionId { get; set; }

    /// <summary>
    /// Gets the execution context bag carrying partition-scoped state to a worker step.
    /// This is an in-process-only signal (like <see cref="RestartedFromExecutionId"/>) —
    /// never persisted via <c>IJobRepository</c>, only ever read by the worker that receives
    /// this specific <see cref="JobExecution"/> instance.
    /// </summary>
    public BatchExecutionContext ExecutionContext { get; init; } = new();
}
