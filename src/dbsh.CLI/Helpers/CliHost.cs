using dbsh.Core.Interfaces;
using dbsh.Core.ValueObjects;
using dbsh.Engine.Execution;
using dbsh.Engine.InMemory;
using dbsh.Engine.Parsing;
using dbsh.Infrastructure.Database;
using dbsh.Infrastructure.Database.Providers;
using dbsh.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace dbsh.CLI.Helpers;

/// <summary>
/// Composition root for a single CLI invocation. Resolves the database provider from
/// configuration and wires either live (relational) or in-memory implementations.
/// </summary>
public sealed class CliHost
{
    public MigrationExecutor Executor { get; }
    public IConfigLoader ConfigLoader { get; }
    public MigrationConfiguration? Config { get; }
    public string EnvironmentName { get; }
    public bool IsLive { get; }
    public string? ConnectionString { get; }
    public string ProviderName { get; }
    public string ScriptsPath { get; }
    public string BasePath { get; }
    public string? Module { get; }

    private CliHost(MigrationExecutor executor, IConfigLoader configLoader, MigrationConfiguration? config,
        string environmentName, bool isLive, string? connectionString, string providerName, string scriptsPath,
        string basePath, string? module)
    {
        Executor = executor;
        ConfigLoader = configLoader;
        Config = config;
        EnvironmentName = environmentName;
        IsLive = isLive;
        ConnectionString = connectionString;
        ProviderName = providerName;
        ScriptsPath = scriptsPath;
        BasePath = basePath;
        Module = module;
    }

    public static CliHost Create(CliHostOptions options)
    {
        var basePath = string.IsNullOrWhiteSpace(options.ConfigBasePath)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(options.ConfigBasePath);

        var configLoader = new FileSystemConfigLoader(basePath);

        MigrationConfiguration? config = null;
        try
        {
            config = configLoader.LoadMigrationConfiguration();
        }
        catch (FileNotFoundException)
        {
            // No migration.json in this directory: commands like create, info and help
            // work fine without one, so fall back to defaults. Malformed or otherwise
            // invalid configuration is a real error and intentionally propagates.
        }
        catch (DirectoryNotFoundException)
        {
            // Same as above: no config directory present.
        }

        var environment = string.IsNullOrWhiteSpace(options.EnvironmentName) ? "local" : options.EnvironmentName;
        var module = string.IsNullOrWhiteSpace(options.Module) ? null : options.Module;

        var connectionString = ResolveConnectionString(options.ConnectionString, config, configLoader, environment);
        var providerName = !string.IsNullOrWhiteSpace(options.Provider) ? options.Provider : (config?.Provider ?? "postgresql");
        var scriptsPath = ResolveScriptsPath(basePath, config, module);
        var commandTimeout = config?.CommandTimeoutSeconds ?? 3600;

        var preferInMemory = options.UseInMemory || string.IsNullOrWhiteSpace(connectionString);
        var logger = new SpectreLogger<MigrationExecutor>(options.Verbose);

        IMigrationTracker tracker;
        IMigrationLockManager lockManager;
        IAuditLogger auditLogger;
        IEnvironmentProvider environmentProvider;
        IMigrationScriptExecutor? scriptExecutor;

        if (preferInMemory)
        {
            tracker = new InMemoryMigrationTracker();
            lockManager = new InMemoryMigrationLockManager();
            auditLogger = new InMemoryAuditLogger();
            environmentProvider = new InMemoryEnvironmentProvider();
            scriptExecutor = null;
        }
        else
        {
            var provider = DatabaseProviderFactory.Create(providerName);

            if (!string.IsNullOrEmpty(module) && !provider.SupportsSchemas)
            {
                throw new InvalidOperationException(
                    $"Provider '{provider.Name}' does not support database schemas. " +
                    $"The -m (--module) flag cannot be used with {provider.Name}. " +
                    $"Remove the -m flag or use a provider that supports schemas (PostgreSQL, SQL Server).");
            }

            tracker = new RelationalMigrationTracker(provider, connectionString!, module);
            lockManager = new RelationalMigrationLockManager(provider, connectionString!, module);
            auditLogger = new RelationalAuditLogger(provider, connectionString!, module);
            environmentProvider = new ConfigEnvironmentProvider(configLoader);
            scriptExecutor = new RelationalMigrationExecutor(provider, module);
            providerName = provider.Name;
        }

        var executor = new MigrationExecutor(
            tracker, lockManager, new ScriptParser(), environmentProvider, auditLogger, logger,
            scriptExecutor, connectionString, commandTimeout, scriptsPath, config?.ScriptsPattern, module: module);

        return new CliHost(executor, configLoader, config, environment, !preferInMemory, connectionString, providerName, scriptsPath, basePath, module);
    }

    private static string? ResolveConnectionString(string? cliOverride, MigrationConfiguration? config, IConfigLoader configLoader, string environment)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride))
        {
            return cliOverride;
        }

        var fromEnv = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        try
        {
            var envConfig = configLoader.LoadEnvironment(environment);
            if (!string.IsNullOrWhiteSpace(envConfig.Database.ConnectionString))
            {
                return envConfig.Database.ConnectionString;
            }
        }
        catch (FileNotFoundException)
        {
            // Environment file may not exist; fall through to global config.
        }
        catch (DirectoryNotFoundException)
        {
            // Same as above: no environments directory.
        }

        return string.IsNullOrWhiteSpace(config?.ConnectionString) ? null : config.ConnectionString;
    }

    private static string ResolveScriptsPath(string basePath, MigrationConfiguration? config, string? module)
    {
        string resolved;
        if (config is null)
        {
            resolved = Path.Combine(basePath, "Database", "Migrations");
        }
        else
        {
            var configured = config.ScriptsPath;
            resolved = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(basePath, configured));
        }

        if (string.IsNullOrEmpty(module))
        {
            return resolved;
        }

        var resolvedBase = Path.GetFullPath(resolved);

        // If the configured scripts.path already ends with the module name (e.g.
        // "./Database/Migrations/postgresql" with -m postgresql), don't double-append.
        var lastSegment = Path.GetFileName(resolvedBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(lastSegment, module, StringComparison.OrdinalIgnoreCase))
        {
            return resolvedBase;
        }

        var fullPath = Path.GetFullPath(Path.Combine(resolved, module));

        if (!fullPath.StartsWith(resolvedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            fullPath != resolvedBase)
        {
            throw new InvalidOperationException(
                $"Module path '{module}' is not within the allowed migrations directory. " +
                "Module names must contain only alphanumeric characters and underscores.");
        }

        return fullPath;
    }

    /// <summary>
    /// Routes engine log output through Spectre.Console. Warnings and errors are surfaced
    /// via <see cref="ConsoleHelper"/>; informational messages are shown only in verbose mode.
    /// All output is suppressed when <see cref="ConsoleHelper.UiSuppressed"/> is set (JSON mode).
    /// </summary>
    private sealed class SpectreLogger<T> : ILogger<T>
    {
        private readonly bool _verbose;

        public SpectreLogger(bool verbose = false)
        {
            _verbose = verbose;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            !ConsoleHelper.UiSuppressed && (logLevel >= LogLevel.Warning || (_verbose && logLevel >= LogLevel.Information));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (ConsoleHelper.UiSuppressed)
            {
                return;
            }

            var message = formatter(state, exception);

            switch (logLevel)
            {
                case LogLevel.Information:
                    if (_verbose)
                    {
                        ConsoleHelper.PrintInfo(message);
                    }
                    break;
                case LogLevel.Warning:
                    ConsoleHelper.PrintWarning(message);
                    break;
                case LogLevel.Error or LogLevel.Critical:
                    ConsoleHelper.PrintError(message);
                    break;
            }
        }
    }
}
