using System.ComponentModel.DataAnnotations.Schema;

namespace Conveyor.Batch.EntityFrameworkCore.Entities;

/// <summary>
/// EF Core entity backing <see cref="Conveyor.Batch.EntityFrameworkCore.EfCoreJobLockProvider"/>.
/// A row's presence represents an exclusive, time-bounded hold on a job identity
/// (job name + parameters); the row is deleted to release the lock.
/// </summary>
[Table("batch_job_locks")]
public sealed class JobLockEntity
{
    /// <summary>Gets or sets the surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the name of the locked job.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized job parameters as a JSON string, identifying the specific
    /// execution being locked.
    /// </summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>Gets or sets an opaque token identifying the lock holder.</summary>
    public string LockToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time at which the lock was acquired.</summary>
    public DateTimeOffset AcquiredAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time after which the lock is considered released even if the row
    /// has not been deleted, guarding against a lock holder crashing without releasing it.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
