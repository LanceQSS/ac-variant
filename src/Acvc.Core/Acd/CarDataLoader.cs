namespace Acvc.Core.Acd;

public enum CarDataOrigin
{
    /// <summary>Decrypted from data.acd.</summary>
    PackedAcd,
    /// <summary>Read from a loose data/ folder (typical for mod cars).</summary>
    LooseData,
}

public sealed record CarData(
    string CarFolderName,
    IReadOnlyDictionary<string, byte[]> Files,
    CarDataOrigin Origin);

/// <summary>
/// Loads a car's physics data regardless of packaging. When both data.acd and a
/// loose data/ folder exist, data.acd wins — that matches the game, which ignores
/// the loose folder whenever an archive is present. Read-only in all paths.
/// </summary>
public static class CarDataLoader
{
    public static CarData Load(string carFolder)
    {
        var full = Path.GetFullPath(carFolder);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Car folder not found: {full}");
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));

        if (File.Exists(Path.Combine(full, "data.acd")))
        {
            var unpacked = AcdUnpacker.Load(full);
            return new CarData(name, unpacked.Files, CarDataOrigin.PackedAcd);
        }

        var dataDir = Path.Combine(full, "data");
        if (Directory.Exists(dataDir))
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(dataDir))
                files[Path.GetFileName(file)] = File.ReadAllBytes(file);
            if (files.Count > 0)
                return new CarData(name, files, CarDataOrigin.LooseData);
        }

        throw new FileNotFoundException(
            $"{full} has neither data.acd nor a non-empty loose data/ folder — no physics data to load.");
    }
}
