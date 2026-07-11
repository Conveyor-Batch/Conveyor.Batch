using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace Conveyor.Batch.Tools.Commands;

/// <summary>Builds the <c>steps &lt;executionId&gt;</c> subcommand.</summary>
internal static class StepsCommand
{
    internal static Command Create(Option<string> connectionOption, Option<string> providerOption)
    {
        var executionIdArgument = new Argument<long>("executionId")
        {
            Description = "The id of the job execution to list step executions for."
        };

        var command = new Command("steps", "Lists all step executions for a job execution.");
        command.Arguments.Add(executionIdArgument);

        command.SetSafeAction(async (parseResult, cancellationToken) =>
        {
            var connectionString = parseResult.GetValue(connectionOption)!;
            var provider = parseResult.GetValue(providerOption)!;
            var executionId = parseResult.GetValue(executionIdArgument);

            await using var handle = await DbContextFactory.TryCreateAsync(connectionString, provider, cancellationToken);
            if (handle is null)
                return 1;

            var rows = await handle.Context.StepExecutions
                .Where(s => s.JobExecutionId == executionId)
                .OrderBy(s => s.Id)
                .Select(s => new
                {
                    s.Id,
                    s.StepName,
                    s.Status,
                    s.ReadCount,
                    s.WriteCount,
                    s.SkipCount,
                    s.StartTime,
                    s.EndTime
                })
                .ToListAsync(cancellationToken);

            var table = new Table();
            table.AddColumn("Id");
            table.AddColumn("Step Name");
            table.AddColumn("Status");
            table.AddColumn("Read");
            table.AddColumn("Written");
            table.AddColumn("Skipped");
            table.AddColumn("Start");
            table.AddColumn("End");

            foreach (var row in rows)
            {
                table.AddRow(
                    row.Id.ToString(),
                    row.StepName.EscapeMarkup(),
                    row.Status.EscapeMarkup(),
                    row.ReadCount.ToString(),
                    row.WriteCount.ToString(),
                    row.SkipCount.ToString(),
                    Formatting.FormatTimestamp(row.StartTime),
                    Formatting.FormatTimestamp(row.EndTime));
            }

            AnsiConsole.Write(table);
            return 0;
        });

        return command;
    }
}
