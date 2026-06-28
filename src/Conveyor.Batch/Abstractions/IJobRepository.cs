using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Persists and retrieves job and step execution state.
/// </summary>
public interface IJobRepository
{
    /// <summary>Creates a new job instance for the given job name and parameters.</summary>
    Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters);

    /// <summary>Creates a new execution record for the given job instance.</summary>
    Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters);

    /// <summary>Persists the current state of a job execution.</summary>
    Task UpdateJobExecutionAsync(JobExecution execution);

    /// <summary>Creates a new step execution record associated with the given job execution.</summary>
    Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName);

    /// <summary>Persists the current state of a step execution.</summary>
    Task UpdateStepExecutionAsync(StepExecution stepExecution);

    /// <summary>
    /// Returns the most recent execution for the given job name and parameters,
    /// or <see langword="null"/> if none exists.
    /// </summary>
    Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters);

    /// <summary>Returns all executions for the given job instance.</summary>
    Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance);
}
