using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Fluent builder for constructing an <see cref="IJob"/> that executes a sequence of steps.
/// </summary>
public sealed class JobBuilder
{
    private readonly string _name;
    private readonly List<IStep> _steps = [];
    private readonly IJobRepository _repository;

    /// <summary>
    /// Initializes a new <see cref="JobBuilder"/> with the given job name and repository.
    /// </summary>
    /// <param name="name">The unique name of the job.</param>
    /// <param name="repository">The job repository used to persist execution state.</param>
    public JobBuilder(string name, IJobRepository repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(repository);
        _name = name;
        _repository = repository;
    }

    /// <summary>
    /// Appends a step to the job's execution sequence.
    /// </summary>
    /// <param name="step">The step to add.</param>
    /// <returns>This builder for chaining.</returns>
    public JobBuilder AddStep(IStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        return this;
    }

    /// <summary>
    /// Builds and returns the configured <see cref="IJob"/>.
    /// </summary>
    /// <returns>A new <see cref="IJob"/> that executes all configured steps in order.</returns>
    public IJob Build() => new SequentialJob(_name, [.. _steps], _repository);
}

internal sealed class SequentialJob : IJob
{
    private readonly IReadOnlyList<IStep> _steps;
    private readonly IJobRepository _repository;

    public string Name { get; }

    internal SequentialJob(string name, IReadOnlyList<IStep> steps, IJobRepository repository)
    {
        Name = name;
        _steps = steps;
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
    {
        var instance = await _repository.CreateJobInstanceAsync(Name, parameters).ConfigureAwait(false);
        var execution = await _repository.CreateJobExecutionAsync(instance, parameters).ConfigureAwait(false);

        execution.Status = BatchStatus.Started;
        await _repository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);

        try
        {
            foreach (var step in _steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepExecution = await step.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);

                if (stepExecution.Status == BatchStatus.Failed)
                {
                    execution.Status = BatchStatus.Failed;
                    execution.FailureException = stepExecution.FailureException;
                    execution.EndTime = DateTimeOffset.UtcNow;
                    await _repository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);
                    return execution;
                }
            }

            execution.Status = BatchStatus.Completed;
        }
        catch (Exception ex)
        {
            execution.Status = BatchStatus.Failed;
            execution.FailureException = ex;
        }
        finally
        {
            execution.EndTime = DateTimeOffset.UtcNow;
            await _repository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);
        }

        return execution;
    }
}
