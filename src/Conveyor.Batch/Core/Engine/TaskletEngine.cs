using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Engine;

/// <summary>
/// Drives execution of an <see cref="ITasklet"/>, calling it repeatedly until it returns
/// <see cref="RepeatStatus.Finished"/> or the cancellation token is signalled.
/// </summary>
public sealed class TaskletEngine
{
    private readonly ITasklet _tasklet;

    /// <summary>Initializes the engine with the tasklet to execute.</summary>
    public TaskletEngine(ITasklet tasklet)
    {
        _tasklet = tasklet;
    }

    /// <summary>
    /// Runs the tasklet until it signals <see cref="RepeatStatus.Finished"/>.
    /// </summary>
    /// <param name="context">The step execution context.</param>
    /// <param name="cancellationToken">Token to cancel execution.</param>
    public async Task ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        RepeatStatus status;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = await _tasklet.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        while (status == RepeatStatus.Continuable);
    }
}
