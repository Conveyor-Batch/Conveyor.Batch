using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Testing;

/// <summary>
/// An <see cref="IItemWriter{TOutput}"/> that captures each committed chunk in memory, useful
/// for asserting on the items written by a step or engine under test.
/// </summary>
/// <typeparam name="T">The type of item to capture.</typeparam>
public sealed class InMemoryItemWriter<T> : IItemWriter<T>
{
    private readonly List<IReadOnlyList<T>> _chunks = [];

    /// <summary>Gets the chunks written so far, in the order they were committed.</summary>
    public IReadOnlyList<IReadOnlyList<T>> Chunks => _chunks;

    /// <summary>Gets all items written so far, flattened across all committed chunks.</summary>
    public IEnumerable<T> AllItems => _chunks.SelectMany(chunk => chunk);

    /// <inheritdoc />
    public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        _chunks.Add(items.ToList());
        return ValueTask.CompletedTask;
    }
}
