# Changelog

All notable changes to the dbsh project will be documented in this file.

## [Unreleased]

## [2.1.5] - 2026-09-06

### Fixed

- **SQLite module support** — `dbsh -m <module>` with SQLite now throws a clear error instead of creating invalid table names (`sqlite____migration_history`) that SQLite rejects. SQLite does not support database schemas; use PostgreSQL or SQL Server for multi-module setups.
- **`-m` flag path resolution** — fixed double-append bug where `-m postgresql` with `scripts.path` already pointing to the `postgresql` subdirectory would resolve to `.../postgresql/postgresql/`, causing zero scripts to be found.
- **Install script CRLF** — added `.gitattributes` to enforce LF line endings for shell scripts, preventing the `'\r': command not found` error when `install.sh` is downloaded from GitHub releases with Windows line endings.

## [2.1.4] - 2026-08-29

### Fixed

- **Documentation accuracy** — updated all docs and CLI help text to list all 8 supported databases (PostgreSQL, SQL Server, MySQL, SQLite, Oracle, CockroachDB, YugabyteDB, Aurora). Previously only 4 were listed in README, architecture, tracking tables, and `--provider` help.

## [2.1.3] - 2026-08-26

### Fixed

- **Unicode encoding** - fixed broken UTF-8 characters in install scripts (box-drawing characters, em dashes, status icons) that caused rendering issues on many terminals.
- **Cross-platform compatibility** - updated install.sh and install.ps1 to use ASCII-compatible box-drawing characters and status icons for better terminal rendering across different platforms.
- **Release packaging** - improved archive generation to include properly encoded install scripts in all release packages.

### Changed

- **Status icons** - replaced Unicode checkmark/warning symbols with ASCII-friendly `[OK]`, `[!]`, `[ERROR]` indicators for maximum compatibility.

## [2.1.2] - 2026-08-25

### Fixed

- **Unicode encoding** - fixed broken UTF-8 characters in install scripts (box-drawing characters, em dashes, status icons) that caused rendering issues on many terminals.
- **Cross-platform compatibility** - updated install.sh and install.ps1 to use ASCII-compatible box-drawing characters and status icons for better terminal rendering across different platforms.
- **Release packaging** - improved archive generation to include properly encoded install scripts in all release packages.

### Changed

- **Status icons** - replaced Unicode checkmark/warning symbols with ASCII-friendly `[OK]`, `[!]`, `[ERROR]` indicators for maximum compatibility.

## [2.1.1] - 2026-08-25

### Changed

- **Custom domain** - documentation site now served from `https://dbsh.azim.me/` (root base `/`) instead of the GitHub Pages project path.
- **`CNAME` file** - added to the published docs root so the custom domain persists across deploys.
- **Favicon** - browser tab icon path fixed to `/icon.png` after the base-path change.

## [2.1.0] - 2026-08-24

### Added

- **Windows ARM64 build** - dbsh-windows-arm64.zip now included in releases.
- **Alpine Linux (musl) build** - dbsh-linux-musl-x64.tar.gz now included in releases for Alpine and musl-based distros.
- **macOS Gatekeeper auto-fix** - install script now automatically removes the quarantine attribute so dbsh runs immediately after install.
- **Windows Git Bash/WSL detection** - install script detects Windows environments and directs users to install.ps1 instead of failing with "Unsupported OS".
- **New logo assets** - dark/light variants at 64x64, 128x128, 256x256, and 512x512 sizes in dbsh_logos/ folder. Updated root logo.png, docs favicon, and NuGet package icon.

### Fixed

- **Version mismatch** - dbsh --version now reads the version from assembly metadata instead of a hardcoded string, ensuring it always matches the installed version.
- **Mojibake in docs** - fixed broken UTF-8 characters (em dashes, middle dots, box-drawing chars) across all documentation files.

### Changed

- **Docs nav logo** - icon + gradient "dbsh" text, dark/light theme swap.
- **Browser favicon** - uses square icon instead of full logo.
- **NuGet package icon** - updated to 256px light variant with `<RepositoryType>git</RepositoryType>` for repo linking.
## [2.0.1] — 2026-08-23

### Added

- **VitePress documentation site** — full documentation site deployed to GitHub Pages via GitHub Actions, covering installation, all commands, configuration, multi-database setup, script conventions, tracking tables, architecture, and CI/CD integration.
- **Custom light/dark theme** — branded VitePress theme with teal-blue gradient palette, styled nav, feature cards, and sidebar.
- **GitHub Pages deployment workflow** — `.github/workflows/docs.yml` auto-deploys the docs site on push to `main`.
- **`gh-pages` branch** — manual deployment option via `npm run docs:deploy`.

### Changed

- **README rewritten** — condensed from 608 to 150 lines with a professional structure: centered header, feature highlights, command table with docs links, and clear installation instructions.
- **Nav design improved** — icon-only logo in nav bar, styled hover/active states, border divider.

### Fixed

- **Favicon** — now uses the square icon SVG instead of the full-width logo, displaying correctly in browser tabs.

## [2.0.0] — 2026-08-22

### Added

- **Real-database integration tests** — 54 new tests in `tests/dbsh.Engine.Tests/Integration/` run the `RelationalMigrationTracker`/`RelationalMigrationLockManager`/`RelationalAuditLogger` contract against live PostgreSQL, MySQL, and SQL Server containers via Testcontainers, closing the gap where only SQLite had real-database coverage. Tagged `Category=Integration`; requires Docker and is excluded from the default `dotnet test` run.
- **CodeQL security scanning** — `.github/workflows/codeql.yml` runs `security-and-quality` analysis on push/PR to `main` and weekly on a schedule.
- **`install.sh --uninstall` / `install.ps1 -Uninstall`** — both installer scripts now double as uninstallers: remove the binary and strip the PATH entry they added (`# Added by dbsh installer` block in `.zshrc`/`.bashrc`, or the matching entry in the Windows user `PATH`). Idempotent — safe to run when nothing is installed. `install.sh` also accepts `UNINSTALL=1` as an env-var alternative to the `--uninstall`/`-u` flag, consistent with its existing `REPO`/`VERSION`/`INSTALL_DIR` overrides.
- **README: Updating / Uninstalling sections** — documents re-running the installer to upgrade in place (verified: no duplicate PATH entry, clean binary overwrite), the new automated uninstall commands, a `which -a dbsh` / `where.exe dbsh` tip for detecting duplicate installs across different directories, and how to handle a system-wide (root-owned) install.
- **Uninstall now fails gracefully on permission-denied** — `install.sh --uninstall` against a root-owned/system-wide install previously crashed with a raw `rm: Permission denied`; it now reports a clear message with the exact `sudo rm ...` command to run. `install.ps1 -Uninstall` gets the equivalent fix (points to running an elevated PowerShell).
- **NuGet package icon** — `.github/assets/icon.png` (128×128, rendered from the existing `icon.svg`), wired up via `<PackageIcon>` in `dbsh.CLI.csproj`. Package ID `dbsh` confirmed available on NuGet.org. Verified end-to-end: packed, installed as a real global tool from the local `.nupkg` (`dotnet tool install --global dbsh --add-source ...`), ran `dbsh --version`/`--help` successfully.

### Changed

- **`ci.yml`** — added a dedicated `integration-tests` job (ubuntu-latest) for the new Testcontainers-backed suite; the cross-OS `build` matrix now excludes `Category=Integration` from its `dotnet test` run; added an explicit least-privilege `permissions: contents: read` block.

### Breaking

- **Dropped net6.0/net8.0 targets — net10.0 only.** `Directory.Build.props` and every workflow now target `net10.0` exclusively (previously multi-targeted `net6.0;net8.0;net10.0` for broad `dotnet tool install` compatibility). Building from source, running as a `dotnet tool`, and CI all require the **.NET 10 SDK/runtime**. `global.json` now pins `10.0.100` with `latestFeature` roll-forward (was `6.0.100`/`latestMajor`). The PolySharp polyfill dependency (needed only for net6.0) was removed. Self-contained release binaries are unaffected — they already bundled `net10.0`.

### Fixed

- **Interactive banner showed .NET runtime/OS info** — `dbsh new`'s interactive wizard printed the .NET `FrameworkDescription` and `OSDescription` on every run via `ConsoleHelper.PrintBanner()`. Removed; the banner now shows only branding and supported providers.
- **Concurrent deploys possible after a lock lease expired mid-deploy** — `MigrationExecutor.DeployAsync` renewed the distributed lock before each batch but discarded the `bool` result; if the lease had already expired and been stolen by another process, the deploy kept executing and recording migrations with no lock held at all, letting two `dbsh migrate` runs race against the same environment. Now stops immediately and reports the lock loss instead. Covered by `DeployFlowTests.Deploy_LockLostBetweenBatches_StopsAndReportsFailure`.
- **Non-`DbException` faults crashed the whole deploy instead of failing one migration** — `RelationalMigrationExecutor.ExecuteAsync` only caught `DbException`, so e.g. cancellation-adjacent provider faults propagated uncaught out of `DeployAsync`, skipping the structured `DeployResult` entirely. Broadened to catch any non-cancellation exception; genuine cancellation (`OperationCanceledException`) still propagates so it isn't misrecorded as a migration failure.
- **`install.sh` always exited 1 despite a successful install** — `workdir` was declared `local` inside `download_release`, but the `trap 'rm -rf "$workdir"' EXIT` fires after the function returns, so under `set -u` the trap failed with `workdir: unbound variable` on every run. `workdir` is no longer `local`.
- **`install.ps1` printed no status text** — `Info`/`Ok`/`Warn`/`Err` referenced `$_` without declaring a parameter, so every call like `Info "Detected: $platform"` printed just the icon with nothing after it (the argument was never bound to `$_` outside a pipeline). All four now take an explicit `$Message` parameter.
- **README links/images that would break on the NuGet.org package page** — the embedded README is rendered standalone there (nothing else from the repo ships in the package), so relative links (`docs/USAGE.md`, `LICENSE`) and the relative logo `<img src=".github/assets/logo.svg">` all resolved to nothing. Converted to absolute GitHub URLs. Also fixed a stale `.NET 6.0 | 8.0 | 10.0` badge (left over from the net10-only migration) with a dead `()` link.

## [1.1.0] — 2026-07-30

### Added

- **`--verbose` flag** — `dbsh migrate/rollback/status/create/repair --verbose` shows `Information`-level log messages (script execution, checksums, lock acquisition). Propagated via `GlobalSettings.Verbose` → `CliHostOptions.Verbose` → `SpectreLogger<T>`.
- **`strictAudit` mode** — `MigrationExecutor` constructor accepts a `strictAudit` parameter; when `true`, `AuditSafe()` re-throws audit failures instead of silently swallowing them.
- **`InvalidateCache()`** — `MigrationExecutor.InvalidateCache()` forces re-reading migration scripts from disk on the next operation.
- **`ParseAsync(string, CancellationToken)`** — `ScriptParser.ParseAsync(filePath, ct)` for async file I/O with cancellation support.
- **`DiscoverAllCoreAsync(CancellationToken)`** — `MigrationExecutor.DiscoverAllCoreAsync(ct)` for async script discovery.
- **`ProviderSqlHelper`** — extracted 7 provider-specific SQL template methods from `NewCommand.cs` (~467→~372 lines), reducing duplication.
- **SQLite integration tests** — 16 new tests in `SqliteRelationalTests.cs` covering `RelationalMigrationTracker`, `RelationalMigrationLockManager`, and `RelationalAuditLogger` against a real SQLite file database.
- **Enhanced test doubles** — `FakeScriptExecutor` now supports configurable delay, `failOnSqlContaining` trigger, `ExecutionCount` and `ExecutedSql` tracking. `FailingScriptExecutor` tracks `ExecutionCount`.

### Changed

- **`OrderKey` doc comment** — added explanation of zero-padded version ordering safety.
- **Self-contained binary framework** — release builds now target `net10.0` (was `net8.0`).

### Fixed

- **SQLite lock manager** — `RelationalMigrationLockManager.ReleaseAsync` and `RenewAsync` were missing the `@true` parameter binding, causing `SqliteException` at runtime. Only SQLite was affected; other providers ignored the unused parameter.

## [1.0.0] — 2026-06-17

### Added

#### Professional tooling
- **CI workflow** (`.github/workflows/ci.yml`) — build, test, code coverage on ubuntu/windows/macos for every push and PR. Produces NuGet packages on main pushes.
- **Release workflow** (`.github/workflows/release.yml`) — triggered by `v*` tags. Builds self-contained binaries for 5 platforms (win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64), packages as `.zip`/`.tar.gz`, publishes to NuGet, creates a GitHub Release with release notes and all artifacts.
- **Dependabot** (`.github/dependabot.yml`) — weekly dependency updates for NuGet and GitHub Actions.
- **Issue templates** — structured bug report and feature request forms via `.github/ISSUE_TEMPLATE/`.
- **PR template** (`.github/pull_request_template.md`) — checklist for contributors covering build, tests, changelog, and docs.
- **EditorConfig** (`.editorconfig`) — consistent indentation and line endings across all file types.
- **CONTRIBUTING.md** — development setup, code conventions, commit style, and PR process.
- **CODE_OF_CONDUCT.md** — standard Contributor Covenant v2.1.
- **SECURITY.md** — vulnerability reporting process and supported versions.
- **Install scripts** — `install.sh` (Linux/macOS curl-bash) and `install.ps1` (Windows iwr-iex). Auto-detect platform, download from GitHub Releases, install to PATH.
- **Build scripts** — `publish.sh` (Linux/macOS) and `publish.ps1` (Windows) for building self-contained binaries locally.
- `dist/` added to `.gitignore`.

#### CLI — project scaffolding
- `dbsh new` — interactive project scaffold. Run without flags to be prompted for project name, database provider, and output directory. Creates the full directory tree, config files, per-environment settings, example migrations (provider-specific SQL), templates, `.gitignore`, and a GitHub Actions CI pipeline.
- `scaffold` / `init-project` aliases for `new`.

#### CLI — command-specific help
- `dbsh <command> --help` now shows options specific to that command, plus the global options (without duplication).

#### CLI — onboarding screen
- `dbsh` (no arguments) now shows a "Quick start" panel with the three most common commands before the full help table.

#### Multi-database provider support
- `IDatabaseProvider` interface with four implementations:
  - `PostgreSqlProvider` — PostgreSQL 12+ (Npgsql)
  - `SqlServerProvider` — SQL Server 2016+ (Microsoft.Data.SqlClient)
  - `MySqlProvider` — MySQL 8+ / MariaDB 10.5+ (MySqlConnector)
  - `SqliteProvider` — SQLite 3 (Microsoft.Data.Sqlite)
- `DatabaseProviderFactory` — resolves the correct provider by string alias.
- Provider override via `--provider` CLI flag or `migration.json → database.provider`.

#### Provider-agnostic infrastructure
- `RelationalMigrationTracker` — DELETE+INSERT upsert pattern (works on all four engines).
- `RelationalMigrationLockManager` — C# date math for lock expiry (no provider-specific SQL).
- `RelationalMigrationExecutor` — transaction-bound SQL execution via `System.Data.Common`.
- `RelationalAuditLogger` — parameterized INSERT for audit trail.
- `ConfigEnvironmentProvider` — environment config from JSON files.

#### Professional project assets
- `README.md` — comprehensive GitHub open-source README with badges, Quick Start, command table, script conventions, config reference, architecture diagram, multi-database explanation, and installation guide.
- `LICENSE` — MIT license.
- `CHANGELOG.md` — this file.
- `Directory.Build.props` — package metadata (authors, copyright, license).
- `docs/USAGE.md` — complete end-to-end usage guide covering installation, setup, migrations, rollbacks, multi-database, CI/CD, approval gates, deployment windows, and troubleshooting.

### Changed

#### Renamed from "DatabaseMigrationPlatform" (dbpilot) to "dbsh"
- Solution: `dbsh.sln`
- Source projects: `dbsh.Core`, `dbsh.Engine`, `dbsh.Infrastructure`, `dbsh.CLI`
- Test project: `dbsh.Engine.Tests`
- Tool command: `dbsh` (was `migration`)
- Package: `dbsh` (was `dbpilot`)
- All namespaces, directories, and project references updated.

#### Configuration
- `migration.json` — added `database.provider` field.
- Environment JSON files — added to `Database/Config/environments/`.
- Connection string resolution: `--connection-string` > `DB_CONNECTION_STRING` env var > config file.

### Removed

- All PostgreSQL-specific infrastructure classes:
  - `PostgresMigrationTracker.cs`
  - `PostgresMigrationLockManager.cs`
  - `PostgresMigrationExecutor.cs`
  - `PostgresAuditLogger.cs`
  - `PostgresEnvironmentProvider.cs`
  - `TrackingSchema.cs`
- Empty directories: `Engine/Rollback/`, `Engine/Tracking/`, `Engine/Validation/`, `Infrastructure/Git/`, `CLI/Output/`, `docs/` (old).
- Old `README.md` and `LICENSE` files (replaced).

### Fixed

- `dbsh <command> --help` now correctly shows command-specific help (was showing global help for all commands).
- Help output no longer duplicates global options when showing command-specific help.
- JSON output is now emitted cleanly without decorative UI text.
- Migration template files generate correct `{{NAME}}` placeholders (used by `dbsh create`).

### Security

- No connection strings, secrets, or tokens are stored in the repository.
- All connection strings use environment variable expansion (`${VAR}`).
