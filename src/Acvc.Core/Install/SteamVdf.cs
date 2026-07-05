using System.Text.RegularExpressions;

namespace Acvc.Core.Install;

/// <summary>
/// Minimal parser for Steam's libraryfolders.vdf — just enough to enumerate library
/// roots. The registry lookup lives in the GUI (OS access); the parsing lives here
/// so it is tested (rule 5).
/// </summary>
public static partial class SteamVdf
{
    /// <summary>All "path" values, unescaped (\\ → \), in file order.</summary>
    public static IReadOnlyList<string> ParseLibraryPaths(string vdfText)
        => PathEntry().Matches(vdfText)
            .Select(m => m.Groups[1].Value.Replace("\\\\", "\\"))
            .ToList();

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex PathEntry();
}
