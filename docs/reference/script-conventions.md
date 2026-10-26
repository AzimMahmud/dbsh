# Script Conventions

dbsh uses Flyway-style naming for migration scripts.

## File patterns

### Versioned migrations (`V` prefix)

```
Database/Migrations/Schema/V001__CreateUsersTable.sql
Database/Migrations/Schema/V002__AddEmailColumn.sql
Database/Migrations/Schema/V20260617120000__CreateOrders.sql
```

- `V` = versioned (applied once)
- Version is a globally-unique, auto-incrementing integer rendered zero-padded (`001`, `002`, ...). `dbsh create` picks the next free number across all type folders.
- A UTC timestamp (`20260617120000`) is available via `dbsh create --timestamp` for teams that prefer date-ordered versions
- `__` (double underscore) separates the version from the name
- The name should be PascalCase, underscores allowed

### Repeatable migrations (`R` prefix)

```
Database/Migrations/Schema/R__RefreshUserView.sql
```

- `R` = repeatable (re-applied every time the checksum changes)
- No version number

### Rollback scripts (`U` prefix)

```
Database/Migrations/Rollback/U001__Rollback_CreateUsersTable.sql
```

- `U` = undo
- The version must match the forward migration's version (without the `V` prefix)
- Paired with their forward counterparts

## Directory structure

```
Database/Migrations/
  Schema/          versioned DDL (V prefix)
  Data/            seed/migration data (V prefix)
  Patch/           hotfix DDL/DML (V prefix)
  Rollback/        undo scripts (U prefix)
```

## Metadata headers

Optional metadata in the SQL file:

```sql
-- Migration: CreateUsersTable
-- Author: jane.doe
-- Created: 2025-01-15
-- Description: Creates the users table
-- Depends: V000__InitialSetup.sql
```

| Header | Required | Description |
|--------|----------|-------------|
| `-- Migration:` | No | Migration name |
| `-- Author:` | No | Who wrote this migration |
| `-- Created:` | No | Creation date |
| `-- Description:` | No | What this migration does |
| `-- Depends:` | No | Comma-separated list of prerequisite scripts |

## Checksums

dbsh computes a SHA-256 checksum for each script. The checksum is computed after normalizing line endings to LF. This ensures cross-platform consistency.

If a previously-applied script is edited in place, the checksum changes, and `dbsh migrate` will refuse to deploy (checksum drift detection). You must either:

1. Create a new migration instead of editing existing ones, or
2. Use `dbsh repair` to clear the history, then re-apply

## States

`dbsh status` reports one state per migration:

| State | Meaning |
|-------|---------|
| `Pending` | Script on disk, not yet applied |
| `Applied` | Applied successfully |
| `Failed` | Applied with errors (re-queue with `dbsh repair`) |
| `RolledBack` | Undo script ran against it |
| `Drifted` | Applied, but the file was edited afterwards |
| `Missing` | Recorded in history, but the script file no longer exists on disk |
