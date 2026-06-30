using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Testing;

/// <summary>
/// An <see cref="IItemProcessor{TInput,TOutput}"/> backed by a delegate, useful for testing
/// step and engine behavior without writing a dedicated processor class.
/// </summary>
/// <typeparam name="TIn">The type of item to transform.</typeparam>
/// <typeparam name="TOut">The type of the transformed output.</typeparam>
public sealed class FuncProcessor<TIn, TOut> : IItemProcessor<TIn, TOut>
{
    private readonly Func<TIn, StepExecutionContext, CancellationToken, ValueTask<TOut?>> _func;

    /// <summary>
    /// Initializes a new <see cref="FuncProcessor{TIn,TOut}"/> backed by an asynchronous delegate.
    /// </summary>
    /// <param name="func">The delegate invoked to process each item.</param>
    public FuncProcessor(Func<TIn, StepExecutionContext, CancellationToken, ValueTask<TOut?>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        _func = func;
    }

    /// <summary>
    /// Initializes a new <see cref="FuncProcessor{TIn,TOut}"/> backed by a synchronous delegate.
    /// </summary>
    /// <param name="func">The delegate invoked to process each item.</param>
    public FuncProcessor(Func<TIn, StepExecutionContext, CancellationToken, TOut?> func)
        : this(WrapSync(func))
    {
    }

    private static Func<TIn, StepExecutionContext, CancellationToken, ValueTask<TOut?>> WrapSync(
        Func<TIn, StepExecutionContext, CancellationToken, TOut?> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return (item, context, cancellationToken) => ValueTask.FromResult(func(item, context, cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<TOut?> ProcessAsync(TIn item, StepExecutionContext context, CancellationToken cancellationToken) =>
        _func(item, context, cancellationToken);
}
