using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Listeners;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.Core.Step;

/// <summary>
/// Fluent builder for constructing a chunk-oriented <see cref="IStep"/>.
/// </summary>
/// <typeparam name="TInput">The type of items read from the source.</typeparam>
/// <typeparam name="TOutput">The type of items written after processing.</typeparam>
public sealed class StepBuilder<TInput, TOutput>
{
    private readonly IJobRepository _repository;
    private IItemReader<TInput>? _reader;
    private IItemProcessor<TInput, TOutput>? _processor;
    private IItemWriter<TOutput>? _writer;
    private int _chunkSize = 10;
    private ISkipPolicy? _skipPolicy;
    private IRetryPolicy? _retryPolicy;
    private IChunkListener? _listener;

    /// <summary>
    /// Initializes a new <see cref="StepBuilder{TInput,TOutput}"/> with the given repository.
    /// </summary>
    /// <param name="repository">The job repository used to persist step execution state.</param>
    public StepBuilder(IJobRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>Sets the item reader that produces input items.</summary>
    /// <param name="reader">The reader implementation.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> Reader(IItemReader<TInput> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        return this;
    }

    /// <summary>Sets the item processor that transforms input items.</summary>
    /// <param name="processor">The processor implementation.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> Processor(IItemProcessor<TInput, TOutput> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
        return this;
    }

    /// <summary>Sets the item writer that receives committed chunks.</summary>
    /// <param name="writer">The writer implementation.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> Writer(IItemWriter<TOutput> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        return this;
    }

    /// <summary>Sets the number of items per committed chunk (commit interval).</summary>
    /// <param name="size">The chunk size; must be at least 1.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> ChunkSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        _chunkSize = size;
        return this;
    }

    /// <summary>Sets the skip policy applied to processing exceptions.</summary>
    /// <param name="policy">The skip policy.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> SkipPolicy(ISkipPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _skipPolicy = policy;
        return this;
    }

    /// <summary>Sets the retry policy applied to processing exceptions.</summary>
    /// <param name="policy">The retry policy.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> RetryPolicy(IRetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _retryPolicy = policy;
        return this;
    }

    /// <summary>Sets the chunk listener that receives before/after write notifications.</summary>
    /// <param name="listener">The chunk listener.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> Listener(IChunkListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listener = listener;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured chunk-oriented <see cref="IStep"/>.
    /// </summary>
    /// <param name="name">The unique name of the step within its job.</param>
    /// <returns>A new <see cref="IStep"/> backed by a <see cref="ChunkOrientedEngine{TInput,TOutput}"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if reader, processor, or writer is not configured.</exception>
    public IStep Build(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_reader is null) throw new InvalidOperationException("A reader must be configured via Reader().");
        if (_processor is null) throw new InvalidOperationException("A processor must be configured via Processor().");
        if (_writer is null) throw new InvalidOperationException("A writer must be configured via Writer().");

        var engine = new ChunkOrientedEngine<TInput, TOutput>(
            _reader, _processor, _writer, _chunkSize, _skipPolicy, _retryPolicy, _listener);

        return new ChunkOrientedStep<TInput, TOutput>(name, engine, _repository);
    }
}

internal sealed class ChunkOrientedStep<TInput, TOutput> : IStep
{
    private readonly ChunkOrientedEngine<TInput, TOutput> _engine;
    private readonly IJobRepository _repository;

    public string Name { get; }

    internal ChunkOrientedStep(string name, ChunkOrientedEngine<TInput, TOutput> engine, IJobRepository repository)
    {
        Name = name;
        _engine = engine;
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
    {
        var stepExecution = await _repository.CreateStepExecutionAsync(jobExecution, Name).ConfigureAwait(false);
        stepExecution.Status = BatchStatus.Started;
        await _repository.UpdateStepExecutionAsync(stepExecution).ConfigureAwait(false);

        var context = new StepExecutionContext(stepExecution);

        try
        {
            await _engine.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            stepExecution.Status = BatchStatus.Completed;
        }
        catch (Exception ex)
        {
            stepExecution.Status = BatchStatus.Failed;
            stepExecution.FailureException = ex;
        }
        finally
        {
            stepExecution.EndTime = DateTimeOffset.UtcNow;
            await _repository.UpdateStepExecutionAsync(stepExecution).ConfigureAwait(false);
        }

        return stepExecution;
    }
}
