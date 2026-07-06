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
public sealed class EfCoreJobRepository : IJobRepository
{
    private readonly ConveyorBatchDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreJobRepository"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context to use for persistence.</param>
    public EfCoreJobRepository(ConveyorBatchDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<JobInstance> CreateJobInstanceAsync(string jobName, JobParameters parameters)
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

    /// <inheritdoc />
    public async Task<JobExecution> CreateJobExecutionAsync(JobInstance instance, JobParameters parameters)
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

    /// <inheritdoc />
    public async Task UpdateJobExecutionAsync(JobExecution execution)
    {
        var entity = await _dbContext.JobExecutions
            .FindAsync(execution.Id)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"JobExecution with Id {execution.Id} was not found in the repository.");

        entity.Status = execution.Status.ToString();
        entity.EndTime = execution.EndTime;
        entity.FailureMessage = execution.FailureException?.Message;

        await _dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StepExecution> CreateStepExecutionAsync(JobExecution jobExecution, string stepName)
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

    /// <inheritdoc />
    public async Task UpdateStepExecutionAsync(StepExecution stepExecution)
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

    /// <inheritdoc />
    public async Task<JobExecution?> GetLastJobExecutionAsync(string jobName, JobParameters parameters)
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobExecution>> GetJobExecutionsAsync(JobInstance instance)
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

    /// <inheritdoc />
    public async Task<JobExecution?> GetRunningJobExecutionAsync(
        string jobName,
        JobParameters parameters,
        CancellationToken cancellationToken = default)
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

    /// <inheritdoc />
    public async Task<StepExecution?> GetLastStepExecutionAsync(long jobExecutionId, string stepName)
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
            EndTime = entity.EndTime
        };

    private static StepExecution ToStepExecution(StepExecutionEntity entity, JobExecution jobExecution) =>
        new()
        {
            Id = entity.Id,
            StepName = entity.StepName,
            JobExecution = jobExecution,
            Status = Enum.Parse<BatchStatus>(entity.Status),
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            ExecutionContext = DeserializeExecutionContext(entity.ExecutionContextJson)
        };

    // -------------------------------------------------------------------------
    // Serialization helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static string SerializeParameters(JobParameters parameters) =>
        JsonSerializer.Serialize(parameters.Values, _jsonOptions);

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
