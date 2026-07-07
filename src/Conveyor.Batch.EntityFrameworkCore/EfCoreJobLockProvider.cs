using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IJobLockProvider"/> that coordinates exclusive job execution
/// across processes via a <c>batch_job_locks</c> table. A unique index on (job name, parameters)
/// makes acquisition atomic: only one process can successfully insert the lock row for a given
/// job identity at a time.
/// </summary>
public sealed class EfCoreJobLockProvider : IJobLockProvider
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConveyorBatchDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreJobLockProvider"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context to use for persistence.</param>
    public EfCoreJobLockProvider(ConveyorBatchDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<IJobLock> TryAcquireAsync(
        string jobName,
        JobParameters parameters,
        CancellationToken cancellationToken)
    {
        var parametersJson = JsonSerializer.Serialize(parameters.Values, JsonOptions);
        var now = DateTimeOffset.UtcNow;

        var expired = await _dbContext.JobLocks
            .Where(l => l.JobName == jobName && l.ParametersJson == parametersJson && l.ExpiresAt < now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (expired.Count > 0)
        {
            _dbContext.JobLocks.RemoveRange(expired);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var entity = new JobLockEntity
        {
            JobName = jobName,
            ParametersJson = parametersJson,
            LockToken = Guid.NewGuid().ToString("N"),
            AcquiredAt = now,
            ExpiresAt = now.Add(LockDuration)
        };

        _dbContext.JobLocks.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
            return NotAcquiredLock.Instance;
        }

        return new AcquiredLock(_dbContext, entity.Id);
    }

    private sealed class NotAcquiredLock : IJobLock
    {
        public static readonly NotAcquiredLock Instance = new();

        public bool IsAcquired => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AcquiredLock(ConveyorBatchDbContext dbContext, long lockId) : IJobLock
    {
        public bool IsAcquired => true;

        public async ValueTask DisposeAsync()
        {
            var entity = await dbContext.JobLocks.FindAsync(lockId).ConfigureAwait(false);
            if (entity is not null)
            {
                dbContext.JobLocks.Remove(entity);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}
