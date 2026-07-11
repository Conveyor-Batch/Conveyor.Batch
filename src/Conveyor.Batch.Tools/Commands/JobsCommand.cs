using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace Conveyor.Batch.Tools.Commands;

/// <summary>Builds the <c>jobs</c> subcommand.</summary>
internal static class JobsCommand
{
    internal static Command Create(Option<string> connectionOption, Option<string> providerOption)
    {
        var command = new Command("jobs", "Lists all job instances.");

        command.SetSafeAction(async (parseResult, cancellationToken) =>
        {
            var connectionString = parseResult.GetValue(connectionOption)!;
            var provider = parseResult.GetValue(providerOption)!;

            await using var handle = await DbContextFactory.TryCreateAsync(connectionString, provider, cancellationToken);
            if (handle is null)
                return 1;

            var rows = await handle.Context.JobInstances
                .OrderBy(j => j.Id)
                .Select(j => new
                {
                    j.Id,
                    j.JobName,
                    j.ParametersJson,
                    Last = j.JobExecutions
                        .OrderByDescending(e => e.Id)
                        .Select(e => new { e.Status, e.StartTime })
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var table = new Table();
            table.AddColumn("Id");
            table.AddColumn("Job Name");
            table.AddColumn("Last Status");
            table.AddColumn("Last Run");
            table.AddColumn("Parameters");

            foreach (var row in rows)
            {
                table.AddRow(
                    row.Id.ToString(),
                    row.JobName.EscapeMarkup(),
                    (row.Last?.Status ?? "-").EscapeMarkup(),
                    Formatting.FormatTimestamp(row.Last?.StartTime),
                    Formatting.FormatParameters(row.ParametersJson).EscapeMarkup());
            }

            AnsiConsole.Write(table);
            return 0;
        });

        return command;
    }
}
