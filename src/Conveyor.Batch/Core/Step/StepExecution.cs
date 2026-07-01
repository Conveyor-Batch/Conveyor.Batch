using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// Records the runtime state of a single step execution.
/// </summary>
public sealed class StepExecution
{
    /// <summary>Gets the unique identifier of this step execution.</summary>
    public long Id { get; init; }

    /// <summary>Gets the name of the step.</summary>
    public string StepName { get; init; } = string.Empty;

    /// <summary>Gets the parent job execution.</summary>
    public JobExecution JobExecution { get; init; } = null!;

    /// <summary>Gets or sets the current status of this step execution.</summary>
    public BatchStatus Status { get; set; } = BatchStatus.Starting;

    /// <summary>Gets the UTC time at which this step execution started.</summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the UTC time at which this step execution ended.</summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Gets the total number of items read during this execution.</summary>
    public long ReadCount { get; private set; }

    /// <summary>Gets the total number of items written during this execution.</summary>
    public long WriteCount { get; private set; }

    /// <summary>Gets the total number of items skipped during this execution.</summary>
    public long SkipCount { get; private set; }

    /// <summary>Gets the total number of items that caused a rollback.</summary>
    public long RollbackCount { get; private set; }

    /// <summary>Gets or sets the exception that caused this step to fail, if any.</summary>
    public Exception? FailureException { get; set; }

    /// <summary>Gets or sets the checkpoint state for this step execution, used to resume a restarted run.</summary>
    public BatchExecutionContext ExecutionContext { get; set; } = new();

    /// <summary>Gets or sets whether this step execution began as a resumption of a previous failed or stopped attempt.</summary>
    public bool IsRestart { get; set; }

    internal void IncrementReadCount() => ReadCount++;
    internal void IncrementWriteCount(int count) => WriteCount += count;
    internal void IncrementSkipCount() => SkipCount++;
    internal void IncrementRollbackCount() => RollbackCount++;
}
