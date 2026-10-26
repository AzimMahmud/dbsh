using dbsh.Core.ValueObjects;
using dbsh.Engine.Execution;
using dbsh.Engine.InMemory;
using dbsh.Engine.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace dbsh.Engine.Tests;

/// <summary>
/// Verifies the "Missing" state (history rows whose script file no longer exists on disk)
/// surfaced by status and dry-run, alongside checksum drift.
/// </summary>
public class MissingMigrationTests
{
    private readonly InMemoryMigrationTracker _tracker = new();
    private readonly InMemoryMigrationLockManager _lockManager = new();
    private readonly InMemoryAuditLogger _auditLogger = new();
    private readonly ConfigurableEnvironmentProvider _envProvider = new();
    private readonly ScriptParser _parser = new();

    private MigrationExecutor CreateExecutor(string scriptsPath)
        => new(_tracker, _lockManager, _parser, _envProvider, _auditLogger,
            NullLogger<MigrationExecutor>.Instance,
            new FakeScriptExecutor(), "fake", scriptsPath: scriptsPath);

    [Fact]
    public async Task Status_FlagsMissing_WhenScriptFileDeleted()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");

        var executor = CreateExecutor(dir.Path);
        await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        File.Delete(System.IO.Path.Combine(dir.Path, "Schema", "V001__CreateTable.sql"));
        executor.InvalidateCache();

        var status = await executor.GetStatusAsync("development");
        Assert.Contains("001", status.MissingMigrations);
    }

    [Fact]
    public async Task Status_DoesNotFlagPresentScripts()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");

        var executor = CreateExecutor(dir.Path);
        await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        var status = await executor.GetStatusAsync("development");
        Assert.Empty(status.MissingMigrations);
    }

    [Fact]
    public async Task DryRun_ReportsBothDriftAndMissing()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V002__AddColumn.sql", "ALTER TABLE t ADD c INT;");

        var executor = CreateExecutor(dir.Path);
        var deployed = await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });
        Assert.True(deployed.IsSuccess);

        // 001 edited in place (drift), 002 deleted from disk (missing).
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "Schema", "V001__CreateTable.sql"), "CREATE TABLE t (id INT, name NVARCHAR(50));");
        File.Delete(System.IO.Path.Combine(dir.Path, "Schema", "V002__AddColumn.sql"));
        executor.InvalidateCache();

        var dryRun = await executor.DryRunAsync(new MigrationContext { Environment = "development" });

        Assert.True(dryRun.IsSuccess);
        Assert.Contains("001", dryRun.DriftedMigrations);
        Assert.Contains("002", dryRun.MissingMigrations);
    }

    [Fact]
    public async Task Status_SkipsMissingCheck_WhenScriptsDirectoryAbsent()
    {
        var executor = CreateExecutor(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dbsh-absent-" + Guid.NewGuid().ToString("N")));
        await _tracker.AddAsync(new dbsh.Core.Entities.MigrationRecord
        {
            Version = "001",
            ScriptName = "V001__CreateTable.sql",
            ScriptHash = "a",
            Status = dbsh.Core.Enums.MigrationStatus.Completed,
            Environment = "development"
        });

        var status = await executor.GetStatusAsync("development");

        Assert.Empty(status.MissingMigrations);
    }
}
