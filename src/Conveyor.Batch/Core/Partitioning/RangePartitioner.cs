using Conveyor.Batch.Abstractions;

namespace Conveyor.Batch.Core.Partitioning;

/// <summary>
/// Divides an inclusive numeric range <c>[minValue, maxValue]</c> into a configurable number
/// of equally-sized partitions, keyed as <c>partition0</c>, <c>partition1</c>, and so on.
/// When the range does not divide evenly, the last partition absorbs the remainder.
/// </summary>
public sealed class RangePartitioner : IPartitioner
{
    private readonly long _minValue;
    private readonly long _maxValue;

    /// <summary>
    /// Initializes a new <see cref="RangePartitioner"/> over the inclusive range
    /// <paramref name="minValue"/> to <paramref name="maxValue"/>.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the range to partition.</param>
    /// <param name="maxValue">The inclusive upper bound of the range to partition.</param>
    public RangePartitioner(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        _minValue = minValue;
        _maxValue = maxValue;
    }

    /// <inheritdoc />
    public IDictionary<string, BatchExecutionContext> Partition(int gridSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gridSize, 1);

        var result = new Dictionary<string, BatchExecutionContext>(gridSize);

        long totalRange = _maxValue - _minValue + 1;
        long baseSize = totalRange / gridSize;
        long remainder = totalRange % gridSize;

        for (int i = 0; i < gridSize; i++)
        {
            long size = baseSize + (i == gridSize - 1 ? remainder : 0);
            long partitionMin = _minValue + i * baseSize;
            long partitionMax = partitionMin + size - 1;

            var context = new BatchExecutionContext();
            context.Put("partition.minValue", partitionMin);
            context.Put("partition.maxValue", partitionMax);

            result[$"partition{i}"] = context;
        }

        return result;
    }
}
