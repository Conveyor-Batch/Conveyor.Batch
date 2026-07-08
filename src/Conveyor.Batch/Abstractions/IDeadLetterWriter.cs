namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Persists <see cref="DeadLetterEntry"/> records for items skipped during chunk-oriented
/// processing, so they remain inspectable rather than silently disappearing.
/// </summary>
public interface IDeadLetterWriter
{
    /// <summary>
    /// Writes a single dead-lettered entry to the destination.
    /// </summary>
    /// <param name="entry">The dead-letter entry to write.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask WriteAsync(DeadLetterEntry entry, CancellationToken cancellationToken);
}
