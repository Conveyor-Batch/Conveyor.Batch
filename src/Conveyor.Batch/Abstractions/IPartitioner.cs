namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Divides a step's workload into a set of named, independently executable partitions.
/// </summary>
public interface IPartitioner
{
    /// <summary>
    /// Divides the step's input data into independent partitions.
    /// </summary>
    /// <param name="gridSize">Hint for the number of partitions to create.</param>
    /// <returns>
    /// Map of partition name to <see cref="BatchExecutionContext"/>. Each context carries the
    /// data-range information the worker step needs (e.g., minValue / maxValue).
    /// </returns>
    IDictionary<string, BatchExecutionContext> Partition(int gridSize);
}
