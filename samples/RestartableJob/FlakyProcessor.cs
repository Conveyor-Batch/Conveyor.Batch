using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace RestartableJob;

/// <summary>Thrown by <see cref="FlakyProcessor"/> to simulate a crash on the first run.</summary>
sealed class SimulatedException(string message) : Exception(message);

/// <summary>
/// Throws on row 51, but only on the very first attempt — guarded by
/// <see cref="StepExecution.IsRestart"/>, which the chunk-oriented step sets before any items are
/// read once it detects it's resuming a prior failed execution. This simulates a job that
/// crashes partway through, without needing a process-wide static flag: the guard is scoped to
/// this run's <see cref="StepExecutionContext"/>, which every <c>ProcessAsync</c> call already
/// receives.
/// </summary>
sealed class FlakyProcessor : IItemProcessor<InputRow, OutputRecord>
{
    private int _rowsSeenThisAttempt;

    public ValueTask<OutputRecord?> ProcessAsync(InputRow item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        _rowsSeenThisAttempt++;

        if (_rowsSeenThisAttempt == 51 && !context.StepExecution.IsRestart)
            throw new SimulatedException("Simulated failure on row 51 of the first run.");

        var record = new OutputRecord
        {
            Id = item.Id,
            Value = item.Value,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        return ValueTask.FromResult<OutputRecord?>(record);
    }
}
