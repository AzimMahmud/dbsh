# AGENTS.md

## Build

```bash
# Full solution (all projects)
dotnet build dbsh.slnx

# Single project
dotnet build src/dbsh.CLI/dbsh.CLI.csproj
```

`TreatWarningsAsErrors` is enabled globally via `Directory.Build.props`. Zero warnings required.

Target framework: `net10.0`.

## Test

```bash
# Unit + SQLite integration tests (no external services)
dotnet test --filter "Category!=Integration"

# Real-database integration tests (Postgres/MySQL/SQL Server via Testcontainers, needs Docker)
dotnet test tests/dbsh.Engine.Tests --filter "Category=Integration"
```

Test count: 140 unit/SQLite tests across 16 test classes, plus 54 Docker-backed integration tests in `tests/dbsh.Engine.Tests/Integration/`.

## Lint / Format

No separate lint step — `TreatWarningsAsErrors` serves as the gate. The build is the lint.

## Project structure

| Project | Responsibility |
|---------|---------------|
| `dbsh.Core` | Entities, enums, value objects, interfaces. Zero dependencies. |
| `dbsh.Engine` | `ScriptParser`, `MigrationExecutor`, in-memory test doubles. |
| `dbsh.Infrastructure` | 5 database providers (PostgreSQL, SqlServer, MySQL, SQLite, Oracle), relational implementations, `FileSystemConfigLoader`. |
| `dbsh.CLI` | `dbsh` executable using `Spectre.Console.Cli` 0.49.1. |

## Key conventions

- Custom exceptions in `dbsh.Core/Exceptions/dbshException.cs`: `dbshException` (base), `ScriptParseException`, `MigrationConfigurationException`, `UnsupportedProviderException`.
- In-memory test doubles in `dbsh.Engine/InMemory/InMemoryImplementations.cs`.
- Migration scripts: `V<digits>__Name.sql` (versioned), `R__Name.sql` (repeatable), `U<digits>__Name.sql` (rollback).
- SHA-256 hashing with LF normalization for checksums.
- 3 tracking tables: `__migration_history`, `__migration_lock`, `__migration_audit`.
