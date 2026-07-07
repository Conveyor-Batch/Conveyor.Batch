namespace Conveyor.Batch.Core.Job;

/// <summary>
/// Immutable set of parameters identifying a unique job execution.
/// </summary>
/// <param name="Values">The key-value pairs that parameterize the job.</param>
public readonly record struct JobParameters(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>An empty parameter set.</summary>
    public static readonly JobParameters Empty = new(new Dictionary<string, string>());

    /// <summary>Returns the value for the given key, or <see langword="null"/> if absent.</summary>
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Determines whether this instance and <paramref name="other"/> carry the same set of
    /// key-value pairs, independent of key order or dictionary implementation/reference identity.
    /// </summary>
    public bool Equals(JobParameters other)
    {
        var values = Values ?? Empty.Values;
        var otherValues = other.Values ?? Empty.Values;

        if (ReferenceEquals(values, otherValues))
            return true;

        if (values.Count != otherValues.Count)
            return false;

        var ordered = values.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var otherOrdered = otherValues.OrderBy(kv => kv.Key, StringComparer.Ordinal);

        return ordered.SequenceEqual(otherOrdered);
    }

    /// <summary>
    /// Computes a hash code consistent with <see cref="Equals(JobParameters)"/>: independent of
    /// key order, based purely on the sorted key-value pair contents.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var kv in (Values ?? Empty.Values).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(kv.Key, StringComparer.Ordinal);
            hash.Add(kv.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
