using dbsh.Core.Enums;
using dbsh.Core.Interfaces;
using dbsh.Core.ValueObjects;
using dbsh.Engine.Execution;
using dbsh.Engine.InMemory;
using dbsh.Engine.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace dbsh.Engine.Tests;

/// <summary>
/// Verifies the per-migration progress stream raised during deploy and rollback,
/// including the structured outcome list used by the CLI for version-first output.
/// </summary>
public class ProgressReportingTests
{
    private readonly InMemoryMigrationTracker _tracker = new();
    private readonly InMemoryMigrationLockManager _lockManager = new();
    private readonly InMemoryAuditLogger _auditLogger = new();
    private readonly ConfigurableEnvironmentProvider _envProvider = new();
    private readonly ScriptParser _parser = new();

    private MigrationExecutor CreateExecutor(string scriptsPath, IMigrationScriptExecutor? executor = null)
        => new(_tracker, _lockManager, _parser, _envProvider, _auditLogger,
            NullLogger<MigrationExecutor>.Instance,
            executor, "fake", scriptsPath: scriptsPath);

    private static List<MigrationProgressEvent> Collect(out IProgress<MigrationProgressEvent> progress)
    {
        var events = new List<MigrationProgressEvent>();
        progress = new CollectingProgress(events);
        return events;
    }

    private sealed class CollectingProgress(List<MigrationProgressEvent> events) : IProgress<MigrationProgressEvent>
    {
        public void Report(MigrationProgressEvent value) => events.Add(value);
    }

    [Fact]
    public async Task Deploy_ReportsStartedAndCompletedForEachMigration()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V002__AddColumn.sql", "ALTER TABLE t ADD c INT;");
        dir.WriteScript("Schema/V003__AddIndex.sql", "CREATE INDEX i ON t (c);");

        var events = Collect(out var progress);
        var executor = CreateExecutor(dir.Path, new FakeScriptExecutor());
        var result = await executor.DeployAsync(new MigrationContext
        {
            Environment = "development",
            ExecutedBy = "tester",
            Progress = progress
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(6, events.Count);

        Assert.Equal(MigrationProgressKind.Started, events[0].Kind);
        Assert.Equal("001", events[0].Version);
        Assert.Equal("CreateTable", events[0].Name);
        Assert.Equal(3, events[0].Total);

        Assert.Equal(MigrationProgressKind.Completed, events[1].Kind);
        Assert.Equal("001", events[1].Version);
        Assert.True(events[1].Elapsed.TotalMilliseconds > 0);

        var order = events.Select(e => (e.Kind, e.Version)).ToList();
        Assert.Equal(
        [
            (MigrationProgressKind.Started, "001"),
            (MigrationProgressKind.Completed, "001"),
            (MigrationProgressKind.Started, "002"),
            (MigrationProgressKind.Completed, "002"),
            (MigrationProgressKind.Started, "003"),
            (MigrationProgressKind.Completed, "003")
        ], order);
    }

    [Fact]
    public async Task Deploy_Failure_ReportsFailedEventWithError()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");

        var events = Collect(out var progress);
        var executor = CreateExecutor(dir.Path, new FailingScriptExecutor());
        var result = await executor.DeployAsync(new MigrationContext
        {
            Environment = "development",
            ExecutedBy = "tester",
            Progress = progress
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(2, events.Count);
        Assert.Equal(MigrationProgressKind.Failed, events[1].Kind);
        Assert.Contains("Simulated execution failure", events[1].ErrorMessage);
    }

    [Fact]
    public async Task Deploy_PopulatesOutcomesForCli()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V002__AddColumn.sql", "ALTER TABLE t ADD c INT;");

        var executor = CreateExecutor(dir.Path, new FakeScriptExecutor());
        var result = await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        Assert.Equal(2, result.Outcomes.Count);
        Assert.All(result.Outcomes, o => Assert.True(o.Success));
        Assert.Equal("001", result.Outcomes[0].Version);
        Assert.Equal("CreateTable", result.Outcomes[0].Name);
        Assert.Equal("002", result.Outcomes[1].Version);
        Assert.True(result.Outcomes[0].ElapsedMs > 0);
    }

    [Fact]
    public async Task Deploy_Failure_RecordsFailedOutcome()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");

        var executor = CreateExecutor(dir.Path, new FailingScriptExecutor());
        var result = await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        var outcome = Assert.Single(result.Outcomes);
        Assert.False(outcome.Success);
        Assert.Equal("001", outcome.Version);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public async Task Rollback_ReportsEventsInReverseOrder()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V002__AddColumn.sql", "ALTER TABLE t ADD c INT;");
        dir.WriteScript("Rollback/U001__Rollback_CreateTable.sql", "DROP TABLE t;");
        dir.WriteScript("Rollback/U002__Rollback_AddColumn.sql", "ALTER TABLE t DROP COLUMN c;");

        var executor = CreateExecutor(dir.Path, new FakeScriptExecutor());
        await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        var events = Collect(out var progress);
        var result = await executor.RollbackAsync(new RollbackRequest
        {
            Environment = "development",
            ExecutedBy = "tester",
            Count = 2,
            Progress = progress
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(4, events.Count);
        Assert.Equal(MigrationProgressKind.Started, events[0].Kind);
        Assert.Equal("002", events[0].Version);
        Assert.Equal(MigrationProgressKind.Completed, events[1].Kind);
        Assert.Equal("002", events[1].Version);
        Assert.Equal("001", events[3].Version);
        Assert.Equal(2, result.RolledBackMigrations.Count);
    }

    [Fact]
    public async Task Rollback_PopulatesOutcomesForCli()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Rollback/U001__Rollback_CreateTable.sql", "DROP TABLE t;");

        var executor = CreateExecutor(dir.Path, new FakeScriptExecutor());
        await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        var result = await executor.RollbackAsync(new RollbackRequest { Environment = "development", ExecutedBy = "tester" });

        var outcome = Assert.Single(result.Outcomes);
        Assert.True(outcome.Success);
        Assert.Equal("001", outcome.Version);
        Assert.Equal("CreateTable", outcome.Name);
    }

    [Fact]
    public async Task Deploy_WithoutProgress_CompletesNormally()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");

        var executor = CreateExecutor(dir.Path, new FakeScriptExecutor());
        var result = await executor.DeployAsync(new MigrationContext { Environment = "development", ExecutedBy = "tester" });

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationStatus.Completed,
            (await _tracker.GetAllAsync("development")).Single(r => r.Version == "001").Status);
    }
}
