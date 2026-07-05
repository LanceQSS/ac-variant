using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Acvc.Core.Emit;

public class EmitException : Exception
{
    public EmitException(string message) : base(message) { }
}

public enum SkinsMode
{
    /// <summary>Copy only the first skin (alphabetical) so the car renders; the rest stay in the source folder.</summary>
    CopyFirst,
    /// <summary>NTFS junctions in skins/ pointing at each source skin folder (experimental — CM/AC verdict pending).</summary>
    Junction,
}

public sealed record EmitOptions
{
    /// <summary>Directory the variant folder is created in (default: content/cars of the AC install).</summary>
    public required string OutRoot { get; init; }
    public bool Force { get; init; }
    public SkinsMode SkinsMode { get; init; } = SkinsMode.CopyFirst;
    /// <summary>Appended to ui_car.json's "name" value, e.g. " — street_600".</summary>
    public required string UiNameSuffix { get; init; }
    /// <summary>
    /// Regenerated specs/curves from the transformed data (M5). Null skips
    /// regeneration and only the name is edited.
    /// </summary>
    public UiMeta.UiSpecsPatch? UiPatch { get; init; }
    /// <summary>Tune spec text, reproduced verbatim in the variant's readme.</summary>
    public required string SpecText { get; init; }
    public string? SpecFileName { get; init; }
}

public sealed record EmitResult(
    string VariantPath,
    string VariantName,
    int DataFileCount,
    string AudioNote,
    IReadOnlyList<string> RenamedBanks,
    string SkinsNote);

/// <summary>
/// Assembles a variant car folder: everything from the source except data.acd (the
/// variant ships loose data/ only), skins/ (handled per <see cref="SkinsMode"/>) and
/// any loose data/ (replaced by the transformed files). Audio identity follows the
/// folder rename: every occurrence of the source folder name in sfx/GUIDs.txt is
/// rewritten to the variant name and car-named .bank files are renamed to match —
/// without this the renamed car is silent. The variant is built in a temp directory
/// beside the target and swapped in atomically; a failed build never leaves a
/// half-written variant, and --force replaces whole folders, never merges.
/// The source car folder is only ever read (CLAUDE.md rule 1).
/// </summary>
public static class VariantEmitter
{
    public static EmitResult Emit(
        string sourceCarFolder,
        string variantName,
        IReadOnlyDictionary<string, byte[]> dataFiles,
        EmitOptions options)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceCarFolder));
        if (!Directory.Exists(source))
            throw new EmitException($"Source car folder not found: {source}");
        var sourceName = Path.GetFileName(source);

        if (string.IsNullOrWhiteSpace(variantName) ||
            variantName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new EmitException($"Variant name '{variantName}' is not a valid folder name.");
        if (string.Equals(variantName, sourceName, StringComparison.OrdinalIgnoreCase))
            throw new EmitException(
                $"Variant name '{variantName}' equals the source folder name; refusing to touch the source car.");

        var outRoot = Path.GetFullPath(options.OutRoot);
        var target = Path.Combine(outRoot, variantName);
        if (IsSameOrInside(target, source))
            throw new EmitException($"Target {target} lies inside the source car folder; refusing.");
        if (Directory.Exists(target) && !options.Force)
            throw new EmitException(
                $"Variant folder already exists: {target}. Pass --force to replace it (the folder is replaced whole, never merged).");

        Directory.CreateDirectory(outRoot);
        var temp = Path.Combine(outRoot, $".acvc-tmp-{Guid.NewGuid():N}");
        try
        {
            var result = BuildTo(temp, source, sourceName, variantName, dataFiles, options);
            Swap(temp, target, options.Force);
            return result with { VariantPath = target };
        }
        catch
        {
            TryDeleteDirectory(temp);
            throw;
        }
    }

    // ---- build --------------------------------------------------------------

    private static EmitResult BuildTo(
        string temp,
        string source,
        string sourceName,
        string variantName,
        IReadOnlyDictionary<string, byte[]> dataFiles,
        EmitOptions options)
    {
        Directory.CreateDirectory(temp);

        // 1. Root files, except data.acd — the variant ships a loose data/ folder only.
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("data.acd", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(temp, name));
        }

        // 2. Subdirectories, except data/ (replaced) and skins/ (handled below).
        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("data", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("skins", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyTree(dir, Path.Combine(temp, name));
        }

        // 3. Audio identity: rewrite or generate GUIDs.txt and rename car-named banks.
        var (audioNote, renamedBanks) = FixupSfx(temp, source, sourceName, variantName);

        // 4. Transformed data/.
        var dataDir = Path.Combine(temp, "data");
        Directory.CreateDirectory(dataDir);
        foreach (var (name, bytes) in dataFiles)
        {
            if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains('/') || name.Contains('\\'))
                throw new EmitException($"Data file name '{name}' is not a safe plain file name.");
            File.WriteAllBytes(Path.Combine(dataDir, name), bytes);
        }

        // 5. Skins.
        var skinsNote = EmitSkins(temp, source, options.SkinsMode);

        // 6. ui_car.json: display name, plus regenerated specs/curves when provided.
        var uiCarJson = Path.Combine(temp, "ui", "ui_car.json");
        if (!File.Exists(uiCarJson))
            throw new EmitException($"Source car has no ui/ui_car.json — it would be invisible in Content Manager.");
        var uiBytes = UiCarJson.AppendToName(File.ReadAllBytes(uiCarJson), options.UiNameSuffix);
        if (options.UiPatch is { } patch)
            uiBytes = UiMeta.UiCarPatcher.Apply(uiBytes, patch);
        File.WriteAllBytes(uiCarJson, uiBytes);

        // 7. Readme.
        File.WriteAllText(Path.Combine(temp, "readme.txt"), BuildReadme(sourceName, variantName, skinsNote, options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new EmitResult(temp, variantName, dataFiles.Count, audioNote, renamedBanks, skinsNote);
    }

    /// <summary>
    /// Audio follows the car id: a renamed folder resolves FMOD events by the NEW id,
    /// so without a matching map the variant is silent. Two source layouts exist:
    /// mod cars carry their own sfx/GUIDs.txt (rewrite every source-name occurrence);
    /// Kunos cars have none — their events live in the install-global
    /// content/sfx/GUIDs.txt, so a per-car GUIDs.txt is GENERATED from the global
    /// file's exact-token source entries, rewritten to the variant id. (Kunos does the
    /// same for their own tuned variants: abarth500_s1's global entries reuse
    /// abarth500's event GUIDs with rewritten paths.) Exact-token matching matters:
    /// a substring match on "abarth500" would wrongly capture abarth500_s1 and
    /// ks_abarth500_assetto_corse.
    /// </summary>
    private static (string AudioNote, IReadOnlyList<string> RenamedBanks) FixupSfx(
        string temp, string source, string sourceName, string variantName)
    {
        var sfx = Path.Combine(temp, "sfx");
        if (!Directory.Exists(sfx))
            return ("source car has no sfx folder", Array.Empty<string>());

        string audioNote;
        var guids = Path.Combine(sfx, "GUIDs.txt");
        if (File.Exists(guids))
        {
            // Latin-1 keeps the rewrite byte-safe regardless of stray high bytes.
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(guids));
            var replacements = CountOccurrences(text, sourceName);
            if (replacements > 0)
            {
                File.WriteAllBytes(guids, Encoding.Latin1.GetBytes(text.Replace(sourceName, variantName, StringComparison.Ordinal)));
                audioNote = $"rewrote {replacements} occurrence(s) in sfx/GUIDs.txt";
            }
            else
            {
                audioNote = "sfx/GUIDs.txt has no source-name entries (borrowed sound?) — left as is";
            }
        }
        else
        {
            audioNote = GenerateGuidsFromGlobal(guids, source, sourceName, variantName);
        }

        var renamed = new List<string>();
        foreach (var bank in Directory.GetFiles(sfx, "*.bank"))
        {
            var name = Path.GetFileName(bank);
            if (!name.Contains(sourceName, StringComparison.Ordinal))
                continue;
            var newName = name.Replace(sourceName, variantName, StringComparison.Ordinal);
            File.Move(bank, Path.Combine(sfx, newName));
            renamed.Add($"{name} -> {newName}");
        }
        return (audioNote, renamed);
    }

    private static string GenerateGuidsFromGlobal(string guidsOut, string source, string sourceName, string variantName)
    {
        // content/cars/<car> -> content/sfx/GUIDs.txt
        var globalGuids = Path.GetFullPath(Path.Combine(source, "..", "..", "sfx", "GUIDs.txt"));
        if (!File.Exists(globalGuids))
            throw new EmitException(
                $"Source car has no sfx/GUIDs.txt and no install-global GUIDs.txt was found at {globalGuids}; " +
                "the variant would be silent. Refusing to emit a broken car.");

        var eventToken = $"event:/cars/{sourceName}/";
        var bankToken = $"bank:/{sourceName}";
        var lines = new List<string>();
        foreach (var line in File.ReadLines(globalGuids))
        {
            var isEvent = line.Contains(eventToken, StringComparison.Ordinal);
            var isBank = line.TrimEnd().EndsWith(bankToken, StringComparison.Ordinal);
            if (isEvent || isBank)
                lines.Add(line.Replace(
                    isEvent ? eventToken : bankToken,
                    isEvent ? $"event:/cars/{variantName}/" : $"bank:/{variantName}",
                    StringComparison.Ordinal));
        }
        if (lines.Count == 0)
            throw new EmitException(
                $"The install-global GUIDs.txt has no entries for '{sourceName}'; the variant would be silent. " +
                "Refusing to emit a broken car.");

        File.WriteAllText(guidsOut, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return $"generated sfx/GUIDs.txt from the install-global GUIDs ({lines.Count} entries)";
    }

    private static string EmitSkins(string temp, string source, SkinsMode mode)
    {
        var sourceSkins = Path.Combine(source, "skins");
        var skinDirs = Directory.Exists(sourceSkins)
            ? Directory.GetDirectories(sourceSkins).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        if (skinDirs.Length == 0)
            throw new EmitException($"Source car has no skins under {sourceSkins}; the variant could not render.");

        var targetSkins = Path.Combine(temp, "skins");
        Directory.CreateDirectory(targetSkins);

        if (mode == SkinsMode.CopyFirst)
        {
            var first = skinDirs[0];
            CopyTree(first, Path.Combine(targetSkins, Path.GetFileName(first)));
            return $"copied first skin '{Path.GetFileName(first)}' (of {skinDirs.Length}); the rest remain in the source car";
        }

        foreach (var skin in skinDirs)
            CreateJunction(Path.Combine(targetSkins, Path.GetFileName(skin)), skin);
        return $"{skinDirs.Length} NTFS junction(s) into the source car's skins folder (experimental)";
    }

    private static string BuildReadme(string sourceName, string variantName, string skinsNote, EmitOptions options)
    {
        var version = typeof(VariantEmitter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var spec = options.SpecFileName is null ? "tune spec" : $"tune spec ({options.SpecFileName})";
        return $"""
            {variantName}
            Generated by acvc {version} on {DateTime.Now:yyyy-MM-dd HH:mm}.

            Source car: {sourceName} (never modified by acvc).
            Skins: {skinsNote}.
            Data: loose data/ folder, transformed from the source car's data.acd.

            This folder is a personal-use variant. Do not redistribute it — the tune
            spec below is the shareable unit, never the tuned data files.

            {spec}:
            ----------------------------------------------------------------------
            {options.SpecText.TrimEnd()}
            ----------------------------------------------------------------------
            """;
    }

    // ---- swap ----------------------------------------------------------------

    private static void Swap(string temp, string target, bool force)
    {
        if (Directory.Exists(target))
        {
            if (!force)
                throw new EmitException($"Variant folder appeared during the build: {target}. Pass --force to replace it.");
            var backup = target + ".acvc-old-" + Guid.NewGuid().ToString("N");
            Directory.Move(target, backup);
            try
            {
                Directory.Move(temp, target);
            }
            catch
            {
                Directory.Move(backup, target); // restore the previous variant
                throw;
            }
            TryDeleteDirectory(backup);
        }
        else
        {
            Directory.Move(temp, target);
        }
    }

    // ---- plumbing --------------------------------------------------------------

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    /// <summary>
    /// NTFS junction. The BCL only creates symlinks (which need elevation on Windows);
    /// junctions don't, so shell out to mklink /J.
    /// </summary>
    private static void CreateJunction(string junctionPath, string targetDir)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{targetDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)
            ?? throw new EmitException("Could not start cmd.exe to create an NTFS junction.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new EmitException(
                $"mklink /J failed for {junctionPath} (exit {process.ExitCode}): {(stderr + " " + stdout).Trim()}");
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static bool IsSameOrInside(string candidate, string root)
    {
        var rel = Path.GetRelativePath(root, candidate);
        return rel == "." || (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort; the temp name is unmistakably ours (.acvc-tmp-/.acvc-old-).
        }
    }
}
