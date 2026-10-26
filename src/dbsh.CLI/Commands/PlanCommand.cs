using System.ComponentModel;
using dbsh.CLI.Helpers;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class PlanCommand : CliCommandBase<PlanCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("-u|--executed-by")]
        [Description("User performing the run")]
        public string? ExecutedBy { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var host = CreateHost(settings);
        if (!settings.Json)
        {
            var title = host.Module is null
                ? $"Execution plan for '{host.EnvironmentName}'"
                : $"Execution plan for '{host.EnvironmentName}' (module: {host.Module})";
            ConsoleHelper.PrintHeader(title);
        }

        var contextObj = new Core.ValueObjects.MigrationContext
        {
            Environment = host.EnvironmentName,
            ExecutedBy = settings.ExecutedBy ?? Environment.UserName
        };

        var dryRun = await ConsoleHelper.RunWithSpinner("Computing pending migrations",
            () => host.Executor.DryRunAsync(contextObj));

        if (!dryRun.IsSuccess)
        {
            return Fail(settings, dryRun.ErrorMessage ?? "Failed to compute execution plan.");
        }

        var plan = dryRun.ExecutionPlan!;

        if (settings.Json)
        {
            WriteJson(new
            {
                success = true,
                environment = plan.Environment,
                count = plan.TotalCount,
                drifted = dryRun.DriftedMigrations,
                missing = dryRun.MissingMigrations,
                migrations = plan.Items.Select(i => new
                {
                    version = i.Version,
                    name = i.Name,
                    type = i.Type.ToString(),
                    category = i.Category,
                    hash = i.Hash,
                    hasRollback = i.HasRollback
                })
            });
            return 0;
        }

        if (dryRun.MissingMigrations.Count > 0)
        {
            ConsoleHelper.PrintWarning(
                $"{dryRun.MissingMigrations.Count} recorded migration(s) are missing their script file: " +
                $"{string.Join(", ", dryRun.MissingMigrations)}. Restore the file(s) or run 'dbsh repair'.");
        }

        if (dryRun.DriftedMigrations.Count > 0)
        {
            ConsoleHelper.PrintWarning(
                $"{dryRun.DriftedMigrations.Count} applied migration(s) have checksum drift: " +
                $"{string.Join(", ", dryRun.DriftedMigrations)}. Run 'dbsh repair' or deploy with --force.");
        }

        if (plan.TotalCount == 0)
        {
            ConsoleHelper.PrintSuccess("The database is up to date - no pending migrations.");
            return 0;
        }

        ConsoleHelper.PrintInfo($"{plan.TotalCount} pending migration(s) will be applied in order:");
        ConsoleHelper.PrintMigrationTable("Pending migrations",
            plan.Items.Select(i => (i.Version, i.Name, i.Type.ToString(), "Pending", i.HasRollback ? "rollback available" : "no rollback")));

        return 0;
    }
}
