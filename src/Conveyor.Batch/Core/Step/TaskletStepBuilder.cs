using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// Fluent builder for constructing a tasklet-based <see cref="IStep"/>.
/// </summary>
public sealed class TaskletStepBuilder
{
    private readonly IJobRepository _repository;
    private ITasklet? _tasklet;

    /// <summary>
    /// Initializes a new <see cref="TaskletStepBuilder"/> with the given repository.
    /// </summary>
    /// <param name="repository">The job repository used to persist step execution state.</param>
    public TaskletStepBuilder(IJobRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>Sets the tasklet to execute.</summary>
    /// <param name="tasklet">The tasklet implementation.</param>
    /// <returns>This builder for chaining.</returns>
    public TaskletStepBuilder Tasklet(ITasklet tasklet)
    {
        ArgumentNullException.ThrowIfNull(tasklet);
        _tasklet = tasklet;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured tasklet <see cref="IStep"/>.
    /// </summary>
    /// <param name="name">The unique name of the step within its job.</param>
    /// <returns>A new <see cref="IStep"/> backed by a <see cref="TaskletEngine"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no tasklet has been configured.</exception>
    public IStep Build(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_tasklet is null)
            throw new InvalidOperationException("A tasklet must be configured via Tasklet().");

        return new TaskletStep(name, new TaskletEngine(_tasklet), _repository);
    }
}

internal sealed class TaskletStep : IStep
{
    private readonly TaskletEngine _engine;
    private readonly IJobRepository _repository;

    public string Name { get; }

    internal TaskletStep(string name, TaskletEngine engine, IJobRepository repository)
    {
        Name = name;
        _engine = engine;
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
    {
        var stepExecution = await _repository.CreateStepExecutionAsync(jobExecution, Name).ConfigureAwait(false);
        stepExecution.Status = BatchStatus.Started;
        await _repository.UpdateStepExecutionAsync(stepExecution).ConfigureAwait(false);

        var context = new StepExecutionContext(stepExecution);

        try
        {
            await _engine.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            stepExecution.Status = BatchStatus.Completed;
        }
        catch (Exception ex)
        {
            stepExecution.Status = BatchStatus.Failed;
            stepExecution.FailureException = ex;
        }
        finally
        {
            stepExecution.EndTime = DateTimeOffset.UtcNow;
            await _repository.UpdateStepExecutionAsync(stepExecution).ConfigureAwait(false);
        }

        return stepExecution;
    }
}
