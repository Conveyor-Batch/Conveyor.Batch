using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.Json;

/// <summary>
/// Writes items to a JSON array file incrementally using <see cref="Utf8JsonWriter"/>.
/// Each call to <see cref="WriteAsync"/> appends array elements to the in-progress JSON array.
/// Call <see cref="CompleteAsync"/> (or dispose) to finalize and close the array.
/// </summary>
/// <remarks>
/// Because JSON arrays require a closing bracket, callers should either use
/// <c>await using</c> or call <see cref="CompleteAsync"/> explicitly after all chunks have been written.
/// </remarks>
/// <typeparam name="T">The type of item to serialize.</typeparam>
public sealed class JsonItemWriter<T> : IItemWriter<T>, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions? _options;
    private FileStream? _fileStream;
    private Utf8JsonWriter? _jsonWriter;
    private bool _completed;

    /// <summary>
    /// Initializes a new <see cref="JsonItemWriter{T}"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the output JSON file.</param>
    /// <param name="options">Optional <see cref="JsonSerializerOptions"/> to control serialization behavior.</param>
    public JsonItemWriter(string filePath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(
        IReadOnlyList<T> items,
        StepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        EnsureStarted();

        foreach (T item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(_jsonWriter!, item, _options);
        }

        await _jsonWriter!.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finalizes the JSON array by writing the closing bracket and flushing the file.
    /// Must be called after all chunks have been written if not using <c>await using</c>.
    /// </summary>
    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
            return;

        _completed = true;

        if (_jsonWriter is not null)
        {
            _jsonWriter.WriteEndArray();
            await _jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Nothing was written — still produce a valid empty JSON array.
            EnsureStarted();
            _jsonWriter!.WriteEndArray();
            await _jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_completed)
            await CompleteAsync().ConfigureAwait(false);

        if (_jsonWriter is not null)
        {
            await _jsonWriter.DisposeAsync().ConfigureAwait(false);
            _jsonWriter = null;
        }

        if (_fileStream is not null)
        {
            await _fileStream.DisposeAsync().ConfigureAwait(false);
            _fileStream = null;
        }
    }

    private void EnsureStarted()
    {
        if (_jsonWriter is not null)
            return;

        _fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);

        _jsonWriter = new Utf8JsonWriter(_fileStream, new JsonWriterOptions
        {
            Indented = _options?.WriteIndented ?? false,
            SkipValidation = false
        });

        _jsonWriter.WriteStartArray();
    }
}
