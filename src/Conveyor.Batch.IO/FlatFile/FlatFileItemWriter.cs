using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.FlatFile;

/// <summary>
/// Writes items to a delimited flat file, converting each item to a line string via a user-supplied formatter.
/// The file is opened lazily on the first write and flushed after each chunk.
/// </summary>
/// <typeparam name="T">The type of item to write.</typeparam>
public sealed class FlatFileItemWriter<T> : IItemWriter<T>, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly Func<T, string> _lineFormatter;
    private readonly bool _append;
    private StreamWriter? _writer;

    /// <summary>
    /// Initializes a new <see cref="FlatFileItemWriter{T}"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the output file.</param>
    /// <param name="lineFormatter">Function that converts an item to a line string.</param>
    /// <param name="append">
    /// When <see langword="true"/>, new items are appended to an existing file.
    /// When <see langword="false"/> (the default), the file is overwritten.
    /// </param>
    public FlatFileItemWriter(string filePath, Func<T, string> lineFormatter, bool append = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(lineFormatter);

        _filePath = filePath;
        _lineFormatter = lineFormatter;
        _append = append;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(
        IReadOnlyList<T> items,
        StepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        _writer ??= new StreamWriter(_filePath, append: _append);

        foreach (T item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _writer.WriteLineAsync(_lineFormatter(item)).ConfigureAwait(false);
        }

        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes and closes the underlying file stream.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = null;
        }
    }
}
