using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.FlatFile;

/// <summary>
/// Reads a delimited flat file line by line, mapping each line to an item of type <typeparamref name="T"/>
/// via a user-supplied mapper function.
/// </summary>
/// <typeparam name="T">The type of item produced from each line.</typeparam>
public sealed class FlatFileItemReader<T> : IItemReader<T>
{
    private readonly string _filePath;
    private readonly Func<string, T> _lineMapper;
    private readonly bool _skipHeader;

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
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(_filePath);

        bool firstLine = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
                break;

            if (firstLine && _skipHeader)
            {
                firstLine = false;
                continue;
            }

            firstLine = false;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            context.IncrementReadCount();
            yield return _lineMapper(line);
        }
    }
}
