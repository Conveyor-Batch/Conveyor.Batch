using System.Text.Json;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Job;
using Conveyor.Batch.Core.Step;
using Conveyor.Batch.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IJobRepository"/> that persists
/// job and step execution state to a relational database.
/// </summary>
/// <remarks>
/// All members serialize their access to the underlying <see cref="ConveyorBatchDbContext"/>
/// via an internal semaphore, since a single <see cref="DbContext"/> instance is not safe for
/// concurrent use. This matters because callers can legitimately invoke this repository from more
/// than one logical path at once against the same instance — e.g. a job's own step execution
/// running concurrently with <c>SimpleJobLauncher</c>'s background heartbeat loop, or a
/// <c>ConcurrentChunkOrientedEngine</c>'s parallel workers checkpointing at the same time.
/// </remarks>
public sealed class EfCoreJobRepository : IJobRepository, IDisposable
{
    private readonly ConveyorBatchDbContext _dbContext;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreJobRepository"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context to use for persistence.</param>
    public EfCoreJobRepository(ConveyorBatchDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Releases the semaphore used to serialize access to the underlying <see cref="DbContext"/>.
    /// Does not dispose the <see cref="ConveyorBatchDbContext"/> itself — its lifetime is owned by
    /// whoever constructed it (typically the DI container, via its own registration).
    /// </summary>
    public void Dispose() => _semaphore.Dispose();

    /// <inheritdoc />
    public async Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entity = new JobInstanceEntity
            {
                JobName = jobName,
                ParametersJson = SerializeParameters(parameters)
            };

            _dbContext.JobInstances.Add(entity);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);

            return ToJobInstance(entity, parameters);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entity = new JobExecutionEntity
            {
                JobInstanceId = instance.Id,
                ParametersJson = SerializeParameters(parameters),
                Status = BatchStatus.Starting.ToString(),
                StartTime = DateTimeOffset.UtcNow
            };

            _dbContext.JobExecutions.Add(entity);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);

            return ToJobExecution(entity, instance, parameters);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateJobExecutionAsync(JobExecution execution)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entity = await _dbContext.JobExecutions
                .FindAsync(execution.Id)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"JobExecution with Id {execution.Id} was not found in the repository.");

            entity.Status = execution.Status.ToString();
            entity.EndTime = execution.EndTime;
            entity.FailureMessage = execution.FailureException?.Message;
            entity.LastHeartbeatAt = execution.LastHeartbeatAt;

            await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entity = new StepExecutionEntity
            {
                StepName = stepName,
                JobExecutionId = jobExecution.Id,
                Status = BatchStatus.Starting.ToString(),
                StartTime = DateTimeOffset.UtcNow
            };

            _dbContext.StepExecutions.Add(entity);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);

            return ToStepExecution(entity, jobExecution);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateStepExecutionAsync(StepExecution stepExecution)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entity = await _dbContext.StepExecutions
                .FindAsync(stepExecution.Id)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"StepExecution with Id {stepExecution.Id} was not found in the repository.");

            entity.Status = stepExecution.Status.ToString();
            entity.EndTime = stepExecution.EndTime;
            entity.ReadCount = stepExecution.ReadCount;
            entity.WriteCount = stepExecution.WriteCount;
            entity.SkipCount = stepExecution.SkipCount;
            entity.RollbackCount = stepExecution.RollbackCount;
            entity.FailureMessage = stepExecution.FailureException?.Message;
            entity.ExecutionContextJson = SerializeExecutionContext(stepExecution.ExecutionContext);

            await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var parametersJson = SerializeParameters(parameters);

            var executionEntity = await _dbContext.JobExecutions
                .Include(e => e.JobInstance)
                .Where(e => e.JobInstance.JobName == jobName
                         && e.JobInstance.ParametersJson == parametersJson)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (executionEntity is null)
                return null;

            var jobInstance = ToJobInstance(executionEntity.JobInstance, parameters);
            return ToJobExecution(executionEntity, jobInstance, parameters);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entities = await _dbContext.JobExecutions
                .Include(e => e.JobInstance)
                .Where(e => e.JobInstanceId == instance.Id)
                .OrderBy(e => e.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            return entities
                .Select(e => ToJobExecution(e, instance, DeserializeParameters(e.ParametersJson)))
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<JobExecution?> GetRunningJobExecutionAsync(
        string jobName,
        JobParameters parameters,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var parametersJson = SerializeParameters(parameters);

            var executionEntity = await _dbContext.JobExecutions
                .Include(e => e.JobInstance)
                .Where(e => e.JobInstance.JobName == jobName
                         && e.JobInstance.ParametersJson == parametersJson
                         && e.Status == nameof(BatchStatus.Started))
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (executionEntity is null)
                return null;

            var jobInstance = ToJobInstance(executionEntity.JobInstance, parameters);
            return ToJobExecution(executionEntity, jobInstance, parameters);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var entity = await _dbContext.StepExecutions
                .Include(e => e.JobExecution).ThenInclude(je => je.JobInstance)
                .Where(e => e.JobExecutionId == jobExecutionId && e.StepName == stepName)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (entity is null)
                return null;

            var parameters = DeserializeParameters(entity.JobExecution.ParametersJson);
            var jobInstance = ToJobInstance(entity.JobExecution.JobInstance, parameters);
            var jobExecution = ToJobExecution(entity.JobExecution, jobInstance, parameters);
            return ToStepExecution(entity, jobExecution);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static JobInstance ToJobInstance(JobInstanceEntity entity, JobParameters parameters) =>
        new()
        {
            Id = entity.Id,
            JobName = entity.JobName,
            Parameters = parameters
        };

    private static JobExecution ToJobExecution(
        JobExecutionEntity entity,
        JobInstance jobInstance,
        JobParameters parameters) =>
        new()
        {
            Id = entity.Id,
            JobInstance = jobInstance,
            Parameters = parameters,
            Status = Enum.Parse<BatchStatus>(entity.Status),
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            LastHeartbeatAt = entity.LastHeartbeatAt
        };

    private static StepExecution ToStepExecution(StepExecutionEntity entity, JobExecution jobExecution)
    {
        var stepExecution = new StepExecution
        {
            Id = entity.Id,
            StepName = entity.StepName,
            JobExecution = jobExecution,
            Status = Enum.Parse<BatchStatus>(entity.Status),
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            ExecutionContext = DeserializeExecutionContext(entity.ExecutionContextJson)
        };

        // ReadCount/WriteCount/SkipCount/RollbackCount are computed properties with no public
        // setter (they're normally only mutated atomically by the running engine), so they must
        // be restored explicitly here rather than via the object initializer above.
        stepExecution.RestoreCounters(entity.ReadCount, entity.WriteCount, entity.SkipCount, entity.RollbackCount);

        return stepExecution;
    }

    // -------------------------------------------------------------------------
    // Serialization helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes <paramref name="parameters"/> with keys in a canonical (ordinal-sorted) order.
    /// <see cref="JobParameters.Equals(JobParameters)"/> is order-independent, but lookups here
    /// (e.g. <see cref="GetLastJobExecutionAsync"/>) compare the persisted <c>ParametersJson</c>
    /// column via a raw string equality in the SQL query — without canonicalizing key order
    /// first, two parameter sets that are logically equal but were constructed with keys in a
    /// different order would serialize to different strings and silently fail to match.
    /// </summary>
    private static string SerializeParameters(JobParameters parameters)
    {
        var values = parameters.Values ?? JobParameters.Empty.Values;
        var ordered = values.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
        return JsonSerializer.Serialize(ordered, _jsonOptions);
    }

    private static JobParameters DeserializeParameters(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions)
                   ?? new Dictionary<string, string>();
        return new JobParameters(dict);
    }

    private static string SerializeExecutionContext(BatchExecutionContext context) =>
        JsonSerializer.Serialize(context.ToDictionary(), _jsonOptions);

    private static BatchExecutionContext DeserializeExecutionContext(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new BatchExecutionContext();

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions)
                   ?? new Dictionary<string, string>();
        return BatchExecutionContext.FromDictionary(dict);
    }
}
