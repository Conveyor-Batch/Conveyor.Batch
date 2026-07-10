namespace Conveyor.Batch.Core.Engine;

/// <summary>
/// Configures graceful shutdown behavior for a chunk-oriented engine. When a stop is
/// requested (e.g. via <c>BatchJobHostedService</c> on SIGTERM), the engine stops reading new
/// items but keeps processing and writing the current partial chunk so it can commit cleanly
/// within the configured drain window.
/// </summary>
public sealed class GracefulShutdownOptions
{
    /// <summary>
    /// How long to wait for the current chunk to finish committing
    /// after a stop is requested before forcing an abort.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The default options instance.</summary>
    public static readonly GracefulShutdownOptions Default = new();
}
