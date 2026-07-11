using System.ComponentModel.DataAnnotations.Schema;

namespace Conveyor.Batch.EntityFrameworkCore.Entities;

/// <summary>
/// EF Core entity representing a persisted <see cref="Conveyor.Batch.Core.Job.JobExecution"/>.
/// </summary>
[Table("batch_job_executions")]
public sealed class JobExecutionEntity
{
    /// <summary>Gets or sets the surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the foreign key to the parent job instance.</summary>
    public long JobInstanceId { get; set; }

    /// <summary>Gets or sets the parent job instance navigation property.</summary>
    public JobInstanceEntity JobInstance { get; set; } = null!;

    /// <summary>
    /// Gets or sets the serialized job parameters as a JSON string.
    /// </summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>Gets or sets the batch status stored as a string.</summary>
    public string Status { get; set; } = "Starting";

    /// <summary>Gets or sets the UTC start time of this execution.</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>Gets or sets the UTC end time of this execution, or <see langword="null"/> if still running.</summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Gets or sets the failure message if this execution failed, or <see langword="null"/>.</summary>
    public string? FailureMessage { get; set; }

    /// <summary>Gets or sets the UTC time this execution last reported a heartbeat, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    /// <summary>Gets or sets the collection of step executions for this job execution.</summary>
    public ICollection<StepExecutionEntity> StepExecutions { get; set; } = new List<StepExecutionEntity>();
}
