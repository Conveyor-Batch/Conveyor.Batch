using System.Xml.Linq;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.IO.Xml;

/// <summary>
/// Writes items as XML elements nested under a single root element, converting each item to an
/// <see cref="XElement"/> via a user-supplied mapper function.
/// </summary>
/// <typeparam name="T">The type of item to write.</typeparam>
/// <remarks>
/// Unlike a flat file, a valid XML document cannot be incrementally appended to without
/// re-parsing and re-closing its root element. <see cref="WriteAsync"/> therefore loads the whole
/// existing document (or creates a new one), adds the chunk's elements under the root, and saves
/// the whole file back on every call. This read-modify-write cycle is guarded by a
/// <see cref="SemaphoreSlim"/> so concurrent writers (e.g. from a step with
/// <c>DegreeOfParallelism &gt; 1</c>) cannot corrupt the file.
/// </remarks>
public sealed class XmlItemWriter<T> : IItemWriter<T>, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly string _rootElementName;
    private readonly string _itemElementName;
    private readonly Func<T, XElement> _elementMapper;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Initializes a new <see cref="XmlItemWriter{T}"/>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the output file.</param>
    /// <param name="rootElementName">The name of the document's root element.</param>
    /// <param name="itemElementName">
    /// The canonical element name each written item is stored under. The element returned by
    /// <paramref name="elementMapper"/> is renamed to this value before being added, so callers
    /// don't need to set it themselves.
    /// </param>
    /// <param name="elementMapper">Function that converts an item to an <see cref="XElement"/>.</param>
    public XmlItemWriter(
        string filePath,
        string rootElementName,
        string itemElementName,
        Func<T, XElement> elementMapper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootElementName);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemElementName);
        ArgumentNullException.ThrowIfNull(elementMapper);

        _filePath = filePath;
        _rootElementName = rootElementName;
        _itemElementName = itemElementName;
        _elementMapper = elementMapper;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(
        IReadOnlyList<T> items,
        StepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = File.Exists(_filePath)
                ? XDocument.Load(_filePath)
                : new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(_rootElementName));

            var root = document.Root!;

            foreach (T item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var element = _elementMapper(item);
                element.Name = _itemElementName;
                root.Add(element);
            }

            document.Save(_filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Releases the semaphore used to serialize concurrent writes.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }
}
