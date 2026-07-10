using System.Xml.Linq;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.IO.Xml;

namespace Conveyor.Batch.UnitTests.IO;

public sealed class XmlItemWriterTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"conveyor_batch_xml_writer_{Guid.NewGuid()}.xml");

    private static StepExecutionContext NewContext() => new(new StepExecution { StepName = "test" });

    private static XElement MapOrder((int Id, string Name) order) =>
        new("order", new XElement("id", order.Id), new XElement("name", order.Name));

    [Fact]
    public async Task WriteAsync_CreatesFile_WhenFileDoesNotExist()
    {
        var writer = new XmlItemWriter<(int Id, string Name)>(_filePath, "orders", "order", MapOrder);

        await writer.WriteAsync([(1, "Widget"), (2, "Gadget")], NewContext(), CancellationToken.None);

        Assert.True(File.Exists(_filePath));
        var document = XDocument.Load(_filePath);
        Assert.NotNull(document.Declaration);
        Assert.Equal("orders", document.Root!.Name.LocalName);
        Assert.Equal(2, document.Root.Elements("order").Count());
    }

    [Fact]
    public async Task WriteAsync_AppendsToExistingFile_WithoutCorruptingStructure()
    {
        var writer = new XmlItemWriter<(int Id, string Name)>(_filePath, "orders", "order", MapOrder);

        await writer.WriteAsync([(1, "Widget")], NewContext(), CancellationToken.None);
        await writer.WriteAsync([(2, "Gadget")], NewContext(), CancellationToken.None);

        var document = XDocument.Load(_filePath);
        Assert.Equal("orders", document.Root!.Name.LocalName);
        var ids = document.Root.Elements("order").Select(e => e.Element("id")!.Value).ToList();
        Assert.Equal(["1", "2"], ids);
    }

    [Fact]
    public async Task WriteAsync_MultipleChunks_AllItemsPresentAndFileIsWellFormed()
    {
        var writer = new XmlItemWriter<(int Id, string Name)>(_filePath, "orders", "order", MapOrder);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => writer.WriteAsync([(i, $"item-{i}")], NewContext(), CancellationToken.None).AsTask());
        await Task.WhenAll(tasks);

        var document = XDocument.Load(_filePath);
        Assert.Equal(10, document.Root!.Elements("order").Count());
        var ids = document.Root.Elements("order").Select(e => int.Parse(e.Element("id")!.Value)).ToList();
        Assert.Equal(Enumerable.Range(0, 10), ids.OrderBy(id => id));
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
