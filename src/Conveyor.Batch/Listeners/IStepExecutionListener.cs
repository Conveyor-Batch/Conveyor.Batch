using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Listeners;

/// <summary>
/// Receives notifications at the start and end of a step execution.
/// </summary>
public interface IStepExecutionListener
{
    /// <summary>Called before the step begins executing.</summary>
    ValueTask BeforeStepAsync(StepExecution stepExecution, CancellationToken cancellationToken);

    /// <summary>Called after the step has finished (successfully or not).</summary>
    ValueTask AfterStepAsync(StepExecution stepExecution, CancellationToken cancellationToken);
}
