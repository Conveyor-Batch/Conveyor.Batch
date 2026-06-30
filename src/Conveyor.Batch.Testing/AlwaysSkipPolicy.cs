using Conveyor.Batch.Policies;

namespace Conveyor.Batch.Testing;

/// <summary>
/// An <see cref="ISkipPolicy"/> that always skips, useful for testing skip-handling behavior
/// without needing to model a realistic skip threshold.
/// </summary>
public sealed class AlwaysSkipPolicy : ISkipPolicy
{
    /// <inheritdoc />
    public bool ShouldSkip(Exception exception, long skipCount) => true;
}
