namespace Acvc.Core.Acd;

/// <summary>Decrypted contents of a car's data.acd, keyed by file name.</summary>
public sealed record UnpackedData(string CarFolderName, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>
/// Orchestrates unpack: locate data.acd → parse container → decrypt with the key
/// derived from the (lowercased) folder name → plausibility-check → optionally write
/// a loose data folder. Never writes inside the source car folder.
/// </summary>
public static class AcdUnpacker
{
    /// <summary>Parses and decrypts <paramref name="carFolder"/>'s data.acd entirely in memory.</summary>
    public static UnpackedData Load(string carFolder)
    {
        var fullCarFolder = Path.GetFullPath(carFolder);
        if (!Directory.Exists(fullCarFolder))
            throw new DirectoryNotFoundException($"Car folder not found: {fullCarFolder}");

        var folderName = Path.GetFileName(fullCarFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var acdPath = Path.Combine(fullCarFolder, "data.acd");
        if (!File.Exists(acdPath))
        {
            var hint = Directory.Exists(Path.Combine(fullCarFolder, "data"))
                ? " The car has a loose data/ folder instead — there is nothing to unpack."
                : string.Empty;
            throw new FileNotFoundException($"No data.acd in {fullCarFolder}.{hint}", acdPath);
        }

        // AC resolves car folders case-insensitively; the canonical key input is the
        // lowercased name (Kunos folders are already lowercase).
        var cipher = AcdCipher.ForFolderName(folderName.ToLowerInvariant());

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in AcdArchive.Read(acdPath))
        {
            ValidateEntryName(entry.Name);
            if (files.ContainsKey(entry.Name))
                throw new AcdFormatException($"Archive contains duplicate entry '{entry.Name}'.");
            files[entry.Name] = cipher.Decrypt(entry.EncryptedContent);
        }

        var reason = AcdPlausibility.FindImplausibility(files);
        if (reason is not null)
            throw new ProtectedDataException(
                $"data.acd for '{folderName}' decrypted to implausible content: {reason}. " +
                "This car is almost certainly a protected mod using CSP/x4fab-era encryption, " +
                "which is separate from the standard Kunos cipher and cannot be unpacked by acvc.");

        return new UnpackedData(folderName, files);
    }

    /// <summary>
    /// Unpacks to <paramref name="outDir"/>, refusing any destination inside the
    /// source folder. Accepts loose-data cars too (M6): with no data.acd, the loose
    /// data/ files are copied out; when both exist, data.acd wins (game behavior).
    /// </summary>
    public static UnpackedData UnpackToDirectory(string carFolder, string outDir)
    {
        var loaded = CarDataLoader.Load(carFolder);
        var data = new UnpackedData(loaded.CarFolderName, loaded.Files);

        var fullCarFolder = Path.GetFullPath(carFolder);
        var fullOutDir = Path.GetFullPath(outDir);
        if (IsSameOrInside(fullOutDir, fullCarFolder))
            throw new ArgumentException(
                $"Output directory {fullOutDir} is inside the source car folder {fullCarFolder}; " +
                "acvc never writes into the original car folder.", nameof(outDir));

        Directory.CreateDirectory(fullOutDir);
        foreach (var (name, content) in data.Files)
            File.WriteAllBytes(Path.Combine(fullOutDir, name), content);
        return data;
    }

    private static void ValidateEntryName(string name)
    {
        if (name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\'))
            throw new AcdFormatException($"Archive entry '{name}' is not a safe plain file name.");
    }

    private static bool IsSameOrInside(string candidate, string root)
    {
        var rel = Path.GetRelativePath(root, candidate);
        return rel == "." || (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel));
    }
}
