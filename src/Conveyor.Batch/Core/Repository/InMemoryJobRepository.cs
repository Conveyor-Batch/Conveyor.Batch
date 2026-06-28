using System.Collections.Concurrent;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Repository;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IJobRepository"/>.
/// Suitable for testing and single-process scenarios without persistence.
/// </summary>
internal sealed class InMemoryJobRepository : IJobRepository
{
    private long _instanceIdCounter;
    private long _executionIdCounter;
    private long _stepExecutionIdCounter;

    private readonly ConcurrentDictionary<long, JobInstance> _instances = new();
    private readonly ConcurrentDictionary<long, JobExecution> _executions = new();
    private readonly ConcurrentDictionary<long, StepExecution> _stepExecutions = new();

    /// <inheritdoc />
    public Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters)
    {
        var instance = new JobInstance
        {
            Id = Interlocked.Increment(ref _instanceIdCounter),
            JobName = jobName,
            Parameters = parameters
        };
        _instances[instance.Id] = instance;
        return Task.FromResult(instance);
    }

    /// <inheritdoc />
    public Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters)
    {
        var execution = new JobExecution
        {
            Id = Interlocked.Increment(ref _executionIdCounter),
            JobInstance = instance,
            Parameters = parameters,
            Status = BatchStatus.Starting
        };
        _executions[execution.Id] = execution;
        return Task.FromResult(execution);
    }

    /// <inheritdoc />
    public Task UpdateJobExecutionAsync(JobExecution execution)
    {
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName)
    {
        var stepExecution = new StepExecution
        {
            Id = Interlocked.Increment(ref _stepExecutionIdCounter),
            StepName = stepName,
            JobExecution = jobExecution,
            Status = BatchStatus.Starting
        };
        _stepExecutions[stepExecution.Id] = stepExecution;
        return Task.FromResult(stepExecution);
    }

    /// <inheritdoc />
    public Task UpdateStepExecutionAsync(StepExecution stepExecution)
    {
        _stepExecutions[stepExecution.Id] = stepExecution;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters)
    {
        var last = _executions.Values
            .Where(e => e.JobInstance.JobName == jobName && e.JobInstance.Parameters == parameters)
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefault();
        return Task.FromResult(last);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance)
    {
        IReadOnlyList<JobExecution> result = _executions.Values
            .Where(e => e.JobInstance.Id == instance.Id)
            .OrderBy(e => e.StartTime)
            .ToList();
        return Task.FromResult(result);
    }
}
