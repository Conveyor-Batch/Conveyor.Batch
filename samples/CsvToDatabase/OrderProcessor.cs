using System.Globalization;
using Conveyor.Batch.Abstractions;
using Conveyor.Batch.Core.Step;

namespace CsvToDatabase;

/// <summary>
/// Validates and enriches a <see cref="RawOrder"/>. <see cref="decimal.Parse(string, IFormatProvider?)"/>
/// throws <see cref="FormatException"/> on a missing or malformed amount — that exception is what
/// the step's <c>ExceptionClassifierSkipPolicy</c> catches to skip the row instead of aborting the job.
/// </summary>
sealed class OrderProcessor : IItemProcessor<RawOrder, ProcessedOrder>
{
    private const decimal TaxRate = 0.08m;

    public ValueTask<ProcessedOrder?> ProcessAsync(RawOrder item, StepExecutionContext context, CancellationToken cancellationToken)
    {
        var amount = decimal.Parse(item.AmountText, CultureInfo.InvariantCulture);

        if (amount <= 0)
            return ValueTask.FromResult<ProcessedOrder?>(null);

        var processed = new ProcessedOrder
        {
            Id = item.Id,
            Product = item.Product,
            Amount = amount,
            Tax = Math.Round(amount * TaxRate, 2),
            ProcessedAt = DateTimeOffset.UtcNow
        };

        return ValueTask.FromResult<ProcessedOrder?>(processed);
    }
}
