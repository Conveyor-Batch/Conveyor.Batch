using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.Listeners;
using Conveyor.Batch.Policies;

namespace Conveyor.Batch.Core.Engine;

/// <summary>
/// Drives chunk-oriented batch processing with parallel item processing: a single producer
/// reads items into an input <see cref="Channel{T}"/>, a pool of worker tasks process items
/// concurrently and forward results into an output <see cref="Channel{T}"/>, and a single
/// chunk-assembler task accumulates and writes committed chunks. See ADR-004 for the choice
/// of <see cref="System.Threading.Channels"/> as the internal transport.
/// </summary>
/// <remarks>
/// <para>
/// <b>Output order is not guaranteed.</b> Because items are processed by
/// <c>degreeOfParallelism</c> workers running concurrently, the order in which processed items
/// reach the writer (and therefore the composition of each committed chunk) does not match the
/// order in which they were read. Use <see cref="ChunkOrientedEngine{TInput,TOutput}"/> instead
/// when strict ordering is required.
/// </para>
/// <para>
/// Skip-policy decisions read the current skip count concurrently across workers, so
/// count-based skip policies (e.g. "skip up to N items") observe a best-effort, potentially
/// racy snapshot of that count under parallel execution — unlike
/// <see cref="ChunkOrientedEngine{TInput,TOutput}"/>, which evaluates skip decisions with exact,
/// sequential ordering.
/// </para>
/// <para>
/// Unlike <see cref="ChunkOrientedEngine{TInput,TOutput}"/>, this engine does not persist a
/// mid-run checkpoint after each committed chunk: it still calls
/// <see cref="IItemStream.OpenAsync"/>/<see cref="IItemStream.CloseAsync"/> when the reader
/// implements <see cref="IItemStream"/>, but never <see cref="IItemStream.UpdateAsync"/>, since
/// restart/checkpoint semantics under concurrent processing are not defined by this engine.
/// </para>
/// </remarks>
public sealed class ConcurrentChunkOrientedEngine<TInput, TOutput>
{
    private readonly IItemReader<TInput> _reader;
    private readonly IItemProcessor<TInput, TOutput> _processor;
    private readonly IItemWriter<TOutput> _writer;
    private readonly int _chunkSize;
    private readonly int _degreeOfParallelism;
    private readonly ISkipPolicy? _skipPolicy;
    private readonly IRetryPolicy? _retryPolicy;
    private readonly IChunkListener? _listener;
    private readonly int _inputChannelCapacity;
    private readonly int _outputChannelCapacity;

    /// <summary>
    /// Initializes a new parallel chunk-oriented engine with required components and optional policies.
    /// </summary>
    /// <param name="reader">The item reader.</param>
    /// <param name="processor">The item processor, invoked concurrently by multiple worker tasks.</param>
    /// <param name="writer">The item writer. Always invoked sequentially from a single chunk-assembler task.</param>
    /// <param name="chunkSize">The commit interval.</param>
    /// <param name="degreeOfParallelism">The number of concurrent processor worker tasks. Must be at least 2.</param>
    /// <param name="skipPolicy">The optional skip policy.</param>
    /// <param name="retryPolicy">The optional retry policy.</param>
    /// <param name="listener">The optional chunk listener.</param>
    /// <param name="inputChannelCapacity">
    /// The bounded capacity of the reader-to-worker channel, or <c>0</c> for an unbounded channel.
    /// </param>
    /// <param name="outputChannelCapacity">
    /// The bounded capacity of the worker-to-assembler channel, or <c>0</c> for an unbounded channel.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="chunkSize"/> is less than 1, <paramref name="degreeOfParallelism"/>
    /// is less than 2, or either channel capacity is negative.
    /// </exception>
    public ConcurrentChunkOrientedEngine(
        IItemReader<TInput> reader,
        IItemProcessor<TInput, TOutput> processor,
        IItemWriter<TOutput> writer,
        int chunkSize,
        int degreeOfParallelism,
        ISkipPolicy? skipPolicy = null,
        IRetryPolicy? retryPolicy = null,
        IChunkListener? listener = null,
        int inputChannelCapacity = 0,
        int outputChannelCapacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(inputChannelCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(outputChannelCapacity);

        _reader = reader;
        _processor = processor;
        _writer = writer;
        _chunkSize = chunkSize;
        _degreeOfParallelism = degreeOfParallelism;
        _skipPolicy = skipPolicy;
        _retryPolicy = retryPolicy;
        _listener = listener;
        _inputChannelCapacity = inputChannelCapacity;
        _outputChannelCapacity = outputChannelCapacity;
    }

    /// <summary>
    /// Executes the full parallel chunk-oriented processing pipeline for the given step context.
    /// </summary>
    /// <param name="context">The step execution context tracking counts and state.</param>
    /// <param name="cancellationToken">Token to cancel the entire engine run.</param>
    /// <exception cref="Exception">
    /// Whatever exception is thrown by the reader, processor, or writer propagates unwrapped
    /// (not as an <see cref="AggregateException"/>) — the first stage to fault wins, and every
    /// other pipeline stage is cancelled promptly rather than left to hang.
    /// </exception>
    public async Task ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var stream = _reader as IItemStream;
        ExceptionDispatchInfo? firstFault = null;

        try
        {
            if (stream is not null)
                await stream.OpenAsync(context.StepExecution.ExecutionContext, cancellationToken).ConfigureAwait(false);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = linkedCts.Token;

            void ReportFault(Exception ex)
            {
                Interlocked.CompareExchange(ref firstFault, ExceptionDispatchInfo.Capture(ex), null);
                linkedCts.Cancel();
            }

            var inputChannel = CreateChannel<TInput>(_inputChannelCapacity, singleWriter: true, singleReader: false);
            var outputChannel = CreateChannel<TOutput>(_outputChannelCapacity, singleWriter: false, singleReader: true);

            var producerTask = RunProducerAsync(inputChannel.Writer, context, ReportFault, token);

            var workerTasks = new Task[_degreeOfParallelism];
            for (var i = 0; i < _degreeOfParallelism; i++)
                workerTasks[i] = RunWorkerAsync(inputChannel.Reader, outputChannel.Writer, context, ReportFault, token);

            var outputCompletionTask = CompleteOutputWhenWorkersFinishAsync(workerTasks, outputChannel.Writer);
            var assemblerTask = RunAssemblerAsync(outputChannel.Reader, context, ReportFault, token);

            var allTasks = new List<Task>(_degreeOfParallelism + 3) { producerTask, outputCompletionTask, assemblerTask };
            allTasks.AddRange(workerTasks);

            try
            {
                await Task.WhenAll(allTasks).ConfigureAwait(false);
            }
            catch
            {
                if (firstFault is null)
                    throw;
            }

            firstFault?.Throw();
        }
        finally
        {
            if (stream is not null)
                await stream.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunProducerAsync(
        ChannelWriter<TInput> writer,
        StepExecutionContext context,
        Action<Exception> reportFault,
        CancellationToken token)
    {
        try
        {
            await foreach (var item in _reader.ReadAsync(context, token).ConfigureAwait(false))
            {
                context.IncrementReadCount();
                await writer.WriteAsync(item, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            reportFault(ex);
            throw;
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task RunWorkerAsync(
        ChannelReader<TInput> reader,
        ChannelWriter<TOutput> writer,
        StepExecutionContext context,
        Action<Exception> reportFault,
        CancellationToken token)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                TOutput? processed;
                try
                {
                    processed = await ProcessItemAsync(item, context, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && _skipPolicy?.ShouldSkip(ex, context.SkipCount) == true)
                {
                    context.IncrementSkipCount();
                    if (_listener is not null)
                        await _listener.OnSkipAsync(item, ex, context, token).ConfigureAwait(false);
                    continue;
                }

                if (processed is not null)
                    await writer.WriteAsync(processed, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            reportFault(ex);
            throw;
        }
    }

    private static async Task CompleteOutputWhenWorkersFinishAsync(Task[] workerTasks, ChannelWriter<TOutput> writer)
    {
        try
        {
            await Task.WhenAll(workerTasks).ConfigureAwait(false);
        }
        catch
        {
            // The failing worker already reported its fault; this task's only job is to
            // unblock the assembler once every worker has finished, one way or another.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task RunAssemblerAsync(
        ChannelReader<TOutput> reader,
        StepExecutionContext context,
        Action<Exception> reportFault,
        CancellationToken token)
    {
        try
        {
            var chunk = new List<TOutput>(_chunkSize);

            await foreach (var item in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                chunk.Add(item);

                if (chunk.Count >= _chunkSize)
                    await CommitChunkAsync(chunk, context, token).ConfigureAwait(false);
            }

            if (chunk.Count > 0)
                await CommitChunkAsync(chunk, context, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            reportFault(ex);
            throw;
        }
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

    private static Channel<T> CreateChannel<T>(int capacity, bool singleWriter, bool singleReader) =>
        capacity > 0
            ? Channel.CreateBounded<T>(new BoundedChannelOptions(capacity) { SingleWriter = singleWriter, SingleReader = singleReader })
            : Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleWriter = singleWriter, SingleReader = singleReader });
}
