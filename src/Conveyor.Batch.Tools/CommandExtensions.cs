using System.CommandLine;
using Spectre.Console;

namespace Conveyor.Batch.Tools;

/// <summary>
/// Shared wiring for command actions so every command uniformly turns an unexpected exception
/// into a printed error and exit code 1, instead of letting it crash out as a raw stack trace.
/// </summary>
internal static class CommandExtensions
{
    internal static void SetSafeAction(this Command command, Func<ParseResult, CancellationToken, Task<int>> action)
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                return await action(parseResult, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
                return 1;
            }
        });
    }
}
