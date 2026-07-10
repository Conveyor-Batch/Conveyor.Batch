using System.Diagnostics;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;
using Conveyor.Batch.Policies;
using Conveyor.Batch.Telemetry;

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
    private readonly IJobRepository? _jobRepository;
    private readonly GracefulShutdownOptions? _gracefulShutdown;

    /// <summary>
    /// Initializes a new chunk-oriented engine with required components and optional policies.
    /// </summary>
    /// <param name="reader">The item reader.</param>
    /// <param name="processor">The item processor.</param>
    /// <param name="writer">The item writer.</param>
    /// <param name="chunkSize">The commit interval.</param>
    /// <param name="skipPolicy">The optional skip policy.</param>
    /// <param name="retryPolicy">The optional retry policy.</param>
    /// <param name="listener">The optional chunk listener.</param>
    /// <param name="jobRepository">
    /// Optional repository used to persist the step execution's checkpoint after every
    /// committed chunk, when <paramref name="reader"/> implements <see cref="IItemStream"/>.
    /// </param>
    /// <param name="stepExecution">
    /// Accepted for API-surface completeness, but not read directly by the engine: checkpoint
    /// and count state is always read and written via the <see cref="StepExecutionContext"/>
    /// passed to <see cref="ExecuteAsync"/> (i.e. <c>context.StepExecution</c>). Callers must
    /// pass the same <see cref="Conveyor.Batch.Core.Step.StepExecution"/> instance that the
    /// execution context wraps, so there is a single source of truth.
    /// </param>
    /// <param name="gracefulShutdown">
    /// Optional graceful shutdown configuration. When set, the <see cref="CancellationToken"/>
    /// passed to <see cref="ExecuteAsync"/> is treated as a "stop" signal rather than a hard
    /// abort: the engine stops reading further items but finishes processing and writing the
    /// item(s) already read, commits the current chunk, and persists a checkpoint before
    /// returning with <c>context.StepExecution.Status</c> set to
    /// <see cref="BatchStatus.Stopped"/>. An internal abort deadline
    /// (<see cref="GracefulShutdownOptions.DrainTimeout"/>) starts once the stop signal fires; if
    /// the drain does not complete in time, the in-flight operation is cancelled for real and an
    /// <see cref="OperationCanceledException"/> propagates. When <see langword="null"/> (the
    /// default), cancellation behaves exactly as before: it aborts immediately, mid-item.
    /// </param>
    public ChunkOrientedEngine(
        IItemReader<TInput> reader,
        IItemProcessor<TInput, TOutput> processor,
        IItemWriter<TOutput> writer,
        int chunkSize,
        ISkipPolicy? skipPolicy = null,
        IRetryPolicy? retryPolicy = null,
        IChunkListener? listener = null,
        IJobRepository? jobRepository = null,
        StepExecution? stepExecution = null,
        GracefulShutdownOptions? gracefulShutdown = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        _reader = reader;
        _processor = processor;
        _writer = writer;
        _chunkSize = chunkSize;
        _skipPolicy = skipPolicy;
        _retryPolicy = retryPolicy;
        _listener = listener;
        _jobRepository = jobRepository;
        _gracefulShutdown = gracefulShutdown;
    }

    /// <summary>
    /// Executes the full chunk-oriented processing loop for the given step context.
    /// </summary>
    /// <param name="context">The step execution context tracking counts and state.</param>
    /// <param name="cancellationToken">
    /// The stop token. With no <see cref="GracefulShutdownOptions"/> configured, cancelling this
    /// token aborts the run immediately, mid-item. With graceful shutdown configured, cancelling
    /// this token stops the engine from reading further items but lets the item(s) already read
    /// finish processing, writing, and checkpointing within the configured drain window — see the
    /// <c>gracefulShutdown</c> constructor parameter.
    /// </param>
    public async Task ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var stream = _reader as IItemStream;
        var stoppingToken = cancellationToken;
        var abortToken = cancellationToken;

        CancellationTokenSource? abortCts = null;
        CancellationTokenRegistration drainRegistration = default;

        if (_gracefulShutdown is not null)
        {
            abortCts = new CancellationTokenSource();
            abortToken = abortCts.Token;
            drainRegistration = cancellationToken.Register(() => abortCts.CancelAfter(_gracefulShutdown.DrainTimeout));
        }

        try
        {
            if (stream is not null)
                await stream.OpenAsync(context.StepExecution.ExecutionContext, cancellationToken).ConfigureAwait(false);

            var chunk = new List<TOutput>(_chunkSize);
            var stopped = false;

            try
            {
                await foreach (var item in _reader.ReadAsync(context, stoppingToken).WithCancellation(stoppingToken).ConfigureAwait(false))
                {
                    if (_gracefulShutdown is null)
                        cancellationToken.ThrowIfCancellationRequested();

                    context.IncrementReadCount();

                    TOutput? processed;
                    try
                    {
                        processed = await ProcessItemAsync(item, context, abortToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (_skipPolicy?.ShouldSkip(ex, context.SkipCount) == true)
                    {
                        context.IncrementSkipCount();
                        if (_listener is not null)
                            await _listener.OnSkipAsync(item, ex, context, abortToken).ConfigureAwait(false);
                        continue;
                    }

                    if (processed is not null)
                        chunk.Add(processed);

                    if (chunk.Count >= _chunkSize)
                    {
                        await CommitChunkAsync(chunk, context, abortToken).ConfigureAwait(false);
                        await CheckpointAsync(stream, context, abortToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (_gracefulShutdown is not null && stoppingToken.IsCancellationRequested && !abortToken.IsCancellationRequested)
            {
                stopped = true;
            }

            if (chunk.Count > 0)
            {
                await CommitChunkAsync(chunk, context, abortToken).ConfigureAwait(false);
                await CheckpointAsync(stream, context, abortToken).ConfigureAwait(false);
            }

            if (stopped)
                context.StepExecution.Status = BatchStatus.Stopped;
        }
        finally
        {
            drainRegistration.Dispose();
            abortCts?.Dispose();

            if (stream is not null)
                await stream.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CheckpointAsync(IItemStream? stream, StepExecutionContext context, CancellationToken cancellationToken)
    {
        if (stream is null)
            return;

        await stream.UpdateAsync(context.StepExecution.ExecutionContext, cancellationToken).ConfigureAwait(false);

        if (_jobRepository is not null)
            await _jobRepository.UpdateStepExecutionAsync(context.StepExecution).ConfigureAwait(false);
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
        var activity = ConveyorBatchTelemetry.ActivitySource.StartActivity(ConveyorBatchTelemetry.ChunkActivityName);
        activity?.SetTag(ConveyorBatchTelemetry.ChunkSizeTag, chunk.Count);
        activity?.SetTag(ConveyorBatchTelemetry.StepNameTag, context.StepName);

        try
        {
            IReadOnlyList<TOutput> committed = chunk.AsReadOnly();

            if (_listener is not null)
                await _listener.BeforeWriteAsync(committed, context, cancellationToken).ConfigureAwait(false);

            await _writer.WriteAsync(committed, context, cancellationToken).ConfigureAwait(false);

            var metricTags = new TagList { { ConveyorBatchTelemetry.StepNameTag, context.StepName } };
            ConveyorBatchTelemetry.ChunksCommitted.Add(1, metricTags);
            ConveyorBatchTelemetry.ChunkSize.Record(chunk.Count, metricTags);
            context.IncrementWriteCount(chunk.Count);

            if (_listener is not null)
                await _listener.AfterWriteAsync(committed, context, cancellationToken).ConfigureAwait(false);

            chunk.Clear();
        }
        finally
        {
            activity?.Stop();
        }
    }
}
