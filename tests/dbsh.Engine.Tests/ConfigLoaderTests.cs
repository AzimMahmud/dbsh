using System.Text.Json;
using dbsh.Core.Exceptions;
using dbsh.Infrastructure.FileSystem;
using Xunit;

namespace dbsh.Engine.Tests;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _envVarsToRestore = new();

    public ConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dbsh-config-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void WriteMigrationConfig(string json)
    {
        var dir = Path.Combine(_tempDir, "Database", "Config");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "migration.json"), json);
    }

    private void WriteEnvironmentConfig(string name, string json)
    {
        var dir = Path.Combine(_tempDir, "Database", "Config", "environments");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), json);
    }

    [Fact]
    public void LoadMigrationConfig_MissingFile_Throws()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<FileNotFoundException>(() => loader.LoadMigrationConfiguration());
    }

    [Fact]
    public void LoadMigrationConfig_MalformedJson_Throws()
    {
        WriteMigrationConfig("{ not valid json !!!");
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<System.Text.Json.JsonException>(() => loader.LoadMigrationConfiguration());
    }

    [Fact]
    public void LoadMigrationConfig_ValidFile_ReturnsValues()
    {
        WriteMigrationConfig(@"{
            ""migration"": {
                ""version"": ""2.1.0"",
                ""database"": { ""provider"": ""sqlserver"", ""connectionString"": ""Server=loc;"" },
                ""scripts"": { ""path"": ""./Db/Up"" },
                ""tracking"": { ""schema"": ""dbo"", ""tableName"": ""migrations"" },
                ""execution"": { ""lockTimeoutSeconds"": 120, ""commandTimeoutSeconds"": 600, ""batchSize"": 5, ""stopOnFailure"": false },
                ""approval"": { ""requireApproval"": [""production""] }
            }
        }");

        var loader = new FileSystemConfigLoader(_tempDir);
        var config = loader.LoadMigrationConfiguration();

        Assert.Equal("2.1.0", config.Version);
        Assert.Equal("sqlserver", config.Provider);
        Assert.Equal("Server=loc;", config.ConnectionString);
        Assert.Equal("./Db/Up", config.ScriptsPath);
        Assert.Equal("dbo", config.TrackingSchema);
        Assert.Equal("migrations", config.TrackingTable);
        Assert.Equal(120, config.LockTimeoutSeconds);
        Assert.Equal(600, config.CommandTimeoutSeconds);
        Assert.Equal(5, config.BatchSize);
        Assert.False(config.StopOnFailure);
        Assert.Contains("production", config.RequireApprovalEnvironments);
    }

    [Fact]
    public void LoadMigrationConfig_MinimalJson_UsesDefaults()
    {
        WriteMigrationConfig(@"{ ""migration"": {} }");

        var loader = new FileSystemConfigLoader(_tempDir);
        var config = loader.LoadMigrationConfiguration();

        Assert.Equal("1.0.0", config.Version);
        Assert.Equal("postgresql", config.Provider);
        Assert.Equal("./Database/Migrations", config.ScriptsPath);
        Assert.Equal("public", config.TrackingSchema);
        Assert.Equal("__migration_history", config.TrackingTable);
        Assert.Equal(300, config.LockTimeoutSeconds);
        Assert.Equal(3600, config.CommandTimeoutSeconds);
        Assert.Equal(10, config.BatchSize);
        Assert.True(config.StopOnFailure);
    }

    [Fact]
    public void LoadMigrationConfig_EnvVarExpansion_ConnectionString()
    {
        Environment.SetEnvironmentVariable("DBSH_TEST_CS", "Host=expanded;Database=test;");
        _envVarsToRestore.Add("DBSH_TEST_CS");

        WriteMigrationConfig(@"{
            ""migration"": {
                ""database"": { ""connectionString"": ""${DBSH_TEST_CS}"" }
            }
        }");

        var loader = new FileSystemConfigLoader(_tempDir);
        var config = loader.LoadMigrationConfiguration();

        Assert.Equal("Host=expanded;Database=test;", config.ConnectionString);
    }

    [Fact]
    public void LoadEnvironment_PathTraversal_DotDot_Throws()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<MigrationConfigurationException>(() => loader.LoadEnvironment("../../etc/passwd"));
    }

    [Fact]
    public void LoadEnvironment_AbsolutePath_Throws()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<MigrationConfigurationException>(() => loader.LoadEnvironment("/etc/passwd"));
    }

    [Fact]
    public void LoadEnvironment_InvalidCharacters_Throws()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<MigrationConfigurationException>(() => loader.LoadEnvironment("prod;rm -rf"));
    }

    [Fact]
    public void LoadEnvironment_TooLong_Throws()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<MigrationConfigurationException>(() => loader.LoadEnvironment(new string('a', 65)));
    }

    [Fact]
    public void LoadEnvironment_MissingFile_Throws()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        Assert.Throws<FileNotFoundException>(() => loader.LoadEnvironment("staging"));
    }

    [Fact]
    public void LoadEnvironment_ValidFile_ReturnsValues()
    {
        WriteEnvironmentConfig("staging", @"{
            ""name"": ""staging"",
            ""database"": { ""host"": ""stg.db"", ""port"": 1433, ""name"": ""stgdb"", ""schema"": ""dbo"" },
            ""migration"": { ""requireApproval"": true, ""allowRollback"": false, ""lockTimeoutSeconds"": 60, ""maxBatchSize"": 3 }
        }");

        var loader = new FileSystemConfigLoader(_tempDir);
        var env = loader.LoadEnvironment("staging");

        Assert.Equal("staging", env.Name);
        Assert.Equal("stg.db", env.Database.Host);
        Assert.Equal(1433, env.Database.Port);
        Assert.Equal("stgdb", env.Database.Name);
        Assert.True(env.Migration.RequireApproval);
        Assert.False(env.Migration.AllowRollback);
        Assert.Equal(60, env.Migration.LockTimeoutSeconds);
        Assert.Equal(3, env.Migration.MaxBatchSize);
    }

    [Fact]
    public void LoadEnvironment_EnvVarExpansion_HostField()
    {
        Environment.SetEnvironmentVariable("DBSH_TEST_HOST", "envhost");
        _envVarsToRestore.Add("DBSH_TEST_HOST");

        WriteEnvironmentConfig("qa", @"{
            ""database"": { ""host"": ""${DBSH_TEST_HOST}"" }
        }");

        var loader = new FileSystemConfigLoader(_tempDir);
        var env = loader.LoadEnvironment("qa");

        Assert.Equal("envhost", env.Database.Host);
    }

    [Fact]
    public void GetAvailableEnvironments_NoDirectory_ReturnsEmpty()
    {
        var loader = new FileSystemConfigLoader(_tempDir);
        var envs = loader.GetAvailableEnvironments();
        Assert.Empty(envs);
    }

    [Fact]
    public void GetAvailableEnvironments_WithFiles_ReturnsSorted()
    {
        WriteEnvironmentConfig("production", @"{}");
        WriteEnvironmentConfig("development", @"{}");
        WriteEnvironmentConfig("qa", @"{}");

        var loader = new FileSystemConfigLoader(_tempDir);
        var envs = loader.GetAvailableEnvironments();

        Assert.Equal(3, envs.Count);
        Assert.Equal("development", envs[0]);
        Assert.Equal("production", envs[1]);
        Assert.Equal("qa", envs[2]);
    }

    [Fact]
    public void LoadEnvironment_ValidSafeName_WithDotsAndUnderscores_Succeeds()
    {
        WriteEnvironmentConfig("env.prod_us-east", @"{}");

        var loader = new FileSystemConfigLoader(_tempDir);
        var env = loader.LoadEnvironment("env.prod_us-east");

        Assert.Equal("env.prod_us-east", env.Name);
    }
}
