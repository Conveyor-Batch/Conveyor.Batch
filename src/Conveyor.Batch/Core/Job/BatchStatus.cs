namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Represents the lifecycle status of a job or step execution.
/// </summary>
public enum BatchStatus
{
    /// <summary>The execution has not yet started.</summary>
    Starting,

    /// <summary>The execution is actively running.</summary>
    Started,

    /// <summary>The execution is stopping.</summary>
    Stopping,

    /// <summary>The execution was stopped before completion.</summary>
    Stopped,

    /// <summary>The execution completed successfully.</summary>
    Completed,

    /// <summary>The execution was abandoned.</summary>
    Abandoned,

    /// <summary>The execution failed.</summary>
    Failed
}
