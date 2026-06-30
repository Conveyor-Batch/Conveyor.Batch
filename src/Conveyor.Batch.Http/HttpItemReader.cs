using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Http;

/// <summary>
/// Reads items from a paginated HTTP endpoint, requesting successive pages and streaming their
/// items until a page yields no results.
/// </summary>
/// <typeparam name="T">The type of item produced from each page.</typeparam>
public sealed class HttpItemReader<T> : IItemReader<T>
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly Func<HttpResponseMessage, CancellationToken, Task<IReadOnlyList<T>>> _pageExtractor;

    /// <summary>
    /// Initializes a new <see cref="HttpItemReader{T}"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> used to issue page requests. Ownership stays with the caller;
    /// this reader never disposes it.
    /// </param>
    /// <param name="baseUrl">The base URL to request pages from. A <c>page</c> query parameter is appended.</param>
    /// <param name="pageExtractor">
    /// Function that extracts the items for a page from the HTTP response. An empty result ends the stream.
    /// </param>
    public HttpItemReader(
        HttpClient httpClient,
        string baseUrl,
        Func<HttpResponseMessage, CancellationToken, Task<IReadOnlyList<T>>> pageExtractor)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(pageExtractor);

        _httpClient = httpClient;
        _baseUrl = baseUrl;
        _pageExtractor = pageExtractor;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url = BuildPageUrl(_baseUrl, page);
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            IReadOnlyList<T> items = await _pageExtractor(response, cancellationToken).ConfigureAwait(false);

            if (items.Count == 0)
                yield break;

            foreach (T item in items)
            {
                context.IncrementReadCount();
                yield return item;
            }

            page++;
        }
    }

    private static string BuildPageUrl(string baseUrl, int page)
    {
        char separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}page={page}";
    }
}
