using System.CommandLine;
using Conveyor.Batch.Tools.Commands;

var connectionOption = new Option<string>("--connection")
{
    Description = "Database connection string.",
    Required = true,
    Recursive = true
};

var providerOption = new Option<string>("--provider")
{
    Description = "Database provider: postgres | sqlserver | sqlite.",
    Recursive = true,
    DefaultValueFactory = _ => "sqlite"
};
providerOption.AcceptOnlyFromAmong("postgres", "sqlserver", "sqlite");

var rootCommand = new RootCommand("Conveyor.Batch job history CLI.");
rootCommand.Options.Add(connectionOption);
rootCommand.Options.Add(providerOption);

rootCommand.Subcommands.Add(JobsCommand.Create(connectionOption, providerOption));
rootCommand.Subcommands.Add(ExecutionsCommand.Create(connectionOption, providerOption));
rootCommand.Subcommands.Add(StepsCommand.Create(connectionOption, providerOption));
rootCommand.Subcommands.Add(RerunCommand.Create(connectionOption, providerOption));

return await rootCommand.Parse(args).InvokeAsync();
