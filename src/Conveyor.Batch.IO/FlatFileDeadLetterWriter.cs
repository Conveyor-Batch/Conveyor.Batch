using System.Text.Json;
using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.IO;

/// <summary>
/// Appends dead-lettered entries as newline-delimited JSON to a flat file.
/// </summary>
public sealed class FlatFileDeadLetterWriter : IDeadLetterWriter
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Initializes a new <see cref="FlatFileDeadLetterWriter"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the output file. Created if it does not exist.</param>
    public FlatFileDeadLetterWriter(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(DeadLetterEntry entry, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(entry);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_filePath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
