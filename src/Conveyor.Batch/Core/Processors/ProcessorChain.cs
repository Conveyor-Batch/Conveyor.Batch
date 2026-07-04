using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Core.Processors;

/// <summary>
/// Chains two heterogeneously-typed <see cref="IItemProcessor{TIn,TOut}"/> instances,
/// passing the output of the first as the input to the second.
/// </summary>
/// <typeparam name="TIn">The input type accepted by the first processor.</typeparam>
/// <typeparam name="TMid">The intermediate type produced by the first processor and consumed by the second.</typeparam>
/// <typeparam name="TOut">The output type produced by the second processor.</typeparam>
/// <remarks>
/// If the first processor returns <see langword="null"/>, the item is considered filtered
/// and the second processor is skipped entirely; <see langword="null"/> is propagated as the result.
/// </remarks>
public sealed class ProcessorChain<TIn, TMid, TOut> : IItemProcessor<TIn, TOut>
{
    private readonly IItemProcessor<TIn, TMid> _first;
    private readonly IItemProcessor<TMid, TOut> _second;

    /// <summary>
    /// Initializes a new <see cref="ProcessorChain{TIn,TMid,TOut}"/> wrapping the given
    /// two-step processor pair.
    /// </summary>
    /// <param name="first">The first processor in the chain, transforming <typeparamref name="TIn"/> to <typeparamref name="TMid"/>.</param>
    /// <param name="second">The second processor in the chain, transforming <typeparamref name="TMid"/> to <typeparamref name="TOut"/>.</param>
    public ProcessorChain(IItemProcessor<TIn, TMid> first, IItemProcessor<TMid, TOut> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        _first = first;
        _second = second;
    }

    /// <inheritdoc />
    public async ValueTask<TOut?> ProcessAsync(TIn item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        var mid = await _first.ProcessAsync(item, context, cancellationToken).ConfigureAwait(false);
        if (mid is null)
            return default;

        return await _second.ProcessAsync(mid, context, cancellationToken).ConfigureAwait(false);
    }
}
