using System.ComponentModel;
using System.Globalization;
using System.Text;
using dbsh.CLI.Helpers;
using dbsh.Core.Enums;
using dbsh.Engine.Parsing;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

public sealed class CreateCommand : CliCommandBase<CreateCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("-n|--name")]
        [Description("Migration name (PascalCase)")]
        public string? Name { get; set; }

        [CommandOption("-t|--type")]
        [Description("Migration type: schema | data | patch | rollback | repeatable")]
        public string Type { get; set; } = "schema";

        [CommandOption("-a|--author")]
        [Description("Author name to embed in the header")]
        public string? Author { get; set; }

        [CommandOption("-d|--description")]
        [Description("Short description to embed in the header")]
        public string? Description { get; set; }

        [CommandOption("-o|--output")]
        [Description("Override the output directory")]
        public string? Dir { get; set; }

        [CommandOption("-V|--for-version")]
        [Description("Version to target when creating a rollback (default: latest version without a rollback)")]
        public string? ForVersion { get; set; }

        [CommandOption("--timestamp")]
        [Description("Use a timestamp version instead of the auto-incrementing number")]
        public bool Timestamp { get; set; }

        [CommandOption("-s|--sequence", IsHidden = true)]
        [Description("Deprecated: auto-incrementing versions are now the default")]
        public bool Sequence { get; set; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var host = CreateHost(settings);
        var name = settings.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(Fail(settings, "The --name option is required."));
        }

        if (settings.Sequence && !settings.Json)
        {
            ConsoleHelper.PrintWarning("The --sequence option is deprecated; auto-incrementing versions are now the default.");
        }

        var (type, folder, prefix, templateName) = ResolveType(settings.Type);

        string version;
        string displayVersion;

        if (type == MigrationType.Repeatable)
        {
            version = "R";
            displayVersion = "R";
        }
        else if (type == MigrationType.Rollback)
        {
            var target = settings.ForVersion;
            if (string.IsNullOrWhiteSpace(target))
            {
                target = MigrationNaming.FindVersionMissingRollback(host.ScriptsPath);
                if (target is null)
                {
                    return Task.FromResult(Fail(settings,
                        "Every forward version already has a rollback script. Pass --for-version <version> to create one anyway."));
                }
            }

            version = int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? MigrationNaming.FormatVersion(parsed)
                : target;
            displayVersion = version;
        }
        else
        {
            version = settings.Timestamp
                ? DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                : MigrationNaming.NextVersion(host.ScriptsPath);
            displayVersion = version;
        }

        var namePart = type == MigrationType.Rollback ? $"Rollback_{name}" : name;
        var fileName = $"{prefix}{version}__{namePart}.sql";

        var outputDir = settings.Dir ?? Path.Combine(host.ScriptsPath, folder);
        Directory.CreateDirectory(outputDir);
        var fullPath = Path.Combine(outputDir, fileName);

        if (File.Exists(fullPath))
        {
            return Task.FromResult(Fail(settings, $"A file already exists at '{fullPath}'."));
        }

        var templatePath = Path.Combine(host.BasePath, "Database", "Templates", templateName);
        var author = settings.Author ?? Environment.UserName;
        var description = settings.Description ?? name;
        var content = File.Exists(templatePath)
            ? RenderTemplate(File.ReadAllText(templatePath), name, author, description)
            : DefaultContent(name, author, description, type);

        File.WriteAllText(fullPath, content);

        if (settings.Json)
        {
            WriteJson(new { success = true, version, path = fullPath, type = type.ToString() });
        }
        else
        {
            var display = type == MigrationType.Rollback
                ? $"{displayVersion} {namePart}"
                : $"{displayVersion} {name}";
            ConsoleHelper.PrintSuccess($"Created {type.ToString().ToLowerInvariant()} migration {display}.");
            ConsoleHelper.PrintKeyValue("file", fullPath);
        }
        return Task.FromResult(0);
    }

    private static (MigrationType Type, string Folder, string Prefix, string Template) ResolveType(string type) => type.ToLowerInvariant() switch
    {
        "data" => (MigrationType.Data, "Data", "V", "data_migration.sql"),
        "patch" => (MigrationType.Patch, "Patch", "V", "patch_migration.sql"),
        "rollback" => (MigrationType.Rollback, "Rollback", "U", "rollback_migration.sql"),
        "repeatable" => (MigrationType.Repeatable, "Schema", "R", "repeatable_migration.sql"),
        _ => (MigrationType.Schema, "Schema", "V", "schema_migration.sql")
    };

    private static string RenderTemplate(string template, string name, string author, string description)
        => template
            .Replace("{{NAME}}", name)
            .Replace("{{AUTHOR}}", author)
            .Replace("{{DATE}}", DateTime.UtcNow.ToString("yyyy-MM-dd"))
            .Replace("{{DESCRIPTION}}", description);

    private static string DefaultContent(string name, string author, string description, MigrationType type)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Migration: {name}");
        sb.AppendLine($"-- Author: {author}");
        sb.AppendLine($"-- Created: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"-- Description: {description}");
        sb.AppendLine($"-- Type: {type}");
        sb.AppendLine();
        sb.AppendLine("-- TODO: add your SQL here");
        return sb.ToString();
    }
}
