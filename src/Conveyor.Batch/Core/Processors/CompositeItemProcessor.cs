using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Processors;

/// <summary>
/// Chains multiple homogeneous <see cref="IItemProcessor{T,T}"/> instances in sequence,
/// passing the output of each processor as the input to the next.
/// </summary>
/// <typeparam name="T">The item type flowing through the chain.</typeparam>
/// <remarks>
/// If any processor in the chain returns <see langword="null"/>, the item is considered
/// filtered and the remaining processors in the chain are skipped entirely for that item.
/// </remarks>
public sealed class CompositeItemProcessor<T> : IItemProcessor<T, T>
{
    private readonly IReadOnlyList<IItemProcessor<T, T>> _processors;

    /// <summary>
    /// Initializes a new <see cref="CompositeItemProcessor{T}"/> that runs the given
    /// processors in order.
    /// </summary>
    /// <param name="processors">The processors to chain, in execution order.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="processors"/> is empty.</exception>
    public CompositeItemProcessor(IEnumerable<IItemProcessor<T, T>> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        var list = processors.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one processor must be provided.", nameof(processors));

        _processors = list;
    }

    /// <inheritdoc />
    public async ValueTask<T?> ProcessAsync(T item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        T? current = item;

        foreach (var processor in _processors)
        {
            current = await processor.ProcessAsync(current!, context, cancellationToken).ConfigureAwait(false);
            if (current is null)
                return default;
        }

        return current;
    }
}
