using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Represents a top-level batch job composed of one or more steps.
/// </summary>
public interface IJob
{
    /// <summary>Gets the unique name of this job.</summary>
    string Name { get; }

    /// <summary>
    /// Executes the job with the given parameters.
    /// </summary>
    /// <param name="parameters">The parameters for this job execution.</param>
    /// <param name="cancellationToken">Token to cancel the job.</param>
    Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken);
}
