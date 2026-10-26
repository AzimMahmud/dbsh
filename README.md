<div align="center">

<img src="./icon.png" alt="dbsh" width="120">

# dbsh

**Database migrations that ship.**

A Flyway-style migration tool for **PostgreSQL**, **SQL Server**, **MySQL**, **SQLite**, **Oracle**, **CockroachDB**, **YugabyteDB**, and **Aurora**.

[![CI](https://github.com/AzimMahmud/dbsh/actions/workflows/ci.yml/badge.svg)](https://github.com/AzimMahmud/dbsh/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/AzimMahmud/dbsh?include_prereleases&color=success)](https://github.com/AzimMahmud/dbsh/releases)
[![NuGet](https://img.shields.io/nuget/v/dbsh?logo=nuget&logoColor=white)](https://www.nuget.org/packages/dbsh)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/AzimMahmud/dbsh/blob/main/LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/AzimMahmud/dbsh/blob/main/CONTRIBUTING.md)

<br>

[Documentation](https://dbsh.azim.me/) · [Report Bug](https://github.com/AzimMahmud/dbsh/issues) · [Request Feature](https://github.com/AzimMahmud/dbsh/issues)

</div>

---

## What is dbsh?

dbsh is a SQL-first database migration tool for .NET. You write plain `.sql` files, and dbsh tracks, validates, and applies them across environments — with built-in safety features for production use.

**Why not Entity Framework migrations?** EF migrations are tightly coupled to your application code and hard to control in production. dbsh keeps migrations as standalone SQL files that live in your repository, work with any language or framework, and give you full control over what runs and when.

## Features

- **SQL-first** — Plain `.sql` files. No embedded DSL, no XML, no surprises.
- **Flyway-style versioning** — `dbsh create` assigns the next auto-incrementing version (`001`, `002`, ...) across all script folders. Timestamps are opt-in.
- **Live progress** — Migrations stream one line each (`✓ 001 CreateUsersTable (0.12s)`), Flyway/Django style.
- **Multi-database** — PostgreSQL, SQL Server, MySQL, SQLite, Oracle, CockroachDB, YugabyteDB, and Aurora. Switch providers without changing your workflow.
- **Safe by design** — Distributed locks, approval gates, deployment windows, and audit trails.
- **CI-friendly** — Every command supports `--json` output and deterministic exit codes.
- **Works offline** — Validate, plan, and scaffold without a database connection.
- **Checksum integrity** — SHA-256 checksums detect edited scripts (`Drifted`) and deleted scripts (`Missing`) in `dbsh status` and `dbsh plan`.

## Quick Start

```bash
# Install (no .NET required)
curl -fsSL https://github.com/AzimMahmud/dbsh/releases/latest/download/install.sh | bash

# Scaffold a project
dbsh new --name MyApp --provider postgresql

# Create a migration (version picked automatically: V001, V002, ...)
dbsh create --name CreateUsersTable

# Validate & preview
dbsh validate
dbsh plan

# Deploy
dbsh migrate -c "Host=localhost;Database=myapp;Username=postgres"

# Undo — fails fast if a rollback script is missing
dbsh rollback --target-version 002
```

## Installation

No .NET SDK or runtime required — the binary is self-contained.

```bash
# Linux / macOS
curl -fsSL https://github.com/AzimMahmud/dbsh/releases/latest/download/install.sh | bash
```

```powershell
# Windows (PowerShell)
powershell -c "iwr -Uri https://github.com/AzimMahmud/dbsh/releases/latest/download/install.ps1 | iex"
```

```bash
# .NET global tool (requires .NET 10 SDK)
dotnet tool install --global dbsh
```

### Manual Download

| Platform | Download |
|----------|----------|
| Windows x64 | [`dbsh-windows-x64.zip`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-windows-x64.zip) |
| Windows ARM64 | [`dbsh-windows-arm64.zip`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-windows-arm64.zip) |
| Linux x64 | [`dbsh-linux-x64.tar.gz`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-linux-x64.tar.gz) |
| Linux ARM64 | [`dbsh-linux-arm64.tar.gz`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-linux-arm64.tar.gz) |
| Linux musl x64 (Alpine) | [`dbsh-linux-musl-x64.tar.gz`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-linux-musl-x64.tar.gz) |
| macOS x64 | [`dbsh-macos-x64.tar.gz`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-macos-x64.tar.gz) |
| macOS ARM64 | [`dbsh-macos-arm64.tar.gz`](https://github.com/AzimMahmud/dbsh/releases/latest/download/dbsh-macos-arm64.tar.gz) |

## Commands

| Command | Description | DB Required |
|---------|-------------|:-----------:|
| [`new`](https://dbsh.azim.me/commands/new) | Scaffold a complete project | No |
| [`create`](https://dbsh.azim.me/commands/create) | Create a migration script | No |
| [`validate`](https://dbsh.azim.me/commands/validate) | Check scripts for errors | No |
| [`plan`](https://dbsh.azim.me/commands/plan) | Preview execution plan | No |
| [`info`](https://dbsh.azim.me/commands/info) | Show configuration | No |
| [`init`](https://dbsh.azim.me/commands/init) | Create tracking tables | Yes |
| [`migrate`](https://dbsh.azim.me/commands/migrate) | Apply pending migrations | Yes |
| [`status`](https://dbsh.azim.me/commands/status) | Show migration status | Yes |
| [`rollback`](https://dbsh.azim.me/commands/rollback) | Undo migrations | Yes |
| [`repair`](https://dbsh.azim.me/commands/repair) | Fix failed migrations | Yes |
| [`history`](https://dbsh.azim.me/commands/history) | View audit trail | Yes |

Run `dbsh <command> --help` for command-specific options.

## Configuration

dbsh uses a two-tier JSON configuration:

```jsonc
// Database/Config/migration.json
{
  "migration": {
    "database": {
      "provider": "postgresql",
      "connectionString": "${DB_CONNECTION_STRING}"
    },
    "scripts": {
      "path": "./Database/Migrations"
    },
    "execution": {
      "batchSize": 10,
      "stopOnFailure": true
    }
  }
}
```

Per-environment overrides in `Database/Config/environments/<name>.json` with `${VAR}` expansion for secrets. See the [Configuration Guide](https://dbsh.azim.me/guide/configuration) for details.

## Supported Databases

| Provider | Version | Config Value |
|----------|---------|--------------|
| PostgreSQL | 12+ | `postgresql` |
| SQL Server | 2016+ | `sqlserver` |
| MySQL / MariaDB | 8+ / 10.5+ | `mysql` |
| SQLite | 3 | `sqlite` |
| Oracle | 19+ | `oracle` |
| CockroachDB | 22+ | `cockroachdb` |
| YugabyteDB | 2.14+ | `yugabyte` |
| Amazon Aurora | — | `aurora` |

## Documentation

Full documentation is available at **[dbsh.azim.me](https://dbsh.azim.me/)**.

## Building from Source

```bash
git clone https://github.com/AzimMahmud/dbsh.git
cd dbsh
dotnet build dbsh.slnx
dotnet test dbsh.slnx --filter "Category!=Integration"
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](https://github.com/AzimMahmud/dbsh/blob/main/CONTRIBUTING.md) for guidelines.

## License

[MIT](https://github.com/AzimMahmud/dbsh/blob/main/LICENSE)
