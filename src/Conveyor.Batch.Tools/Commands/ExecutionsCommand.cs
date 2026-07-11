using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace Conveyor.Batch.Tools.Commands;

/// <summary>Builds the <c>executions &lt;jobName&gt;</c> subcommand.</summary>
internal static class ExecutionsCommand
{
    internal static Command Create(Option<string> connectionOption, Option<string> providerOption)
    {
        var jobNameArgument = new Argument<string>("jobName")
        {
            Description = "The name of the job to list executions for."
        };

        var command = new Command("executions", "Lists all executions for a named job, newest first.");
        command.Arguments.Add(jobNameArgument);

        command.SetSafeAction(async (parseResult, cancellationToken) =>
        {
            var connectionString = parseResult.GetValue(connectionOption)!;
            var provider = parseResult.GetValue(providerOption)!;
            var jobName = parseResult.GetValue(jobNameArgument)!;

            await using var handle = await DbContextFactory.TryCreateAsync(connectionString, provider, cancellationToken);
            if (handle is null)
                return 1;

            var rows = await handle.Context.JobExecutions
                .Where(e => e.JobInstance.JobName == jobName)
                .OrderByDescending(e => e.Id)
                .Select(e => new { e.Id, e.Status, e.StartTime, e.EndTime, e.LastHeartbeatAt })
                .ToListAsync(cancellationToken);

            var table = new Table();
            table.AddColumn("Id");
            table.AddColumn("Status");
            table.AddColumn("Start Time");
            table.AddColumn("End Time");
            table.AddColumn("Duration");
            table.AddColumn("Last Heartbeat");

            foreach (var row in rows)
            {
                table.AddRow(
                    row.Id.ToString(),
                    row.Status.EscapeMarkup(),
                    Formatting.FormatTimestamp(row.StartTime),
                    Formatting.FormatTimestamp(row.EndTime),
                    Formatting.FormatDuration(row.StartTime, row.EndTime),
                    Formatting.FormatTimestamp(row.LastHeartbeatAt));
            }

            AnsiConsole.Write(table);
            return 0;
        });

        return command;
    }
}
