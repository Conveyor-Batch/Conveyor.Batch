using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Persists and retrieves job and step execution state.
/// </summary>
/// <remarks>
/// Every method accepts a <see cref="CancellationToken"/>. It defaults to
/// <see langword="default"/> so that existing callers written before this parameter was added
/// keep compiling unchanged during the beta period; implementations that persist to an external
/// store (e.g. an EF Core-backed repository) should honor it on every underlying async call.
/// </remarks>
public interface IJobRepository
{
    /// <summary>Creates a new job instance for the given job name and parameters.</summary>
    Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Creates a new execution record for the given job instance.</summary>
    Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Persists the current state of a job execution.</summary>
    Task UpdateJobExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default);

    /// <summary>Creates a new step execution record associated with the given job execution.</summary>
    Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName, CancellationToken cancellationToken = default);

    /// <summary>Persists the current state of a step execution.</summary>
    Task UpdateStepExecutionAsync(StepExecution stepExecution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent execution for the given job name and parameters,
    /// or <see langword="null"/> if none exists.
    /// </summary>
    Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Returns all executions for the given job instance.</summary>
    Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the step execution for the given job execution and step name,
    /// or <see langword="null"/> if none exists.
    /// </summary>
    Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent execution that is currently in progress (status
    /// <see cref="BatchStatus.Started"/>) for the given job name and parameters,
    /// or <see langword="null"/> if none is currently running.
    /// </summary>
    /// <param name="jobName">The name of the job.</param>
    /// <param name="parameters">The parameters identifying the execution to look for.</param>
    /// <param name="cancellationToken">Token to cancel the lookup.</param>
    Task<JobExecution?> GetRunningJobExecutionAsync(
        string jobName,
        JobParameters parameters,
        CancellationToken cancellationToken = default);
}
