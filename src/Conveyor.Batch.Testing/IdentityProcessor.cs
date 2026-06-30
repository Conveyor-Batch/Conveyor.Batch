using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Testing;

/// <summary>
/// An <see cref="IItemProcessor{TInput,TOutput}"/> that returns each item unchanged, useful as a
/// no-op processor in tests focused on reading or writing behavior.
/// </summary>
/// <typeparam name="T">The type of item passed through unchanged.</typeparam>
public sealed class IdentityProcessor<T> : IItemProcessor<T, T>
{
    /// <inheritdoc />
    public ValueTask<T?> ProcessAsync(T item, StepExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<T?>(item);
}
