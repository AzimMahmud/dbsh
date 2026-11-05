using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using dbsh.CLI.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class NewCommand : CliCommandBase<NewCommand.Settings>
{
    private static readonly JsonSerializerOptions JsonPretty = new() { WriteIndented = true };

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("-n|--name")]
        [Description("Project name (used in example migrations and CI)")]
        public string? Name { get; set; }

        [CommandOption("-o|--output")]
        [Description("Output directory (default: current directory)")]
        public string? Output { get; set; }

        [CommandOption("-f|--force")]
        [Description("Overwrite existing files")]
        public bool Force { get; set; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var isInteractive = !settings.Json && string.IsNullOrWhiteSpace(settings.Name) && string.IsNullOrWhiteSpace(settings.Output) && string.IsNullOrWhiteSpace(settings.Provider);

        string projectName;
        string provider;
        string outputDir;
        bool force;
        string modules;

        if (isInteractive)
        {
            ConsoleHelper.PrintBanner();

            var welcome = new Panel(
                new Markup($"[white]This wizard will scaffold a complete [bold {Theme.Primary}]dbsh[/] project structure[/]{Environment.NewLine}" +
                           $"[grey]including config files, migration directories, templates, and CI pipelines.[/]"))
                .Border(BoxBorder.Rounded)
                .BorderStyle(Style.Parse(Theme.Muted))
                .Header($" [{Theme.Primary}] Project setup [/]", Justify.Left)
                .Padding(1, 0);
            AnsiConsole.Write(welcome);
            AnsiConsole.WriteLine();

            projectName = AnsiConsole.Ask<string>($"  [bold {Theme.Primary}]Project name[/] [grey](MyApp)[/]:", "MyApp");
            provider = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"  [bold {Theme.Primary}]Database provider[/]:")
                    .PageSize(5)
                    .HighlightStyle(Style.Parse(Theme.Primary))
                    .AddChoices("postgresql", "sqlserver", "mysql", "sqlite"));

            modules = AnsiConsole.Ask<string>(
                $"  [bold {Theme.Primary}]Module names[/] [grey](comma-separated, for modular monolith; empty for single-app)[/]:", "");

            var useCurrentDir = AnsiConsole.Confirm($"  [bold {Theme.Primary}]Use current directory?[/]", true);
            outputDir = useCurrentDir
                ? Directory.GetCurrentDirectory()
                : AnsiConsole.Ask<string>($"  [bold {Theme.Primary}]Output directory[/]:");

            if (Directory.Exists(Path.Combine(outputDir, "Database")))
            {
                force = AnsiConsole.Confirm(
                    $"  [bold {Theme.Warning}]Overwrite existing files?[/] [grey](a Database directory already exists)[/]", false);
            }
            else
            {
                force = false;
            }

            AnsiConsole.WriteLine();
        }
        else
        {
            projectName = settings.Name ?? "MyApp";
            provider = settings.Provider ?? "postgresql";
            outputDir = settings.Output ?? Directory.GetCurrentDirectory();
            force = settings.Force;
            modules = string.Empty;
        }

        outputDir = Path.GetFullPath(outputDir);

        Directory.CreateDirectory(outputDir);

        var created = new List<string>();
        var skipped = new List<string>();

        void WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(outputDir, relativePath);
            var dir = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(dir);

            if (File.Exists(fullPath) && !force)
            {
                skipped.Add(relativePath);
                return;
            }

            File.WriteAllText(fullPath, content);
            created.Add(relativePath);
        }

        // ── Database/Config/migration.json ──────────────────────────────
        var config = new Dictionary<string, object>
        {
            ["migration"] = new Dictionary<string, object>
            {
                ["version"] = "1.0.0",
                ["database"] = new Dictionary<string, object>
                {
                    ["provider"] = provider,
                    ["connectionString"] = "${DB_CONNECTION_STRING}"
                },
                ["scripts"] = new Dictionary<string, object>
                {
                    ["path"] = "./Database/Migrations"
                },
                ["tracking"] = new Dictionary<string, object>
                {
                    ["schema"] = "public",
                    ["tableName"] = "__migration_history"
                },
                ["execution"] = new Dictionary<string, object>
                {
                    ["lockTimeoutSeconds"] = 300,
                    ["commandTimeoutSeconds"] = 3600,
                    ["batchSize"] = 10,
                    ["stopOnFailure"] = true
                }
            }
        };
        WriteFile("Database/Config/migration.json",
            JsonSerializer.Serialize(config, JsonPretty));

        // ── Database/Config/environments/*.json ─────────────────────────
        var environments = new[]
        {
            ("local", "DB_CONNECTION_STRING", false, 30, 10),
            ("development", "DB_CONNECTION_STRING", false, 60, 10),
            ("staging", "DB_CONNECTION_STRING", false, 120, 5),
            ("production", "PROD_DB_CONNECTION_STRING", true, 300, 5),
        };

        foreach (var (envName, csVar, requireApproval, lockTimeout, maxBatch) in environments)
        {
            var env = new Dictionary<string, object>
            {
                ["name"] = envName,
                ["database"] = new Dictionary<string, object>
                {
                    ["connectionString"] = $"${{{csVar}}}"
                },
                ["migration"] = new Dictionary<string, object>
                {
                    ["requireApproval"] = requireApproval,
                    ["lockTimeoutSeconds"] = lockTimeout,
                    ["maxBatchSize"] = maxBatch
                }
            };

            if (envName == "production")
            {
                env["deploymentWindow"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["startTime"] = "02:00",
                    ["endTime"] = "06:00",
                    ["allowedDays"] = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }
                };
            }

            WriteFile($"Database/Config/environments/{envName}.json",
                JsonSerializer.Serialize(env, JsonPretty));
        }

        // ── Migration directories and example migrations ────────────────
        var moduleList = string.IsNullOrWhiteSpace(modules)
            ? new List<string> { "" }
            : modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        foreach (var module in moduleList)
        {
            if (!string.IsNullOrEmpty(module) && !IsValidModuleName(module))
            {
                throw new InvalidOperationException(
                    $"Invalid module name '{module}'. Module names must contain only alphanumeric characters " +
                    "and underscores, and must start with a letter or underscore.");
            }

            var prefix = string.IsNullOrEmpty(module) ? "" : $"{module}/";
            var safePath = Path.Combine("Database/Migrations", module ?? string.Empty, "Schema/.gitkeep");
            if (!IsPathSafe(outputDir, safePath))
            {
                throw new InvalidOperationException($"Invalid module path: {module}");
            }

            WriteFile($"Database/Migrations/{prefix}Schema/.gitkeep", "");
            WriteFile($"Database/Migrations/{prefix}Data/.gitkeep", "");
            WriteFile($"Database/Migrations/{prefix}Patch/.gitkeep", "");
            WriteFile($"Database/Migrations/{prefix}Rollback/.gitkeep", "");

            var moduleName = string.IsNullOrEmpty(module) ? projectName : module;
            WriteFile($"Database/Migrations/{prefix}Schema/V001__Init_{moduleName}.sql", $$"""
                -- Migration: Init_{{moduleName}}
                -- Author: {{projectName}}
                -- Created: {{DateTime.UtcNow:yyyy-MM-dd}}
                -- Description: Initial schema for {{moduleName}}

                CREATE TABLE IF NOT EXISTS example_{{(module ?? "app").ToLowerInvariant()}} (
                    id              {{IdType(provider)}} PRIMARY KEY {{DefaultId(provider)}},
                    name            VARCHAR(255) NOT NULL,
                    is_active       {{BoolType(provider)}} NOT NULL DEFAULT {{BoolTrue(provider)}},
                    created_at      {{TimestampType(provider)}} NOT NULL {{DefaultNow(provider)}},
                    updated_at      {{TimestampType(provider)}} NOT NULL {{DefaultNow(provider)}}
                );

                CREATE {{UniqueIndex(provider)}} idx_example_name ON example_{{(module ?? "app").ToLowerInvariant()}} (name);
                """.Replace("\r\n", "\n"));

            WriteFile($"Database/Migrations/{prefix}Rollback/U001__Init_{moduleName}.sql", $$"""
                -- Rollback: Init_{{moduleName}}
                -- Author: {{projectName}}
                -- Created: {{DateTime.UtcNow:yyyy-MM-dd}}
                -- Description: Drops the {{moduleName}} example table

                DROP TABLE IF EXISTS example_{{(module ?? "app").ToLowerInvariant()}};
                """.Replace("\r\n", "\n"));
        }

        // ── Templates (plain """ with no interpolation — {{NAME}} etc. are literal) ──
        WriteFile("Database/Templates/schema_migration.sql", """
            -- Migration: {{NAME}}
            -- Author: {{AUTHOR}}
            -- Created: {{DATE}}
            -- Description: {{DESCRIPTION}}

            -- TODO: add your DDL here
            """);

        WriteFile("Database/Templates/data_migration.sql", """
            -- Migration: {{NAME}}
            -- Author: {{AUTHOR}}
            -- Created: {{DATE}}
            -- Description: {{DESCRIPTION}}

            -- TODO: add your DML here
            """);

        WriteFile("Database/Templates/patch_migration.sql", """
            -- Migration: {{NAME}}
            -- Author: {{AUTHOR}}
            -- Created: {{DATE}}
            -- Description: {{DESCRIPTION}}

            -- TODO: add your DDL/DML here
            """);

        WriteFile("Database/Templates/rollback_migration.sql", """
            -- Rollback: {{NAME}}
            -- Author: {{AUTHOR}}
            -- Created: {{DATE}}
            -- Description: {{DESCRIPTION}}

            -- TODO: add your rollback SQL here
            """);

        WriteFile("Database/Templates/repeatable_migration.sql", """
            -- Migration: {{NAME}}
            -- Author: {{AUTHOR}}
            -- Created: {{DATE}}
            -- Description: {{DESCRIPTION}}

            -- TODO: add your repeatable SQL here (re-applied when checksum changes)
            """);

        // ── .gitignore ──────────────────────────────────────────────────
        WriteFile(".gitignore", $$"""
            # dbsh project scaffold
            # Ignore OS / editor files

            # Windows
            Thumbs.db
            Desktop.ini

            # macOS
            .DS_Store

            # Editor
            *.swp
            *.swo
            .vscode/
            .idea/
            *.suo
            *.user
            .vs/

            # Build
            bin/
            obj/
            *.nupkg
            """.Replace("\r\n", "\n"));

        // ── .github/workflows/database-migration.yml ───────────────────
        // Plain """ (no interpolation) so ${{ }} is passed through verbatim.
        WriteFile(".github/workflows/database-migration.yml", """
            name: database-migration

            on:
              push:
                branches: [main, develop]
                paths:
                  - 'Database/Migrations/**'
              pull_request:
                paths:
                  - 'Database/Migrations/**'
              workflow_dispatch:

            jobs:
              validate:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: '10.0.x'
                  - run: dotnet tool restore
                  - run: dbsh validate --json

              deploy-dev:
                needs: validate
                if: github.ref == 'refs/heads/develop'
                environment: development
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: '10.0.x'
                  - run: dotnet tool restore
                  - run: dbsh migrate --environment development --yes
                    env:
                      DB_CONNECTION_STRING: ${{ secrets.DB_CONNECTION_STRING }}

              deploy-prod:
                needs: validate
                if: github.ref == 'refs/heads/main'
                environment: production
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: '10.0.x'
                  - run: dotnet tool restore
                  - run: dbsh migrate --environment production --approver deploy-bot --yes
                    env:
                      DB_CONNECTION_STRING: ${{ secrets.PROD_DB_CONNECTION_STRING }}
            """.Replace("\r\n", "\n"));

        // .NET local tool manifest so `dotnet tool restore` resolves the `dbsh` command
        // referenced by the generated workflow above. Pairs with the published NuGet package.
        WriteFile(".config/dotnet-tools.json", $$"""
            {
              "version": 1,
              "isRoot": true,
              "tools": {
                "dbsh": {
                  "version": "1.0.0",
                  "commands": [
                    "dbsh"
                  ]
                }
              }
            }
            """.Replace("\r\n", "\n"));

        if (settings.Json)
        {
            var allFiles = new List<object>();
            foreach (var f in created) allFiles.Add(new { file = f, skipped = false });
            foreach (var f in skipped) allFiles.Add(new { file = f, skipped = true });
            WriteJson(new
            {
                success = true,
                output = outputDir,
                createdFiles = created.Count,
                skippedFiles = skipped.Count,
                files = allFiles
            });
        }
        else
        {
            ConsoleHelper.PrintSuccess("Scaffolded dbsh project");
            ConsoleHelper.PrintSummary("Summary", new[]
            {
                ("location", outputDir, Theme.Text),
                ("provider", provider, Theme.Success),
                ("project", projectName, Theme.Text),
                ("files", $"{created.Count} created", created.Count > 0 ? Theme.Success : Theme.Muted),
            });

            ConsoleHelper.PrintList("Created", created);

            if (skipped.Count > 0)
            {
                ConsoleHelper.PrintWarning($"Skipped {skipped.Count} existing file(s) (use --force to overwrite):");
                foreach (var s in skipped)
                    ConsoleHelper.PrintStep("  " + s);
            }

            ConsoleHelper.PrintDivider();
            var nextSteps = new Panel(
                new Markup(
                    $"  {Theme.Step} [grey]Set [bold]$DB_CONNECTION_STRING[/] or edit [bold]Database/Config/migration.json[/][/]{Environment.NewLine}" +
                    $"  {Theme.Step} [grey]Run [bold]dbsh init[/] to create the tracking tables[/]{Environment.NewLine}" +
                    $"  {Theme.Step} [grey]Run [bold]dbsh create --name YourMigration[/] and write your SQL[/]{Environment.NewLine}" +
                    $"  {Theme.Step} [grey]Run [bold]dbsh plan[/] to preview, then [bold]dbsh migrate[/] to apply[/]"))
                .Border(BoxBorder.Rounded)
                .BorderStyle(Style.Parse(Theme.Muted))
                .Header($" [{Theme.Warning}] Next steps [/]", Justify.Left)
                .Padding(1, 0);
            AnsiConsole.Write(nextSteps);
        }

        return Task.FromResult(0);
    }

    private static string IdType(string provider) => ProviderSqlHelper.IdType(provider);
    private static string DefaultId(string provider) => ProviderSqlHelper.DefaultId(provider);
    private static string BoolType(string provider) => ProviderSqlHelper.BoolType(provider);
    private static string BoolTrue(string provider) => ProviderSqlHelper.BoolTrue(provider);
    private static string TimestampType(string provider) => ProviderSqlHelper.TimestampType(provider);
    private static string DefaultNow(string provider) => ProviderSqlHelper.DefaultNow(provider);
    private static string UniqueIndex(string provider) => ProviderSqlHelper.UniqueIndex(provider);

    private static bool IsValidModuleName(string module)
    {
        if (string.IsNullOrEmpty(module))
            return true;
        return Regex.IsMatch(module, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }

    private static bool IsPathSafe(string outputDir, string relativePath)
    {
        try
        {
            var basePath = Path.GetFullPath(Path.Combine(outputDir, "Database", "Migrations"));
            var resolvedPath = Path.GetFullPath(Path.Combine(outputDir, relativePath));
            return resolvedPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
