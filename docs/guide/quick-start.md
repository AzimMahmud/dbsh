# Quick Start

From zero to your first migration in 60 seconds.

## 1. Scaffold a project

::: code-group

```bash [Interactive]
dbsh new
```

```bash [Non-interactive]
dbsh new --name MyApp --provider postgresql
```

```bash [With output directory]
dbsh new --name MyApp --provider sqlserver --output ./my-db-project
```

:::

When called without flags, `dbsh new` enters interactive mode — it prompts for your project name, lets you select the database provider, and asks whether to use the current directory.

This creates:

```
Database/
  Config/
    migration.json                 # global config (edit connection string)
    environments/
      local.json                   # dev defaults, approval off
      development.json             # CI/CD friendly
      staging.json                 # gated environment
      production.json              # approval + deployment window
  Migrations/
    Schema/
      V001__Example_Users.sql      # runnable example (provider-specific SQL)
    Data/
    Patch/
    Rollback/
      U001__Example_Users.sql      # example rollback
  Templates/                       # used by dbsh create
.github/workflows/
  database-migration.yml           # GitHub Actions CI pipeline
.gitignore
```

## 2. Create a migration

```bash
dbsh create --name CreateUsersTable --type schema --author jane
```

This creates `Database/Migrations/Schema/V002__CreateUsersTable.sql` (the scaffold's example migration is `V001`; the next number is picked automatically):

```sql
-- Migration: CreateUsersTable
-- Author: jane
-- Created: 2026-06-17

-- TODO: add your SQL here
```

## 3. Write SQL

Replace the TODO with your actual DDL:

```sql
-- Migration: CreateUsersTable
-- Author: jane
-- Created: 2026-06-17
-- Description: Creates the users table

CREATE TABLE users (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email       VARCHAR(255) NOT NULL UNIQUE,
    name        VARCHAR(100) NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_email ON users (email);
```

## 4. Validate

```bash
dbsh validate
```

Checks naming conventions, duplicate versions, missing dependencies, and required metadata fields. No database required.

## 5. Preview the plan

```bash
dbsh plan
```

Shows exactly what will run and in what order. No database required.

## 6. Deploy

```bash
# Set your connection string
export DB_CONNECTION_STRING="Host=localhost;Database=myapp;Username=postgres"

# Create tracking tables (once per database)
dbsh init -c "$DB_CONNECTION_STRING"

# Apply pending migrations
dbsh migrate -c "$DB_CONNECTION_STRING"
```

## 7. Check status

```bash
dbsh status
```

Shows a summary and detailed table of all migrations with their status.

## 8. Rollback

```bash
dbsh rollback --count 1
```

Requires a matching `U` script in `Database/Migrations/Rollback/`.

## What's next?

- Learn about [configuration options](/guide/configuration)
- Browse [all commands](/commands/new)
- Set up [CI/CD integration](/reference/ci-cd)
