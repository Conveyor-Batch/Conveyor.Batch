namespace PartitionedProcessing;

/// <summary>The result of processing one <see cref="SourceItem"/>.</summary>
sealed class ProcessedItem
{
    public long Id { get; set; }
    public long SourceId { get; set; }
    public double Result { get; set; }
}
