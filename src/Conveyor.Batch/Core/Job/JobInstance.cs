namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Represents a unique logical run of a named job with a specific set of parameters.
/// </summary>
public sealed class JobInstance
{
    /// <summary>Gets the unique identifier of this job instance.</summary>
    public long Id { get; init; }

    /// <summary>Gets the name of the job.</summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>Gets the parameters that identify this instance.</summary>
    public JobParameters Parameters { get; init; }
}
