# dbsh — Full Code Explained

A comprehensive, step-by-step walkthrough of every component in the dbsh database migration tool.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Project Structure](#2-project-structure)
3. [dbsh.Core — Domain Layer](#3-dbshcore--domain-layer)
4. [dbsh.Engine — Orchestration Layer](#4-dbshengine--orchestration-layer)
5. [dbsh.Infrastructure — Database & File System](#5-dbshinfrastructure--database--file-system)
6. [dbsh.CLI — Command-Line Interface](#6-dbshcli--command-line-interface)
7. [Database Schema — Tracking Tables](#7-database-schema--tracking-tables)
8. [Configuration System](#8-configuration-system)
9. [Migration Flows — Step by Step](#9-migration-flows--step-by-step)
10. [Test Suite](#10-test-suite)

---

## 1. Overview

**dbsh** is a SQL-first database migration tool for .NET, modeled after Flyway. It tracks, validates, and applies `.sql` migration files across multiple environments (local, development, staging, production) with built-in safety features for production use.

**Key characteristics:**
- Migrations are standalone `.sql` files in the repository — framework/language agnostic
- Supports **PostgreSQL**, **SQL Server**, **MySQL/MariaDB**, and **SQLite**
- Distributed lock with lease expiry to serialize concurrent deployments
- Checksum drift detection to prevent silent schema corruption
- Deployment windows for production safety
- Approval gates for regulated environments
- Module support for multi-project repositories
- Built on .NET 10, distributed as a self-contained binary or .NET global tool

**Version:** 2.1.3

---

## 2. Project Structure

```
dbsh/
├── dbsh.slnx                              # Solution file
├── Directory.Build.props                   # Global: net10.0, TreatWarningsAsErrors
├── global.json                             # .NET SDK 10.0.100
│
├── src/
│   ├── dbsh.Core/                          # Domain model, zero dependencies
│   ├── dbsh.Engine/                        # Orchestration (parsing, execution)
│   ├── dbsh.Infrastructure/                # Database providers, config loader
│   └── dbsh.CLI/                           # Executable (Spectre.Console.Cli)
│
├── tests/
│   └── dbsh.Engine.Tests/                  # 116 unit + 54 integration tests
│
└── Database/                               # Dogfooding: dbsh's own migrations
    ├── Config/
    │   ├── migration.json
    │   └── environments/{local,production}.json
    ├── Migrations/
    │   ├── Schema/V001-V003 + Rollback/U001-U003
    │   └── Templates/                      # 5 migration templates
    └── ...
```

**Dependency chain:** `Core` ← `Engine` ← `Infrastructure` ← `CLI`

```
┌──────────┐    ┌──────────┐    ┌──────────────────┐    ┌──────────┐
│ dbsh.Core│◄───│dbsh.Engine│◄───│dbsh.Infrastructure│◄───│ dbsh.CLI │
│          │    │          │    │                  │    │          │
│ Entities │    │ ScriptParser   │ Providers (4)   │    │ Commands │
│ Enums    │    │ MigrationExec  │ Relational impls │    │ Helpers  │
│ Interfaces    │ InMemory      │ ConfigLoader     │    │ Program  │
│ Exceptions│   │              │                  │    │          │
│ ValueObjs │   │              │                  │    │          │
└──────────┘    └──────────┘    └──────────────────┘    └──────────┘
```

---

## 3. dbsh.Core — Domain Layer

**Location:** `src/dbsh.Core/`

Zero external dependencies. Defines the entire domain model: entities, enums, interfaces, exceptions, and value objects. This is the innermost layer — every other project depends on it.

### 3.1 Entities (`Entities/MigrationEntities.cs`)

Three sealed classes representing persisted rows in the tracking tables:

#### `MigrationRecord`
Maps to `__migration_history`. One row per applied migration.

| Property | Type | Purpose |
|----------|------|---------|
| `Id` | `Guid` | Primary key |
| `Version` | `string` | Numeric version (e.g., "001") or "R" for repeatables |
| `Name` | `string` | Human-readable name from filename |
| `ScriptName` | `string` | Original filename on disk |
| `ScriptHash` | `string` | SHA-256 hash of LF-normalized content |
| `Type` | `MigrationType` | Schema, Data, Patch, Repeatable, or Rollback |
| `Category` | `string` | Folder name (e.g., "Schema", "Data") |
| `ExecutedBy` | `string` | User or process that ran it |
| `ExecutedAtUtc` | `DateTime` | Execution timestamp (UTC) |
| `ExecutionTimeMs` | `long` | Duration in milliseconds |
| `Environment` | `string` | Target environment name |
| `Status` | `MigrationStatus` | Pending, InProgress, Completed, Failed, or RolledBack |
| `RollbackAvailable` | `bool` | Whether a U-prefix script exists |
| `RollbackScriptName` | `string?` | Filename of the rollback script |
| `ErrorMessage` | `string?` | Error details on failure |
| `BatchNumber` | `int` | Batch grouping number |

#### `MigrationLock`
Maps to `__migration_lock`. Row-based distributed lock with lease expiry.

| Property | Type | Purpose |
|----------|------|---------|
| `Id` | `Guid` | Primary key |
| `LockKey` | `string` | Logical lock name (e.g., "migration:production") |
| `LockedBy` | `string` | Owner identity |
| `LockedAtUtc` | `DateTime` | When the lock was acquired |
| `ExpiresAtUtc` | `DateTime` | When the lease expires |
| `Environment` | `string` | Target environment |
| `IsActive` | `bool` | Whether this lease is currently held |

#### `MigrationAuditEntry`
Maps to `__migration_audit`. Append-only audit trail.

| Property | Type | Purpose |
|----------|------|---------|
| `Id` | `Guid` | Primary key |
| `Action` | `AuditAction` | Validate, DryRun, Deploy, Rollback, or Repair |
| `PerformedBy` | `string` | User who performed the action |
| `PerformedAtUtc` | `DateTime` | When the action occurred |
| `Environment` | `string` | Target environment |
| `Details` | `string?` | Additional context (e.g., "Applied 3 migrations") |

### 3.2 Enums (`Enums/MigrationEnums.cs`)

#### `MigrationStatus`
Lifecycle state of a migration: `Pending` → `InProgress` → `Completed` | `Failed` | `RolledBack`

#### `MigrationType`
Classification from filename and folder:
- `Schema` — DDL (tables, columns, indexes)
- `Data` — DML (INSERT, UPDATE)
- `Patch` — Ad-hoc fix
- `Repeatable` — Re-ran when content changes (R__ scripts)
- `Rollback` — Reversal script (U__ scripts)

#### `AuditAction`
What was logged: `Validate`, `DryRun`, `Deploy`, `Rollback`, `Repair`

### 3.3 Exceptions (`Exceptions/dbshException.cs`)

```
dbshException (abstract)
├── MigrationConfigurationException    # Missing/invalid config files
├── ScriptParseException               # Bad migration filename
└── UnsupportedProviderException       # Unknown database provider
```

All inherit from `dbshException`, so catching the base type catches any tool error.

### 3.4 Interfaces (`Interfaces/IMigrationInterfaces.cs`)

Seven interfaces defining the contract between layers:

| Interface | Purpose |
|-----------|---------|
| `IMigrationTracker` | CRUD on `__migration_history` |
| `IMigrationLockManager` | Distributed lock: Acquire, Release, Renew, IsActive |
| `IAuditLogger` | Write/query `__migration_audit` |
| `IEnvironmentProvider` | Load environment configurations |
| `IMigrationScriptExecutor` | Execute SQL, ensure tracking schema |
| `IConfigLoader` | Load `migration.json` and environment files |
| `ScriptExecutionResult` | Outcome of a single SQL execution (success, elapsed, error) |

### 3.5 Value Objects

#### Configuration Models (`ValueObjects/ConfigurationModels.cs`)

- **`MigrationConfiguration`** — Global config from `migration.json` (provider, connection string, scripts path, batch size, timeout, approval envs)
- **`DatabaseEndpoint`** — Host/port/name/schema for an environment
- **`MigrationEnvironmentSettings`** — Per-environment policy (approval, rollback, lock timeout, batch size)
- **`DeploymentWindow`** — Time-based deployment restriction with `IsWithinWindow()` supporting overnight windows and day-of-week filtering
- **`EnvironmentConfiguration`** — Full per-environment config combining the above

#### Migration Models (`ValueObjects/MigrationModels.cs`)

- **`ParsedMigration`** — A script parsed from disk (version, name, type, hash, content, dependencies, metadata)
- **`MigrationContext`** — Runtime context for an operation (environment, user, approval, force, module)
- **`RollbackRequest`** — Rollback target specification (version or count)
- **`MigrationExecutionItem`** — Single step in an execution plan
- **`MigrationExecutionPlan`** — Ordered list of pending migrations
- **`ValidationResult`** — Errors, warnings, scripts checked
- **`DryRunResult`** — Plan with optional error
- **`DeployResult`** — Applied/failed/drifted counts and lists
- **`RepairResult`** — Repaired migration versions
- **`RollbackResult`** — Rolled-back migration versions
- **`StatusResult`** — Aggregate counts (total, applied, pending, failed, drifted)
- **`InitResult`** — Created database objects

---

## 4. dbsh.Engine — Orchestration Layer

**Location:** `src/dbsh.Engine/`

Depends on `dbsh.Core` and `Microsoft.Extensions.Logging.Abstractions`. Contains the business logic for parsing, validating, planning, deploying, rolling back, and repairing migrations.

### 4.1 ScriptParser (`Parsing/ScriptParser.cs`, 217 lines)

Parses Flyway-style migration filenames into `ParsedMigration` objects.

#### Supported filename conventions:

| Pattern | Type | Example |
|---------|------|---------|
| `V<digits>__<Name>.sql` | Versioned | `V001__CreateUsers.sql` |
| `V<timestamp>__<Name>.sql` | Versioned | `V202601150001__CreateUsers.sql` |
| `R__<Name>.sql` | Repeatable | `R__RefreshViews.sql` |
| `U<digits>__<Name>.sql` | Rollback | `U001__RollbackUsers.sql` |

#### `Parse(filePath, content)` — The core method:

1. **Normalizes line endings** (CRLF → LF) so SHA-256 hashes are identical across Windows/Linux
2. Extracts filename from path, gets category (parent folder name)
3. Splits on `__` separator — throws `ScriptParseException` if missing
4. Classifies prefix via `Classify()`:
   - `R` (case-insensitive) → Repeatable
   - `U` + digits → Rollback
   - `V` + digits → Versioned (Schema/Data/Patch based on folder)
5. Computes SHA-256 hash of normalized content
6. Extracts metadata via regex: `-- Depends:`, `-- Author:`, `-- Description:`

#### `GenerateHash(content)`:

```
Normalize CRLF → LF → UTF-8 encode → SHA-256 → lowercase hex
```

Cross-platform deterministic hashing.

#### `HasExecutableContent(content)`:

Returns `true` if at least one non-comment, non-blank line exists. Rejects scripts that are only `--` comments or whitespace.

#### `ExtractDependencies(content)`:

Parses `-- Depends: dep1, dep2` into a string array. Used to validate that dependencies exist.

### 4.2 MigrationExecutor (`Execution/MigrationExecutor.cs`, 692 lines)

The central orchestrator. Coordinates all migration operations.

#### Constructor (12 parameters):

```csharp
MigrationExecutor(
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
    string? module = null
)
```

#### `ValidateAsync(environment, ct)` → `ValidationResult`:

1. Discovers all scripts via `DiscoverAllCore()` (reads files, parses filenames)
2. Caches results in `_discoveryCache` (lazy, invalidated by `InvalidateCache()`)
3. Checks for: parse errors, duplicate versions (excluding rollbacks), comment-only scripts, missing dependencies
4. Logs audit entry

#### `DryRunAsync(context, ct)` → `DryRunResult`:

1. Fails fast on parse errors
2. Calls `ComputePendingAsync()` — discovers all scripts, gets applied records, filters pending
3. Calls `ComputeChecksumDriftAsync()` — compares on-disk hashes against stored hashes
4. Builds `MigrationExecutionPlan` with ordered items
5. Appends drift warning if any

#### `DeployAsync(context, ct)` → `DeployResult` (the core flow):

```
1. Validate connection string
2. Load environment config
3. Check approval gate (RequireApproval + no Approver = refuse)
4. Fail fast on parse errors
5. Acquire distributed lock (INSERT...ON CONFLICT)
6. Check for checksum drift (refuse unless --force)
7. Compute pending migrations
8. If nothing pending → return success
9. For each batch (configurable batch size):
   a. Renew lock (fail if lease expired/stolen)
   b. For each migration in batch:
      - Create MigrationRecord
      - Execute SQL in transaction
      - Record success/failure in tracker
      - If failure + StopOnFailure → stop
10. Release lock (finally block)
11. Log audit entry
```

#### `RollbackAsync(request, ct)` → `RollbackResult`:

1. Discover rollback scripts
2. Resolve targets (specific version or last N)
3. Acquire lock
4. For each target: find U-prefix script, execute, update status to RolledBack
5. Release lock

#### `RepairAsync(environment, version, ct)` → `RepairResult`:

- No version specified: deletes all Failed records
- Specific version: deletes that Failed record (or clears error on non-failed)

#### `GetStatusAsync(environment, ct)` → `StatusResult`:

Fetches all records, counts by status, checks for checksum drift.

#### `InitAsync(ct)` → `InitResult`:

Creates the 3 tracking tables via `EnsureTrackingSchemaAsync()`.

#### Private helper methods:

| Method | Purpose |
|--------|---------|
| `ComputePendingAsync()` | Filters versioned (not in applied set) and repeatables (hash changed) |
| `ComputeChecksumDriftAsync()` | Compares on-disk vs stored hashes for applied migrations |
| `DiscoverAllCore()` | Reads all `.sql` files, parses each, collects errors |
| `DiscoverVersioned()` | Filters non-rollback, non-repeatable scripts |
| `DiscoverRepeatables()` | Filters repeatable scripts, ordered by name |
| `DiscoverRollbacks()` | Filters rollback scripts |
| `ResolveRollbackTargets()` | Maps request to specific applied records |
| `HasRollbackFor()` / `FindRollbackName()` | Looks up U-prefix script for a version |
| `AuditSafe()` | Logs audit entry, rethrows only if strictAudit is true |
| `OrderKey()` | Zero-pads version to 20 chars for correct string sorting |
| `ResolveScriptsPath()` | Finds scripts directory (configured or default locations) |
| `ResolveDefaultScriptsPath()` | Searches `./Database/Migrations`, `../`, `../../` |

### 4.3 InMemory Implementations (`InMemory/InMemoryImplementations.cs`, 228 lines)

Four in-memory implementations for tests and offline workflows:

#### `InMemoryMigrationTracker`
- Uses `ConcurrentDictionary<(string Env, string ScriptName), MigrationRecord>`
- `AddAsync`: For versioned, evicts prior record with same version; for repeatables, keyed by script name (multiple can coexist)
- `GetAppliedAsync`: Returns only `Completed` records, ordered by version descending

#### `InMemoryMigrationLockManager`
- Uses `Dictionary<(string Env, string Key), MigrationLock>` with `lock` gate
- `AcquireAsync`: Fails if active non-expired lock exists
- `RenewAsync`: Owner-only, extends `ExpiresAtUtc`
- `ReleaseAsync`: Owner-scoped (or any caller if lockedBy is null)

#### `InMemoryAuditLogger`
- Uses `ConcurrentQueue<MigrationAuditEntry>`
- `GetHistoryAsync`: Filters by environment, orders by timestamp descending, applies limit in C#

#### `InMemoryEnvironmentProvider`
- Hardcoded set: development, local, qa, production
- Returns default `EnvironmentConfiguration` with localhost:5432

---

## 5. dbsh.Infrastructure — Database & File System

**Location:** `src/dbsh.Infrastructure/`

Depends on `dbsh.Core`, `dbsh.Engine`, and 4 database NuGet packages (Npgsql, Microsoft.Data.SqlClient, MySqlConnector, Microsoft.Data.Sqlite).

### 5.1 Database Providers

#### `IDatabaseProvider` Interface (`Providers/IDatabaseProvider.cs`)

The abstraction over database engines. Every relational component depends on this.

| Member | Purpose |
|--------|---------|
| `Name` | Human-readable name |
| `SupportsSchemas` | `true` for PG/MSSQL, `false` for MySQL/SQLite |
| `CreateConnection(connectionString)` | Returns engine-specific `DbConnection` |
| `CreateParameter(name, value)` | Returns engine-specific `DbParameter` |
| `GetTrackingSchemaDdl(module)` | Complete DDL for all 3 tracking tables |
| `GetAcquireLockSql(module)` | Atomic UPSERT for distributed lock acquisition |
| `GetTableName(baseName, module)` | Correctly qualified table name |

#### `DatabaseProviderFactory` (`Providers/DatabaseProviderFactory.cs`)

Resolves provider from string (case-insensitive):

| Input | Provider |
|-------|----------|
| `"postgresql"`, `"postgres"`, `"npgsql"`, `"pgsql"` | `PostgreSqlProvider` |
| `"sqlserver"`, `"mssql"`, `"sql-server"`, `"sql server"` | `SqlServerProvider` |
| `"mysql"`, `"mariadb"`, `"maria"` | `MySqlProvider` |
| `"sqlite"` | `SqliteProvider` |
| `null` or `""` | `PostgreSqlProvider` (default) |
| Unknown | `UnsupportedProviderException` |

#### PostgreSQL Provider (`Providers/PostgreSqlProvider.cs`)

- Uses `NpgsqlConnection`, `NpgsqlParameter` (no `@` prefix)
- Schema-qualified names: `"module".table`
- DDL: `CREATE TABLE IF NOT EXISTS` with PostgreSQL types (`UUID`, `BOOLEAN`, `TIMESTAMP`, `TEXT`)
- Lock SQL: `INSERT ... ON CONFLICT (lock_key) DO UPDATE SET ... WHERE is_active = @false OR expires_at_utc <= @now`

#### SQL Server Provider (`Providers/SqlServerProvider.cs`)

- Uses `SqlConnection`, `SqlParameter` (adds `@` prefix)
- Schema-qualified names: `[module].[table]`
- DDL: `IF NOT EXISTS (SELECT 1 FROM [INFORMATION_SCHEMA].[TABLES])` guards, `UNIQUEIDENTIFIER` with `NEWID()`
- Lock SQL: `MERGE ... WITH (HOLDLOCK)` (exclusive lock hint for atomicity)

#### MySQL Provider (`Providers/MySqlProvider.cs`)

- Uses `MySqlConnection`, `MySqlParameter` (adds `@` prefix)
- Prefix-named tables: `` `module__table` `` (backtick-quoted)
- DDL: `CREATE TABLE IF NOT EXISTS` with MySQL types (`CHAR(36)`, `TINYINT(1)`, `DATETIME`)
- Lock SQL: `INSERT ... ON DUPLICATE KEY UPDATE` with `IF(is_active = 0 OR expires_at_utc <= @now, ...)`
- Special: Uses a generated stored column `version_key` for partial unique index (MySQL doesn't support partial indexes)

#### SQLite Provider (`Providers/SqliteProvider.cs`)

- Uses `SqliteConnection`, `SqliteParameter` (adds `@` prefix)
- Prefix-named tables: `"module__table"` (double-quote-quoted)
- DDL: `CREATE TABLE IF NOT EXISTS` with SQLite types (`TEXT`, `INTEGER`)
- Lock SQL: `INSERT ... ON CONFLICT(lock_key) DO UPDATE SET ... WHERE is_active = @false OR expires_at_utc <= @now`

### 5.2 Relational Implementations

All four use `System.Data.Common` abstractions (`DbConnection`, `DbCommand`, `DbDataReader`) — same code works across all providers.

#### `RelationalMigrationTracker` (`Database/RelationalMigrationTracker.cs`)

CRUD on `__migration_history`.

**`AddAsync`** — The most interesting method:
```
BEGIN TRANSACTION
  DELETE existing row (by version for versioned, by script_name for repeatables)
  INSERT new row with all 16 columns
COMMIT
```
This DELETE+INSERT pattern ensures idempotent replacement and preserves the `UNIQUE(version, environment)` constraint.

**`GetAppliedAsync`** — Returns only `Completed` records, ordered by version descending.

**`ExistsAsync`** — Uses `SELECT CASE WHEN EXISTS (...) THEN 1 ELSE 0 END`.

#### `RelationalMigrationLockManager` (`Database/RelationalMigrationLockManager.cs`)

Row-based distributed lock with lease expiry.

**`AcquireAsync`**:
1. Compute `expires_at = now + timeoutSeconds`
2. Run `PurgeExpiredAsync` (best-effort cleanup of inactive rows > 30 days old)
3. Execute provider-specific atomic UPSERT
4. Returns `true` if affected > 0

**`ReleaseAsync`** — `UPDATE SET is_active = @false WHERE ... AND locked_by = @lockedBy` (owner-scoped)

**`RenewAsync`** — `UPDATE SET expires_at_utc = @newExpiry WHERE ... AND locked_by = @lockedBy`

**`PurgeExpiredAsync`** — `DELETE WHERE is_active = @false AND expires_at_utc < (now - 30 days)`. Failures are ignored.

#### `RelationalMigrationExecutor` (`Database/RelationalMigrationExecutor.cs`)

Executes SQL in idempotent transactions.

**`ExecuteAsync`**:
```
OPEN connection
BEGIN TRANSACTION
  SET CommandTimeout
  EXECUTE SQL (ExecuteNonQueryAsync)
COMMIT
on failure: ROLLBACK (explicit, defensive over dispose)
```

**`EnsureTrackingSchemaAsync`** — Executes provider's DDL (no transaction, `CommandTimeout = 300`).

Key design: `OperationCanceledException` propagates and is NOT caught as a failure.

#### `RelationalAuditLogger` (`Database/RelationalAuditLogger.cs`)

Append-only INSERT into `__migration_audit`.

**`GetHistoryAsync`** — Applies limit in C# via `.Take(Math.Max(1, limit))` to avoid engine-specific `LIMIT`/`TOP`/`FETCH` syntax.

### 5.3 Config Environment Provider (`Database/ConfigEnvironmentProvider.cs`)

Wraps `IConfigLoader` to implement `IEnvironmentProvider`. Delegates all calls synchronously (wrapped in `Task.FromResult`).

### 5.4 FileSystem Config Loader (`FileSystem/FileSystemConfigLoader.cs`)

Loads `Database/Config/migration.json` and per-environment files.

**Key features:**

- **`${VAR}` environment variable expansion** via regex `\$\{([A-Z0-9_]+)\}` with 5-second timeout
- **Path traversal protection**: environment names validated against `^[A-Za-z0-9._-]+$`, max 64 chars, canonical path containment check
- **JSON deserialization** with `PropertyNameCaseInsensitive = true`

**`LoadMigrationConfiguration(basePath)`**:
1. Resolves `{root}/Database/Config/migration.json`
2. Throws `FileNotFoundException` if missing
3. Deserializes into internal DTO, maps to `MigrationConfiguration`
4. Applies defaults: provider="postgresql", scripts="./Database/Migrations", batch=10, etc.

**`LoadEnvironment(name, basePath)`**:
1. Validates environment name (1-64 chars, safe characters only)
2. Resolves canonical path, checks containment within environments directory
3. Loads `{name}.json` from `Database/Config/environments/`
4. Maps to `EnvironmentConfiguration` with defaults

**`GetAvailableEnvironments(basePath)`**:
- Lists `*.json` files in environments directory, extracts names, returns sorted alphabetically

---

## 6. dbsh.CLI — Command-Line Interface

**Location:** `src/dbsh.CLI/`

.NET 10 console application packaged as a dotnet global tool. Uses Spectre.Console.Cli v0.49.1.

### 6.1 Entry Point (`Program.cs`, 77 lines)

```csharp
var app = new CommandApp();
app.Configure(config => {
    config.AddCommand<NewCommand>("new").WithAlias("scaffold");
    config.AddCommand<InitCommand>("init");
    // ... 9 more commands
});
```

- Handles `--no-color` by replacing global `AnsiConsole`
- Sets `ConsoleHelper.UiSuppressed = true` when `--json` is active
- On unhandled exception: renders via `ConsoleHelper.RenderException`, returns exit code 1

### 6.2 Global Settings (`Commands/GlobalSettings.cs`)

All 11 commands inherit these CLI options:

| Flag | Type | Default | Purpose |
|------|------|---------|---------|
| `-e \| --environment` | `string` | `"local"` | Target environment |
| `-p \| --provider` | `string?` | `null` | Database provider |
| `-c \| --connection-string` | `string?` | `null` | Connection string override |
| `-C \| --base-path` | `string?` | `null` | Repository root |
| `-i \| --in-memory` | `bool` | `false` | Force offline mode |
| `-y \| --yes` | `bool` | `false` | Skip prompts |
| `-j \| --json` | `bool` | `false` | JSON output |
| `-v \| --verbose` | `bool` | `false` | Verbose output |
| `-m \| --module` | `string?` | `null` | Module subfolder |
| `--no-color` | `bool` | `false` | Disable colors |

Module name validated against `^[a-zA-Z_][a-zA-Z0-9_]*$`.

### 6.3 Command Base (`Commands/CliCommandBase.cs`)

Abstract base providing shared methods:
- `CreateHost(settings)` → Creates `CliHost` (composition root)
- `Fail(settings, message)` → Outputs error, returns exit code 1
- `TryResolveEnvironment(settings, host, out env)` → Loads env config
- `RequireLive(settings, host)` → Returns error if not connected to DB
- `WriteJson(value)` → Serializes as indented JSON

### 6.4 The 11 Commands

#### `new` (aliases: `scaffold`, `init-project`) — 466 lines

Interactive wizard that scaffolds a complete project:

```
Database/
├── Config/
│   ├── migration.json
│   └── environments/{local,development,staging,production}.json
├── Migrations/
│   ├── Schema/   (.gitkeep + V001__Init_<name>.sql)
│   ├── Data/     (.gitkeep)
│   ├── Patch/    (.gitkeep)
│   └── Rollback/ (.gitkeep + U001__Init_<name>.sql)
└── Templates/
    ├── schema_migration.sql
    ├── data_migration.sql
    ├── patch_migration.sql
    ├── rollback_migration.sql
    └── repeatable_migration.sql
```

Also generates: `.gitignore`, `.github/workflows/database-migration.yml`, `.config/dotnet-tools.json`.

Provider-specific SQL types are generated via `ProviderSqlHelper`.

#### `create` — 148 lines

Generates a new migration script from a template.

| Type | Prefix | Folder | Example |
|------|--------|--------|---------|
| schema | `V` | `Schema` | `V20260826120000__CreateUsers.sql` |
| data | `V` | `Data` | `V20260826120000__SeedRoles.sql` |
| patch | `V` | `Patch` | `V20260826120000__FixNulls.sql` |
| rollback | `U` | `Rollback` | `U20260826120000__Rollback_CreateUsers.sql` |
| repeatable | `R` | `Schema` | `R__RefreshViews.sql` |

Supports `--sequence` flag for sequential versioning instead of timestamp.

#### `validate` — 74 lines

Calls `host.Executor.ValidateAsync()`. Reports scripts checked, errors, warnings.

#### `plan` (alias: `dry-run`) — 75 lines

Calls `host.Executor.DryRunAsync()`. Shows pending migrations table. Does NOT require live DB.

#### `info` (alias: `config`) — 70 lines

Displays: configuration summary, runtime info (base path, scripts path, mode, environment, module), available environments.

#### `init` — 41 lines

Creates the 3 tracking tables on the target database. Requires live connection.

#### `migrate` (aliases: `deploy`, `apply`) — 183 lines

The core deployment command:
1. Requires live connection
2. Resolves environment config
3. Checks deployment window (unless `--force`)
4. Runs `DryRunAsync` to compute plan
5. Displays pending migrations
6. Checks approval requirement
7. Prompts for confirmation (unless `--yes`)
8. Runs `DeployAsync` with timing
9. Reports results

#### `status` — 77 lines

Shows migration status: total, applied, pending, failed, in-progress, drifted.

#### `rollback` — 110 lines

Rolls back using U-prefix scripts. Supports `--target-version` or `--count`.

#### `repair` — 69 lines

Removes failed migration records so they can be re-applied.

#### `history` (alias: `audit`) — 64 lines

Shows audit trail from `__migration_audit`. Configurable `--limit` (default 25).

### 6.5 Helpers

#### `CliHost` (`Helpers/CliHost.cs`, 233 lines)

The **composition root** for each CLI invocation. `CliHost.Create()`:

1. Creates `FileSystemConfigLoader` from base path
2. Loads `migration.json` (tolerates missing file)
3. Resolves connection string via priority chain: CLI > `DB_CONNECTION_STRING` env var > environment JSON > global config
4. Resolves provider name (CLI > config > default "postgresql")
5. Resolves scripts path (config > default `./Database/Migrations`; appends module subfolder)
6. If in-memory or no connection string: wires `InMemory*` implementations
7. Otherwise: creates provider via `DatabaseProviderFactory`, wires `Relational*` implementations
8. Creates `MigrationExecutor` with all dependencies

Also contains `SpectreLogger<T>` — routes engine log output through Spectre.Console:
- `Information` → shown only in verbose mode
- `Warning` → always shown
- `Error`/`Critical` → always shown
- All suppressed when JSON mode is active

#### `ConsoleHelper` (`Helpers/ConsoleHelper.cs`, 222 lines)

Static utility for all console rendering using Spectre.Console:
- `PrintBanner()` — Brand banner with gradient text
- `PrintMigrationTable()` — Bordered table with color-coded status
- `PrintSummary()` — Key-value panel
- `RunWithSpinner()` — Wraps async action in spinner status
- `Confirm()` — Yes/no prompt
- `RenderException()` — Heavy-bordered danger panel
- `StatusMarkup()` — Returns Spectre markup with color based on status

#### `ProviderSqlHelper` (`Helpers/ProviderSqlHelper.cs`, 58 lines)

Maps provider names to SQL types and syntax for code generation:
- `IdType()`: UUID / UNIQUEIDENTIFIER / CHAR(36) / TEXT
- `BoolType()`: BOOLEAN / BIT / TINYINT(1) / INTEGER
- `TimestampType()`: TIMESTAMPTZ / DATETIME2 / DATETIME / TEXT
- `DefaultNow()`: NOW() / GETUTCDATE() / UTC_TIMESTAMP / (empty)

#### `Theme` (`Helpers/Theme.cs`, 46 lines)

Color constants, Spectre styles, and glyphs (✓, ✗, ⚠, →, •, etc.).

---

## 7. Database Schema — Tracking Tables

### 7.1 `__migration_history`

```sql
CREATE TABLE IF NOT EXISTS __migration_history (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    version             VARCHAR(50) NOT NULL,
    name                TEXT NOT NULL,
    script_name         TEXT NOT NULL,
    script_hash         VARCHAR(64) NOT NULL,
    migration_type      VARCHAR(20) NOT NULL DEFAULT 'Schema',
    category            TEXT NOT NULL DEFAULT '',
    executed_by         TEXT NOT NULL DEFAULT '',
    executed_at_utc     TIMESTAMP NOT NULL DEFAULT NOW(),
    execution_time_ms   BIGINT NOT NULL DEFAULT 0,
    environment         VARCHAR(100) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'Pending',
    rollback_available  BOOLEAN NOT NULL DEFAULT FALSE,
    rollback_script_name TEXT NULL,
    error_message       TEXT NULL,
    batch_number        INT NOT NULL DEFAULT 1
);

-- Partial unique index: one version per environment (excluding repeatables)
CREATE UNIQUE INDEX uk_migration_version_env
    ON __migration_history (version, environment)
    WHERE version <> 'R';

-- Unique script name per environment
CREATE UNIQUE INDEX uk_migration_script_name
    ON __migration_history (script_name, environment);

-- Performance indexes
CREATE INDEX idx_migration_environment ON __migration_history (environment);
CREATE INDEX idx_migration_status ON __migration_history (status);
CREATE INDEX idx_migration_executed ON __migration_history (executed_at_utc);
CREATE INDEX idx_migration_version ON __migration_history (version);
```

### 7.2 `__migration_lock`

```sql
CREATE TABLE IF NOT EXISTS __migration_lock (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    lock_key        VARCHAR(100) NOT NULL,
    locked_by       TEXT NOT NULL,
    locked_at_utc   TIMESTAMP NOT NULL DEFAULT NOW(),
    expires_at_utc  TIMESTAMP NOT NULL,
    environment     VARCHAR(100) NOT NULL,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE INDEX idx_lock_env_active ON __migration_lock (environment, is_active);
```

### 7.3 `__migration_audit`

```sql
CREATE TABLE IF NOT EXISTS __migration_audit (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action              VARCHAR(20) NOT NULL,
    performed_by        TEXT NOT NULL DEFAULT '',
    performed_at_utc    TIMESTAMP NOT NULL DEFAULT NOW(),
    environment         VARCHAR(100) NOT NULL,
    details             TEXT NULL
);

CREATE INDEX idx_audit_date_env ON __migration_audit (performed_at_utc, environment);
```

---

## 8. Configuration System

### 8.1 Global Config: `Database/Config/migration.json`

```json
{
  "migration": {
    "version": "1.0.0",
    "database": {
      "provider": "postgresql",
      "connectionString": "${DB_CONNECTION_STRING}"
    },
    "scripts": {
      "path": "./Database/Migrations",
      "pattern": "*.sql"
    },
    "tracking": {
      "schema": "public",
      "tableName": "__migration_history"
    },
    "execution": {
      "lockTimeoutSeconds": 300,
      "commandTimeoutSeconds": 3600,
      "batchSize": 10,
      "stopOnFailure": true
    },
    "approval": {
      "requireApproval": ["production"]
    }
  }
}
```

### 8.2 Per-Environment Config: `Database/Config/environments/<name>.json`

```json
{
  "name": "production",
  "database": {
    "host": "${PROD_DB_HOST}",
    "port": 5432,
    "name": "fooddelivery_prod",
    "schema": "public"
  },
  "migration": {
    "requireApproval": true,
    "allowRollback": true,
    "lockTimeoutSeconds": 300,
    "maxBatchSize": 5
  },
  "deploymentWindow": {
    "enabled": true,
    "startTime": "02:00",
    "endTime": "06:00",
    "allowedDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
  }
}
```

### 8.3 Connection String Resolution Priority

```
1. CLI --connection-string flag
2. DB_CONNECTION_STRING environment variable
3. Environment JSON file connectionString field
4. Global migration.json connectionString field
```

### 8.4 Environment Variable Expansion

Any `${VAR_NAME}` in config values is replaced with the environment variable value. Missing variables become empty strings.

### 8.5 Path Traversal Protection

Environment names are validated:
- 1-64 characters
- Must match `^[A-Za-z0-9._-]+$`
- Canonical path must resolve within the environments directory

### 8.6 Deployment Windows

`DeploymentWindow.IsWithinWindow(DateTime now, out string reason)`:
- Supports overnight windows (e.g., 22:00–04:00)
- Day-of-week filtering
- Culture-insensitive time parsing

---

## 9. Migration Flows — Step by Step

### 9.1 Deploy (`dbsh migrate`)

```
User runs: dbsh migrate --environment production

1. Program.cs → MigrateCommand
2. CliHost.Create():
   a. Load migration.json
   b. Load environments/production.json
   c. Resolve connection string (CLI > env var > config)
   d. Create PostgreSqlProvider via DatabaseProviderFactory
   e. Wire RelationalMigrationTracker, LockManager, AuditLogger, Executor
   f. Create MigrationExecutor
3. MigrateCommand:
   a. Check deployment window (reject if outside, unless --force)
   b. DryRunAsync → compute pending migrations
   c. Display pending migrations table
   d. Check approval gate (RequireApproval + no Approver → refuse)
   e. Prompt "Apply N migrations? [y/n]"
4. MigrationExecutor.DeployAsync():
   a. Acquire lock: INSERT INTO __migration_lock ... ON CONFLICT DO UPDATE WHERE is_active=false OR expires
   b. Check checksum drift: compare SHA-256 of applied scripts vs on-disk
   c. Compute pending: versioned (not in applied set) + repeatables (hash changed)
   d. For each batch:
      i.   Renew lock (extend expires_at_utc)
      ii.  For each migration:
           - Create MigrationRecord
           - Execute SQL in transaction
           - Record in __migration_history (DELETE old + INSERT new)
   e. Release lock: UPDATE SET is_active = false
   f. Log audit entry: __migration_audit
5. ConsoleHelper displays results
```

### 9.2 Rollback (`dbsh rollback`)

```
1. Discover applied migrations from tracker
2. Resolve targets: specific version or last N
3. Acquire lock (same as deploy)
4. For each target:
   a. Find matching U-prefix script
   b. Execute rollback SQL in transaction
   c. Update status to RolledBack
5. Release lock
6. If any target has no U-script → entire rollback fails
```

### 9.3 Validate (`dbsh validate`)

```
1. Discover all .sql files in scripts path
2. Parse each filename
3. Check: naming conventions, duplicate versions, comment-only scripts, missing dependencies
4. Report: scripts checked, errors, warnings
5. Log audit entry
```

### 9.4 Status (`dbsh status`)

```
1. Fetch all migration records for environment
2. Count by status: Completed, Pending, InProgress, Failed, RolledBack
3. Check for checksum drift (compare stored vs on-disk hashes)
4. Display summary + migration table
```

---

## 10. Test Suite

**Location:** `tests/dbsh.Engine.Tests/`

**Framework:** xunit 2.9.0 + coverlet for coverage

### 10.1 Test Doubles (`TestHelpers.cs`)

| Double | Purpose |
|--------|---------|
| `TempScriptsDirectory` | Creates temp dir with `.WriteScript()`, auto-disposes |
| `FakeScriptExecutor` | Configurable delay, failure injection, execution tracking |
| `FailingScriptExecutor` | Always fails |
| `FakeLockManager` | Controllable `RenewAsync` outcome (simulates lease theft) |
| `ConfigurableEnvironmentProvider` | Wraps in-memory, overrides `RequireApproval` |

### 10.2 Unit Tests (116 tests, 13 classes)

| Class | Tests | What It Tests |
|-------|-------|---------------|
| `ScriptParserTests` | 11 | V/R/U parsing, hash consistency, executable content, dependencies |
| `ScriptParserEdgeCaseTests` | 12 | Missing separator, empty version, false positives, CRLF normalization, category detection |
| `MigrationExecutorTests` | 4 | Basic validate/dry-run/repair/rollback with no data |
| `DeployFlowTests` | 15 | No connection, no scripts, apply all, skip applied, stop-on-failure, lock-lost, approval gates, validation errors |
| `RollbackFlowTests` | 7 | With/without scripts, specific version vs count, lock acquire/release |
| `RepeatableMigrationTests` | 7 | New repeatables, same-hash skip, changed-hash re-apply, ordering, multiple repeatables |
| `ChecksumDriftTests` | 5 | Drift detection in status, deploy refusal, `--force` override, dry-run warnings |
| `DeploymentWindowTests` | 7 | Normal/overnight windows, day-of-week enforcement |
| `LockManagerTests` | 9 | Acquire/release/renew, owner-scoped operations, different environments |
| `ExceptionTypeTests` | 4 | Exception hierarchy validation |
| `ConfigLoaderTests` | 14 | Valid/missing/malformed config, env var expansion, path traversal protection |
| `SqliteRelationalTests` | 17 | Full ADO.NET integration against real SQLite (tracker, lock, audit) |

### 10.3 Integration Tests (54 tests, Docker-backed)

| Class | Tests | Database |
|-------|-------|----------|
| `PostgreSqlRelationalTests` | 17 | PostgreSQL 16 (Testcontainer) |
| `MySqlRelationalTests` | 17 | MySQL 8.0 (Testcontainer) |
| `SqlServerRelationalTests` | 17 | SQL Server 2022 (Testcontainer) |

All inherit from `RelationalProviderContractTests<TFixture>` — shared contract tests run against all 3 real databases. Tagged with `[Trait("Category", "Integration")]`.

### 10.4 Running Tests

```bash
# Unit + SQLite tests (no Docker)
dotnet test --filter "Category!=Integration"

# Docker-backed integration tests (needs Docker)
dotnet test tests/dbsh.Engine.Tests --filter "Category=Integration"
```

---

*This document covers every source file in the dbsh codebase as of version 2.1.3.*
