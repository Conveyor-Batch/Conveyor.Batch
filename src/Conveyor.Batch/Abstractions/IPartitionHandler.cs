using Conveyor.Batch.Core.Step;

namespace Conveyor.Batch.Abstractions;

/// <summary>
/// Executes a worker step once per partition and returns the resulting step executions.
/// </summary>
public interface IPartitionHandler
{
    /// <summary>
    /// Executes the worker step once per partition context and returns all resulting
    /// <see cref="StepExecution"/>s.
    /// </summary>
    /// <param name="workerStep">The step to execute per partition.</param>
    /// <param name="managerExecution">The manager's own step execution (for naming/traceability).</param>
    /// <param name="partitions">The partition name → context map produced by an <see cref="IPartitioner"/>.</param>
    /// <param name="cancellationToken">Token to cancel the overall partitioned run.</param>
    Task<IReadOnlyList<StepExecution>> HandleAsync(
        IStep workerStep,
        StepExecution managerExecution,
        IReadOnlyDictionary<string, BatchExecutionContext> partitions,
        CancellationToken cancellationToken);
}
