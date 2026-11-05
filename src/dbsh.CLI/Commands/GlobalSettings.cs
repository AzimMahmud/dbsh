using System.ComponentModel;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

/// <summary>Shared options available on every command, inherited by per-command settings.</summary>
public abstract class GlobalSettings : CommandSettings
{
    [CommandOption("-e|--environment")]
    [Description("Target environment name (default: local)")]
    public string Environment { get; set; } = "local";

    [CommandOption("-p|--provider")]
    [Description("Database provider: postgresql | sqlserver | mysql | sqlite | oracle | cockroachdb | yugabyte | aurora")]
    public string? Provider { get; set; }

    [CommandOption("-c|--connection-string")]
    [Description("Database connection string override")]
    public string? ConnectionString { get; set; }

    [CommandOption("-C|--base-path")]
    [Description("Path to the repository root (config base)")]
    public string? Config { get; set; }

    [CommandOption("--config", IsHidden = true)]
    [Description("Deprecated alias for --base-path")]
    public string? LegacyConfig { get; set; }

    [CommandOption("-i|--in-memory")]
    [Description("Run offline against an in-memory store (no database; for testing)")]
    public bool UseInMemory { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Answer yes to confirmation prompts (non-interactive)")]
    public bool AssumeYes { get; set; }

    // Declared so the help text exposes --no-color. The actual suppression happens
    // in Program.cs before Spectre renders anything (it must be applied to the
    // AnsiConsole instance up front, so the bound property here is informational only).
    [CommandOption("--no-color")]
    [Description("Disable colored output")]
    public bool NoColor { get; set; }

    [CommandOption("-j|--json")]
    [Description("Print machine-readable JSON instead of formatted output")]
    public bool Json { get; set; }

    [CommandOption("-v|--verbose")]
    [Description("Show detailed informational output")]
    public bool Verbose { get; set; }

    [CommandOption("-m|--module")]
    [Description("Module name (subfolder under Database/Migrations/)")]
    public string? Module { get; set; }

    public override ValidationResult Validate()
    {
        if (!string.IsNullOrEmpty(Module) && !IsValidModuleName(Module))
        {
            return ValidationResult.Error(
                "Module name must contain only alphanumeric characters and underscores, " +
                "and must start with a letter or underscore.");
        }

        return ValidationResult.Success();
    }

    private static bool IsValidModuleName(string module)
    {
        if (string.IsNullOrEmpty(module))
            return true;
        return Regex.IsMatch(module, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }
}
