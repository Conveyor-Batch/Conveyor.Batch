using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Default implementation of <see cref="IJobLauncher"/> that executes jobs synchronously
/// within the caller's context. Suitable for single-process and hosted scenarios.
/// </summary>
internal sealed class SimpleJobLauncher : IJobLauncher
{
    private readonly IJobRepository _jobRepository;

    /// <summary>
    /// Initialises a new instance of <see cref="SimpleJobLauncher"/>.
    /// </summary>
    /// <param name="jobRepository">The repository used to persist execution state.</param>
    public SimpleJobLauncher(IJobRepository jobRepository)
    {
        _jobRepository = jobRepository;
    }

    /// <inheritdoc />
    public async Task<JobExecution> RunAsync(
        IJob job,
        JobParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var instance = await _jobRepository.CreateJobInstanceAsync(job.Name, parameters).ConfigureAwait(false);
        var execution = await _jobRepository.CreateJobExecutionAsync(instance, parameters).ConfigureAwait(false);

        try
        {
            var result = await job.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);

            execution.Status = result.Status;
            execution.EndTime = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            execution.Status = BatchStatus.Stopped;
            execution.EndTime = DateTimeOffset.UtcNow;
            throw;
        }
        catch (Exception ex)
        {
            execution.Status = BatchStatus.Failed;
            execution.EndTime = DateTimeOffset.UtcNow;
            execution.FailureException = ex;
            throw;
        }
        finally
        {
            await _jobRepository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);
        }

        return execution;
    }
}
