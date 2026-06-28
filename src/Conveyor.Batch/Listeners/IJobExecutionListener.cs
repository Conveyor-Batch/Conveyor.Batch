using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Listeners;

/// <summary>
/// Receives notifications at the start and end of a job execution.
/// </summary>
public interface IJobExecutionListener
{
    /// <summary>Called before the job begins executing.</summary>
    ValueTask BeforeJobAsync(JobExecution jobExecution, CancellationToken cancellationToken);

    /// <summary>Called after the job has finished (successfully or not).</summary>
    ValueTask AfterJobAsync(JobExecution jobExecution, CancellationToken cancellationToken);
}
