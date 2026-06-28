using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// A simple, non-chunk unit of work executed by a tasklet step.
/// </summary>
public interface ITasklet
{
    /// <summary>
    /// Executes the tasklet and returns whether to continue or finish.
    /// </summary>
    /// <param name="context">The current step execution context.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<RepeatStatus> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken);
}
