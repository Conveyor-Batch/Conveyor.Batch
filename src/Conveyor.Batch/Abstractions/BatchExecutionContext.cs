using System.Text.Json;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// A serializable bag of key/value checkpoint state that a restart-aware component
/// (typically an <see cref="IItemStream"/>-implementing reader) can persist into and
/// restore from a step execution, enabling a failed step to resume from its last
/// committed position rather than starting over. Values are stored internally as JSON
/// strings so the whole context round-trips cleanly through a persistence layer
/// (e.g. a single JSON column in EF Core).
/// </summary>
public sealed class BatchExecutionContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string> _values;

    /// <summary>
    /// Initializes a new, empty <see cref="BatchExecutionContext"/>.
    /// </summary>
    public BatchExecutionContext()
    {
        _values = new Dictionary<string, string>();
    }

    private BatchExecutionContext(Dictionary<string, string> values)
    {
        _values = values;
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON and stores it under <paramref name="key"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value to store.</typeparam>
    /// <param name="key">The key to store the value under.</param>
    /// <param name="value">The value to serialize and store.</param>
    public void Put<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Deserializes the value stored under <paramref name="key"/>, or returns
    /// <see langword="default"/> if no value is stored under that key.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the stored value as.</typeparam>
    /// <param name="key">The key to look up.</param>
    public T? Get<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.TryGetValue(key, out var json)
            ? JsonSerializer.Deserialize<T>(json, JsonOptions)
            : default;
    }

    /// <summary>
    /// Returns <see langword="true"/> if a value is stored under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key to check.</param>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>
    /// Returns a read-only snapshot of the underlying JSON-valued dictionary, suitable
    /// for persistence.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToDictionary() => _values;

    /// <summary>
    /// Creates a <see cref="BatchExecutionContext"/> from a previously persisted
    /// dictionary (e.g. one loaded from a repository).
    /// </summary>
    /// <param name="dict">The dictionary of JSON-valued checkpoint state to restore from.</param>
    public static BatchExecutionContext FromDictionary(IDictionary<string, string> dict)
    {
        ArgumentNullException.ThrowIfNull(dict);
        return new BatchExecutionContext(new Dictionary<string, string>(dict));
    }
}
