using System.Text.Json;
using dbsh.CLI.Helpers;
using dbsh.Core.ValueObjects;
using Spectre.Console.Cli;

namespace dbsh.CLI.Commands;

/// <summary>Shared helpers for all commands: host wiring, environment/live gating and JSON output.</summary>
public abstract class CliCommandBase<TSettings> : AsyncCommand<TSettings> where TSettings : GlobalSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    protected CliHost CreateHost(GlobalSettings settings)
    {
        var basePath = !string.IsNullOrWhiteSpace(settings.Config) ? settings.Config : settings.LegacyConfig;
        if (!string.IsNullOrWhiteSpace(settings.LegacyConfig) && string.IsNullOrWhiteSpace(settings.Config) && !settings.Json)
        {
            ConsoleHelper.PrintWarning("The --config option is deprecated; use --base-path instead.");
        }

        return CliHost.Create(new CliHostOptions(
            settings.Environment,
            settings.Provider,
            settings.ConnectionString,
            basePath,
            settings.UseInMemory,
            settings.Verbose,
            settings.Module));
    }

    protected int Fail(GlobalSettings settings, string message)
    {
        if (settings.Json)
        {
            WriteJson(new { success = false, error = message });
            return 1;
        }
        ConsoleHelper.PrintFatal(message);
        return 1;
    }

    protected bool TryResolveEnvironment(GlobalSettings settings, CliHost host, out EnvironmentConfiguration environment)
    {
        try
        {
            environment = host.ConfigLoader.LoadEnvironment(host.EnvironmentName);
            return true;
        }
        catch (Exception ex)
        {
            environment = null!;
            if (!settings.Json)
            {
                ConsoleHelper.PrintError(ex.Message);
            }
            return false;
        }
    }

    protected int RequireLive(GlobalSettings settings, CliHost host)
    {
        if (host.IsLive)
        {
            return 0;
        }
        return Fail(settings, "This command requires a live database connection. Set a connection string via --connection-string, the DB_CONNECTION_STRING environment variable, or migration.json.");
    }

    protected void WriteJson(object value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value, value.GetType(), JsonOptions));
    }
}
