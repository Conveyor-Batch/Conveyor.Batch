namespace CsvToDatabase;

/// <summary>An order row after validation and tax calculation, persisted to SQLite.</summary>
sealed class ProcessedOrder
{
    public string Id { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Tax { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
