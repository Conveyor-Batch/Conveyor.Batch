using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Entry point for triggering job execution.
/// </summary>
public interface IJobLauncher
{
    /// <summary>
    /// Launches the given job with the provided parameters.
    /// </summary>
    /// <param name="job">The job to run.</param>
    /// <param name="parameters">Parameters for this execution.</param>
    /// <param name="cancellationToken">Token to cancel the launch.</param>
    Task<JobExecution> RunAsync(IJob job, JobParameters parameters, CancellationToken cancellationToken = default);
}
