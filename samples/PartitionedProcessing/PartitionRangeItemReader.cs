using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace PartitionedProcessing;

/// <summary>
/// Wraps <see cref="EfCoreItemReader{TContext,TEntity,TKey}"/> to filter each partition's query
/// to the <c>[min,max]</c> key range <see cref="Conveyor.Batch.Core.Partitioning.LocalPartitionHandler"/>
/// assigns it.
/// </summary>
/// <remarks>
/// <para>
/// <c>PartitionStepBuilder.Worker()</c> takes a single <c>IStep</c> instance that's shared by
/// every partition, and <c>EfCoreItemReader</c>'s query is a fixed <c>Func</c> captured once at
/// construction — it has no way to see a per-partition range. The partition's range isn't
/// available from <c>IItemStream.OpenAsync</c>'s <c>BatchExecutionContext</c> either, since that
/// context starts empty for every partition attempt. It IS available via
/// <c>context.StepExecution.JobExecution.ExecutionContext</c>: <c>LocalPartitionHandler</c>
/// clones a <c>JobExecution</c> per partition carrying <c>partition.minValue</c>/
/// <c>partition.maxValue</c> in exactly that execution context, and the worker step's fresh
/// <c>StepExecution</c> (created per partition) points its <c>JobExecution</c> reference at that
/// clone — so this reader reads its assigned range from there, on every <see cref="ReadAsync"/>
/// call, and constructs a fresh inner reader scoped to that one call.
/// </para>
/// <para>
/// Deliberately does not implement <see cref="IItemStream"/>: partition-scoped restart would need
/// its own per-partition checkpoint key, which the partitioning subsystem doesn't provide today —
/// out of scope for this sample.
/// </para>
/// </remarks>
sealed class PartitionRangeItemReader<TContext, TEntity, TKey>(
    IDbContextFactory<TContext> contextFactory,
    Func<TContext, long, long, IQueryable<TEntity>> rangeQueryBuilder,
    Func<TEntity, TKey> keySelector)
    : IItemReader<TEntity>
    where TContext : DbContext
    where TEntity : class
    where TKey : IComparable<TKey>
{
    public async IAsyncEnumerable<TEntity> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long min = context.StepExecution.JobExecution.ExecutionContext.Get<long>("partition.minValue");
        long max = context.StepExecution.JobExecution.ExecutionContext.Get<long>("partition.maxValue");

        // Scoped entirely to this ReadAsync call — never stored on `this` — so concurrent
        // partitions sharing this same outer reader instance never race on keyset state.
        var inner = new EfCoreItemReader<TContext, TEntity, TKey>(
            contextFactory, ctx => rangeQueryBuilder(ctx, min, max), keySelector);

        await foreach (var entity in inner.ReadAsync(context, cancellationToken).ConfigureAwait(false))
            yield return entity;
    }
}
