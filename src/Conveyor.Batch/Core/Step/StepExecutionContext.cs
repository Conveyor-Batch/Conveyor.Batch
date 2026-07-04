using System.Diagnostics;
using Conveyor.Batch.Telemetry;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// Provides contextual state for the currently executing step, passed to readers, processors, and writers.
/// </summary>
public sealed class StepExecutionContext
{
    private readonly StepExecution _stepExecution;
    private readonly TagList _metricTags;

    /// <summary>Initializes a new context wrapping the given step execution.</summary>
    public StepExecutionContext(StepExecution stepExecution)
    {
        _stepExecution = stepExecution;
        _metricTags = new TagList { { ConveyorBatchTelemetry.StepNameTag, stepExecution.StepName } };
    }

    /// <summary>Gets the underlying step execution record.</summary>
    public StepExecution StepExecution => _stepExecution;

    /// <summary>Gets the name of the current step.</summary>
    public string StepName => _stepExecution.StepName;

    /// <summary>Gets the total number of items written so far in this step.</summary>
    public long WriteCount => _stepExecution.WriteCount;

    /// <summary>Gets the total number of items skipped so far in this step.</summary>
    public long SkipCount => _stepExecution.SkipCount;

    /// <summary>Increments the skip counter by one.</summary>
    public void IncrementSkipCount()
    {
        _stepExecution.IncrementSkipCount();
        ConveyorBatchTelemetry.ItemsSkipped.Add(1, _metricTags);
    }

    /// <summary>Increments the write counter by the given amount.</summary>
    public void IncrementWriteCount(int count)
    {
        _stepExecution.IncrementWriteCount(count);
        ConveyorBatchTelemetry.ItemsWritten.Add(count, _metricTags);
    }

    /// <summary>Increments the read counter by one.</summary>
    public void IncrementReadCount()
    {
        _stepExecution.IncrementReadCount();
        ConveyorBatchTelemetry.ItemsRead.Add(1, _metricTags);
    }
}
