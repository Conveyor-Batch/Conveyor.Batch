namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Configures periodic heartbeat writes for a long-running job execution. When enabled, a
/// <see cref="SimpleJobLauncher"/> updates <see cref="JobExecution.LastHeartbeatAt"/> on this
/// interval for the duration of the run, letting operators detect stuck jobs by alerting on
/// "no heartbeat in N minutes".
/// </summary>
public sealed class HeartbeatOptions
{
    /// <summary>How often to write a heartbeat. Default: 30 seconds.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The default options instance.</summary>
    public static readonly HeartbeatOptions Default = new();
}
