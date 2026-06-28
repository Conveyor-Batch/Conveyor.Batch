using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Reads input items as an async stream for processing by a batch step.
/// </summary>
/// <typeparam name="TInput">The type of item produced by this reader.</typeparam>
public interface IItemReader<out TInput>
{
    /// <summary>
    /// Returns an async stream of input items.
    /// </summary>
    /// <param name="context">The current step execution context.</param>
    /// <param name="cancellationToken">Token to cancel the read operation.</param>
    IAsyncEnumerable<TInput> ReadAsync(StepExecutionContext context, CancellationToken cancellationToken);
}
