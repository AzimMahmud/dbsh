using dbsh.Core.Enums;

namespace dbsh.Core.ValueObjects;

/// <summary>A migration script parsed from disk, ready for validation or execution.</summary>
public sealed class ParsedMigration
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public MigrationType Type { get; set; } = MigrationType.Schema;
    public string Category { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public bool IsRepeatable { get; set; }
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Runtime context for an execution operation (deploy, dry-run).</summary>
public sealed class MigrationContext
{
    public string Environment { get; set; } = string.Empty;
    public string ExecutedBy { get; set; } = string.Empty;
    public string? Approver { get; set; }
    public int? BatchSize { get; set; }
    public bool StopOnFailure { get; set; } = true;
    public bool Force { get; set; }
    public bool SkipApproval { get; set; }
    public string? Module { get; set; }

    /// <summary>Optional per-migration progress receiver (UI-agnostic; the engine never requires it).</summary>
    public IProgress<MigrationProgressEvent>? Progress { get; set; }
}

/// <summary>A request to roll back one or more migrations.</summary>
public sealed class RollbackRequest
{
    public string Version { get; set; } = "last";
    public int Count { get; set; } = 1;
    public string Environment { get; set; } = string.Empty;
    public string ExecutedBy { get; set; } = string.Empty;
    public string? Module { get; set; }

    /// <summary>Optional per-migration progress receiver (UI-agnostic; the engine never requires it).</summary>
    public IProgress<MigrationProgressEvent>? Progress { get; set; }
}

/// <summary>Per-migration progress notification raised while deploying or rolling back.</summary>
public sealed class MigrationProgressEvent
{
    public MigrationProgressKind Kind { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Zero-based position within the current run.</summary>
    public int Index { get; set; }
    /// <summary>Total migrations in the current run.</summary>
    public int Total { get; set; }
    /// <summary>Execution duration, populated for Completed/Failed events.</summary>
    public TimeSpan Elapsed { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Structured outcome for a single migration execution (deploy or rollback).</summary>
public sealed class MigrationOutcome
{
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>A single step inside an execution plan.</summary>
public sealed class MigrationExecutionItem
{
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public MigrationType Type { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool HasRollback { get; set; }
}

/// <summary>An ordered, computed plan of migrations that would be applied.</summary>
public sealed class MigrationExecutionPlan
{
    public string Environment { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public IReadOnlyList<MigrationExecutionItem> Items { get; set; } = Array.Empty<MigrationExecutionItem>();
}

/// <summary>Result of validating the migration scripts for an environment.</summary>
public sealed class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int ScriptsChecked { get; set; }

    public static ValidationResult Valid(int scriptsChecked) => new() { IsValid = true, ScriptsChecked = scriptsChecked };
}

/// <summary>Result of computing a dry-run execution plan.</summary>
public sealed class DryRunResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public MigrationExecutionPlan? ExecutionPlan { get; set; }

    /// <summary>Versions of applied migrations whose on-disk checksum no longer matches history.</summary>
    public List<string> DriftedMigrations { get; } = new();

    /// <summary>Versions recorded in history whose script file is no longer on disk.</summary>
    public List<string> MissingMigrations { get; } = new();

    public static DryRunResult Success(MigrationExecutionPlan plan) => new() { IsSuccess = true, ExecutionPlan = plan };
    public static DryRunResult Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}

/// <summary>Result of applying migrations to a target database.</summary>
public sealed class DeployResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalApplied { get; set; }
    public List<string> AppliedMigrations { get; set; } = new();
    public List<string> FailedMigrations { get; set; } = new();
    public List<string> DriftedMigrations { get; set; } = new();
    public List<string> PendingRepeatables { get; set; } = new();
    public List<MigrationOutcome> Outcomes { get; } = new();
    public TimeSpan Elapsed { get; set; }
}

/// <summary>Result of repairing the migration history.</summary>
public sealed class RepairResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> RepairedMigrations { get; } = new();
}

/// <summary>Result of rolling back migrations.</summary>
public sealed class RollbackResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> RolledBackMigrations { get; } = new();
    public List<MigrationOutcome> Outcomes { get; } = new();
}

/// <summary>Status summary for an environment.</summary>
public sealed class StatusResult
{
    public string Environment { get; set; } = string.Empty;
    public IReadOnlyList<Core.Entities.MigrationRecord> Records { get; set; } = Array.Empty<Core.Entities.MigrationRecord>();
    public int Total { get; set; }
    public int Applied { get; set; }
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Failed { get; set; }
    public int RolledBack { get; set; }
    public List<string> DriftedMigrations { get; set; } = new();

    /// <summary>Versions recorded in history whose script file is no longer on disk.</summary>
    public List<string> MissingMigrations { get; } = new();
}

/// <summary>Result of initialising the tracking schema.</summary>
public sealed class InitResult
{
    public bool IsSuccess { get; set; }
    public List<string> CreatedObjects { get; } = new();
    public string? ErrorMessage { get; set; }
}
