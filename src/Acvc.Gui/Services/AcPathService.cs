using System.IO;
using Acvc.Core.Install;
using Microsoft.Win32;

namespace Acvc.Gui.Services;

/// <summary>
/// AC install root: acvc.config.toml (beside the exe) wins, then Steam autodetect
/// (registry SteamPath + libraryfolders.vdf for secondary libraries — the vdf
/// parsing lives in Core where it is tested). Never hardcoded.
/// </summary>
public static class AcPathService
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "acvc.config.toml");

    public static string? LoadConfigured()
    {
        if (!File.Exists(ConfigPath))
            return null;
        try
        {
            var config = Tomlyn.TomlSerializer.Deserialize<GuiConfig>(File.ReadAllText(ConfigPath));
            return string.IsNullOrWhiteSpace(config?.AcPath) ? null : config.AcPath;
        }
        catch (Tomlyn.TomlException)
        {
            return null; // unreadable config: fall through to autodetect / manual pick
        }
    }

    public static void Persist(string acPath)
        => File.WriteAllText(ConfigPath, $"ac_path = '{acPath}'\n");

    public static string? Autodetect()
    {
        var steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")
            ?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath))
            return null;
        steamPath = steamPath.Replace('/', '\\');

        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(SteamVdf.ParseLibraryPaths(File.ReadAllText(vdf)));

        return libraries
            .Select(lib => Path.Combine(lib, "steamapps", "common", "assettocorsa"))
            .FirstOrDefault(Directory.Exists);
    }

    private sealed class GuiConfig
    {
        [Tomlyn.Serialization.TomlPropertyName("ac_path")]
        public string? AcPath { get; set; }
    }
}
