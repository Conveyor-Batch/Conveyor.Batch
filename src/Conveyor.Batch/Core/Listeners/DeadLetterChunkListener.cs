using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;

namespace Conveyor.Batch.Core.Listeners;

/// <summary>
/// An <see cref="IChunkListener"/> that captures every skipped item as a
/// <see cref="DeadLetterEntry"/> and hands it to an <see cref="IDeadLetterWriter"/> sink,
/// so poison-pill items remain inspectable instead of silently disappearing.
/// </summary>
public sealed class DeadLetterChunkListener : IChunkListener
{
    private readonly IDeadLetterWriter _deadLetterWriter;

    /// <summary>
    /// Initializes a new <see cref="DeadLetterChunkListener"/>.
    /// </summary>
    /// <param name="deadLetterWriter">The sink that persists dead-lettered entries.</param>
    public DeadLetterChunkListener(IDeadLetterWriter deadLetterWriter)
    {
        ArgumentNullException.ThrowIfNull(deadLetterWriter);
        _deadLetterWriter = deadLetterWriter;
    }

    /// <inheritdoc />
    public ValueTask BeforeChunkAsync(StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask AfterChunkAsync(StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnChunkErrorAsync(StepExecutionContext context, Exception exception, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask BeforeWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask AfterWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="DeadLetterEntry.SkipCountAtTime"/> is exact and sequential under
    /// <see cref="Conveyor.Batch.Core.Engine.ChunkOrientedEngine{TInput,TOutput}"/>, but only a
    /// best-effort, potentially racy snapshot under
    /// <see cref="Conveyor.Batch.Core.Engine.ConcurrentChunkOrientedEngine{TInput,TOutput}"/>,
    /// which can invoke this method from multiple worker tasks concurrently.
    /// </remarks>
    public ValueTask OnSkipAsync<TInput>(TInput item, Exception exception, StepExecutionContext context, CancellationToken cancellationToken)
    {
        var entry = new DeadLetterEntry
        {
            JobName = context.StepExecution.JobExecution.JobInstance.JobName,
            StepName = context.StepName,
            ItemJson = SerializeItem(item),
            ItemTypeName = item?.GetType().FullName ?? typeof(TInput).FullName ?? typeof(TInput).Name,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace,
            SkipCountAtTime = context.SkipCount - 1,
            OccurredAt = DateTimeOffset.UtcNow
        };

        return _deadLetterWriter.WriteAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Serializes <paramref name="item"/> to JSON, falling back to a JSON object describing the
    /// serialization failure if the item's type cannot be serialized. A poison item that also
    /// happens to be unserializable must still be captured rather than turning into an unhandled
    /// exception that fails the whole step.
    /// </summary>
    private static string SerializeItem<TInput>(TInput item)
    {
        try
        {
            return JsonSerializer.Serialize(item, typeof(TInput));
        }
        catch (Exception serializationException)
        {
            return JsonSerializer.Serialize(new { SerializationError = serializationException.Message });
        }
    }
}
