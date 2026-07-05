namespace Acvc.Tests;

/// <summary>
/// Locates tests/fixtures/ (gitignored; populated locally by scripts/make-fixtures.ps1
/// from the user's own AC install — Kunos data never goes in the repo).
/// </summary>
internal static class Fixtures
{
    public static string? Directory
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "acvc.slnx")))
                dir = Path.GetDirectoryName(dir);
            if (dir is null)
                return null;
            var fixtures = Path.Combine(dir, "tests", "fixtures");
            return System.IO.Directory.Exists(fixtures) ? fixtures : null;
        }
    }

    /// <summary>Fixture car folders that contain a data.acd.</summary>
    public static IReadOnlyList<string> CarFolders()
    {
        var root = Directory;
        if (root is null)
            return Array.Empty<string>();
        return System.IO.Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "data.acd")))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
