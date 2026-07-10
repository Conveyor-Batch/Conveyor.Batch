using System.Xml.Linq;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.IO.Xml;

namespace Conveyor.Batch.UnitTests.IO;

public sealed class XmlItemReaderTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"conveyor_batch_xml_reader_{Guid.NewGuid()}.xml");

    private static StepExecutionContext NewContext() => new(new StepExecution { StepName = "test" });

    private static (int Id, string Name) MapOrder(XElement element) =>
        (int.Parse(element.Element("id")!.Value), element.Element("name")!.Value);

    private void WriteFile(string content) => File.WriteAllText(_filePath, content);

    [Fact]
    public async Task ReadsAllElements_FromValidXmlFile()
    {
        WriteFile("""
            <?xml version="1.0" encoding="utf-8"?>
            <orders>
              <order><id>1</id><name>Widget</name></order>
              <order><id>2</id><name>Gadget</name></order>
              <order><id>3</id><name>Gizmo</name></order>
            </orders>
            """);

        var reader = new XmlItemReader<(int Id, string Name)>(_filePath, "order", MapOrder);
        var context = NewContext();
        await reader.OpenAsync(context.StepExecution.ExecutionContext, CancellationToken.None);

        var items = new List<(int Id, string Name)>();
        await foreach (var item in reader.ReadAsync(context, CancellationToken.None))
            items.Add(item);

        Assert.Equal(3, items.Count);
        Assert.Equal([(1, "Widget"), (2, "Gadget"), (3, "Gizmo")], items);
    }

    [Fact]
    public async Task EmptyFile_WithRootOnly_YieldsNoItems()
    {
        WriteFile("""<?xml version="1.0" encoding="utf-8"?><orders></orders>""");

        var reader = new XmlItemReader<(int Id, string Name)>(_filePath, "order", MapOrder);
        var context = NewContext();
        await reader.OpenAsync(context.StepExecution.ExecutionContext, CancellationToken.None);

        var items = new List<(int Id, string Name)>();
        await foreach (var item in reader.ReadAsync(context, CancellationToken.None))
            items.Add(item);

        Assert.Empty(items);
    }

    [Fact]
    public async Task Restart_OpenAsyncWithSavedIndex_SkipsAlreadyConsumedElements()
    {
        WriteFile("""
            <?xml version="1.0" encoding="utf-8"?>
            <orders>
              <order><id>1</id><name>Widget</name></order>
              <order><id>2</id><name>Gadget</name></order>
              <order><id>3</id><name>Gizmo</name></order>
              <order><id>4</id><name>Sprocket</name></order>
            </orders>
            """);

        // First (partial) attempt: consume the first two elements, then checkpoint.
        var firstReader = new XmlItemReader<(int Id, string Name)>(_filePath, "order", MapOrder);
        var executionContext = new BatchExecutionContext();
        await firstReader.OpenAsync(executionContext, CancellationToken.None);

        var firstContext = NewContext();
        var consumed = new List<(int Id, string Name)>();
        await foreach (var item in firstReader.ReadAsync(firstContext, CancellationToken.None))
        {
            consumed.Add(item);
            if (consumed.Count == 2)
                break;
        }
        await firstReader.UpdateAsync(executionContext, CancellationToken.None);

        // Restart: a fresh reader instance opens from the persisted checkpoint.
        var secondReader = new XmlItemReader<(int Id, string Name)>(_filePath, "order", MapOrder);
        await secondReader.OpenAsync(executionContext, CancellationToken.None);

        var remaining = new List<(int Id, string Name)>();
        await foreach (var item in secondReader.ReadAsync(NewContext(), CancellationToken.None))
            remaining.Add(item);

        Assert.Equal([(3, "Gizmo"), (4, "Sprocket")], remaining);
    }

    [Fact]
    public async Task UpdateAsync_SavesCurrentIndex_AfterPartialRead()
    {
        WriteFile("""
            <?xml version="1.0" encoding="utf-8"?>
            <orders>
              <order><id>1</id><name>Widget</name></order>
              <order><id>2</id><name>Gadget</name></order>
              <order><id>3</id><name>Gizmo</name></order>
            </orders>
            """);

        var reader = new XmlItemReader<(int Id, string Name)>(_filePath, "order", MapOrder);
        var executionContext = new BatchExecutionContext();
        await reader.OpenAsync(executionContext, CancellationToken.None);

        var consumed = 0;
        await foreach (var _ in reader.ReadAsync(NewContext(), CancellationToken.None))
        {
            consumed++;
            if (consumed == 2)
                break;
        }
        await reader.UpdateAsync(executionContext, CancellationToken.None);

        Assert.Equal(2, executionContext.Get<int>("XmlItemReader.currentIndex"));
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
