namespace dbsh.Core.Enums;

/// <summary>Lifecycle state of a single migration within an environment.</summary>
public enum MigrationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    RolledBack
}

/// <summary>Classification of a migration script, derived from its filename and folder.</summary>
public enum MigrationType
{
    Schema,
    Data,
    Patch,
    Repeatable,
    Rollback
}

/// <summary>Auditable operations recorded against the migration history.</summary>
public enum AuditAction
{
    Validate,
    DryRun,
    Deploy,
    Rollback,
    Repair
}

/// <summary>Lifecycle phase of a per-migration progress notification.</summary>
public enum MigrationProgressKind
{
    Started,
    Completed,
    Failed
}
