namespace RestartableJob;

/// <summary>A processed row, persisted to SQLite.</summary>
sealed class OutputRecord
{
    public string Id { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
