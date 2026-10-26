using System.Globalization;
using System.Text.RegularExpressions;

namespace dbsh.Engine.Parsing;

/// <summary>
/// Version-number calculations shared by the CLI scaffolding commands.
/// Versions are simple, globally-unique integers rendered zero-padded to three
/// digits (001, 002, ...) - the Flyway convention. The next version is derived
/// from the highest version found anywhere under the scripts path so that the
/// (version, environment) uniqueness contract of the tracking schema can never
/// be violated by scaffolding a script into a different location folder.
/// </summary>
public static partial class MigrationNaming
{
    /// <summary>Above this value a version is treated as a timestamp, not a sequence.</summary>
    public const int MaxSequenceVersion = 99999;

    [GeneratedRegex(@"^[Vv](\d+)__")]
    private static partial Regex ForwardRegex();

    [GeneratedRegex(@"^[Uu](\d+)__")]
    private static partial Regex RollbackRegex();

    /// <summary>Returns the next forward version (zero-padded) across all location folders.</summary>
    public static string NextVersion(string scriptsPath)
    {
        var max = 0;
        foreach (var version in ScanForwardVersions(scriptsPath))
        {
            if (version > max)
            {
                max = version;
            }
        }

        return FormatVersion(max + 1);
    }

    /// <summary>
    /// Returns the highest forward version that has no matching rollback (U) script,
    /// or null when every forward version already has one.
    /// </summary>
    public static string? FindVersionMissingRollback(string scriptsPath)
    {
        var rollbacks = new HashSet<int>();
        foreach (var file in EnumerateScripts(scriptsPath))
        {
            var match = RollbackRegex().Match(Path.GetFileNameWithoutExtension(file));
            if (match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var u)
                && u <= MaxSequenceVersion)
            {
                rollbacks.Add(u);
            }
        }

        foreach (var version in ScanForwardVersions(scriptsPath).OrderDescending())
        {
            if (!rollbacks.Contains(version))
            {
                return FormatVersion(version);
            }
        }

        return null;
    }

    /// <summary>Renders a version number zero-padded to three digits (Flyway-style display).</summary>
    public static string FormatVersion(int version) => version.ToString("000", CultureInfo.InvariantCulture);

    private static IEnumerable<int> ScanForwardVersions(string scriptsPath)
    {
        foreach (var file in EnumerateScripts(scriptsPath))
        {
            var match = ForwardRegex().Match(Path.GetFileNameWithoutExtension(file));
            if (match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
                && v <= MaxSequenceVersion)
            {
                yield return v;
            }
        }
    }

    private static IEnumerable<string> EnumerateScripts(string scriptsPath) =>
        Directory.Exists(scriptsPath)
            ? Directory.EnumerateFiles(scriptsPath, "*.sql", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();
}
