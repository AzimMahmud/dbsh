using dbsh.CLI.Helpers;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class InfoCommand : CliCommandBase<InfoCommand.Settings>
{
    public sealed class Settings : GlobalSettings { }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var host = CreateHost(settings);

        if (settings.Json)
        {
            WriteJson(new
            {
                success = true,
                basePath = host.BasePath,
                scriptsPath = host.ScriptsPath,
                isLive = host.IsLive,
                module = host.Module,
                config = host.Config is null ? null : new
                {
                    host.Config.Provider,
                    host.Config.Version,
                    host.Config.TrackingTable,
                    host.Config.BatchSize,
                    host.Config.CommandTimeoutSeconds,
                    requireApproval = host.Config.RequireApprovalEnvironments
                },
                environments = host.ConfigLoader.GetAvailableEnvironments()
            });
            return Task.FromResult(0);
        }

        ConsoleHelper.PrintHeader("dbsh configuration");

        if (host.Config is null)
        {
            ConsoleHelper.PrintWarning("No migration.json found - running with defaults.");
        }
        else
        {
            ConsoleHelper.PrintSummary("Configuration", new[]
            {
                ("provider", host.Config.Provider, Theme.Text),
                ("config version", host.Config.Version, Theme.Text),
                ("tracking table", host.Config.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
                    ? host.Config.TrackingTable
                    : $"{host.Config.TrackingSchema}.{host.Config.TrackingTable}", Theme.Text),
                ("batch size", host.Config.BatchSize.ToString(), Theme.Text),
                ("command timeout", $"{host.Config.CommandTimeoutSeconds}s", Theme.Text),
                ("approval envs", host.Config.RequireApprovalEnvironments.Count == 0
                    ? "none"
                    : string.Join(", ", host.Config.RequireApprovalEnvironments), Theme.Warning)
            });
        }

        ConsoleHelper.PrintSummary("Runtime", new[]
        {
            ("base path", host.BasePath, Theme.Muted),
            ("scripts path", host.ScriptsPath, Theme.Muted),
            ("mode", host.IsLive ? $"live ({host.ProviderName})" : "offline - no connection string found", host.IsLive ? Theme.Success : Theme.Warning),
            ("environment", host.EnvironmentName, Theme.Text),
            ("module", host.Module ?? "(default)", host.Module is null ? Theme.Muted : Theme.Text)
        });

        var environments = host.ConfigLoader.GetAvailableEnvironments();
        ConsoleHelper.PrintList("Environments", environments.Count == 0 ? new[] { "(none found)" } : environments);

        if (!host.IsLive)
        {
            ConsoleHelper.PrintNext("Connect to a database with", "dbsh status -c \"<connection string>\"  (or set DB_CONNECTION_STRING)");
        }

        return Task.FromResult(0);
    }
}
