using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Listeners;

/// <summary>
/// Receives notifications at key points during chunk-oriented step processing.
/// </summary>
public interface IChunkListener
{
    /// <summary>Called before a new chunk begins processing.</summary>
    ValueTask BeforeChunkAsync(StepExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Called after a chunk has been fully processed and written.</summary>
    ValueTask AfterChunkAsync(StepExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Called when an error occurs during chunk processing.</summary>
    ValueTask OnChunkErrorAsync(StepExecutionContext context, Exception exception, CancellationToken cancellationToken);

    /// <summary>Called immediately before the writer receives a completed chunk.</summary>
    ValueTask BeforeWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Called immediately after the writer successfully writes a chunk.</summary>
    ValueTask AfterWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Called when an item is skipped due to a skip policy decision.</summary>
    ValueTask OnSkipAsync<TInput>(TInput item, Exception exception, StepExecutionContext context, CancellationToken cancellationToken);
}
