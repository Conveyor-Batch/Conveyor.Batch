using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.FlatFile;

/// <summary>
/// Reads a delimited flat file line by line, mapping each line to an item of type <typeparamref name="T"/>
/// via a user-supplied mapper function. Implements <see cref="IItemStream"/> to support restart: every
/// physical line read via <c>ReadLineAsync</c> (including the header line and blank lines) advances an
/// internal line counter, so resuming simply means replaying that many <c>ReadLineAsync</c> calls before
/// resuming normal iteration — the header/blank-skip business logic never needs to be replayed separately.
/// </summary>
/// <typeparam name="T">The type of item produced from each line.</typeparam>
public sealed class FlatFileItemReader<T> : IItemReader<T>, IItemStream
{
    private const string CurrentLineKey = "FlatFileItemReader.currentLine";

    private readonly string _filePath;
    private readonly Func<string, T> _lineMapper;
    private readonly bool _skipHeader;
    private int _currentLine;
    private int _linesToSkipOnOpen;

    /// <summary>
    /// Initializes a new <see cref="FlatFileItemReader{T}"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the flat file to read.</param>
    /// <param name="lineMapper">Function that converts a raw line string to <typeparamref name="T"/>.</param>
    /// <param name="skipHeader">
    /// When <see langword="true"/> (the default), the first line of the file is skipped.
    /// </param>
    public FlatFileItemReader(string filePath, Func<string, T> lineMapper, bool skipHeader = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(lineMapper);

        _filePath = filePath;
        _lineMapper = lineMapper;
        _skipHeader = skipHeader;
    }

    /// <inheritdoc />
    public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        _linesToSkipOnOpen = context.Get<int>(CurrentLineKey);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        context.Put(CurrentLineKey, _currentLine);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// No-op: <see cref="ReadAsync"/> disposes its own <see cref="StreamReader"/> via a <c>using</c>
    /// declaration, which the compiler lowers to a <c>try/finally</c> inside the async iterator's state
    /// machine — the file handle is released on completion or on any exception, so there is nothing left
    /// for this method to release.
    /// </remarks>
    public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(_filePath);
        int currentLine = 0;

        for (int i = 0; i < _linesToSkipOnOpen; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is null)
                break;
            currentLine++;
        }

        bool firstLine = currentLine == 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
                break;

            currentLine++;

            if (firstLine && _skipHeader)
            {
                firstLine = false;
                continue;
            }

            firstLine = false;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            context.IncrementReadCount();
            _currentLine = currentLine;
            yield return _lineMapper(line);
        }

        _currentLine = currentLine;
    }
}
