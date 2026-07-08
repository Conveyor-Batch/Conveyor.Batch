using Conveyor.Batch.Abstractions;
using Conveyor.Batch.EntityFrameworkCore.Entities;

namespace Conveyor.Batch.EntityFrameworkCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IDeadLetterWriter"/> that persists dead-lettered
/// entries to the <c>batch_dead_letter_entries</c> table.
/// </summary>
/// <remarks>
/// A single <see cref="ConveyorBatchDbContext"/> instance is not safe for concurrent use, but
/// <see cref="Conveyor.Batch.Core.Engine.ConcurrentChunkOrientedEngine{TInput,TOutput}"/> can invoke
/// <c>OnSkipAsync</c> from multiple worker tasks at once (e.g. when a step is configured with a
/// <c>DegreeOfParallelism</c> greater than 1). Writes are therefore serialized with a
/// <see cref="SemaphoreSlim"/> so the shared context is never accessed by more than one caller
/// at a time.
/// </remarks>
public sealed class EfCoreDeadLetterWriter : IDeadLetterWriter
{
    private readonly ConveyorBatchDbContext _dbContext;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreDeadLetterWriter"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context to use for persistence.</param>
    public EfCoreDeadLetterWriter(ConveyorBatchDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(DeadLetterEntry entry, CancellationToken cancellationToken)
    {
        var entity = new DeadLetterEntryEntity
        {
            JobName = entry.JobName,
            StepName = entry.StepName,
            ItemJson = entry.ItemJson,
            ItemTypeName = entry.ItemTypeName,
            ExceptionType = entry.ExceptionType,
            ExceptionMessage = entry.ExceptionMessage,
            StackTrace = entry.StackTrace,
            SkipCountAtTime = entry.SkipCountAtTime,
            OccurredAt = entry.OccurredAt
        };

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _dbContext.DeadLetterEntries.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
