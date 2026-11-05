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

    // Typical workflow, shown at the top of 'dbsh --help'.
    config.AddExample("new", "--name", "Shop", "--provider", "sqlite");
    config.AddExample("init", "-c", "\"Data Source=shop.db\"");
    config.AddExample("create", "--name", "AddUsers");
    config.AddExample("plan", "-c", "\"Data Source=shop.db\"");
    config.AddExample("migrate", "-c", "\"Data Source=shop.db\"", "-y");
    config.AddExample("status", "-c", "\"Data Source=shop.db\"");

    config.AddCommand<NewCommand>("new")
        .WithDescription("1. Scaffold a new dbsh project (config, folders, templates, CI).")
        .WithAlias("scaffold")
        .WithAlias("init-project")
        .WithExample("new", "--name", "Shop")
        .WithExample("new", "--name", "Shop", "--provider", "sqlite", "--output", "./shop");

    config.AddCommand<InitCommand>("init")
        .WithDescription("2. Create the tracking tables on the target database (one-time).")
        .WithExample("init", "-e", "local")
        .WithExample("init", "--connection-string", "\"Host=localhost;Database=app\"");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("4. Check scripts for naming, syntax and duplicate problems.")
        .WithExample("validate", "-e", "production")
        .WithExample("validate", "--module", "billing");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show applied, pending, failed and drifted migrations.")
        .WithExample("status", "-e", "production")
        .WithExample("status", "--json");

    config.AddCommand<PlanCommand>("plan")
        .WithDescription("5. Preview the pending migrations without changing anything.")
        .WithAlias("dry-run")
        .WithExample("plan", "-e", "production")
        .WithExample("plan", "--json");

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("6. Apply pending migrations to the database.")
        .WithAlias("deploy")
        .WithAlias("apply")
        .WithExample("migrate", "-e", "local", "-y")
        .WithExample("migrate", "-e", "production", "--approver", "alice");

    config.AddCommand<RollbackCommand>("rollback")
        .WithDescription("Undo the last applied migration(s) using their rollback scripts.")
        .WithExample("rollback", "--target-version", "002")
        .WithExample("rollback", "--count", "2", "-y");

    config.AddCommand<RepairCommand>("repair")
        .WithDescription("Re-queue failed migrations after you fix their scripts.")
        .WithExample("repair", "-e", "production")
        .WithExample("repair", "-e", "production", "--target-version", "003");

    config.AddCommand<HistoryCommand>("history")
        .WithDescription("Show who ran what and when (audit log).")
        .WithAlias("audit")
        .WithExample("history", "-e", "production", "--limit", "50");

    config.AddCommand<CreateCommand>("create")
        .WithDescription("3. Create a new migration script with the next version number.")
        .WithExample("create", "--name", "AddUsers")
        .WithExample("create", "--name", "SeedUsers", "--type", "data")
        .WithExample("create", "--type", "rollback")
        .WithExample("create", "--name", "RefreshView", "--type", "repeatable", "--timestamp");

    config.AddCommand<InfoCommand>("info")
        .WithDescription("Show the resolved configuration, paths and connection mode.")
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
