using dbsh.CLI.Commands;
using dbsh.CLI.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("dbsh");
    config.SetApplicationVersion(FileVersionInfo.GetVersionString());
    config.PropagateExceptions();

    config.AddCommand<NewCommand>("new")
        .WithDescription("Scaffold a complete dbsh project structure in the current directory.")
        .WithAlias("scaffold")
        .WithAlias("init-project")
        .WithExample("new", "--name", "Shop")
        .WithExample("new", "--name", "Shop", "--provider", "sqlite", "--output", "./shop");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Create the migration tracking schema on the target database.")
        .WithExample("init", "-e", "local")
        .WithExample("init", "--connection-string", "\"Host=localhost;Database=app\"");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate migration scripts (naming, syntax, duplicates, dependencies).")
        .WithExample("validate", "-e", "production")
        .WithExample("validate", "--module", "billing");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show the migration status for an environment.")
        .WithExample("status", "-e", "production")
        .WithExample("status", "--json");

    config.AddCommand<PlanCommand>("plan")
        .WithDescription("Compute and display the pending migration execution plan (dry-run).")
        .WithAlias("dry-run")
        .WithExample("plan", "-e", "production")
        .WithExample("plan", "--json");

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply pending migrations to the target environment.")
        .WithAlias("deploy")
        .WithAlias("apply")
        .WithExample("migrate", "-e", "local", "-y")
        .WithExample("migrate", "-e", "production", "--approver", "alice");

    config.AddCommand<RollbackCommand>("rollback")
        .WithDescription("Roll back one or more previously applied migrations.")
        .WithExample("rollback", "--target-version", "002")
        .WithExample("rollback", "--count", "2", "-y");

    config.AddCommand<RepairCommand>("repair")
        .WithDescription("Repair the migration history (re-queue a failed migration).")
        .WithExample("repair", "-e", "production")
        .WithExample("repair", "-e", "production", "--target-version", "003");

    config.AddCommand<HistoryCommand>("history")
        .WithDescription("Show the audit history for an environment.")
        .WithAlias("audit")
        .WithExample("history", "-e", "production", "--limit", "50");

    config.AddCommand<CreateCommand>("create")
        .WithDescription("Create a new migration script with the next auto-incrementing version.")
        .WithExample("create", "--name", "AddUsers")
        .WithExample("create", "--name", "SeedUsers", "--type", "data")
        .WithExample("create", "--type", "rollback")
        .WithExample("create", "--name", "RefreshView", "--type", "repeatable", "--timestamp");

    config.AddCommand<InfoCommand>("info")
        .WithDescription("Show configuration, environments and repository details.")
        .WithAlias("config")
        .WithExample("info")
        .WithExample("info", "-e", "production");
});

// Handle --no-color before Spectre.Console starts rendering.
var noColor = args.Contains("--no-color", StringComparer.OrdinalIgnoreCase);
if (noColor)
{
    try
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { ColorSystem = ColorSystemSupport.NoColors });
    }
    catch (Exception) { /* ignore - best effort */ }
}

// Suppress interactive UI when --json is active.
ConsoleHelper.UiSuppressed = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    ConsoleHelper.RenderException(ex);
    return 1;
}
