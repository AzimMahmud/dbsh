using System.ComponentModel;
using dbsh.CLI.Helpers;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class HistoryCommand : CliCommandBase<HistoryCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("-l|--limit")]
        [Description("Maximum entries to display")]
        public int Limit { get; set; } = 25;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var host = CreateHost(settings);
        var live = RequireLive(settings, host);
        if (live != 0)
        {
            return live;
        }

        if (!settings.Json)
        {
            var title = host.Module is null
                ? $"Audit history for '{host.EnvironmentName}'"
                : $"Audit history for '{host.EnvironmentName}' (module: {host.Module})";
            ConsoleHelper.PrintHeader(title);
        }

        var entries = await ConsoleHelper.RunWithSpinner("Querying audit log", () => host.Executor.GetHistoryAsync(host.EnvironmentName, settings.Limit));

        if (settings.Json)
        {
            WriteJson(new
            {
                success = true,
                environment = host.EnvironmentName,
                count = entries.Count,
                entries = entries.Select(e => new
                {
                    action = e.Action.ToString(),
                    performedBy = e.PerformedBy,
                    performedAtUtc = e.PerformedAtUtc,
                    details = e.Details
                })
            });
            return 0;
        }

        if (entries.Count == 0)
        {
            ConsoleHelper.PrintInfo("No audit entries recorded for this environment.");
            return 0;
        }

        ConsoleHelper.PrintTable($"Audit entries (latest {entries.Count})",
            new[] { "When (UTC)", "Action", "User", "Details" },
            entries.Select(e => new[]
            {
                e.PerformedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                e.Action.ToString(),
                e.PerformedBy,
                e.Details ?? string.Empty
            }));

        return 0;
    }
}
