namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Optional, opt-in lifecycle contract for restart-aware item readers. A reader implements
/// this interface <i>alongside</i> <see cref="IItemReader{TInput}"/> (not instead of it) to
/// participate in checkpointing: <see cref="OpenAsync"/> lets the reader seek to a
/// previously-saved position before <see cref="IItemReader{TInput}.ReadAsync"/> is first
/// called; <see cref="UpdateAsync"/> lets the reader save its current position into the
/// context after each committed chunk; <see cref="CloseAsync"/> releases any resources when
/// the step finishes, regardless of outcome. Readers that do not implement this interface are
/// simply not restartable — the engine skips the stream lifecycle for them and they behave
/// exactly as before.
/// </summary>
public interface IItemStream
{
    /// <summary>
    /// Called once before reading begins. If <paramref name="context"/> contains previously
    /// saved checkpoint state, the reader must seek to the saved position.
    /// </summary>
    /// <param name="context">The execution context to read saved checkpoint state from.</param>
    /// <param name="cancellationToken">Token to cancel the open operation.</param>
    ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called after every committed chunk. The reader saves its current position into
    /// <paramref name="context"/> so it can be restored on restart.
    /// </summary>
    /// <param name="context">The execution context to write current checkpoint state into.</param>
    /// <param name="cancellationToken">Token to cancel the update operation.</param>
    ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called when the step completes, whether it succeeded or failed.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the close operation.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);
}
