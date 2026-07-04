using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job.Flow;

/// <summary>
/// An <see cref="IJob"/> that executes a directed graph of steps, following
/// <see cref="TransitionRule"/>s configured via <see cref="FluentJobBuilder"/> to decide which
/// step runs next based on the previous step's exit status.
/// </summary>
internal sealed class FlowJob : IJob
{
    private readonly IStep _startStep;
    private readonly IReadOnlyDictionary<IStep, StepTransitions> _transitions;
    private readonly IJobRepository _repository;

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Initializes a new <see cref="FlowJob"/>.
    /// </summary>
    /// <param name="name">The unique name of the job.</param>
    /// <param name="startStep">The first step to execute.</param>
    /// <param name="transitions">
    /// The validated transition graph: for every registered step, the rules describing what to do
    /// for each possible exit status.
    /// </param>
    /// <param name="repository">The repository used to persist execution state.</param>
    internal FlowJob(
        string name,
        IStep startStep,
        IReadOnlyDictionary<IStep, StepTransitions> transitions,
        IJobRepository repository)
    {
        Name = name;
        _startStep = startStep;
        _transitions = transitions;
        _repository = repository;
    }

    /// <summary>
    /// Executes the job, walking the transition graph from the start step until a terminal
    /// action (<see cref="FlowAction.End"/>, <see cref="FlowAction.Fail"/>, or
    /// <see cref="FlowAction.Stop"/>) is reached.
    /// </summary>
    /// <param name="parameters">The parameters for this job execution.</param>
    /// <param name="cancellationToken">Token to cancel the job.</param>
    /// <returns>The final <see cref="JobExecution"/>, persisted with its terminal status.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a step's exit status does not match any configured transition rule. The job
    /// execution is persisted with <see cref="BatchStatus.Failed"/> before this exception is thrown.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="cancellationToken"/> is cancelled before or during a step's
    /// execution. The job execution is persisted with <see cref="BatchStatus.Stopped"/> before
    /// this exception propagates.
    /// </exception>
    public async Task<JobExecution> ExecuteAsync(JobParameters parameters, CancellationToken cancellationToken)
    {
        var instance = await _repository.CreateJobInstanceAsync(Name, parameters).ConfigureAwait(false);
        var execution = await _repository.CreateJobExecutionAsync(instance, parameters).ConfigureAwait(false);

        execution.Status = BatchStatus.Started;
        await _repository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);

        var current = _startStep;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stepExecution = await current.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);
                var exitStatus = ToExitStatus(stepExecution.Status);
                var rule = _transitions[current].Match(exitStatus);

                if (rule is null)
                {
                    var error = new InvalidOperationException(
                        $"No transition defined for step '{current.Name}' with exit status '{exitStatus}'.");
                    await FinishAsync(execution, BatchStatus.Failed, error).ConfigureAwait(false);
                    throw error;
                }

                switch (rule.Action)
                {
                    case FlowAction.Continue:
                        current = rule.NextStep!;
                        continue;

                    case FlowAction.End:
                        await FinishAsync(execution, stepExecution.Status).ConfigureAwait(false);
                        return execution;

                    case FlowAction.Fail:
                        await FinishAsync(execution, BatchStatus.Failed, stepExecution.FailureException)
                            .ConfigureAwait(false);
                        return execution;

                    default: // FlowAction.Stop
                        await FinishAsync(execution, BatchStatus.Stopped).ConfigureAwait(false);
                        return execution;
                }
            }
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(execution, BatchStatus.Stopped).ConfigureAwait(false);
            throw;
        }
    }

    private async Task FinishAsync(JobExecution execution, BatchStatus status, Exception? failureException = null)
    {
        execution.Status = status;
        execution.FailureException = failureException;
        execution.EndTime = DateTimeOffset.UtcNow;
        await _repository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps a step's terminal <see cref="BatchStatus"/> to the exit status string used to match
    /// <see cref="TransitionRule.OnStatus"/>.
    /// </summary>
    /// <param name="status">The step execution's final status.</param>
    /// <returns>
    /// <c>"COMPLETED"</c>, <c>"FAILED"</c>, or <c>"STOPPED"</c> for the corresponding
    /// <see cref="BatchStatus"/> values; otherwise the upper-invariant name of the status, so that
    /// an explicit rule or a wildcard rule can still match it.
    /// </returns>
    private static string ToExitStatus(BatchStatus status) => status switch
    {
        BatchStatus.Completed => "COMPLETED",
        BatchStatus.Failed => "FAILED",
        BatchStatus.Stopped => "STOPPED",
        _ => status.ToString().ToUpperInvariant()
    };
}
