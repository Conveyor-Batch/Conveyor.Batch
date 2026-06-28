namespace Conveyor.Batch.Policies;

/// <summary>
/// Determines whether a given exception should cause the current item to be skipped
/// rather than aborting the entire step.
/// </summary>
public interface ISkipPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> if the item that caused <paramref name="exception"/>
    /// should be skipped and processing should continue.
    /// </summary>
    /// <param name="exception">The exception thrown during item processing.</param>
    /// <param name="skipCount">The number of items already skipped in this step.</param>
    bool ShouldSkip(Exception exception, long skipCount);
}
