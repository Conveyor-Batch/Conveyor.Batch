namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Indicates whether a tasklet should be called again or is finished.
/// </summary>
public enum RepeatStatus
{
    /// <summary>The tasklet has more work and should be called again.</summary>
    Continuable,

    /// <summary>The tasklet has completed its work.</summary>
    Finished
}
