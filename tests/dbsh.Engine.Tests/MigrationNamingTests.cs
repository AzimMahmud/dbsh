using dbsh.Engine.Parsing;
using Xunit;

namespace dbsh.Engine.Tests;

public class MigrationNamingTests
{
    [Fact]
    public void NextVersion_EmptyDirectory_Returns001()
    {
        using var dir = new TempScriptsDirectory();
        Assert.Equal("001", MigrationNaming.NextVersion(dir.Path));
    }

    [Fact]
    public void NextVersion_MissingDirectory_Returns001()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dbsh-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Equal("001", MigrationNaming.NextVersion(path));
    }

    [Fact]
    public void NextVersion_ScansAllLocationFolders()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V003__AddIndex.sql", "CREATE INDEX i ON t (id);");
        dir.WriteScript("Data/V002__Seed.sql", "INSERT INTO t VALUES (1);");

        Assert.Equal("004", MigrationNaming.NextVersion(dir.Path));
    }

    [Fact]
    public void NextVersion_PadsToThreeDigits()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V7__AddColumn.sql", "ALTER TABLE t ADD c INT;");

        Assert.Equal("008", MigrationNaming.NextVersion(dir.Path));
    }

    [Fact]
    public void NextVersion_IgnoresTimestampsRollbacksAndRepeatables()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V20260101000000__Timestamped.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Rollback/U20260101000000__Rollback_Timestamped.sql", "DROP TABLE t;");
        dir.WriteScript("Schema/R__RefreshView.sql", "CREATE VIEW v AS SELECT 1;");

        Assert.Equal("001", MigrationNaming.NextVersion(dir.Path));
    }

    [Fact]
    public void NextVersion_SkipsOutOfWorkingRangeSequences()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V999999__TooBig.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V004__Real.sql", "CREATE TABLE t (id INT);");

        Assert.Equal("005", MigrationNaming.NextVersion(dir.Path));
    }

    [Fact]
    public void FindVersionMissingRollback_ReturnsHighestUncoveredVersion()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V002__AddColumn.sql", "ALTER TABLE t ADD c INT;");
        dir.WriteScript("Schema/V003__AddIndex.sql", "CREATE INDEX i ON t (c);");
        dir.WriteScript("Rollback/U003__Rollback_AddIndex.sql", "DROP INDEX i;");

        Assert.Equal("002", MigrationNaming.FindVersionMissingRollback(dir.Path));
    }

    [Fact]
    public void FindVersionMissingRollback_AllCovered_ReturnsNull()
    {
        using var dir = new TempScriptsDirectory();
        dir.WriteScript("Schema/V001__CreateTable.sql", "CREATE TABLE t (id INT);");
        dir.WriteScript("Schema/V002__AddColumn.sql", "ALTER TABLE t ADD c INT;");
        dir.WriteScript("Rollback/U001__Rollback_CreateTable.sql", "DROP TABLE t;");
        dir.WriteScript("Rollback/U002__Rollback_AddColumn.sql", "ALTER TABLE t DROP COLUMN c;");

        Assert.Null(MigrationNaming.FindVersionMissingRollback(dir.Path));
    }

    [Fact]
    public void FindVersionMissingRollback_NoScripts_ReturnsNull()
    {
        using var dir = new TempScriptsDirectory();
        Assert.Null(MigrationNaming.FindVersionMissingRollback(dir.Path));
    }

    [Theory]
    [InlineData(1, "001")]
    [InlineData(42, "042")]
    [InlineData(1234, "1234")]
    public void FormatVersion_PadsToThreeDigits(int input, string expected)
    {
        Assert.Equal(expected, MigrationNaming.FormatVersion(input));
    }
}
