using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Job;

/// <summary>
/// A straightforward <see cref="IJobLauncher"/> that runs a job synchronously in the caller's context.
/// </summary>
internal sealed class SimpleJobLauncher : IJobLauncher
{
    private readonly IJobRepository _repository;

    /// <summary>
    /// Initializes a new <see cref="SimpleJobLauncher"/> with the given repository.
    /// </summary>
    /// <param name="repository">The job repository used to detect and prevent duplicate concurrent runs.</param>
    public SimpleJobLauncher(IJobRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<JobExecution> RunAsync(
        IJob job,
        JobParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var lastExecution = await _repository.GetLastJobExecutionAsync(job.Name, parameters).ConfigureAwait(false);

        if (lastExecution is { Status: BatchStatus.Started })
            throw new InvalidOperationException(
                $"Job '{job.Name}' is already running with the given parameters. " +
                "Stop the running instance before launching a new one.");

        return await job.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
    }
}
