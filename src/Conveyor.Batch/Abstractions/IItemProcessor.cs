using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Transforms a single input item into an output item.
/// Returning <see langword="null"/> filters the item from the output chunk.
/// </summary>
/// <typeparam name="TInput">The type of item to transform.</typeparam>
/// <typeparam name="TOutput">The type of the transformed output.</typeparam>
public interface IItemProcessor<in TInput, TOutput>
{
    /// <summary>
    /// Processes a single item, returning the transformed result or <see langword="null"/> to skip it.
    /// </summary>
    /// <param name="item">The item to process.</param>
    /// <param name="context">The current step execution context.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<TOutput?> ProcessAsync(TInput item, StepExecutionContext context, CancellationToken cancellationToken);
}
