using System.Diagnostics;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Telemetry;
using Microsoft.Extensions.Logging;

namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Default implementation of <see cref="IJobLauncher"/> that executes jobs synchronously
/// within the caller's context. Suitable for single-process and hosted scenarios.
/// </summary>
internal sealed class SimpleJobLauncher : IJobLauncher
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobLockProvider _lockProvider;
    private readonly HeartbeatOptions? _heartbeat;
    private readonly ILogger<SimpleJobLauncher>? _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="SimpleJobLauncher"/>.
    /// </summary>
    /// <param name="jobRepository">The repository used to persist execution state.</param>
    /// <param name="lockProvider">
    /// The cross-process lock provider guarding against concurrent execution of the same job
    /// identity. Defaults to <see cref="NoOpJobLockProvider"/> for single-process deployments.
    /// </param>
    /// <param name="heartbeat">
    /// When supplied, periodically updates <see cref="JobExecution.LastHeartbeatAt"/> at
    /// <see cref="HeartbeatOptions.Interval"/> for the duration of the run. Defaults to
    /// <see langword="null"/> (heartbeat disabled).
    /// </param>
    /// <param name="logger">Optional logger used to report non-fatal heartbeat write failures.</param>
    public SimpleJobLauncher(
        IJobRepository jobRepository,
        IJobLockProvider? lockProvider = null,
        HeartbeatOptions? heartbeat = null,
        ILogger<SimpleJobLauncher>? logger = null)
    {
        _jobRepository = jobRepository;
        _lockProvider = lockProvider ?? NoOpJobLockProvider.Instance;
        _heartbeat = heartbeat;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JobExecution> RunAsync(
        IJob job,
        JobParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var running = await _jobRepository
            .GetRunningJobExecutionAsync(job.Name, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (running is not null)
            throw new InvalidOperationException(
                $"Job '{job.Name}' with the given parameters is already running " +
                $"(ExecutionId: {running.Id}). " +
                $"Wait for it to complete or use different parameters.");

        await using var jobLock = await _lockProvider
            .TryAcquireAsync(job.Name, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (!jobLock.IsAcquired)
            throw new InvalidOperationException(
                $"Could not acquire lock for job '{job.Name}'. " +
                "Another process may be running this job.");

        var instance = await _jobRepository.CreateJobInstanceAsync(job.Name, parameters).ConfigureAwait(false);
        var execution = await _jobRepository.CreateJobExecutionAsync(instance, parameters).ConfigureAwait(false);
        execution.Status = BatchStatus.Started;
        await _jobRepository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);

        var activity = ConveyorBatchTelemetry.ActivitySource.StartActivity(ConveyorBatchTelemetry.JobActivityName);
        activity?.SetTag(ConveyorBatchTelemetry.JobNameTag, job.Name);
        activity?.SetTag(ConveyorBatchTelemetry.JobExecutionIdTag, execution.Id);

        var stopwatch = Stopwatch.StartNew();

        CancellationTokenSource? heartbeatCts = null;
        Task? heartbeatTask = null;
        if (_heartbeat is not null)
        {
            // Independent of the caller's cancellationToken: the heartbeat must keep beating
            // (and must stop) based on the job's own lifetime, not on whether the caller ever
            // requests cancellation.
            heartbeatCts = new CancellationTokenSource();
            heartbeatTask = RunHeartbeatLoopAsync(execution, _heartbeat.Interval, heartbeatCts.Token);
        }

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
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            if (heartbeatCts is not null)
            {
                heartbeatCts.Cancel();
                await heartbeatTask!.ConfigureAwait(false);
                heartbeatCts.Dispose();
            }

            stopwatch.Stop();
            await _jobRepository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);

            activity?.SetTag(ConveyorBatchTelemetry.JobStatusTag, execution.Status.ToString());
            RecordJobMetrics(job.Name, execution.Status, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Stop();
        }

        return execution;
    }

    private async Task RunHeartbeatLoopAsync(JobExecution execution, TimeSpan interval, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                execution.LastHeartbeatAt = DateTimeOffset.UtcNow;
                await _jobRepository.UpdateJobExecutionAsync(execution).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Heartbeat update failed for job execution {JobExecutionId}", execution.Id);
                // Deliberately does not break/rethrow: a transient repository failure on one
                // heartbeat tick must not kill the loop or abort the job — the next tick retries.
            }
        }
    }

    private static void RecordJobMetrics(string jobName, BatchStatus status, double elapsedMilliseconds)
    {
        var nameTag = new TagList { { ConveyorBatchTelemetry.JobNameTag, jobName } };

        if (status == BatchStatus.Completed)
            ConveyorBatchTelemetry.JobsCompleted.Add(1, nameTag);
        else if (status == BatchStatus.Failed)
            ConveyorBatchTelemetry.JobsFailed.Add(1, nameTag);

        var durationTags = new TagList
        {
            { ConveyorBatchTelemetry.JobNameTag, jobName },
            { ConveyorBatchTelemetry.JobStatusTag, status.ToString() }
        };
        ConveyorBatchTelemetry.JobDuration.Record(elapsedMilliseconds, durationTags);
    }
}
