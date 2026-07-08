using System.Collections.Concurrent;
using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Listeners;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDeadLetterWriter"/> that captures
/// entries in a <see cref="ConcurrentQueue{T}"/>. Suitable for tests and single-process
/// inspection scenarios without persistence.
/// </summary>
public sealed class InMemoryDeadLetterWriter : IDeadLetterWriter
{
    private readonly ConcurrentQueue<DeadLetterEntry> _entries = new();

    /// <summary>Gets the entries captured so far.</summary>
    public IReadOnlyCollection<DeadLetterEntry> Entries => _entries;

    /// <inheritdoc />
    public ValueTask WriteAsync(DeadLetterEntry entry, CancellationToken cancellationToken)
    {
        _entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }
}
