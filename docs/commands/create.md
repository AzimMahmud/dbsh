# create

Create a new migration script with the next auto-incrementing version.

**Database required:** No

## Usage

```bash
# Schema migration - version picked automatically (001, 002, ...)
dbsh create --name CreateUsersTable

# Schema migration with an author
dbsh create --name CreateUsersTable --author jane

# Data migration (version numbering is global across all type folders)
dbsh create --name SeedRoles --type data

# Patch migration
dbsh create --name FixIndexes --type patch

# Rollback script - targets the latest version without a rollback
dbsh create --name Rollback_CreateUsersTable --type rollback

# Rollback script for a specific version
dbsh create --name Rollback_CreateUsersTable --type rollback --for-version 003

# Repeatable script (re-runs whenever its content changes)
dbsh create --name RefreshUserView --type repeatable

# Opt out of auto-incrementing versions and use a timestamp instead
dbsh create --name CreateOrders --timestamp
```

## Options

| Option | Short | Description |
|--------|-------|-------------|
| `--name` | `-n` | **(Required)** Migration name in PascalCase |
| `--type` | `-t` | Migration type: `schema` (default), `data`, `patch`, `rollback`, `repeatable` |
| `--author` | `-a` | Author name for the metadata header |
| `--description` | `-d` | Short description for the metadata header |
| `--output` | `-o` | Override output directory |
| `--for-version` | `-V` | Version to target when `--type rollback` (default: latest version without a rollback) |
| `--timestamp` | | Use a timestamp version (`V20260617120000`) instead of the auto-incrementing number |
| `--sequence` | `-s` | *(Deprecated)* Auto-incrementing versions are now the default |

## Version numbering

Versions are simple, globally-unique integers rendered zero-padded to three
digits - the Flyway convention. The next version is computed from the highest
version found anywhere under `Database/Migrations` (all type folders), so a
data migration created after `Schema/V003` becomes `V004__Name.sql` and never
collides with the `(version, environment)` uniqueness rule of the tracking schema.

## Generated filenames

| Type | Pattern | Example |
|------|---------|---------|
| schema/data/patch | `V<version>__<Name>.sql` | `V004__CreateUsers.sql` |
| schema (`--timestamp`) | `V<timestamp>__<Name>.sql` | `V20260617120000__CreateUsers.sql` |
| rollback | `U<version>__Rollback_<Name>.sql` | `U003__Rollback_CreateUsers.sql` |
| repeatable | `R__<Name>.sql` | `R__RefreshUserView.sql` |

## Generated file content

```sql
-- Migration: CreateOrders
-- Author: jane
-- Created: 2026-06-17
-- Description: Orders and order_items tables

-- TODO: add your SQL here
```
