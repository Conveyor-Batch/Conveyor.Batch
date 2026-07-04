using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Writers;

/// <summary>
/// Fans out a committed chunk to multiple <see cref="IItemWriter{T}"/> instances,
/// invoking each in sequence with the same items.
/// </summary>
/// <typeparam name="T">The type of item to write.</typeparam>
/// <remarks>
/// Writers run sequentially, not in parallel, to avoid ordering issues and keep individual
/// writers simple. If any writer throws, the remaining writers are skipped and the exception
/// propagates to the caller.
/// </remarks>
public sealed class CompositeItemWriter<T> : IItemWriter<T>
{
    private readonly IReadOnlyList<IItemWriter<T>> _writers;

    /// <summary>
    /// Initializes a new <see cref="CompositeItemWriter{T}"/> that writes to the given
    /// writers in order.
    /// </summary>
    /// <param name="writers">The writers to fan out to, in execution order.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="writers"/> is empty.</exception>
    public CompositeItemWriter(IEnumerable<IItemWriter<T>> writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        var list = writers.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one writer must be provided.", nameof(writers));

        _writers = list;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var writer in _writers)
        {
            await writer.WriteAsync(items, context, cancellationToken).ConfigureAwait(false);
        }
    }
}
