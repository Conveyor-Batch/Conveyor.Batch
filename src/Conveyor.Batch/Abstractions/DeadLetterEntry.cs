namespace Conveyor.Batch.Abstractions;

/// <summary>
/// A record of an item that was skipped during chunk-oriented processing, captured so it
/// remains inspectable rather than silently disappearing.
/// </summary>
public sealed class DeadLetterEntry
{
    /// <summary>Gets the name of the job the item was being processed by.</summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>Gets the name of the step the item was being processed by.</summary>
    public string StepName { get; init; } = string.Empty;

    /// <summary>Gets the original item, serialized as JSON via <see cref="System.Text.Json.JsonSerializer"/>.</summary>
    public string ItemJson { get; init; } = string.Empty;

    /// <summary>Gets the fully qualified type name of the original item.</summary>
    public string ItemTypeName { get; init; } = string.Empty;

    /// <summary>Gets the fully qualified type name of the exception that caused the skip.</summary>
    public string ExceptionType { get; init; } = string.Empty;

    /// <summary>Gets the message of the exception that caused the skip.</summary>
    public string ExceptionMessage { get; init; } = string.Empty;

    /// <summary>Gets the stack trace of the exception that caused the skip, if available.</summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// Gets the number of items already skipped in the step before this one. Exact and
    /// sequential when items are processed one at a time, but only a best-effort, potentially
    /// racy snapshot when multiple worker tasks can skip items concurrently.
    /// </summary>
    public long SkipCountAtTime { get; init; }

    /// <summary>Gets the UTC time at which the item was skipped.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
