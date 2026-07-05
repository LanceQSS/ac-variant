using System.Text;
using Acvc.Core.Acd;

namespace Acvc.Core.Survey;

public sealed record CatalogCar(
    string Name,
    string Folder,
    string Classification,
    bool IsKunos,
    string? Reason)
{
    public bool IsBuildable => Classification is "kunos-packed" or "loose-data" or "both";
}

/// <summary>
/// Lightweight car listing for pickers: classification only (no health sweeps —
/// that's the survey's job). Kunos detection is data-driven: original cars have
/// their FMOD events registered in the install-global content/sfx/GUIDs.txt, mods
/// never do (they carry per-car maps). Read-only against the install.
///
/// Startup-at-scale (M9): an optional persisted cache keyed on folder name +
/// data.acd size/mtime skips the decrypt+plausibility pass for unchanged archives.
/// Only the acd verdict is cached — loose-data presence, Kunos detection and the
/// both/kunos-packed split are recomputed fresh every run (they're cheap and can
/// change without touching the acd), so classification results are identical with
/// or without the cache.
/// </summary>
public static class CarCatalog
{
    private sealed record AcdVerdictEntry(long Size, long MtimeTicks, string Verdict, string? Reason);

    public static IReadOnlyList<CatalogCar> List(string acPath, string? cachePath = null)
    {
        var carsRoot = Path.Combine(acPath, "content", "cars");
        if (!Directory.Exists(carsRoot))
            throw new DirectoryNotFoundException(
                $"'{acPath}' does not look like an Assetto Corsa install: {carsRoot} does not exist.");

        var globalGuidsPath = Path.Combine(acPath, "content", "sfx", "GUIDs.txt");
        var globalGuids = File.Exists(globalGuidsPath)
            ? Encoding.Latin1.GetString(File.ReadAllBytes(globalGuidsPath))
            : "";

        var cache = LoadCache(cachePath);
        var list = Directory.GetDirectories(carsRoot)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .Select(dir => Classify(dir, globalGuids, cache))
            .ToList();
        SaveCache(cachePath, cache);
        return list;
    }

    public static CatalogCar Classify(string carFolder, string globalGuids)
        => Classify(carFolder, globalGuids, null);

    private static CatalogCar Classify(string carFolder, string globalGuids, Dictionary<string, AcdVerdictEntry>? cache)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(carFolder));
        var isKunos = globalGuids.Contains($"event:/cars/{name}/", StringComparison.Ordinal);

        var acdPath = Path.Combine(carFolder, "data.acd");
        var hasAcd = File.Exists(acdPath);
        var dataDir = Path.Combine(carFolder, "data");
        var hasLoose = Directory.Exists(dataDir) && Directory.GetFiles(dataDir).Length > 0;

        if (!hasAcd)
        {
            return new CatalogCar(name, carFolder,
                hasLoose ? "loose-data" : "no-data",
                isKunos,
                hasLoose ? null : "no data.acd and no loose data folder");
        }

        var info = new FileInfo(acdPath);
        var (verdict, reason) = AcdVerdict(carFolder, name, info, cache);
        return verdict switch
        {
            "ok" => new CatalogCar(name, carFolder, hasLoose ? "both" : "kunos-packed", isKunos, null),
            _ => new CatalogCar(name, carFolder, verdict, isKunos, reason),
        };
    }

    private static (string Verdict, string? Reason) AcdVerdict(
        string carFolder, string name, FileInfo acd, Dictionary<string, AcdVerdictEntry>? cache)
    {
        if (cache is not null && cache.TryGetValue(name, out var hit) &&
            hit.Size == acd.Length && hit.MtimeTicks == acd.LastWriteTimeUtc.Ticks)
            return (hit.Verdict, hit.Reason);

        string verdict;
        string? reason = null;
        try
        {
            _ = AcdUnpacker.Load(carFolder);
            verdict = "ok";
        }
        catch (ProtectedDataException ex)
        {
            verdict = "encrypted";
            reason = ex.Message;
        }
        catch (Exception ex) when (ex is AcdFormatException or NotSupportedException or ArgumentException)
        {
            verdict = "broken-container";
            reason = ex.Message;
        }

        if (cache is not null)
            cache[name] = new AcdVerdictEntry(acd.Length, acd.LastWriteTimeUtc.Ticks, verdict, reason);
        return (verdict, reason);
    }

    private static Dictionary<string, AcdVerdictEntry>? LoadCache(string? cachePath)
    {
        if (cachePath is null)
            return null;
        try
        {
            if (File.Exists(cachePath))
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, AcdVerdictEntry>>(
                           File.ReadAllText(cachePath))
                       ?? new Dictionary<string, AcdVerdictEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // A corrupt or unreadable cache is discarded, never fatal.
        }
        return new Dictionary<string, AcdVerdictEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveCache(string? cachePath, Dictionary<string, AcdVerdictEntry>? cache)
    {
        if (cachePath is null || cache is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, System.Text.Json.JsonSerializer.Serialize(cache));
        }
        catch (IOException)
        {
            // Best effort; the next scan just runs uncached.
        }
    }
}
