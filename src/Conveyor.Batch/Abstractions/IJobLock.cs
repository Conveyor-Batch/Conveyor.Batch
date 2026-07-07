namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Represents a handle to an exclusive job execution lock, acquired via
/// <see cref="IJobLockProvider.TryAcquireAsync"/>. Disposing the handle releases the lock,
/// regardless of whether it was successfully acquired.
/// </summary>
public interface IJobLock : IAsyncDisposable
{
    /// <summary>Whether this instance successfully acquired the lock.</summary>
    bool IsAcquired { get; }
}
