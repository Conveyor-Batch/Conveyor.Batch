using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Default <see cref="IJobLockProvider"/> for single-process deployments, where no cross-process
/// coordination is needed. Always reports the lock as acquired.
/// </summary>
public sealed class NoOpJobLockProvider : IJobLockProvider
{
    /// <summary>The shared singleton instance.</summary>
    public static readonly NoOpJobLockProvider Instance = new();

    /// <inheritdoc />
    public Task<IJobLock> TryAcquireAsync(
        string jobName,
        JobParameters parameters,
        CancellationToken cancellationToken) =>
        Task.FromResult<IJobLock>(AlwaysAcquiredLock.Instance);

    private sealed class AlwaysAcquiredLock : IJobLock
    {
        public static readonly AlwaysAcquiredLock Instance = new();

        public bool IsAcquired => true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
