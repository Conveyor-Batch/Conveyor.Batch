# IO

`Conveyor.Batch.IO` provides flat-file, JSON, and XML readers and writers. `Conveyor.Batch.Http` provides a paginated HTTP reader.

## Flat file

Namespace `Conveyor.Batch.IO.FlatFile`.

```csharp
public sealed class FlatFileItemReader<T> : IItemReader<T>, IItemStream
{
    public FlatFileItemReader(string filePath, Func<string, T> lineMapper, bool skipHeader = true);
}

public sealed class FlatFileItemWriter<T> : IItemWriter<T>, IAsyncDisposable
{
    public FlatFileItemWriter(string filePath, Func<T, string> lineFormatter, bool append = false);
}
```

| Type | Parameter | Description |
|---|---|---|
| `FlatFileItemReader<T>` | `filePath` | Path to the file to read. |
| | `lineMapper` | Maps a raw line to a `T`. |
| | `skipHeader` | Skips the first line when `true` (default). |
| `FlatFileItemWriter<T>` | `filePath` | Path to the file to write. |
| | `lineFormatter` | Formats a `T` into a line of output. |
| | `append` | Appends to an existing file instead of overwriting when `true`. |

`FlatFileItemReader<T>` implements `IItemStream`, so it supports [restart checkpointing](/guide/restartability) out of the box.

## JSON

Namespace `Conveyor.Batch.IO.Json`.

```csharp
public sealed class JsonItemReader<T> : IItemReader<T>
{
    public JsonItemReader(string filePath, JsonSerializerOptions? options = null);
}

public sealed class JsonItemWriter<T> : IItemWriter<T>, IAsyncDisposable
{
    public JsonItemWriter(string filePath, JsonSerializerOptions? options = null);

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default);
}
```

`JsonItemReader<T>` streams items with `JsonSerializer.DeserializeAsyncEnumerable` and does **not** implement `IItemStream` — it has no restart support. `JsonItemWriter<T>` writes a JSON array incrementally; call `CompleteAsync` (or dispose the writer) once writing is finished to close the array — omitting this leaves the output file with an unterminated JSON array.

## XML

Namespace `Conveyor.Batch.IO.Xml`.

```csharp
public sealed class XmlItemReader<T> : IItemReader<T>, IItemStream
{
    public XmlItemReader(
        string filePath,
        string elementName,
        Func<XElement, T> elementMapper,
        string contextKey = "XmlItemReader.currentIndex");
}

public sealed class XmlItemWriter<T> : IItemWriter<T>, IAsyncDisposable
{
    public XmlItemWriter(
        string filePath,
        string rootElementName,
        string itemElementName,
        Func<T, XElement> elementMapper);
}
```

| Type | Parameter | Description |
|---|---|---|
| `XmlItemReader<T>` | `filePath` | Path to the XML file to read. |
| | `elementName` | The element name identifying each item. |
| | `elementMapper` | Maps an `XElement` to a `T`. |
| | `contextKey` | The execution-context key the current index is checkpointed under. |
| `XmlItemWriter<T>` | `filePath` | Path to the XML file to write. |
| | `rootElementName` | The root element wrapping all items. |
| | `itemElementName` | The element name written for each item. |
| | `elementMapper` | Maps a `T` to the `XElement` written for it. |

`XmlItemReader<T>` loads the whole document via `XDocument.Load`, so it isn't suited to very large files. `XmlItemWriter<T>` performs a read-modify-write on every `WriteAsync` call, guarded by an internal semaphore.

## HTTP

Namespace `Conveyor.Batch.Http`.

```csharp
public sealed class HttpItemReader<T> : IItemReader<T>
{
    public HttpItemReader(
        HttpClient httpClient,
        string baseUrl,
        Func<HttpResponseMessage, CancellationToken, Task<IReadOnlyList<T>>> pageExtractor);
}
```

| Parameter | Description |
|---|---|
| `httpClient` | The `HttpClient` used to issue requests. |
| `baseUrl` | The base URL to page through. |
| `pageExtractor` | Extracts the items from one page's `HttpResponseMessage`. |

`HttpItemReader<T>` paginates by appending a `page` query parameter and stops once a page returns zero items. There is currently no `IItemWriter` in the HTTP package.

## Dead-letter writer

```csharp
public sealed class FlatFileDeadLetterWriter : IDeadLetterWriter
{
    public FlatFileDeadLetterWriter(string filePath);
}
```

Appends each dead-lettered item as a line of newline-delimited JSON (NDJSON) to `filePath`. See [Dead-Lettering](/guide/dead-lettering) for how this is wired into a step, and [Repository API](/api/repository) for the EF Core–backed alternative.
