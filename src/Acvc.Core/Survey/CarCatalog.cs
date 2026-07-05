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
/// never do (they carry per-car maps). Read-only.
/// </summary>
public static class CarCatalog
{
    public static IReadOnlyList<CatalogCar> List(string acPath)
    {
        var carsRoot = Path.Combine(acPath, "content", "cars");
        if (!Directory.Exists(carsRoot))
            throw new DirectoryNotFoundException(
                $"'{acPath}' does not look like an Assetto Corsa install: {carsRoot} does not exist.");

        var globalGuidsPath = Path.Combine(acPath, "content", "sfx", "GUIDs.txt");
        var globalGuids = File.Exists(globalGuidsPath)
            ? Encoding.Latin1.GetString(File.ReadAllBytes(globalGuidsPath))
            : "";

        return Directory.GetDirectories(carsRoot)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .Select(dir => Classify(dir, globalGuids))
            .ToList();
    }

    public static CatalogCar Classify(string carFolder, string globalGuids)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(carFolder));
        var isKunos = globalGuids.Contains($"event:/cars/{name}/", StringComparison.Ordinal);

        var hasAcd = File.Exists(Path.Combine(carFolder, "data.acd"));
        var dataDir = Path.Combine(carFolder, "data");
        var hasLoose = Directory.Exists(dataDir) && Directory.GetFiles(dataDir).Length > 0;

        if (!hasAcd)
        {
            return new CatalogCar(name, carFolder,
                hasLoose ? "loose-data" : "no-data",
                isKunos,
                hasLoose ? null : "no data.acd and no loose data folder");
        }

        try
        {
            _ = AcdUnpacker.Load(carFolder);
            return new CatalogCar(name, carFolder, hasLoose ? "both" : "kunos-packed", isKunos, null);
        }
        catch (ProtectedDataException ex)
        {
            return new CatalogCar(name, carFolder, "encrypted", isKunos, ex.Message);
        }
        catch (Exception ex) when (ex is AcdFormatException or NotSupportedException or ArgumentException)
        {
            return new CatalogCar(name, carFolder, "broken-container", isKunos, ex.Message);
        }
    }
}
