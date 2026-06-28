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
}
