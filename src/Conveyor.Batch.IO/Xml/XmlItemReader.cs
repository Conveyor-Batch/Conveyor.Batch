using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.Xml;

/// <summary>
/// Reads elements matching <c>elementName</c> from an XML file, mapping each matching
/// <see cref="XElement"/> to an item of type <typeparamref name="T"/> via a user-supplied mapper
/// function. Implements <see cref="IItemStream"/> to support restart: every matching element
/// yielded advances an internal index counter, so resuming simply means skipping that many
/// matching elements before resuming normal iteration.
/// </summary>
/// <typeparam name="T">The type of item produced from each matching element.</typeparam>
/// <remarks>
/// The whole file is parsed into memory via <see cref="XDocument.Load(string)"/> on every call to
/// <see cref="ReadAsync"/> (including on restart), since XML requires a single well-formed
/// document and has no equivalent of flat-file line-by-line streaming. This is not suitable for
/// very large XML files.
/// </remarks>
public sealed class XmlItemReader<T> : IItemReader<T>, IItemStream
{
    private readonly string _filePath;
    private readonly string _elementName;
    private readonly Func<XElement, T> _elementMapper;
    private readonly string _contextKey;
    private int _currentIndex;
    private int _elementsToSkipOnOpen;

    /// <summary>
    /// Initializes a new <see cref="XmlItemReader{T}"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the XML file to read.</param>
    /// <param name="elementName">The XML element name that maps to one item of type <typeparamref name="T"/>.</param>
    /// <param name="elementMapper">Function that converts a matching <see cref="XElement"/> to <typeparamref name="T"/>.</param>
    /// <param name="contextKey">The <see cref="BatchExecutionContext"/> key used to persist the restart checkpoint.</param>
    public XmlItemReader(
        string filePath,
        string elementName,
        Func<XElement, T> elementMapper,
        string contextKey = "XmlItemReader.currentIndex")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);
        ArgumentNullException.ThrowIfNull(elementMapper);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);

        _filePath = filePath;
        _elementName = elementName;
        _elementMapper = elementMapper;
        _contextKey = contextKey;
    }

    /// <inheritdoc />
    public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        _elementsToSkipOnOpen = context.Get<int>(_contextKey);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken cancellationToken)
    {
        context.Put(_contextKey, _currentIndex);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// No-op: <see cref="ReadAsync"/> loads the whole document synchronously via
    /// <see cref="XDocument.Load(string)"/>, which fully reads and closes the file before any
    /// element is yielded — there is no persistent handle left for this method to release.
    /// </remarks>
    public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync(
        StepExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var document = XDocument.Load(_filePath);
        await Task.Yield();

        int index = 0;

        foreach (var element in document.Descendants(_elementName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (index < _elementsToSkipOnOpen)
            {
                index++;
                continue;
            }

            index++;
            context.IncrementReadCount();
            _currentIndex = index;
            yield return _elementMapper(element);
        }
    }
}
