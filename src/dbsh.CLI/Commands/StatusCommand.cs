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

        // The tracking table only knows about scripts that were attempted; scripts that are
        // still waiting on disk have to be added so "pending" is accurate.
        var recorded = status.Records.Select(r => r.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waiting = (await host.Executor.GetPendingAsync(host.EnvironmentName))
            .Where(i => string.Equals(i.Version, "R", StringComparison.OrdinalIgnoreCase) || !recorded.Contains(i.Version))
            .ToList();
        var pendingTotal = status.Pending + waiting.Count;

        if (settings.Json)
        {
            WriteJson(new
            {
                success = true,
                environment = status.Environment,
                total = status.Total + waiting.Count,
                applied = status.Applied,
                pending = pendingTotal,
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
                }).Concat(waiting.Select(i => new
                {
                    version = i.Version,
                    name = i.Name,
                    type = i.Type.ToString(),
                    status = "Pending",
                    executedAtUtc = default(DateTime),
                    executionTimeMs = 0L
                }))
            });
            return 0;
        }

        var summary = new List<(string Key, string Value, string Color)>
        {
            ("environment", status.Environment, Theme.Text),
            ("total", (status.Total + waiting.Count).ToString(), Theme.Text),
            ("applied", status.Applied.ToString(), status.Applied == 0 ? Theme.Muted : Theme.Success),
            ("pending", pendingTotal.ToString(), pendingTotal == 0 ? Theme.Muted : Theme.Warning),
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

        if (status.Records.Count == 0 && waiting.Count == 0)
        {
            ConsoleHelper.PrintInfo("No migration scripts found and none recorded for this environment.");
            return 0;
        }

        var drifted = status.DriftedMigrations.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = status.MissingMigrations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = status.Records.Select(r =>
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
            })
            .Concat(waiting.Select(i => (i.Version, i.Name, i.Type.ToString(), "Pending", "not applied yet")))
            .ToList();

        ConsoleHelper.PrintMigrationTable("Migrations", rows);

        if (status.Failed > 0)
        {
            ConsoleHelper.PrintNext("Fix the script, then re-queue the failed migration", "dbsh repair");
        }
        else if (status.DriftedMigrations.Count > 0 || status.MissingMigrations.Count > 0)
        {
            ConsoleHelper.PrintWarning("Applied scripts no longer match the files on disk. Review them before deploying.");
        }
        else if (pendingTotal > 0)
        {
            ConsoleHelper.PrintNext("Preview, then apply the pending migrations", "dbsh plan  /  dbsh migrate");
        }
        else
        {
            ConsoleHelper.PrintSuccess("Database is up to date.");
        }

        return 0;
    }
}
