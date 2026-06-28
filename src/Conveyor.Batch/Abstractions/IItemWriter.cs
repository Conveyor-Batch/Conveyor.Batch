using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Writes a committed chunk of processed output items.
/// </summary>
/// <typeparam name="TOutput">The type of item to write.</typeparam>
public interface IItemWriter<in TOutput>
{
    /// <summary>
    /// Writes a chunk of items to the output destination.
    /// </summary>
    /// <param name="items">The committed chunk of items to write.</param>
    /// <param name="context">The current step execution context.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask WriteAsync(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken);
}
