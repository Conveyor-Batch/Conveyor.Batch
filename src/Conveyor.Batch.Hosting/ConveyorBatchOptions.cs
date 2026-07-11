using Conveyor.Batch.Core.Job;

namespace Conveyor.Batch.Hosting;

/// <summary>
/// Configures Conveyor.Batch services registered via <see cref="ServiceCollectionExtensions"/>'s
/// <c>AddConveyorBatch</c> extension methods.
/// </summary>
public sealed class ConveyorBatchOptions
{
    /// <summary>
    /// How often the launcher writes a heartbeat for a running job execution. Default: 30 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = HeartbeatOptions.Default.Interval;
}
