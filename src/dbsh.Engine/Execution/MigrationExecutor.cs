using System.Diagnostics;
using dbsh.Core.Entities;
using dbsh.Core.Enums;
using dbsh.Core.Exceptions;
using dbsh.Core.Interfaces;
using dbsh.Core.ValueObjects;
using dbsh.Engine.Parsing;
using Microsoft.Extensions.Logging;

namespace dbsh.Engine.Execution;

/// <summary>
/// Orchestrates validation, planning, deployment, rollback and repair of migrations.
/// Acts as the application core that coordinates the tracker, lock manager, audit log,
/// environment provider and (optionally) the target-database script executor.
/// </summary>
public sealed class MigrationExecutor
{
    private readonly IMigrationTracker _tracker;
    private readonly IMigrationLockManager _lockManager;
    private readonly ScriptParser _parser;
    private readonly IEnvironmentProvider _environmentProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<MigrationExecutor> _logger;
    private readonly IMigrationScriptExecutor? _scriptExecutor;
    private readonly string _scriptsPath;
    private readonly string _scriptsPattern;
    private readonly string? _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly bool _strictAudit;
    private readonly string? _module;
    private List<ParsedMigration>? _discoveryCache;
    private List<string>? _discoveryErrors;

    public MigrationExecutor(
        IMigrationTracker tracker,
        IMigrationLockManager lockManager,
        ScriptParser parser,
        IEnvironmentProvider environmentProvider,
        IAuditLogger auditLogger,
        ILogger<MigrationExecutor> logger,
        IMigrationScriptExecutor? scriptExecutor = null,
        string? connectionString = null,
        int commandTimeoutSeconds = 3600,
        string? scriptsPath = null,
        string? scriptsPattern = null,
        bool strictAudit = false,
        string? module = null)
    {
        _tracker = tracker;
        _lockManager = lockManager;
        _parser = parser;
        _environmentProvider = environmentProvider;
        _auditLogger = auditLogger;
        _logger = logger;
        _scriptExecutor = scriptExecutor;
        _connectionString = connectionString;
        _commandTimeoutSeconds = commandTimeoutSeconds;
        _module = module;
        _scriptsPath = ResolveScriptsPath(scriptsPath);
        _scriptsPattern = string.IsNullOrWhiteSpace(scriptsPattern) ? "*.sql" : scriptsPattern;
        _strictAudit = strictAudit;
    }

    /// <summary>Validates every script under the scripts path: naming, syntax, uniqueness and dependencies.</summary>
    public async Task<ValidationResult> ValidateAsync(string environment, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();
        var (migrations, errors) = DiscoverAllCore();
        _discoveryCache = migrations;

        result.Errors.AddRange(errors);
        result.ScriptsChecked = migrations.Count;

        var byVersion = new Dictionary<string, ParsedMigration>(StringComparer.OrdinalIgnoreCase);
        var fileNames = migrations.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var migration in migrations.OrderBy(m => OrderKey(m.Version)))
        {
            // Rollback scripts (U-prefix) intentionally share a version with their forward
            // counterpart, so they are excluded from the version-uniqueness check.
            if (migration.Type != MigrationType.Rollback && !byVersion.TryAdd(migration.Version, migration))
            {
                result.Errors.Add($"Duplicate migration version '{migration.Version}' ({migration.FileName} conflicts with {byVersion[migration.Version].FileName}).");
            }

            if (!_parser.HasExecutableContent(migration.Content))
            {
                result.Errors.Add($"Migration '{migration.FileName}' has no executable content.");
            }

            foreach (var dependency in migration.Dependencies)
            {
                if (!fileNames.Contains(dependency))
                {
                    result.Errors.Add($"Migration '{migration.FileName}' depends on missing script '{dependency}'.");
                }
            }
        }

        result.IsValid = result.Errors.Count == 0;
        await AuditSafe(environment, AuditAction.Validate, performedBy: "system",
            details: result.IsValid ? $"validated {result.ScriptsChecked} scripts" : $"{result.Errors.Count} error(s)");

        _logger.LogInformation("Validation for {Environment}: {Status} ({Count} scripts, {Errors} errors)",
            environment, result.IsValid ? "valid" : "invalid", result.ScriptsChecked, result.Errors.Count);

        return result;
    }

    /// <summary>Computes the ordered set of migrations that would be applied without touching the database.</summary>
    public async Task<DryRunResult> DryRunAsync(MigrationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Fail fast on parse errors so they cannot be silently dropped on the deploy path.
            var discoveryErrors = GetCachedDiscoveryErrors();
            if (discoveryErrors.Count > 0)
            {
                return DryRunResult.Failure(
                    $"{discoveryErrors.Count} script(s) failed to parse:\n - " + string.Join("\n - ", discoveryErrors));
            }

            var pending = await ComputePendingAsync(context.Environment, cancellationToken);
            var drift = await ComputeChecksumDriftAsync(context.Environment, cancellationToken);
            var plan = new MigrationExecutionPlan
            {
                Environment = context.Environment,
                TotalCount = pending.Count,
                Items = pending.Select(m => new MigrationExecutionItem
                {
                    Version = m.Version,
                    Name = m.Name,
                    ScriptPath = m.FilePath,
                    Hash = m.Hash,
                    Type = m.Type,
                    Category = m.Category,
                    HasRollback = HasRollbackFor(m.Version)
                }).ToArray()
            };

            var driftNote = drift.Count > 0
                ? $"; WARNING: {drift.Count} applied migration(s) have checksum drift: {string.Join(", ", drift.Select(d => d.Version))}"
                : string.Empty;

            await AuditSafe(context.Environment, AuditAction.DryRun, context.ExecutedBy,
                $"{plan.TotalCount} migration(s) planned{driftNote}");
            return DryRunResult.Success(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dry-run failed for {Environment}", context.Environment);
            return DryRunResult.Failure(ex.Message);
        }
    }

    /// <summary>Applies all pending migrations to the target environment within a distributed lock.</summary>
    public async Task<DeployResult> DeployAsync(MigrationContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DeployResult();
        var lockKey = string.IsNullOrEmpty(_module)
            ? $"migration:{context.Environment}"
            : $"migration:{_module}:{context.Environment}";

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            result.ErrorMessage = "No connection string is configured. A database connection is required to deploy.";
            return result;
        }

        var env = await _environmentProvider.GetEnvironmentAsync(context.Environment, cancellationToken);

        // Approval gate: an environment flagged RequireApproval may not be deployed without an
        // approver unless the caller explicitly force-skips (e.g. via --yes on a CI recovery).
        if (env.Migration.RequireApproval
            && !context.SkipApproval
            && !context.Force
            && string.IsNullOrWhiteSpace(context.Approver))
        {
            result.ErrorMessage = $"Environment '{context.Environment}' requires an approver. " +
                                  "Pass --approver <name> (or set SkipApproval/Force from an authorised caller).";
            return result;
        }

        // Fail fast on parse errors: silently skipping malformed scripts during deploy would
        // leave the database in an inconsistent state relative to the source tree.
        var discoveryErrors = GetCachedDiscoveryErrors();
        if (discoveryErrors.Count > 0)
        {
            result.ErrorMessage = $"{discoveryErrors.Count} script(s) failed to parse:\n - "
                                  + string.Join("\n - ", discoveryErrors);
            return result;
        }

        var locked = await _lockManager.AcquireAsync(context.Environment, lockKey, context.ExecutedBy, env.Migration.LockTimeoutSeconds, _module, cancellationToken);
        if (!locked)
        {
            result.ErrorMessage = $"Could not acquire migration lock for environment '{context.Environment}'. Another deployment may be in progress.";
            return result;
        }

        try
        {
            // Refuse to deploy when a previously-applied script has been edited in place.
            // The caller must repair the history (via `dbsh repair`) or explicitly set Force.
            var drift = await ComputeChecksumDriftAsync(context.Environment, cancellationToken);
            if (drift.Count > 0 && !context.Force)
            {
                foreach (var (version, fileName) in drift)
                {
                    result.DriftedMigrations.Add(version);
                }
                result.ErrorMessage = $"Checksum drift detected for {drift.Count} migration(s): " +
                                      string.Join(", ", drift.Select(d => $"{d.Version} ({d.FileName})")) +
                                      ". Run 'dbsh repair' or pass --force to override.";
                return result;
            }

            var pending = await ComputePendingAsync(context.Environment, cancellationToken);
            if (pending.Count == 0)
            {
                result.IsSuccess = true;
                await AuditSafe(context.Environment, AuditAction.Deploy, context.ExecutedBy, "nothing to deploy");
                return result;
            }

            if (_scriptExecutor is null)
            {
                result.ErrorMessage = "No script executor is configured. A database connection is required to deploy.";
                return result;
            }

            // Surface the pending repeatable count separately so callers can report it.
            foreach (var p in pending.Where(p => p.IsRepeatable))
            {
                result.PendingRepeatables.Add(p.FileName);
            }

            var batchSize = context.BatchSize ?? env.Migration.MaxBatchSize;
            var batchNumber = 1;
            foreach (var batch in pending.Chunk(Math.Max(1, batchSize)))
            {
                // Renew before each batch; if the lease already expired and got stolen, stop
                // rather than keep applying migrations with no lock held.
                var renewed = await _lockManager.RenewAsync(context.Environment, lockKey, context.ExecutedBy, env.Migration.LockTimeoutSeconds, _module, cancellationToken);
                if (!renewed)
                {
                    result.ErrorMessage = $"Lost the migration lock for environment '{context.Environment}' (lease expired before batch {batchNumber} could run). " +
                                          $"Applied {result.TotalApplied} migration(s) before the lock was lost; another deployment may now be running concurrently. Re-run once it completes.";
                    await AuditSafe(context.Environment, AuditAction.Deploy, context.ExecutedBy,
                        $"lock lost before batch {batchNumber}: applied {result.TotalApplied}, failed {result.FailedMigrations.Count}");
                    return result;
                }

                foreach (var migration in batch)
                {
                    var record = new MigrationRecord
                    {
                        Version = migration.Version,
                        Name = migration.Name,
                        ScriptName = migration.FileName,
                        ScriptHash = migration.Hash,
                        Type = migration.Type,
                        Category = migration.Category,
                        ExecutedBy = context.ExecutedBy,
                        Environment = context.Environment,
                        Status = MigrationStatus.InProgress,
                        RollbackAvailable = HasRollbackFor(migration.Version),
                        RollbackScriptName = FindRollbackName(migration.Version),
                        BatchNumber = batchNumber
                    };

                    var execution = await _scriptExecutor.ExecuteAsync(_connectionString!, migration.Content, _commandTimeoutSeconds, cancellationToken);
                    record.ExecutionTimeMs = execution.ElapsedMs;

                    if (execution.IsSuccess)
                    {
                        record.Status = MigrationStatus.Completed;
                        record.ExecutedAtUtc = DateTime.UtcNow;
                        await _tracker.AddAsync(record, _module, cancellationToken);
                        result.AppliedMigrations.Add(migration.Version);
                        result.TotalApplied++;
                        _logger.LogInformation("Applied {Version} {Name} in {Ms}ms", migration.Version, migration.Name, execution.ElapsedMs);
                    }
                    else
                    {
                        record.Status = MigrationStatus.Failed;
                        record.ErrorMessage = execution.ErrorMessage;
                        await _tracker.AddAsync(record, _module, cancellationToken);
                        result.FailedMigrations.Add(migration.Version);
                        _logger.LogError("Migration {Version} failed: {Error}", migration.Version, execution.ErrorMessage);

                        if (context.StopOnFailure)
                        {
                            result.ErrorMessage = $"Migration '{migration.Version}' failed: {execution.ErrorMessage}";
                            await AuditSafe(context.Environment, AuditAction.Deploy, context.ExecutedBy,
                                $"stopped at {migration.Version}: applied {result.TotalApplied}, failed {result.FailedMigrations.Count}");
                            return result;
                        }
                    }
                }
                batchNumber++;
            }

            result.IsSuccess = result.FailedMigrations.Count == 0;
            await AuditSafe(context.Environment, AuditAction.Deploy, context.ExecutedBy,
                $"applied {result.TotalApplied}, failed {result.FailedMigrations.Count}");
            return result;
        }
        finally
        {
            await _lockManager.ReleaseAsync(context.Environment, lockKey, context.ExecutedBy, _module, cancellationToken);
            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;
        }
    }

    /// <summary>Repairs a failed migration by re-queuing it as pending (and optionally removing its failed record). When <paramref name="version"/> is null or empty, all failed migrations are repaired.</summary>
    public async Task<RepairResult> RepairAsync(string environment, string? version, CancellationToken cancellationToken = default)
    {
        var result = new RepairResult();

        if (string.IsNullOrWhiteSpace(version))
        {
            var all = await _tracker.GetAllAsync(environment, _module, cancellationToken);
            foreach (var failed in all.Where(r => r.Status == MigrationStatus.Failed))
            {
                await _tracker.DeleteAsync(environment, failed.Version, _module, cancellationToken);
                result.RepairedMigrations.Add(failed.Version);
            }

            result.IsSuccess = true;
            await AuditSafe(environment, AuditAction.Repair, "system",
                result.RepairedMigrations.Count > 0
                    ? $"repaired {result.RepairedMigrations.Count} migration(s)"
                    : "no failed migrations to repair");
            return result;
        }

        var record = await _tracker.GetByVersionAsync(environment, version, _module, cancellationToken);

        if (record is null)
        {
            result.IsSuccess = true;
            return result;
        }

        if (record.Status == MigrationStatus.Failed)
        {
            await _tracker.DeleteAsync(environment, version, _module, cancellationToken);
            result.RepairedMigrations.Add(version);
        }
        else
        {
            await _tracker.UpdateStatusAsync(environment, version, record.Status, null, _module, cancellationToken);
        }

        result.IsSuccess = true;
        await AuditSafe(environment, AuditAction.Repair, "system", $"repaired {version}");
        return result;
    }

    /// <summary>Rolls back one or more previously applied migrations using their rollback scripts.</summary>
    public async Task<RollbackResult> RollbackAsync(RollbackRequest request, CancellationToken cancellationToken = default)
    {
        var result = new RollbackResult();

        // Surface parse errors so a malformed U-script is never silently ignored.
        var discoveryErrors = GetCachedDiscoveryErrors();
        if (discoveryErrors.Count > 0)
        {
            result.ErrorMessage = $"{discoveryErrors.Count} script(s) failed to parse:\n - "
                                  + string.Join("\n - ", discoveryErrors);
            return result;
        }

        var applied = await _tracker.GetAppliedAsync(request.Environment, _module, cancellationToken);
        if (applied.Count == 0)
        {
            result.IsSuccess = true;
            return result;
        }

        var targets = ResolveRollbackTargets(request, applied).ToList();
        if (targets.Count == 0)
        {
            result.IsSuccess = true;
            return result;
        }

        if (string.IsNullOrWhiteSpace(_connectionString) || _scriptExecutor is null)
        {
            result.ErrorMessage = "No database connection is configured. A connection is required to roll back.";
            return result;
        }

        var env = await _environmentProvider.GetEnvironmentAsync(request.Environment, cancellationToken);

        var module = _module ?? request.Module;
        var lockKey = string.IsNullOrEmpty(module)
            ? $"migration:{request.Environment}"
            : $"migration:{module}:{request.Environment}";
        // Rollback is just as destructive as deploy; serialise it with the same lock.
        var locked = await _lockManager.AcquireAsync(request.Environment, lockKey, request.ExecutedBy, env.Migration.LockTimeoutSeconds, module, cancellationToken);
        if (!locked)
        {
            result.ErrorMessage = $"Could not acquire migration lock for environment '{request.Environment}'. Another deployment may be in progress.";
            return result;
        }

        try
        {
            var rollbacks = DiscoverRollbacks();
            foreach (var target in targets)
            {
                var rollback = rollbacks.FirstOrDefault(r => r.Version.Equals(target.Version, StringComparison.OrdinalIgnoreCase));
                if (rollback is null)
                {
                    result.ErrorMessage = $"No rollback script found for migration '{target.Version}'.";
                    await AuditSafe(request.Environment, AuditAction.Rollback, request.ExecutedBy,
                        $"rollback incomplete: missing script for {target.Version}; rolled back {result.RolledBackMigrations.Count}");
                    return result;
                }

                var execution = await _scriptExecutor.ExecuteAsync(_connectionString!, rollback.Content, _commandTimeoutSeconds, cancellationToken);
                if (!execution.IsSuccess)
                {
                    result.ErrorMessage = $"Rollback of '{target.Version}' failed: {execution.ErrorMessage}";
                    await AuditSafe(request.Environment, AuditAction.Rollback, request.ExecutedBy,
                        $"rollback incomplete: {target.Version} failed ({execution.ErrorMessage}); rolled back {result.RolledBackMigrations.Count}");
                    return result;
                }

                await _tracker.UpdateStatusAsync(request.Environment, target.Version, MigrationStatus.RolledBack, null, module, cancellationToken);
                result.RolledBackMigrations.Add(target.Version);
            }

            result.IsSuccess = true;
            await AuditSafe(request.Environment, AuditAction.Rollback, request.ExecutedBy,
                $"rolled back {result.RolledBackMigrations.Count}");
            return result;
        }
        finally
        {
            await _lockManager.ReleaseAsync(request.Environment, lockKey, request.ExecutedBy, module, cancellationToken);
        }
    }

    /// <summary>Returns the migration status summary for an environment.</summary>
    public async Task<StatusResult> GetStatusAsync(string environment, CancellationToken cancellationToken = default)
    {
        var records = await _tracker.GetAllAsync(environment, _module, cancellationToken);
        var status = new StatusResult
        {
            Environment = environment,
            Records = records,
            Total = records.Count,
            Applied = records.Count(r => r.Status == MigrationStatus.Completed),
            Pending = records.Count(r => r.Status == MigrationStatus.Pending),
            InProgress = records.Count(r => r.Status == MigrationStatus.InProgress),
            Failed = records.Count(r => r.Status == MigrationStatus.Failed),
            RolledBack = records.Count(r => r.Status == MigrationStatus.RolledBack)
        };

        var drift = await ComputeChecksumDriftAsync(environment, cancellationToken);
        foreach (var (version, _) in drift)
        {
            status.DriftedMigrations.Add(version);
        }

        return status;
    }

    /// <summary>Returns recent audit entries for an environment.</summary>
    public Task<IReadOnlyList<MigrationAuditEntry>> GetHistoryAsync(string environment, int limit, CancellationToken cancellationToken = default)
        => _auditLogger.GetHistoryAsync(environment, limit, _module, cancellationToken);

    /// <summary>Creates the tracking schema on the target database.</summary>
    public async Task<InitResult> InitAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || _scriptExecutor is null)
        {
            return new InitResult { ErrorMessage = "No database connection is configured. A connection is required to initialise." };
        }

        await _scriptExecutor.EnsureTrackingSchemaAsync(_connectionString, _module, cancellationToken);
        return new InitResult { IsSuccess = true, CreatedObjects = { "__migration_history", "__migration_lock", "__migration_audit" } };
    }

    private async Task<List<ParsedMigration>> ComputePendingAsync(string environment, CancellationToken cancellationToken)
    {
        var versioned = DiscoverVersioned();
        if (versioned.Count == 0 && DiscoverRepeatables().Count == 0)
        {
            return new List<ParsedMigration>();
        }

        var applied = await _tracker.GetAppliedAsync(environment, _module, cancellationToken);
        var appliedByVersion = applied
            .Where(r => !string.Equals(r.Version, "R", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => r.Version, r => r, StringComparer.OrdinalIgnoreCase);

        // Pending versioned migrations = those whose version has never been applied.
        var pending = versioned
            .Where(m => !appliedByVersion.ContainsKey(m.Version))
            .OrderBy(m => OrderKey(m.Version))
            .ToList();

        // Pending repeatable migrations = those whose checksum changed since last apply
        // (or have never been applied). Repeatables always run after versioned ones.
        var appliedRepeatables = applied
            .Where(r => string.Equals(r.Version, "R", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => r.ScriptName, r => r.ScriptHash, StringComparer.OrdinalIgnoreCase);

        foreach (var repeatable in DiscoverRepeatables())
        {
            if (!appliedRepeatables.TryGetValue(repeatable.FileName, out var storedHash)
                || !string.Equals(storedHash, repeatable.Hash, StringComparison.OrdinalIgnoreCase))
            {
                pending.Add(repeatable);
            }
        }

        return pending;
    }

    /// <summary>
    /// Computes the set of already-applied versioned migrations whose on-disk hash no longer
    /// matches the hash recorded in <c>__migration_history</c>. A non-empty result means a
    /// previously-applied script has been edited in place, which is unsafe to silently ignore.
    /// </summary>
    private async Task<List<(string Version, string FileName)>> ComputeChecksumDriftAsync(
        string environment, CancellationToken cancellationToken)
    {
        var drift = new List<(string, string)>();
        var applied = await _tracker.GetAppliedAsync(environment, _module, cancellationToken);
        var versioned = DiscoverVersioned();

        foreach (var migration in versioned)
        {
            var record = applied.FirstOrDefault(r =>
                r.Version.Equals(migration.Version, StringComparison.OrdinalIgnoreCase));
            if (record is null) continue;

            if (!string.Equals(record.ScriptHash, migration.Hash, StringComparison.OrdinalIgnoreCase))
            {
                drift.Add((migration.Version, migration.FileName));
            }
        }

        return drift;
    }

    private (List<ParsedMigration> Migrations, List<string> Errors) DiscoverAllCore()
    {
        var migrations = new List<ParsedMigration>();
        var errors = new List<string>();

        if (!Directory.Exists(_scriptsPath))
        {
            return (migrations, errors);
        }

        foreach (var file in Directory.EnumerateFiles(_scriptsPath, _scriptsPattern, SearchOption.AllDirectories).OrderBy(f => f))
        {
            try
            {
                var content = File.ReadAllText(file);
                migrations.Add(_parser.Parse(file, content));
            }
            catch (ScriptParseException ex)
            {
                errors.Add(ex.Message);
            }
            catch (IOException ex)
            {
                errors.Add($"Could not read '{file}': {ex.Message}");
            }
        }

        return (migrations, errors);
    }

    private List<ParsedMigration> DiscoverVersioned() =>
        GetCachedDiscovered()
            .Where(m => m.Type != MigrationType.Rollback && !m.IsRepeatable)
            .ToList();

    private List<ParsedMigration> DiscoverRepeatables() =>
        GetCachedDiscovered()
            .Where(m => m.IsRepeatable)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<ParsedMigration> DiscoverRollbacks() =>
        GetCachedDiscovered().Where(m => m.Type == MigrationType.Rollback).ToList();

    private List<ParsedMigration> GetCachedDiscovered()
    {
        EnsureDiscovered();
        return _discoveryCache!;
    }

    private List<string> GetCachedDiscoveryErrors()
    {
        EnsureDiscovered();
        return _discoveryErrors!;
    }

    private void EnsureDiscovered()
    {
        if (_discoveryCache is not null) return;
        var (migrations, errors) = DiscoverAllCore();
        _discoveryCache = migrations;
        _discoveryErrors = errors;
    }

    /// <summary>Invalidates the discovery cache so the next operation re-reads scripts from disk.</summary>
    public void InvalidateCache()
    {
        _discoveryCache = null;
        _discoveryErrors = null;
    }

    private bool HasRollbackFor(string version) => DiscoverRollbacks().Any(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase));

    private string? FindRollbackName(string version) =>
        DiscoverRollbacks().FirstOrDefault(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase))?.FileName;

    private static IEnumerable<MigrationRecord> ResolveRollbackTargets(RollbackRequest request, IReadOnlyList<MigrationRecord> applied)
    {
        if (!string.IsNullOrWhiteSpace(request.Version) &&
            !request.Version.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            return applied.Where(r => r.Version.Equals(request.Version, StringComparison.OrdinalIgnoreCase));
        }

        return applied.Take(Math.Max(1, request.Count));
    }

    private async Task AuditSafe(string environment, AuditAction action, string performedBy, string details)
    {
        try
        {
            await _auditLogger.LogAsync(new MigrationAuditEntry
            {
                Action = action,
                PerformedBy = string.IsNullOrEmpty(performedBy) ? "system" : performedBy,
                Environment = environment,
                Details = details
            },
            _module);
        }
        catch (Exception ex)
        {
            if (_strictAudit)
            {
                throw;
            }
            _logger.LogWarning(ex, "Failed to write audit entry for {Action}", action);
        }
    }

    /// <summary>
    /// Produces a zero-padded sort key for versioned migrations so that "2" sorts after "10"
    /// when compared as strings. Safe because IsValidVersion() in ScriptParser guarantees
    /// all version tokens contain only digit characters.
    /// </summary>
    private static string OrderKey(string version) => version.PadLeft(20, '0');

    private string ResolveScriptsPath(string? configuredPath)
    {
        var basePath = configuredPath ?? ResolveDefaultScriptsPath();
        if (string.IsNullOrEmpty(_module))
        {
            return basePath;
        }

        var resolvedBase = Path.GetFullPath(basePath);

        // If the configured path already ends with the module name, don't double-append.
        var lastSegment = Path.GetFileName(resolvedBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(lastSegment, _module, StringComparison.OrdinalIgnoreCase))
        {
            return resolvedBase;
        }

        return Path.Combine(basePath, _module);
    }

    private static string ResolveDefaultScriptsPath()
    {
        var candidates = new[] { "./Database/Migrations", "../Database/Migrations", "../../Database/Migrations" };
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return Path.GetFullPath("./Database/Migrations");
    }
}
