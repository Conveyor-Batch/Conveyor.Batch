using Conveyor.Batch.Core.Partitioning;

namespace Conveyor.Batch.UnitTests.Partitioning;

public sealed class RangePartitionerTests
{
    [Fact]
    public void EvenDivision_CreatesEqualPartitions()
    {
        var result = new RangePartitioner(1, 100).Partition(4);

        Assert.Equal(4, result.Count);
        for (int i = 0; i < 4; i++)
        {
            var ctx = result[$"partition{i}"];
            Assert.Equal(1 + i * 25, ctx.Get<long>("partition.minValue"));
            Assert.Equal(25 + i * 25, ctx.Get<long>("partition.maxValue"));
        }
    }

    [Fact]
    public void UnevenDivision_LastPartitionGetsRemainder()
    {
        var result = new RangePartitioner(1, 10).Partition(3);

        Assert.Equal(1L, result["partition0"].Get<long>("partition.minValue"));
        Assert.Equal(3L, result["partition0"].Get<long>("partition.maxValue"));

        Assert.Equal(4L, result["partition1"].Get<long>("partition.minValue"));
        Assert.Equal(6L, result["partition1"].Get<long>("partition.maxValue"));

        Assert.Equal(7L, result["partition2"].Get<long>("partition.minValue"));
        Assert.Equal(10L, result["partition2"].Get<long>("partition.maxValue"));
    }

    [Fact]
    public void GridSizeOne_SinglePartitionCoversFullRange()
    {
        var result = new RangePartitioner(5, 50).Partition(1);

        Assert.Single(result);
        Assert.Equal(5L, result["partition0"].Get<long>("partition.minValue"));
        Assert.Equal(50L, result["partition0"].Get<long>("partition.maxValue"));
    }

    [Fact]
    public void PartitionNamesAreUnique()
    {
        var result = new RangePartitioner(1, 97).Partition(5);

        Assert.Equal(5, result.Keys.Count);
        Assert.Equal(result.Keys.Count, result.Keys.Distinct().Count());
        Assert.All(Enumerable.Range(0, 5), i => Assert.True(result.ContainsKey($"partition{i}")));
    }

    [Fact]
    public void GridSizeZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var partitioner = new RangePartitioner(1, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => partitioner.Partition(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => partitioner.Partition(-1));
    }
}
