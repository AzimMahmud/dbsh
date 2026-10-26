using System.ComponentModel;
using System.Diagnostics;
using dbsh.CLI.Helpers;
using dbsh.Core.ValueObjects;
using Spectre.Console;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class MigrateCommand : CliCommandBase<MigrateCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("-u|--executed-by")]
        [Description("User performing the deployment")]
        public string? ExecutedBy { get; set; }

        [CommandOption("-A|--approver")]
        [Description("Approver identity (required for approval-gated environments)")]
        public string? Approver { get; set; }

        [CommandOption("-b|--batch-size")]
        [Description("Override the migration batch size")]
        public int? BatchSize { get; set; }

        [CommandOption("-f|--force")]
        [Description("Proceed even outside the deployment window")]
        public bool Force { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var host = CreateHost(settings);
        var live = RequireLive(settings, host);
        if (live != 0)
        {
            return live;
        }

        if (!TryResolveEnvironment(settings, host, out var environment))
        {
            return Fail(settings, $"Environment '{host.EnvironmentName}' is not configured.");
        }

        if (!settings.Json)
        {
            var title = host.Module is null
                ? $"Deploying migrations to '{host.EnvironmentName}'"
                : $"Deploying migrations to '{host.EnvironmentName}' (module: {host.Module})";
            ConsoleHelper.PrintHeader(title);
        }

        if (!settings.Force && environment.DeploymentWindow is { Enabled: true } window)
        {
            if (!window.IsWithinWindow(DateTime.Now, out var reason))
            {
                return Fail(settings, $"Outside the configured deployment window. {reason} (override with --force)");
            }
        }

        var executedBy = settings.ExecutedBy ?? Environment.UserName;

        var planningContext = new MigrationContext { Environment = host.EnvironmentName, ExecutedBy = executedBy, Module = host.Module };
        var dryRun = await ConsoleHelper.RunWithSpinner("Computing pending migrations", () => host.Executor.DryRunAsync(planningContext));
        if (!dryRun.IsSuccess)
        {
            return Fail(settings, dryRun.ErrorMessage ?? "Failed to compute the execution plan.");
        }

        var plan = dryRun.ExecutionPlan!;
        if (plan.TotalCount == 0)
        {
            if (settings.Json)
            {
                WriteJson(new { success = true, applied = 0, message = "up to date" });
            }
            else
            {
                ConsoleHelper.PrintSuccess("The database is up to date - nothing to deploy.");
            }
            return 0;
        }

        if (!settings.Json)
        {
            ConsoleHelper.PrintMigrationTable("Pending migrations",
                plan.Items.Select(i => (i.Version, i.Name, i.Type.ToString(), "Pending", i.HasRollback ? "rollback available" : "no rollback")));
        }

        var approver = ResolveApprover(settings, environment);
        if (environment.Migration.RequireApproval && string.IsNullOrWhiteSpace(approver))
        {
            return Fail(settings, "This environment requires approval. Provide --approver or confirm interactively.");
        }

        if (!settings.Json && !settings.AssumeYes)
        {
            if (!ConsoleHelper.Confirm($"Apply {plan.TotalCount} migration(s) to '{host.EnvironmentName}'?", false))
            {
                ConsoleHelper.PrintWarning("Deployment cancelled.");
                return 1;
            }
        }

        var deployContext = new MigrationContext
        {
            Environment = host.EnvironmentName,
            ExecutedBy = executedBy,
            Approver = approver,
            BatchSize = settings.BatchSize,
            StopOnFailure = true,
            Force = settings.Force,
            SkipApproval = settings.AssumeYes,
            Module = host.Module
        };

        var stopwatch = Stopwatch.StartNew();
        var progress = new MigrationProgress();
        deployContext.Progress = progress;
        var result = await ConsoleHelper.RunWithProgress(
            $"Applying {plan.TotalCount} migration(s)",
            progress,
            () => host.Executor.DeployAsync(deployContext));
        stopwatch.Stop();

        if (settings.Json)
        {
            WriteJson(new
            {
                success = result.IsSuccess,
                environment = host.EnvironmentName,
                applied = result.TotalApplied,
                appliedMigrations = result.AppliedMigrations,
                failedMigrations = result.FailedMigrations,
                outcomes = result.Outcomes.Select(o => new
                {
                    version = o.Version,
                    name = o.Name,
                    elapsedMs = o.ElapsedMs,
                    success = o.Success,
                    error = o.ErrorMessage
                }),
                elapsedMs = stopwatch.ElapsedMilliseconds,
                error = result.ErrorMessage
            });
            return result.IsSuccess ? 0 : 1;
        }

        ConsoleHelper.PrintSummary("Deployment result", new[]
        {
            ("environment", host.EnvironmentName, Theme.Text),
            ("applied", result.TotalApplied.ToString(), Theme.Success),
            ("failed", result.FailedMigrations.Count.ToString(), result.FailedMigrations.Count == 0 ? Theme.Text : Theme.Danger),
            ("elapsed", $"{stopwatch.Elapsed.TotalSeconds:0.00}s", Theme.Muted),
            ("approver", approver ?? "n/a", Theme.Muted)
        });

        var appliedOutcomes = result.Outcomes.Where(o => o.Success).ToList();
        var failedOutcomes = result.Outcomes.Where(o => !o.Success).ToList();
        if (appliedOutcomes.Count > 0)
        {
            ConsoleHelper.PrintList("Applied", appliedOutcomes.Select(ConsoleHelper.FormatOutcome));
        }
        if (failedOutcomes.Count > 0)
        {
            ConsoleHelper.PrintList("Failed", failedOutcomes.Select(o => $"{o.Version} {o.Name}"));
        }

        if (result.IsSuccess)
        {
            ConsoleHelper.PrintSuccess($"Successfully applied {result.TotalApplied} migration(s).");
            return 0;
        }

        ConsoleHelper.PrintError(result.ErrorMessage ?? "Deployment completed with errors.");
        return 1;
    }

    private static string? ResolveApprover(Settings settings, EnvironmentConfiguration environment)
    {
        if (!environment.Migration.RequireApproval)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.Approver))
        {
            return settings.Approver;
        }

        if (settings.Json || settings.AssumeYes)
        {
            return null;
        }

        return AnsiConsole.Ask<string>($"[bold {Theme.Primary}]Approver identity:[/] ");
    }
}
