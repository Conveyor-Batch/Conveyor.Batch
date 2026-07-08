using Conveyor.Batch.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="DbContext"/> that owns all Conveyor.Batch persistence tables.
/// Tables are prefixed with <c>batch_</c> to avoid collisions in shared schemas.
/// </summary>
public class ConveyorBatchDbContext : DbContext
{
    /// <summary>Initializes a new instance with the given options.</summary>
    /// <param name="options">The context options, typically provided by DI.</param>
    public ConveyorBatchDbContext(DbContextOptions<ConveyorBatchDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the set of job instance records.</summary>
    public DbSet<JobInstanceEntity> JobInstances => Set<JobInstanceEntity>();

    /// <summary>Gets the set of job execution records.</summary>
    public DbSet<JobExecutionEntity> JobExecutions => Set<JobExecutionEntity>();

    /// <summary>Gets the set of step execution records.</summary>
    public DbSet<StepExecutionEntity> StepExecutions => Set<StepExecutionEntity>();

    /// <summary>Gets the set of job lock records used by <see cref="EfCoreJobLockProvider"/>.</summary>
    public DbSet<JobLockEntity> JobLocks => Set<JobLockEntity>();

    /// <summary>Gets the set of dead-lettered entries for skipped items.</summary>
    public DbSet<DeadLetterEntryEntity> DeadLetterEntries => Set<DeadLetterEntryEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<JobInstanceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.JobName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ParametersJson).IsRequired();
            entity.HasIndex(e => e.JobName);
        });

        modelBuilder.Entity<JobExecutionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ParametersJson).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.StartTime).IsRequired();
            entity.HasOne(e => e.JobInstance)
                  .WithMany(i => i.JobExecutions)
                  .HasForeignKey(e => e.JobInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.JobInstanceId);
        });

        modelBuilder.Entity<StepExecutionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.StepName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.StartTime).IsRequired();
            entity.HasOne(e => e.JobExecution)
                  .WithMany(j => j.StepExecutions)
                  .HasForeignKey(e => e.JobExecutionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.JobExecutionId);
        });

        modelBuilder.Entity<JobLockEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.JobName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ParametersJson).IsRequired();
            entity.Property(e => e.LockToken).IsRequired().HasMaxLength(64);
            entity.Property(e => e.AcquiredAt).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.HasIndex(e => new { e.JobName, e.ParametersJson }).IsUnique();
        });

        modelBuilder.Entity<DeadLetterEntryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.JobName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StepName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ItemJson).IsRequired();
            entity.Property(e => e.ItemTypeName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ExceptionType).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ExceptionMessage).IsRequired();
            entity.Property(e => e.OccurredAt).IsRequired();
            entity.HasIndex(e => new { e.JobName, e.StepName });
        });
    }
}
