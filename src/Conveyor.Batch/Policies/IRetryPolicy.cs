namespace Conveyor.Batch.Policies;

/// <summary>
/// Adapter interface for retry behavior. Implementations wrap Polly or another
/// retry library without introducing a hard dependency on it in the core package.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Executes <paramref name="operation"/> with retry semantics defined by the implementation.
    /// </summary>
    /// <param name="operation">The async operation to execute with retries.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken);
}
