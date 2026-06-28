using System.ComponentModel.DataAnnotations.Schema;

namespace Conveyor.Batch.EntityFrameworkCore.Entities;

/// <summary>
/// EF Core entity representing a persisted <see cref="Conveyor.Batch.Core.Step.StepExecution"/>.
/// </summary>
[Table("batch_step_executions")]
public sealed class StepExecutionEntity
{
    /// <summary>Gets or sets the surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the name of the step.</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Gets or sets the foreign key to the parent job execution.</summary>
    public long JobExecutionId { get; set; }

    /// <summary>Gets or sets the parent job execution navigation property.</summary>
    public JobExecutionEntity JobExecution { get; set; } = null!;

    /// <summary>Gets or sets the batch status stored as a string.</summary>
    public string Status { get; set; } = "Starting";

    /// <summary>Gets or sets the UTC start time of this step execution.</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>Gets or sets the UTC end time of this step execution, or <see langword="null"/> if still running.</summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Gets or sets the number of items read during this step execution.</summary>
    public long ReadCount { get; set; }

    /// <summary>Gets or sets the number of items written during this step execution.</summary>
    public long WriteCount { get; set; }

    /// <summary>Gets or sets the number of items skipped during this step execution.</summary>
    public long SkipCount { get; set; }

    /// <summary>Gets or sets the number of rollbacks during this step execution.</summary>
    public long RollbackCount { get; set; }

    /// <summary>Gets or sets the failure message if this step failed, or <see langword="null"/>.</summary>
    public string? FailureMessage { get; set; }
}
