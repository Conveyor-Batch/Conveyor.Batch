namespace CsvToDatabase;

/// <summary>
/// A CSV row split into its raw string fields. Parsing is deliberately tolerant here — a
/// missing field is padded with an empty string rather than throwing — so real validation
/// happens in <see cref="OrderProcessor"/>, where the chunk engine's skip policy can catch it.
/// </summary>
sealed record RawOrder(string Id, string Product, string AmountText);
