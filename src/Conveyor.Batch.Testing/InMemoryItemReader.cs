using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Testing;

/// <summary>
/// An <see cref="IItemReader{TInput}"/> backed by an in-memory sequence, useful for testing
/// steps and engines without an external data source.
/// </summary>
/// <typeparam name="T">The type of item produced by this reader.</typeparam>
public sealed class InMemoryItemReader<T> : IItemReader<T>
{
    private readonly IEnumerable<T> _items;

    /// <summary>
    /// Initializes a new <see cref="InMemoryItemReader{T}"/> over the given sequence of items.
    /// </summary>
    /// <param name="items">The items to yield, in order.</param>
    public InMemoryItemReader(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (T item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }
}
