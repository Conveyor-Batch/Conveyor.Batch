using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Processors;
using Conveyor.Batch.Core.Writers;
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
    private int _degreeOfParallelism = 1;
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

    /// <summary>
    /// Replaces the configured processor with a <see cref="CompositeItemProcessor{T}"/> that
    /// runs the given processors in sequence, short-circuiting to <see langword="null"/> if
    /// any processor filters the item.
    /// </summary>
    /// <param name="processors">The processors to chain, in execution order.</param>
    /// <returns>This builder for chaining.</returns>
    /// <remarks>
    /// Only valid when <typeparamref name="TInput"/> and <typeparamref name="TOutput"/> are the
    /// same type, since the resulting composite processes <typeparamref name="TOutput"/> items
    /// to <typeparamref name="TOutput"/> items.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if <paramref name="processors"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <typeparamref name="TInput"/> and <typeparamref name="TOutput"/> are not the same type.
    /// </exception>
    public StepBuilder<TInput, TOutput> Processors(params IItemProcessor<TOutput, TOutput>[] processors)
    {
        var composite = new CompositeItemProcessor<TOutput>(processors);
        _processor = composite as IItemProcessor<TInput, TOutput>
            ?? throw new InvalidOperationException(
                $"{nameof(Processors)}() is only valid when TInput and TOutput are the same type.");
        return this;
    }

    /// <summary>
    /// Replaces the configured writer with a <see cref="CompositeItemWriter{T}"/> that fans out
    /// each committed chunk to all of the given writers, in sequence.
    /// </summary>
    /// <param name="writers">The writers to fan out to, in execution order.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="writers"/> is empty.</exception>
    public StepBuilder<TInput, TOutput> Writers(params IItemWriter<TOutput>[] writers)
    {
        _writer = new CompositeItemWriter<TOutput>(writers);
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

    /// <summary>
    /// Sets the number of concurrent processor worker tasks. Defaults to 1 (sequential
    /// processing via <see cref="ChunkOrientedEngine{TInput,TOutput}"/>). Values greater than 1
    /// switch the built step to <see cref="ConcurrentChunkOrientedEngine{TInput,TOutput}"/>,
    /// whose output order is not guaranteed — see that engine's XML docs for details.
    /// </summary>
    /// <param name="dop">The degree of parallelism; must be at least 1.</param>
    /// <returns>This builder for chaining.</returns>
    public StepBuilder<TInput, TOutput> DegreeOfParallelism(int dop)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dop, 1);
        _degreeOfParallelism = dop;
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
    /// <returns>
    /// A new <see cref="IStep"/> backed by a <see cref="ChunkOrientedEngine{TInput,TOutput}"/> when
    /// <see cref="DegreeOfParallelism"/> is 1 (the default), or a
    /// <see cref="ConcurrentChunkOrientedEngine{TInput,TOutput}"/> when it is greater than 1.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown if reader, processor, or writer is not configured.</exception>
    public IStep Build(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_reader is null) throw new InvalidOperationException("A reader must be configured via Reader().");
        if (_processor is null) throw new InvalidOperationException("A processor must be configured via Processor().");
        if (_writer is null) throw new InvalidOperationException("A writer must be configured via Writer().");

        if (_degreeOfParallelism == 1)
            return new ChunkOrientedStep<TInput, TOutput>(
                name, _reader, _processor, _writer, _chunkSize, _skipPolicy, _retryPolicy, _listener, _repository);

        return new ConcurrentChunkOrientedStep<TInput, TOutput>(
            name, _reader, _processor, _writer, _chunkSize, _degreeOfParallelism, _skipPolicy, _retryPolicy, _listener, _repository);
    }
}

internal sealed class ChunkOrientedStep<TInput, TOutput> : IStep
{
    private readonly IItemReader<TInput> _reader;
    private readonly IItemProcessor<TInput, TOutput> _processor;
    private readonly IItemWriter<TOutput> _writer;
    private readonly int _chunkSize;
    private readonly ISkipPolicy? _skipPolicy;
    private readonly IRetryPolicy? _retryPolicy;
    private readonly IChunkListener? _listener;
    private readonly IJobRepository _repository;

    public string Name { get; }

    internal ChunkOrientedStep(
        string name,
        IItemReader<TInput> reader,
        IItemProcessor<TInput, TOutput> processor,
        IItemWriter<TOutput> writer,
        int chunkSize,
        ISkipPolicy? skipPolicy,
        IRetryPolicy? retryPolicy,
        IChunkListener? listener,
        IJobRepository repository)
    {
        Name = name;
        _reader = reader;
        _processor = processor;
        _writer = writer;
        _chunkSize = chunkSize;
        _skipPolicy = skipPolicy;
        _retryPolicy = retryPolicy;
        _listener = listener;
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
    {
        var stepExecution = await _repository.CreateStepExecutionAsync(jobExecution, Name).ConfigureAwait(false);

        if (jobExecution.RestartedFromExecutionId is long previousJobExecutionId)
        {
            var previousStepExecution = await _repository
                .GetLastStepExecutionAsync(previousJobExecutionId, Name)
                .ConfigureAwait(false);

            if (previousStepExecution is not null)
            {
                stepExecution.ExecutionContext = BatchExecutionContext.FromDictionary(
                    new Dictionary<string, string>(previousStepExecution.ExecutionContext.ToDictionary()));
                stepExecution.IsRestart = true;
            }
        }

        stepExecution.Status = BatchStatus.Started;
        await _repository.UpdateStepExecutionAsync(stepExecution).ConfigureAwait(false);

        var context = new StepExecutionContext(stepExecution);
        var engine = new ChunkOrientedEngine<TInput, TOutput>(
            _reader, _processor, _writer, _chunkSize, _skipPolicy, _retryPolicy, _listener,
            jobRepository: _repository, stepExecution: stepExecution);

        try
        {
            await engine.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
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

internal sealed class ConcurrentChunkOrientedStep<TInput, TOutput> : IStep
{
    private readonly IItemReader<TInput> _reader;
    private readonly IItemProcessor<TInput, TOutput> _processor;
    private readonly IItemWriter<TOutput> _writer;
    private readonly int _chunkSize;
    private readonly int _degreeOfParallelism;
    private readonly ISkipPolicy? _skipPolicy;
    private readonly IRetryPolicy? _retryPolicy;
    private readonly IChunkListener? _listener;
    private readonly IJobRepository _repository;

    public string Name { get; }

    internal ConcurrentChunkOrientedStep(
        string name,
        IItemReader<TInput> reader,
        IItemProcessor<TInput, TOutput> processor,
        IItemWriter<TOutput> writer,
        int chunkSize,
        int degreeOfParallelism,
        ISkipPolicy? skipPolicy,
        IRetryPolicy? retryPolicy,
        IChunkListener? listener,
        IJobRepository repository)
    {
        Name = name;
        _reader = reader;
        _processor = processor;
        _writer = writer;
        _chunkSize = chunkSize;
        _degreeOfParallelism = degreeOfParallelism;
        _skipPolicy = skipPolicy;
        _retryPolicy = retryPolicy;
        _listener = listener;
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<StepExecution> ExecuteAsync(JobExecution jobExecution, CancellationToken cancellationToken)
    {
        var stepExecution = await _repository.CreateStepExecutionAsync(jobExecution, Name).ConfigureAwait(false);

        if (jobExecution.RestartedFromExecutionId is long previousJobExecutionId)
        {
            var previousStepExecution = await _repository
                .GetLastStepExecutionAsync(previousJobExecutionId, Name)
                .ConfigureAwait(false);

            if (previousStepExecution is not null)
            {
                stepExecution.ExecutionContext = BatchExecutionContext.FromDictionary(
                    new Dictionary<string, string>(previousStepExecution.ExecutionContext.ToDictionary()));
                stepExecution.IsRestart = true;
            }
        }

        stepExecution.Status = BatchStatus.Started;
        await _repository.UpdateStepExecutionAsync(stepExecution).ConfigureAwait(false);

        var context = new StepExecutionContext(stepExecution);
        var engine = new ConcurrentChunkOrientedEngine<TInput, TOutput>(
            _reader, _processor, _writer, _chunkSize, _degreeOfParallelism, _skipPolicy, _retryPolicy, _listener);

        try
        {
            await engine.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
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
