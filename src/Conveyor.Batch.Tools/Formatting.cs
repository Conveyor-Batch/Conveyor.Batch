using System.Text.Json;

namespace Conveyor.Batch.Tools;

/// <summary>
/// Shared rendering helpers for the table columns printed by the CLI commands.
/// </summary>
internal static class Formatting
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Renders a serialized parameters JSON object as "key=value, key2=value2".</summary>
    internal static string FormatParameters(string parametersJson)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson, JsonOptions);

        if (values is null || values.Count == 0)
            return "(none)";

        return string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>Renders a nullable timestamp, or "-" if absent.</summary>
    internal static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    /// <summary>Renders the elapsed time between start and end, or "-" while still running.</summary>
    internal static string FormatDuration(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end is null)
            return "-";

        // TimeSpan's "hh" custom format specifier is hours-within-a-day (0-23), not total
        // hours, so it silently truncates the day component for executions running 24h+.
        var span = end.Value - start;
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
