using dbsh.CLI.Helpers;
using dbsh.Core.Enums;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class StatusCommand : CliCommandBase<StatusCommand.Settings>
{
    public sealed class Settings : GlobalSettings { }

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
                ? $"Migration status for '{host.EnvironmentName}'"
                : $"Migration status for '{host.EnvironmentName}' (module: {host.Module})";
            ConsoleHelper.PrintHeader(title);
        }

        var status = await ConsoleHelper.RunWithSpinner("Querying migration history",
            () => host.Executor.GetStatusAsync(host.EnvironmentName));

        if (settings.Json)
        {
            WriteJson(new
            {
                success = true,
                environment = status.Environment,
                total = status.Total,
                applied = status.Applied,
                pending = status.Pending,
                failed = status.Failed,
                drifted = status.DriftedMigrations.Count,
                missing = status.MissingMigrations.Count,
                migrations = status.Records.Select(r => new
                {
                    version = r.Version,
                    name = r.Name,
                    type = r.Type.ToString(),
                    status = r.Status.ToString(),
                    executedAtUtc = r.ExecutedAtUtc,
                    executionTimeMs = r.ExecutionTimeMs
                })
            });
            return 0;
        }

        var summary = new List<(string Key, string Value, string Color)>
        {
            ("environment", status.Environment, Theme.Text),
            ("total", status.Total.ToString(), Theme.Text),
            ("applied", status.Applied.ToString(), Theme.Success),
            ("pending", status.Pending.ToString(), Theme.Warning),
            ("failed", status.Failed.ToString(), status.Failed == 0 ? Theme.Text : Theme.Danger)
        };
        if (status.DriftedMigrations.Count > 0)
        {
            summary.Add(("drifted", status.DriftedMigrations.Count.ToString(), Theme.Warning));
        }
        if (status.MissingMigrations.Count > 0)
        {
            summary.Add(("missing", status.MissingMigrations.Count.ToString(), Theme.Danger));
        }

        ConsoleHelper.PrintSummary("Summary", summary);

        if (status.Records.Count == 0)
        {
            ConsoleHelper.PrintInfo("No migrations have been recorded for this environment yet.");
            return 0;
        }

        var drifted = status.DriftedMigrations.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = status.MissingMigrations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        ConsoleHelper.PrintMigrationTable("Migrations",
            status.Records.Select(r =>
            {
                var state = missing.Contains(r.Version) ? "Missing"
                    : drifted.Contains(r.Version) ? "Drifted"
                    : r.Status == MigrationStatus.Completed ? "Applied"
                    : r.Status.ToString();
                var detail = state switch
                {
                    "Missing" => "script removed from disk",
                    "Drifted" => "edited after apply",
                    "Failed" => r.ErrorMessage ?? string.Empty,
                    _ => r.ExecutedAtUtc == default ? string.Empty : r.ExecutedAtUtc.ToString("u")
                };
                return (r.Version, r.Name, r.Type.ToString(), state, detail);
            }));

        return 0;
    }
}
