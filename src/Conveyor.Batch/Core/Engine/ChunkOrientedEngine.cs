using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.Core.Engine;

/// <summary>
/// Drives chunk-oriented batch processing: reads items, processes them, and writes
/// committed chunks. Supports skip policies, retry policies, and chunk listeners.
/// </summary>
public sealed class ChunkOrientedEngine<TInput, TOutput>
{
    private readonly IItemReader<TInput> _reader;
    private readonly IItemProcessor<TInput, TOutput> _processor;
    private readonly IItemWriter<TOutput> _writer;
    private readonly int _chunkSize;
    private readonly ISkipPolicy? _skipPolicy;
    private readonly IRetryPolicy? _retryPolicy;
    private readonly IChunkListener? _listener;

    /// <summary>
    /// Initializes a new chunk-oriented engine with required components and optional policies.
    /// </summary>
    public ChunkOrientedEngine(
        IItemReader<TInput> reader,
        IItemProcessor<TInput, TOutput> processor,
        IItemWriter<TOutput> writer,
        int chunkSize,
        ISkipPolicy? skipPolicy = null,
        IRetryPolicy? retryPolicy = null,
        IChunkListener? listener = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        _reader = reader;
        _processor = processor;
        _writer = writer;
        _chunkSize = chunkSize;
        _skipPolicy = skipPolicy;
        _retryPolicy = retryPolicy;
        _listener = listener;
    }

    /// <summary>
    /// Executes the full chunk-oriented processing loop for the given step context.
    /// </summary>
    /// <param name="context">The step execution context tracking counts and state.</param>
    /// <param name="cancellationToken">Token to cancel the entire engine run.</param>
    public async Task ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var chunk = new List<TOutput>(_chunkSize);

        await foreach (var item in _reader.ReadAsync(context, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.IncrementReadCount();

            TOutput? processed;
            try
            {
                processed = await ProcessItemAsync(item, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (_skipPolicy?.ShouldSkip(ex, context.SkipCount) == true)
            {
                context.IncrementSkipCount();
                if (_listener is not null)
                    await _listener.OnSkipAsync(item, ex, context, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (processed is not null)
                chunk.Add(processed);

            if (chunk.Count >= _chunkSize)
                await CommitChunkAsync(chunk, context, cancellationToken).ConfigureAwait(false);
        }

        if (chunk.Count > 0)
            await CommitChunkAsync(chunk, context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<TOutput?> ProcessItemAsync(TInput item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        if (_retryPolicy is null)
            return await _processor.ProcessAsync(item, context, cancellationToken).ConfigureAwait(false);

        TOutput? result = default;
        await _retryPolicy.ExecuteAsync(async ct =>
        {
            result = await _processor.ProcessAsync(item, context, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async ValueTask CommitChunkAsync(List<TOutput> chunk, StepExecutionContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<TOutput> committed = chunk.AsReadOnly();

        if (_listener is not null)
            await _listener.BeforeWriteAsync(committed, context, cancellationToken).ConfigureAwait(false);

        await _writer.WriteAsync(committed, context, cancellationToken).ConfigureAwait(false);
        context.IncrementWriteCount(chunk.Count);

        if (_listener is not null)
            await _listener.AfterWriteAsync(committed, context, cancellationToken).ConfigureAwait(false);

        chunk.Clear();
    }
}
