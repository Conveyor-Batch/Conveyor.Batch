using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Engine;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Repository;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.IO.FlatFile;

namespace Conveyor.Batch.UnitTests.Engine;

public sealed class RestartabilityTests
{
    // ──────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────

    private static StepExecutionContext MakeContext() =>
        new(new StepExecution { StepName = "test", JobExecution = new JobExecution { JobInstance = new JobInstance { JobName = "job" } } });

    private sealed class ListReader<T>(IEnumerable<T> items) : IItemReader<T>
    {
        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }

    private sealed class IdentityProcessor<T> : IItemProcessor<T, T>
    {
        public ValueTask<T?> ProcessAsync(T item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult<T?>(item);
    }

    private sealed class CapturingWriter<T> : IItemWriter<T>
    {
        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingOnNthChunkWriter<T>(int throwOnChunkNumber) : IItemWriter<T>
    {
        private int _chunkCount;
        public List<IReadOnlyList<T>> Chunks { get; } = [];

        public ValueTask WriteAsync(IReadOnlyList<T> items, StepExecutionContext ctx, CancellationToken ct)
        {
            _chunkCount++;
            if (_chunkCount == throwOnChunkNumber)
                throw new InvalidOperationException("simulated writer failure");
            Chunks.Add(items.ToList());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RestartableListReader<T>(IEnumerable<T> items) : IItemReader<T>, IItemStream
    {
        private readonly List<T> _items = items.ToList();
        private int _skip;
        private int _index;

        public List<string> Calls { get; } = [];
        public BatchExecutionContext? LastOpenContext { get; private set; }

        public ValueTask OpenAsync(BatchExecutionContext context, CancellationToken ct)
        {
            Calls.Add("Open");
            LastOpenContext = context;
            _skip = context.Get<int>("RestartableListReader.index");
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(BatchExecutionContext context, CancellationToken ct)
        {
            Calls.Add("Update");
            context.Put("RestartableListReader.index", _index);
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken ct)
        {
            Calls.Add("Close");
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<T> ReadAsync(StepExecutionContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            _index = _skip;
            for (int i = _skip; i < _items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                _index = i + 1;
                yield return _items[i];
            }
        }
    }

    /// <summary>Wraps an <see cref="IJobRepository"/> and counts calls to <see cref="UpdateStepExecutionAsync"/>.</summary>
    private sealed class SpyJobRepository(IJobRepository inner) : IJobRepository
    {
        public int UpdateStepExecutionCallCount { get; private set; }

        public Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters) =>
            inner.CreateJobInstanceAsync(jobName, parameters);

        public Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters) =>
            inner.CreateJobExecutionAsync(instance, parameters);

        public Task UpdateJobExecutionAsync(JobExecution execution) =>
            inner.UpdateJobExecutionAsync(execution);

        public Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName) =>
            inner.CreateStepExecutionAsync(jobExecution, stepName);

        public Task UpdateStepExecutionAsync(StepExecution stepExecution)
        {
            UpdateStepExecutionCallCount++;
            return inner.UpdateStepExecutionAsync(stepExecution);
        }

        public Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters) =>
            inner.GetLastJobExecutionAsync(jobName, parameters);

        public Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance) =>
            inner.GetJobExecutionsAsync(instance);

        public Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName) =>
            inner.GetLastStepExecutionAsync(jobExecutionId, stepName);
    }

    // ──────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task NonRestartableReader_CompletesNormally()
    {
        var writer = new CapturingWriter<int>();
        var engine = new ChunkOrientedEngine<int, int>(
            new ListReader<int>([1, 2, 3]),
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 10);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Single(writer.Chunks);
        Assert.Equal([1, 2, 3], writer.Chunks[0]);
    }

    [Fact]
    public async Task RestartableReader_OpenCalledWithContext()
    {
        var reader = new RestartableListReader<int>([1, 2, 3]);
        var context = MakeContext();
        var engine = new ChunkOrientedEngine<int, int>(
            reader,
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 10);

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("Open", reader.Calls[0]);
        Assert.Same(context.StepExecution.ExecutionContext, reader.LastOpenContext);
    }

    [Fact]
    public async Task RestartableReader_UpdateCalledAfterEachChunk()
    {
        var reader = new RestartableListReader<int>(Enumerable.Range(1, 10));
        var engine = new ChunkOrientedEngine<int, int>(
            reader,
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 3);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Equal(4, reader.Calls.Count(c => c == "Update"));
    }

    [Fact]
    public async Task RestartableReader_CloseCalledOnCompletion()
    {
        var reader = new RestartableListReader<int>([1, 2]);
        var engine = new ChunkOrientedEngine<int, int>(
            reader,
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 10);

        await engine.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Equal("Close", reader.Calls[^1]);
    }

    [Fact]
    public async Task RestartableReader_CloseCalledOnFailure()
    {
        var reader = new RestartableListReader<int>(Enumerable.Range(1, 6));
        var writer = new ThrowingOnNthChunkWriter<int>(throwOnChunkNumber: 2);
        var engine = new ChunkOrientedEngine<int, int>(
            reader,
            new IdentityProcessor<int>(),
            writer,
            chunkSize: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(MakeContext(), CancellationToken.None));

        Assert.Contains("Close", reader.Calls);
    }

    [Fact]
    public async Task Checkpoint_PersistedToRepositoryAfterEachChunk()
    {
        var innerRepository = new InMemoryJobRepository();
        var spy = new SpyJobRepository(innerRepository);

        var instance = await innerRepository.CreateJobInstanceAsync("job", JobParameters.Empty);
        var jobExecution = await innerRepository.CreateJobExecutionAsync(instance, JobParameters.Empty);
        var stepExecution = await innerRepository.CreateStepExecutionAsync(jobExecution, "step");

        var context = new StepExecutionContext(stepExecution);
        var reader = new RestartableListReader<int>(Enumerable.Range(1, 9));
        var engine = new ChunkOrientedEngine<int, int>(
            reader,
            new IdentityProcessor<int>(),
            new CapturingWriter<int>(),
            chunkSize: 3,
            jobRepository: spy,
            stepExecution: stepExecution);

        await engine.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(3, spy.UpdateStepExecutionCallCount);
        Assert.Equal(9, stepExecution.WriteCount);
    }

    [Fact]
    public async Task FlatFileItemReader_Restart_ResumesFromSavedLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            var lines = Enumerable.Range(1, 10).Select(i => $"line{i}").ToArray();
            await File.WriteAllLinesAsync(path, lines);

            var firstReader = new FlatFileItemReader<string>(path, line => line, skipHeader: false);
            await firstReader.OpenAsync(new BatchExecutionContext(), CancellationToken.None);

            var firstFive = new List<string>();
            await foreach (var item in firstReader.ReadAsync(MakeContext(), CancellationToken.None))
            {
                firstFive.Add(item);
                if (firstFive.Count == 5)
                    break;
            }

            var savedContext = new BatchExecutionContext();
            await firstReader.UpdateAsync(savedContext, CancellationToken.None);
            Assert.Equal(5, savedContext.Get<int>("FlatFileItemReader.currentLine"));

            var secondReader = new FlatFileItemReader<string>(path, line => line, skipHeader: false);
            await secondReader.OpenAsync(savedContext, CancellationToken.None);

            var remaining = new List<string>();
            await foreach (var item in secondReader.ReadAsync(MakeContext(), CancellationToken.None))
                remaining.Add(item);

            Assert.Equal(lines.Skip(5), remaining);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
