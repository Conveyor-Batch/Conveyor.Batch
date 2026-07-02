using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Partitioning;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// Fluent builder for constructing a partitioned <see cref="IStep"/> that divides its workload
/// across N partitions and runs a worker step once per partition.
/// </summary>
public sealed class PartitionStepBuilder
{
    private readonly IJobRepository _repository;
    private IStep? _workerStep;
    private IPartitioner? _partitioner;
    private int _gridSize = 4;
    private int _maxDegreeOfParallelism = -1;

    /// <summary>
    /// Initializes a new <see cref="PartitionStepBuilder"/> with the given repository.
    /// </summary>
    /// <param name="repository">The job repository used to persist step execution state.</param>
    public PartitionStepBuilder(IJobRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>Sets the worker step executed once per partition.</summary>
    /// <param name="step">The worker step implementation.</param>
    /// <returns>This builder for chaining.</returns>
    public PartitionStepBuilder Worker(IStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _workerStep = step;
        return this;
    }

    /// <summary>Sets the partitioner that divides the workload into partitions.</summary>
    /// <param name="partitioner">The partitioner implementation.</param>
    /// <returns>This builder for chaining.</returns>
    public PartitionStepBuilder Partitioner(IPartitioner partitioner)
    {
        ArgumentNullException.ThrowIfNull(partitioner);
        _partitioner = partitioner;
        return this;
    }

    /// <summary>Sets the number of partitions to create.</summary>
    /// <param name="gridSize">The requested number of partitions; must be at least 1.</param>
    /// <returns>This builder for chaining.</returns>
    public PartitionStepBuilder GridSize(int gridSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gridSize, 1);
        _gridSize = gridSize;
        return this;
    }

    /// <summary>Sets the maximum number of partitions to run concurrently.</summary>
    /// <param name="maxDegreeOfParallelism">The concurrency cap, or <c>-1</c> for unbounded.</param>
    /// <returns>This builder for chaining.</returns>
    public PartitionStepBuilder MaxDegreeOfParallelism(int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism != -1)
            ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured partitioned <see cref="IStep"/>.
    /// </summary>
    /// <param name="name">The unique name of the step within its job.</param>
    /// <returns>A new <see cref="IStep"/> backed by a <see cref="PartitionStep"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if worker or partitioner is not configured.</exception>
    public IStep Build(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_workerStep is null) throw new InvalidOperationException("A worker step must be configured via Worker().");
        if (_partitioner is null) throw new InvalidOperationException("A partitioner must be configured via Partitioner().");

        var handler = new LocalPartitionHandler(_repository, _maxDegreeOfParallelism);
        return new PartitionStep(name, _workerStep, _partitioner, handler, _repository, _gridSize);
    }
}
