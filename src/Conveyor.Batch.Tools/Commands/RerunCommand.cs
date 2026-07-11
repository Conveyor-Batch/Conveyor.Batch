using System.CommandLine;
using Conveyor.Batch.Core.Job;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace Conveyor.Batch.Tools.Commands;

/// <summary>Builds the <c>rerun &lt;executionId&gt;</c> subcommand.</summary>
internal static class RerunCommand
{
    internal static Command Create(Option<string> connectionOption, Option<string> providerOption)
    {
        var executionIdArgument = new Argument<long>("executionId")
        {
            Description = "The id of the FAILED job execution to mark ABANDONED."
        };

        var command = new Command(
            "rerun",
            "Marks a FAILED execution as ABANDONED so the next launch with the same parameters creates a fresh execution.");
        command.Arguments.Add(executionIdArgument);

        command.SetSafeAction(async (parseResult, cancellationToken) =>
        {
            var connectionString = parseResult.GetValue(connectionOption)!;
            var provider = parseResult.GetValue(providerOption)!;
            var executionId = parseResult.GetValue(executionIdArgument);

            await using var handle = await DbContextFactory.TryCreateAsync(connectionString, provider, cancellationToken);
            if (handle is null)
                return 1;

            var execution = await handle.Context.JobExecutions
                .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken);

            if (execution is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] Execution {executionId} was not found.");
                return 1;
            }

            if (execution.Status != nameof(BatchStatus.Failed))
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Error:[/] Execution {executionId} is not FAILED (current status: {execution.Status}). Only FAILED executions can be rerun.");
                return 1;
            }

            if (!AnsiConsole.Confirm($"Mark execution {executionId} as ABANDONED?"))
                return 0;

            // The confirmation prompt above can block on human input for an arbitrary amount of
            // time, during which the execution's status could change (e.g. a concurrent
            // heartbeat or restart). Re-check the FAILED condition atomically as part of the
            // write itself, rather than trusting the read from before the prompt, so a
            // concurrently-changed execution isn't silently clobbered.
            var updatedCount = await handle.Context.JobExecutions
                .Where(e => e.Id == executionId && e.Status == nameof(BatchStatus.Failed))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(e => e.Status, nameof(BatchStatus.Abandoned)),
                    cancellationToken);

            if (updatedCount == 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]Error:[/] Execution {executionId} is no longer FAILED (its status changed since this command started). No changes made.");
                return 1;
            }

            AnsiConsole.MarkupLineInterpolated(
                $"Execution {executionId} marked ABANDONED. Re-launch the job with the same parameters to start fresh.");
            return 0;
        });

        return command;
    }
}
