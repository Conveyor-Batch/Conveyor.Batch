using System.Data;
using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Conveyor.Batch.Dapper;

/// <summary>
/// Streams rows from a Dapper-executed SQL query using offset-based pagination, and
/// participates in restart via <see cref="IItemStream"/> by checkpointing the current
/// offset into the step's <see cref="BatchExecutionContext"/>.
/// </summary>
/// <typeparam name="T">The type each row is mapped to via Dapper.</typeparam>
/// <remarks>
/// <para>
/// <c>sql</c> must be written by the caller to support offset pagination, e.g.
/// <c>SELECT * FROM Orders ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY</c>.
/// This reader injects the current page by merging <c>@Offset</c> and <c>@PageSize</c> into
/// <c>parameters</c> before each fetch; the query's <c>ORDER BY</c> must be stable across pages
/// (typically an ascending sort on a primary/unique key) or rows can be skipped or repeated as
/// the underlying table changes between fetches.
/// </para>
/// <para>
/// Each page is fetched over its own short-lived connection obtained from
/// <c>connectionFactory</c> and fully buffered before being yielded, so no connection is held
/// open while downstream processing consumes the page.
/// </para>
/// </remarks>
public sealed class DapperItemReader<T> : IItemReader<T>, IItemStream
{
    private const int PageSize = 1000;

    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _sql;
    private readonly object? _parameters;
    private readonly string _contextKey;
    private readonly ILogger<DapperItemReader<T>>? _logger;

    private int _offset;

    /// <summary>
    /// Initializes a new <see cref="DapperItemReader{T}"/>.
    /// </summary>
    /// <param name="connectionFactory">
    /// Factory used to create a fresh <see cref="IDbConnection"/> for each page fetch. The
    /// reader opens and closes a connection from this factory once per page.
    /// </param>
    /// <param name="sql">
    /// The SQL query to execute. Must be written to support offset pagination via an
    /// <c>@Offset</c> parameter (and typically <c>@PageSize</c>), which this reader supplies
    /// automatically on every fetch.
    /// </param>
    /// <param name="parameters">
    /// Optional additional parameters for <paramref name="sql"/>. Merged with the reader's
    /// <c>@Offset</c> and <c>@PageSize</c> values on each fetch; the caller's values for any
    /// other parameter names are passed through unchanged.
    /// </param>
    /// <param name="contextKey">
    /// The key under which the current offset is checkpointed into the step's
    /// <see cref="BatchExecutionContext"/>.
    /// </param>
    /// <param name="logger">Optional logger used to report per-page fetch diagnostics.</param>
    public DapperItemReader(
        Func<IDbConnection> connectionFactory,
        string sql,
        object? parameters = null,
        string contextKey = "DapperItemReader.offset",
        ILogger<DapperItemReader<T>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);

        _connectionFactory = connectionFactory;
        _sql = sql;
        _parameters = parameters;
        _contextKey = contextKey;
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        _offset = context.Get<int>(_contextKey);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        context.Put(_contextKey, _offset);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dynamicParameters = new DynamicParameters(_parameters);
            dynamicParameters.Add("Offset", _offset);
            dynamicParameters.Add("PageSize", PageSize);

            List<T> page;
            using (var connection = _connectionFactory())
            {
                connection.Open();
                var command = new CommandDefinition(_sql, dynamicParameters, cancellationToken: cancellationToken);
                page = (await connection.QueryAsync<T>(command).ConfigureAwait(false)).AsList();
            }

            _logger?.LogDebug("DapperItemReader fetched {Count} row(s) at offset {Offset}", page.Count, _offset);

            foreach (var item in page)
            {
                context.IncrementReadCount();
                _offset++;
                yield return item;
            }

            if (page.Count < PageSize)
                yield break;
        }
    }
}
