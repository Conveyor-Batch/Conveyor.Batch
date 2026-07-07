using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Guards against the same job identity (name + parameters) running concurrently across
/// multiple processes. Complements the in-repository check performed via
/// <see cref="IJobRepository.GetRunningJobExecutionAsync"/>, which only guards a single
/// shared repository.
/// </summary>
public interface IJobLockProvider
{
    /// <summary>
    /// Attempts to acquire an exclusive lock for the given job identity.
    /// Returns immediately — does not wait if the lock is held.
    /// </summary>
    /// <param name="jobName">The name of the job to lock.</param>
    /// <param name="parameters">The parameters identifying the specific execution to lock.</param>
    /// <param name="cancellationToken">Token to cancel the acquisition attempt.</param>
    /// <returns>
    /// An <see cref="IJobLock"/> whose <see cref="IJobLock.IsAcquired"/> indicates whether the
    /// lock was obtained. The caller must dispose the returned handle to release the lock.
    /// </returns>
    Task<IJobLock> TryAcquireAsync(
        string jobName,
        JobParameters parameters,
        CancellationToken cancellationToken);
}
