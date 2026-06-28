using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Represents a single phase of a batch job.
/// </summary>
public interface IStep
{
    /// <summary>Gets the unique name of this step within its job.</summary>
    string Name { get; }

    /// <summary>
    /// Executes the step within the context of the given job execution.
    /// </summary>
    /// <param name="jobExecution">The parent job execution.</param>
    /// <param name="cancellationToken">Token to cancel the step.</param>
    Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken);
}
