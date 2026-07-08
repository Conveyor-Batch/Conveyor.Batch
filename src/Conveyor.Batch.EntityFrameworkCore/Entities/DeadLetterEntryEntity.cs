using System.ComponentModel.DataAnnotations.Schema;

namespace Conveyor.Batch.EntityFrameworkCore.Entities;

/// <summary>
/// EF Core entity representing a persisted <see cref="Conveyor.Batch.Abstractions.DeadLetterEntry"/>.
/// </summary>
[Table("batch_dead_letter_entries")]
public sealed class DeadLetterEntryEntity
{
    /// <summary>Gets or sets the surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the name of the job the item was being processed by.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the step the item was being processed by.</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Gets or sets the original item, serialized as JSON.</summary>
    public string ItemJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the fully qualified type name of the original item.</summary>
    public string ItemTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the fully qualified type name of the exception that caused the skip.</summary>
    public string ExceptionType { get; set; } = string.Empty;

    /// <summary>Gets or sets the message of the exception that caused the skip.</summary>
    public string ExceptionMessage { get; set; } = string.Empty;

    /// <summary>Gets or sets the stack trace of the exception that caused the skip, if available.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Gets or sets the number of items already skipped in the step before this one.</summary>
    public long SkipCountAtTime { get; set; }

    /// <summary>Gets or sets the UTC time at which the item was skipped.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
