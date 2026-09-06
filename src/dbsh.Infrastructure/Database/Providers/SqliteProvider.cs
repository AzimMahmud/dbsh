using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace dbsh.Infrastructure.Database.Providers;

/// <summary>SQLite provider backed by Microsoft.Data.Sqlite.</summary>
public sealed class SqliteProvider : IDatabaseProvider
{
    public string Name => "SQLite";

    public bool SupportsSchemas => false;

    public DbConnection CreateConnection(string connectionString)
        => new SqliteConnection(connectionString);

    public DbParameter CreateParameter(string name, object? value)
        => new SqliteParameter("@" + name, value ?? DBNull.Value);

    public string GetTableName(string baseName, string? module = null)
    {
        if (string.IsNullOrEmpty(module)) return baseName;
        ValidateModule(module);
        throw new InvalidOperationException(
            $"SQLite does not support database schemas. " +
            $"Cannot create module-prefixed table '{module}__{baseName}'. " +
            $"Remove the -m flag or use a provider that supports schemas (PostgreSQL, SQL Server).");
    }

    public string GetTrackingSchemaDdl(string? module = null)
    {
        if (!string.IsNullOrEmpty(module))
            throw new InvalidOperationException(
                $"SQLite does not support database schemas. " +
                $"Cannot create module-prefixed tracking tables for module '{module}'. " +
                $"Remove the -m flag or use a provider that supports schemas (PostgreSQL, SQL Server).");

        var h = GetTableName("__migration_history");
        var l = GetTableName("__migration_lock");
        var a = GetTableName("__migration_audit");

        return $"""
            CREATE TABLE IF NOT EXISTS {h} (
                id                   TEXT NOT NULL PRIMARY KEY,
                version              TEXT NOT NULL,
                name                 TEXT NOT NULL,
                script_name          TEXT NOT NULL,
                script_hash          TEXT NOT NULL,
                migration_type       TEXT NOT NULL,
                category             TEXT NOT NULL,
                executed_by          TEXT NOT NULL,
                executed_at_utc      TEXT NOT NULL,
                execution_time_ms    INTEGER NOT NULL,
                environment          TEXT NOT NULL,
                status               TEXT NOT NULL,
                rollback_available   INTEGER NOT NULL DEFAULT 0,
                rollback_script_name TEXT,
                error_message        TEXT,
                batch_number         INTEGER NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IF NOT EXISTS uk_migration_version_env ON {h}(version, environment) WHERE version <> 'R';
            CREATE UNIQUE INDEX IF NOT EXISTS uk_migration_script_name ON {h}(script_name, environment);
            CREATE INDEX IF NOT EXISTS idx_migration_history_env      ON {h}(environment);
            CREATE INDEX IF NOT EXISTS idx_migration_history_status   ON {h}(status);
            CREATE INDEX IF NOT EXISTS idx_migration_history_executed ON {h}(executed_at_utc);
            CREATE INDEX IF NOT EXISTS idx_migration_history_version  ON {h}(version);

            CREATE TABLE IF NOT EXISTS {l} (
                id              TEXT NOT NULL PRIMARY KEY,
                lock_key        TEXT NOT NULL,
                locked_by       TEXT NOT NULL,
                locked_at_utc   TEXT NOT NULL,
                expires_at_utc  TEXT NOT NULL,
                environment     TEXT NOT NULL,
                is_active       INTEGER NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IF NOT EXISTS uk_migration_lock_key ON {l}(lock_key);
            CREATE INDEX IF NOT EXISTS idx_migration_lock_env_active ON {l}(environment, is_active);

            CREATE TABLE IF NOT EXISTS {a} (
                id               TEXT NOT NULL PRIMARY KEY,
                action           TEXT NOT NULL,
                performed_by     TEXT NOT NULL,
                performed_at_utc TEXT NOT NULL,
                environment      TEXT NOT NULL,
                details          TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_migration_audit_date ON {a}(performed_at_utc);
            CREATE INDEX IF NOT EXISTS idx_migration_audit_env  ON {a}(environment);
            """;
    }

    public string GetAcquireLockSql(string? module = null)
    {
        if (!string.IsNullOrEmpty(module))
            throw new InvalidOperationException(
                $"SQLite does not support database schemas. " +
                $"Cannot use module-prefixed lock table for module '{module}'. " +
                $"Remove the -m flag or use a provider that supports schemas (PostgreSQL, SQL Server).");

        var l = GetTableName("__migration_lock");
        return $"""
            INSERT INTO {l} (id, lock_key, locked_by, locked_at_utc, expires_at_utc, environment, is_active)
            VALUES (@id, @lock_key, @locked_by, @locked_at_utc, @expires_at_utc, @environment, @true)
            ON CONFLICT(lock_key) DO UPDATE
            SET id            = excluded.id,
                locked_by     = excluded.locked_by,
                locked_at_utc = excluded.locked_at_utc,
                expires_at_utc= excluded.expires_at_utc,
                environment   = excluded.environment,
                is_active     = excluded.is_active
            WHERE {l}.is_active = @false OR {l}.expires_at_utc <= @now
            """;
    }

    private static string ValidateModule(string? module)
    {
        if (string.IsNullOrEmpty(module))
            return module!;
        if (!Regex.IsMatch(module, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new InvalidOperationException($"Invalid module name '{module}'. Must contain only alphanumeric characters and underscores.");
        return module;
    }
}
