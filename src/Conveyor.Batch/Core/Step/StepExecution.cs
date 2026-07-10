using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// Records the runtime state of a single step execution.
/// </summary>
public sealed class StepExecution
{
    private long _readCount;
    private long _writeCount;
    private long _skipCount;
    private long _rollbackCount;

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
    public long ReadCount => Interlocked.Read(ref _readCount);

    /// <summary>Gets the total number of items written during this execution.</summary>
    public long WriteCount => Interlocked.Read(ref _writeCount);

    /// <summary>Gets the total number of items skipped during this execution.</summary>
    public long SkipCount => Interlocked.Read(ref _skipCount);

    /// <summary>Gets the total number of items that caused a rollback.</summary>
    public long RollbackCount => Interlocked.Read(ref _rollbackCount);

    /// <summary>Gets or sets the exception that caused this step to fail, if any.</summary>
    public Exception? FailureException { get; set; }

    /// <summary>Gets or sets the checkpoint state for this step execution, used to resume a restarted run.</summary>
    public BatchExecutionContext ExecutionContext { get; set; } = new();

    /// <summary>Gets or sets whether this step execution began as a resumption of a previous failed or stopped attempt.</summary>
    public bool IsRestart { get; set; }

    /// <summary>
    /// Increments <see cref="ReadCount"/> by one. Atomic, so it is safe to call concurrently
    /// from multiple worker tasks (e.g. from a parallel chunk-oriented engine).
    /// </summary>
    internal void IncrementReadCount() => Interlocked.Increment(ref _readCount);

    /// <summary>
    /// Increments <see cref="WriteCount"/> by <paramref name="count"/>. Atomic, so it is safe
    /// to call concurrently from multiple worker tasks (e.g. from a parallel chunk-oriented engine).
    /// </summary>
    internal void IncrementWriteCount(int count) => Interlocked.Add(ref _writeCount, count);

    /// <summary>
    /// Increments <see cref="SkipCount"/> by one. Atomic, so it is safe to call concurrently
    /// from multiple worker tasks (e.g. from a parallel chunk-oriented engine).
    /// </summary>
    internal void IncrementSkipCount() => Interlocked.Increment(ref _skipCount);

    /// <summary>
    /// Increments <see cref="RollbackCount"/> by one. Atomic, so it is safe to call
    /// concurrently from multiple worker tasks (e.g. from a parallel chunk-oriented engine).
    /// </summary>
    internal void IncrementRollbackCount() => Interlocked.Increment(ref _rollbackCount);

    /// <summary>
    /// Copies the read/write/skip/rollback counts from <paramref name="other"/> onto this
    /// instance. Used to transfer a worker step's final counts onto a separately-tracked
    /// <see cref="StepExecution"/> (e.g. a partition's manager-persisted record) without
    /// exposing the counters as generally settable.
    /// </summary>
    /// <param name="other">The step execution to copy counts from.</param>
    internal void CopyCountersFrom(StepExecution other)
    {
        _readCount = other.ReadCount;
        _writeCount = other.WriteCount;
        _skipCount = other.SkipCount;
        _rollbackCount = other.RollbackCount;
    }

    /// <summary>
    /// Sets the read/write/skip/rollback counts directly. Used by a persistence layer to
    /// rehydrate a <see cref="StepExecution"/>'s counters from previously-saved values (e.g. when
    /// mapping a database entity back to a domain object) without exposing the counters as
    /// generally settable.
    /// </summary>
    internal void RestoreCounters(long readCount, long writeCount, long skipCount, long rollbackCount)
    {
        _readCount = readCount;
        _writeCount = writeCount;
        _skipCount = skipCount;
        _rollbackCount = rollbackCount;
    }
}
