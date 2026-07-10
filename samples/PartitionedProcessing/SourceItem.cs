namespace PartitionedProcessing;

/// <summary>A seeded input row, partitioned by <see cref="Id"/> range.</summary>
sealed class SourceItem
{
    public long Id { get; set; }
    public double Value { get; set; }
}
