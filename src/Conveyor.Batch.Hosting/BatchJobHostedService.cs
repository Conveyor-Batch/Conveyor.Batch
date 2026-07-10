using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conveyor.Batch.Hosting;

/// <summary>
/// An <see cref="IHostedService"/> that launches a single <see cref="IJob"/> on application
/// startup and cancels it gracefully on shutdown.
/// </summary>
public sealed class BatchJobHostedService : IHostedService
{
    private readonly IJobLauncher _jobLauncher;
    private readonly IJob _job;
    private readonly ILogger<BatchJobHostedService>? _logger;

    private CancellationTokenSource? _cts;
    private Task? _jobTask;

    /// <summary>
    /// How long <see cref="StopAsync"/> waits for the running job to drain after signalling its
    /// stop token before giving up and returning control to the host. Does not itself force-abort
    /// the job — the actual drain deadline enforced against the engine is
    /// <see cref="Conveyor.Batch.Core.Engine.GracefulShutdownOptions.DrainTimeout"/>, configured
    /// per-step via <see cref="Conveyor.Batch.Core.Step.StepBuilder{TInput,TOutput}.GracefulShutdown"/>.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initialises a new instance of <see cref="BatchJobHostedService"/>.
    /// </summary>
    /// <param name="jobLauncher">The launcher used to start the job.</param>
    /// <param name="job">The job to run on startup.</param>
    /// <param name="logger">Optional logger for lifecycle events.</param>
    public BatchJobHostedService(
        IJobLauncher jobLauncher,
        IJob job,
        ILogger<BatchJobHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(jobLauncher);
        ArgumentNullException.ThrowIfNull(job);

        _jobLauncher = jobLauncher;
        _job = job;
        _logger = logger;
    }

    /// <summary>
    /// Launches the job in the background. The returned <see cref="Task"/> completes once the
    /// job has been started (not necessarily finished).
    /// </summary>
    /// <param name="cancellationToken">Token signalled when the host is aborting startup.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _logger?.LogInformation("Starting batch job '{JobName}'.", _job.Name);

        _jobTask = RunJobAsync(_cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Signals the running job's stop token and waits for it to drain, bounded by whichever of
    /// <paramref name="cancellationToken"/> (the host's own shutdown deadline) or
    /// <see cref="ShutdownTimeout"/> elapses first.
    /// </summary>
    /// <param name="cancellationToken">Token signalled when the host stop deadline is exceeded.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_jobTask is null)
        {
            return;
        }

        _logger?.LogInformation("Stopping batch job '{JobName}'.", _job.Name);

        try
        {
            await _cts!.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ShutdownTimeout);

            var completed = await Task.WhenAny(_jobTask, Task.Delay(Timeout.Infinite, timeoutCts.Token))
                .ConfigureAwait(false);

            if (completed == _jobTask)
                _logger?.LogInformation("Batch job '{JobName}' stopped gracefully.", _job.Name);
            else
                _logger?.LogWarning(
                    "Batch job '{JobName}' did not stop within the shutdown timeout of {ShutdownTimeout}.",
                    _job.Name, ShutdownTimeout);

            _cts?.Dispose();
        }
    }

    private async Task RunJobAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _jobLauncher.RunAsync(_job, JobParameters.Empty, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation("Batch job '{JobName}' completed successfully.", _job.Name);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Batch job '{JobName}' was cancelled.", _job.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Batch job '{JobName}' failed with an unhandled exception.", _job.Name);
        }
    }
}
