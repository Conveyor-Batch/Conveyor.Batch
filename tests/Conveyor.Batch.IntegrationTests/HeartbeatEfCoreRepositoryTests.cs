using System.Runtime.CompilerServices;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore;
using Conveyor.Batch.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Conveyor.Batch.IntegrationTests;

/// <summary>
/// Regression coverage for a heartbeat-enabled <c>SimpleJobLauncher</c> running against a real
/// <see cref="EfCoreJobRepository"/> — the combination that originally surfaced a
/// <see cref="DbUpdateException"/> because the heartbeat background task and the job's own step
/// execution both called into the same non-thread-safe <c>DbContext</c> without synchronization.
/// Uses the public DI surface (<c>AddConveyorBatch</c> + <c>AddConveyorBatchEntityFrameworkCore</c>)
/// rather than constructing <c>SimpleJobLauncher</c> directly, since that's how this combination is
/// actually exercised in real applications.
/// </summary>
public sealed class HeartbeatEfCoreRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"conveyor_batch_heartbeat_{Guid.NewGuid()}.db");

    private sealed class SlowReader(IEnumerable<int> items, int delayMs) : IItemReader<int>
    {
        public async IAsyncEnumerable<int> ReadAsync(StepExecutionContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                yield return item;
            }
        }
    }

    private sealed class IdentityProcessor : IItemProcessor<int, int>
    {
        public ValueTask<int> ProcessAsync(int item, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.FromResult(item);
    }

    private sealed class NoOpWriter : IItemWriter<int>
    {
        public ValueTask WriteAsync(IReadOnlyList<int> items, StepExecutionContext ctx, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    [Fact]
    public async Task HeartbeatEnabled_WithEfCoreJobRepository_CompletesWithoutDbConcurrencyError()
    {
        var services = new ServiceCollection();
        services.AddConveyorBatch(options => options.HeartbeatInterval = TimeSpan.FromMilliseconds(15));
        services.AddConveyorBatchEntityFrameworkCore<object>(db => db.UseSqlite($"Data Source={_dbPath}"));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ConveyorBatchDbContext>();
        await dbContext.Database.MigrateAsync();

        var jobRepository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var launcher = scope.ServiceProvider.GetRequiredService<IJobLauncher>();

        // Two slow steps give the 15ms heartbeat many opportunities to tick concurrently with the
        // job's own step-execution calls against the same EfCoreJobRepository/DbContext.
        var step1 = new StepBuilder<int, int>(jobRepository)
            .Reader(new SlowReader([1, 2, 3, 4, 5], delayMs: 20))
            .Processor(new IdentityProcessor())
            .Writer(new NoOpWriter())
            .ChunkSize(1)
            .Build("step-one");

        var step2 = new StepBuilder<int, int>(jobRepository)
            .Reader(new SlowReader([1, 2, 3, 4, 5], delayMs: 20))
            .Processor(new IdentityProcessor())
            .Writer(new NoOpWriter())
            .ChunkSize(1)
            .Build("step-two");

        var job = new JobBuilder("heartbeat-efcore-job", jobRepository).AddStep(step1).AddStep(step2).Build();

        var execution = await launcher.RunAsync(job, JobParameters.Empty, CancellationToken.None);

        Assert.Equal(BatchStatus.Completed, execution.Status);
        Assert.NotNull(execution.LastHeartbeatAt);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
