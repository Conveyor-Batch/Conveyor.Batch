using System.ComponentModel.DataAnnotations.Schema;

namespace Conveyor.Batch.EntityFrameworkCore.Entities;

/// <summary>
/// EF Core entity representing a persisted <see cref="Conveyor.Batch.Core.Job.JobInstance"/>.
/// </summary>
[Table("batch_job_instances")]
public sealed class JobInstanceEntity
{
    /// <summary>Gets or sets the surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the name of the job.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized job parameters as a JSON string.
    /// </summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>Gets or sets the collection of job executions for this instance.</summary>
    public ICollection<JobExecutionEntity> JobExecutions { get; set; } = new List<JobExecutionEntity>();
}
