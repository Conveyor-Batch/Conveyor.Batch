using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// Bulk-writes a committed chunk to any user-owned <typeparamref name="TContext"/>: each chunk
/// gets its own fresh <typeparamref name="TContext"/> from <see cref="IDbContextFactory{TContext}"/>,
/// added via <c>AddRangeAsync</c> and persisted with a single <c>SaveChangesAsync</c> call, so no
/// state or tracked entities leak between chunks.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> to write to.</typeparam>
/// <typeparam name="TEntity">The entity type to write.</typeparam>
public sealed class EfCoreItemWriter<TContext, TEntity> : IItemWriter<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly bool _clearChangeTrackerAfterChunk;

    /// <summary>
    /// Initializes a new <see cref="EfCoreItemWriter{TContext,TEntity}"/>.
    /// </summary>
    /// <param name="contextFactory">
    /// Factory used to create a fresh <typeparamref name="TContext"/> for each committed chunk.
    /// </param>
    /// <param name="clearChangeTrackerAfterChunk">
    /// When <see langword="true"/> (the default), the change tracker of the chunk's context is
    /// cleared after <c>SaveChangesAsync</c> to avoid memory accumulation on large datasets.
    /// </param>
    public EfCoreItemWriter(IDbContextFactory<TContext> contextFactory, bool clearChangeTrackerAfterChunk = true)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        _contextFactory = contextFactory;
        _clearChangeTrackerAfterChunk = clearChangeTrackerAfterChunk;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(IReadOnlyList<TEntity> items, StepExecutionContext context, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await dbContext.Set<TEntity>().AddRangeAsync(items, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_clearChangeTrackerAfterChunk)
            dbContext.ChangeTracker.Clear();
    }
}
