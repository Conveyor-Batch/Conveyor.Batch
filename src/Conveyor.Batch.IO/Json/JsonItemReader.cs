using System.Runtime.CompilerServices;
using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.Json;

/// <summary>
/// Reads a JSON array file, streaming each element as a deserialized <typeparamref name="T"/> instance.
/// Uses <c>JsonSerializer.DeserializeAsyncEnumerable</c> for memory-efficient streaming.
/// </summary>
/// <typeparam name="T">The type to deserialize each JSON array element into.</typeparam>
public sealed class JsonItemReader<T> : IItemReader<T>
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions? _options;

    /// <summary>
    /// Initializes a new <see cref="JsonItemReader{T}"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the JSON array file.</param>
    /// <param name="options">Optional <see cref="JsonSerializerOptions"/> to control deserialization behavior.</param>
    public JsonItemReader(string filePath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _options = options;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_filePath);

        await foreach (T? item in JsonSerializer.DeserializeAsyncEnumerable<T>(stream, _options, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (item is null)
                continue;

            context.IncrementReadCount();
            yield return item;
        }
    }
}
