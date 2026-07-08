using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// Streams entities from any user-owned <typeparamref name="TContext"/> using keyset ("seek
/// method") pagination on <typeparamref name="TKey"/>, and participates in restart via
/// <see cref="IItemStream"/> by checkpointing the last-read key into the step's
/// <see cref="BatchExecutionContext"/>.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> to read from.</typeparam>
/// <typeparam name="TEntity">The entity type to read.</typeparam>
/// <typeparam name="TKey">The type of the entity's stable, ordered primary key.</typeparam>
/// <remarks>
/// <para>
/// <typeparamref name="TEntity"/> must have a stable, ordered primary key for keyset pagination to
/// be correct: the key must be assigned once and never change, and ascending order over the key
/// must be a total, consistent order over the rows returned by <c>queryBuilder</c>.
/// </para>
/// <para>
/// <c>queryBuilder</c>'s result must already be ordered ascending by the same key
/// <c>keySelector</c> extracts, e.g. <c>ctx =&gt; ctx.Set&lt;TEntity&gt;().OrderBy(x =&gt; x.Id)</c>.
/// This reader does not add that ordering itself: <c>keySelector</c> is a compiled
/// <see cref="Func{TEntity,TKey}"/>, not an expression tree, so EF Core cannot translate it into a
/// SQL <c>ORDER BY</c> or <c>WHERE</c> clause. Instead, each page is fetched via a fresh
/// <see cref="DbContext"/> by re-running the caller-ordered query from the start and skipping,
/// client-side, any entity whose key is not strictly greater than the last key read so far. As a
/// result, total database scan work grows with the number of pages already read (each page
/// re-scans the prefix it has already passed) — this does not scale the way true server-side
/// keyset pagination would on very large tables, but it never materializes the full result set in
/// .NET memory at once, and each page uses a fresh, untracked (<c>AsNoTracking()</c>) context so no
/// tracked-entity accumulation occurs across a long read.
/// </para>
/// </remarks>
public sealed class EfCoreItemReader<TContext, TEntity, TKey> : IItemReader<TEntity>, IItemStream
    where TContext : DbContext
    where TEntity : class
    where TKey : IComparable<TKey>
{
    private const int PageSize = 100;

    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly Func<TContext, IQueryable<TEntity>> _queryBuilder;
    private readonly Func<TEntity, TKey> _keySelector;
    private readonly string _contextKey;

    private TKey? _lastKey;
    private bool _hasLastKey;

    /// <summary>
    /// Initializes a new <see cref="EfCoreItemReader{TContext,TEntity,TKey}"/>.
    /// </summary>
    /// <param name="contextFactory">
    /// Factory used to create a fresh <typeparamref name="TContext"/> for each page read, avoiding
    /// long-lived tracked-entity accumulation on large datasets.
    /// </param>
    /// <param name="queryBuilder">
    /// Builds the query to read from, given a <typeparamref name="TContext"/>. Must return a query
    /// already ordered ascending by the same key <paramref name="keySelector"/> extracts.
    /// </param>
    /// <param name="keySelector">Extracts the ordering key from an entity.</param>
    /// <param name="contextKey">
    /// The key under which the last-read key is checkpointed into the step's
    /// <see cref="BatchExecutionContext"/>.
    /// </param>
    public EfCoreItemReader(
        IDbContextFactory<TContext> contextFactory,
        Func<TContext, IQueryable<TEntity>> queryBuilder,
        Func<TEntity, TKey> keySelector,
        string contextKey = "EfCoreItemReader.lastKey")
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(queryBuilder);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);

        _contextFactory = contextFactory;
        _queryBuilder = queryBuilder;
        _keySelector = keySelector;
        _contextKey = contextKey;
    }

    /// <inheritdoc />
    public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ContainsKey(_contextKey))
        {
            _lastKey = context.Get<TKey>(_contextKey);
            _hasLastKey = true;
        }
        else
        {
            _hasLastKey = false;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        if (_hasLastKey)
            context.Put(_contextKey, _lastKey);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async IAsyncEnumerable<TEntity> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            int yieldedInPage = 0;

            await using (var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var query = _queryBuilder(dbContext).AsNoTracking();

                await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    var key = _keySelector(entity);

                    if (_hasLastKey && key.CompareTo(_lastKey!) <= 0)
                        continue;

                    _lastKey = key;
                    _hasLastKey = true;

                    yield return entity;
                    yieldedInPage++;

                    if (yieldedInPage >= PageSize)
                        break;
                }
            }

            if (yieldedInPage < PageSize)
                yield break;
        }
    }
}
