# Partitioning

Partitioning splits a large dataset into independent ranges and processes them concurrently, each range running the same worker step. `RangePartitioner` divides a numeric range into equal partitions, and `LocalPartitionHandler` runs each partition's worker step locally, up to a configurable degree of parallelism.

## Building a partitioned step

```csharp
using Conveyor.Batch.Core.Partitioning;
using Conveyor.Batch.Core.Step;

const int itemCount = 10_000;
const int gridSize = 4;

var workerStep = new StepBuilder<SourceItem, ProcessedItem>(jobRepository)
    .Reader(reader)       // reads only the [min,max] range assigned to this partition
    .Processor(processor)
    .Writer(writer)
    .ChunkSize(250)
    .Build("range-worker");

var partitionStep = new PartitionStepBuilder(jobRepository)
    .Worker(workerStep)
    .Partitioner(new RangePartitioner(1, itemCount))
    .GridSize(gridSize)
    .MaxDegreeOfParallelism(gridSize)
    .Build("partitioned-step");

var job = new JobBuilder("partitioned-processing", jobRepository)
    .AddStep(partitionStep)
    .Build();

var execution = await job.ExecuteAsync(JobParameters.Empty, CancellationToken.None);
```

`RangePartitioner(1, itemCount)` divides `[1, itemCount]` into `gridSize` equal partitions (the last partition absorbs any remainder). `PartitionStepBuilder` runs the *same* worker step once per partition — up to `MaxDegreeOfParallelism` concurrently — and each worker reads only the rows in its assigned range, typically by filtering an `EfCoreItemReader` query to the partition's `[min,max]` bounds (see the [`PartitionedProcessing` sample](https://github.com/Conveyor-Batch/Conveyor.Batch/blob/main/samples/PartitionedProcessing/Program.cs) for a complete, runnable version of this pattern).

::: tip When to use
Use when a single dataset is large enough that splitting it into independent ranges and processing them concurrently meaningfully reduces wall-clock time — for example, a nightly batch over millions of rows keyed by a monotonically increasing ID.
:::
