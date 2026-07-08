using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;

namespace Conveyor.Batch.Core.Listeners;

/// <summary>
/// An <see cref="IChunkListener"/> that fans out every notification to a fixed sequence of
/// inner listeners, invoked one after another. Lets a step be configured with more than one
/// <see cref="IChunkListener"/> (e.g. a user's own listener alongside <see cref="DeadLetterChunkListener"/>)
/// without changing the engine's single-listener API.
/// </summary>
public sealed class CompositeChunkListener : IChunkListener
{
    private readonly IReadOnlyList<IChunkListener> _listeners;

    /// <summary>
    /// Initializes a new <see cref="CompositeChunkListener"/>.
    /// </summary>
    /// <param name="listeners">The listeners to notify, in invocation order.</param>
    public CompositeChunkListener(IEnumerable<IChunkListener> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        _listeners = [.. listeners];
    }

    /// <inheritdoc />
    /// <remarks>
    /// If a listener throws, the remaining listeners in the sequence are not invoked and the
    /// exception propagates to the caller.
    /// </remarks>
    public async ValueTask BeforeChunkAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await listener.BeforeChunkAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// If a listener throws, the remaining listeners in the sequence are not invoked and the
    /// exception propagates to the caller.
    /// </remarks>
    public async ValueTask AfterChunkAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await listener.AfterChunkAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// If a listener throws, the remaining listeners in the sequence are not invoked and the
    /// exception propagates to the caller.
    /// </remarks>
    public async ValueTask OnChunkErrorAsync(StepExecutionContext context, Exception exception, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await listener.OnChunkErrorAsync(context, exception, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// If a listener throws, the remaining listeners in the sequence are not invoked and the
    /// exception propagates to the caller.
    /// </remarks>
    public async ValueTask BeforeWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await listener.BeforeWriteAsync(items, context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// If a listener throws, the remaining listeners in the sequence are not invoked and the
    /// exception propagates to the caller.
    /// </remarks>
    public async ValueTask AfterWriteAsync<TOutput>(IReadOnlyList<TOutput> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await listener.AfterWriteAsync(items, context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// If a listener throws, the remaining listeners in the sequence are not invoked and the
    /// exception propagates to the caller.
    /// </remarks>
    public async ValueTask OnSkipAsync<TInput>(TInput item, Exception exception, StepExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await listener.OnSkipAsync(item, exception, context, cancellationToken).ConfigureAwait(false);
    }
}
