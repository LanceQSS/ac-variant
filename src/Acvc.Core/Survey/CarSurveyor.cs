using System.Text.RegularExpressions;
using Acvc.Core.Acd;
using Acvc.Core.Model;
using Acvc.Core.UiMeta;

namespace Acvc.Core.Survey;

/// <summary>One car folder's survey result. Everything here came from reads only.</summary>
public sealed record CarSurveyEntry
{
    public required string Car { get; init; }
    /// <summary>kunos-packed | loose-data | both | encrypted | broken-container | no-data</summary>
    public required string Classification { get; init; }
    /// <summary>Why the car is not buildable (encrypted/broken), when applicable.</summary>
    public string? FailureSignature { get; init; }
    /// <summary>Per-file status of the core files + tyres.ini: ok | missing | error: ...</summary>
    public Dictionary<string, string> FileChecks { get; init; } = new();
    /// <summary>Files whose no-op parse→emit was not byte-identical. Always a core bug.</summary>
    public List<string> RoundTripMismatches { get; init; } = new();
    /// <summary>Category (a): tool defects — round-trip mismatches or unexpected exceptions.</summary>
    public List<string> CoreBugs { get; init; } = new();
    /// <summary>ok | missing | (with UiMissingFields naming absent regenerable fields)</summary>
    public string? UiStatus { get; init; }
    public List<string> UiMissingFields { get; init; } = new();
    public string? TyresVersion { get; init; }
    public List<string> TyreCompoundKeys { get; init; } = new();
}

public sealed record SurveyReport
{
    public required string CarsRoot { get; init; }
    public required List<CarSurveyEntry> Cars { get; init; }
    public required Dictionary<string, int> ClassificationCounts { get; init; }
    public required Dictionary<string, int> TyresVersionCounts { get; init; }
    /// <summary>Union of compound-section key names per tyres VERSION — M7's grip-key input.</summary>
    public required Dictionary<string, List<string>> TyreCompoundKeysByVersion { get; init; }
    public required Dictionary<string, int> FailureSignatureCounts { get; init; }
    public required int BuildableCount { get; init; }
    public required int CoreBugCount { get; init; }
}

/// <summary>
/// Classifies and health-checks every car in content/cars, entirely in memory.
/// Absolute rule: the survey never writes inside any car folder — all methods here
/// are read-only; the JSON report is written by the CLI outside the cars tree.
/// </summary>
public static partial class CarSurveyor
{
    public static SurveyReport Survey(string carsRoot)
    {
        var entries = Directory.GetDirectories(carsRoot)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .Select(SurveyCar)
            .ToList();

        var buildable = new[] { "kunos-packed", "loose-data", "both" };
        return new SurveyReport
        {
            CarsRoot = Path.GetFullPath(carsRoot),
            Cars = entries,
            ClassificationCounts = entries.GroupBy(e => e.Classification)
                .ToDictionary(g => g.Key, g => g.Count()),
            TyresVersionCounts = entries.Where(e => e.TyresVersion is not null)
                .GroupBy(e => e.TyresVersion!)
                .ToDictionary(g => g.Key, g => g.Count()),
            TyreCompoundKeysByVersion = entries.Where(e => e.TyresVersion is not null)
                .GroupBy(e => e.TyresVersion!)
                .ToDictionary(g => g.Key,
                    g => g.SelectMany(e => e.TyreCompoundKeys)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()),
            FailureSignatureCounts = entries.Where(e => e.FailureSignature is not null)
                .GroupBy(e => e.FailureSignature!)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            BuildableCount = entries.Count(e => buildable.Contains(e.Classification)),
            CoreBugCount = entries.Sum(e => e.CoreBugs.Count),
        };
    }

    public static CarSurveyEntry SurveyCar(string carFolder)
    {
        var car = Path.GetFileName(Path.TrimEndingDirectorySeparator(carFolder));
        var hasAcd = File.Exists(Path.Combine(carFolder, "data.acd"));
        var dataDir = Path.Combine(carFolder, "data");
        var hasLoose = Directory.Exists(dataDir) && Directory.GetFiles(dataDir).Length > 0;

        if (!hasAcd && !hasLoose)
            return new CarSurveyEntry { Car = car, Classification = "no-data" };

        IReadOnlyDictionary<string, byte[]>? files = null;
        string classification;
        string? signature = null;
        var coreBugs = new List<string>();

        if (hasAcd)
        {
            classification = hasLoose ? "both" : "kunos-packed";
            try
            {
                files = AcdUnpacker.Load(carFolder).Files;
            }
            catch (ProtectedDataException ex)
            {
                return new CarSurveyEntry
                {
                    Car = car, Classification = "encrypted",
                    FailureSignature = Signature(ex),
                };
            }
            catch (Exception ex) when (ex is AcdFormatException or NotSupportedException or ArgumentException)
            {
                return new CarSurveyEntry
                {
                    Car = car, Classification = "broken-container",
                    FailureSignature = Signature(ex),
                };
            }
            catch (Exception ex)
            {
                // Anything else out of our own decrypt path is a tool defect.
                coreBugs.Add($"unpack: {Signature(ex)}");
                return new CarSurveyEntry
                {
                    Car = car, Classification = classification,
                    FailureSignature = Signature(ex), CoreBugs = coreBugs,
                };
            }
        }
        else
        {
            classification = "loose-data";
            files = Directory.GetFiles(dataDir)
                .ToDictionary(Path.GetFileName!, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase)!;
        }

        var entry = CheckBuildable(carFolder, car, classification, files!, coreBugs);
        return entry with { FailureSignature = signature ?? entry.FailureSignature };
    }

    private static CarSurveyEntry CheckBuildable(
        string carFolder, string car, string classification,
        IReadOnlyDictionary<string, byte[]> files, List<string> coreBugs)
    {
        var fileChecks = new Dictionary<string, string>();
        var roundTripMismatches = new List<string>();
        string? tyresVersion = null;
        var tyreKeys = new List<string>();

        // Core files + tyres.ini: parse individually so each miss has its own line.
        foreach (var name in new[] { "car.ini", "engine.ini", "drivetrain.ini", "tyres.ini" })
            fileChecks[name] = CheckIni(files, name, out _);

        // power.lut goes through engine.ini's POWER_CURVE indirection when possible.
        var lutName = "power.lut";
        if (fileChecks["engine.ini"] == "ok")
        {
            try
            {
                lutName = new EngineIni(IniDocument.Parse(files["engine.ini"], "engine.ini")).PowerCurveFile;
            }
            catch (Exception ex) when (ex is KeyNotFoundException or FormatException)
            {
                fileChecks["engine.ini"] = $"error: {Signature(ex)}";
            }
        }
        fileChecks[lutName] = CheckLut(files, lutName);

        // Typed model probe: data issues (missing keys etc.), not tool bugs.
        try
        {
            var models = CarModelSet.FromFiles(files);
            _ = models.Car.TotalMass;
            _ = models.Engine.Limiter;
            _ = models.Drivetrain.FinalRatio;
            _ = models.PowerLut.RowCount;
            fileChecks["model"] = "ok";
        }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or FileNotFoundException or NotSupportedException)
        {
            fileChecks["model"] = $"error: {Signature(ex)}";
        }
        catch (Exception ex)
        {
            coreBugs.Add($"model: {Signature(ex)}");
        }

        // Lossless sweep over EVERY ini/lut: any byte drift is a category-(a) bug.
        foreach (var (name, bytes) in files)
        {
            try
            {
                byte[]? emitted = null;
                if (name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    emitted = IniDocument.Parse(bytes, name).ToBytes();
                else if (name.EndsWith(".lut", StringComparison.OrdinalIgnoreCase))
                    emitted = PowerLut.Parse(bytes, name).ToBytes();
                if (emitted is not null && !emitted.AsSpan().SequenceEqual(bytes))
                {
                    roundTripMismatches.Add(name);
                    coreBugs.Add($"round-trip mismatch: {name}");
                }
            }
            catch (NotSupportedException)
            {
                fileChecks.TryAdd(name, "utf-16 (unsupported encoding)");
            }
            catch (Exception ex)
            {
                coreBugs.Add($"parse crash on {name}: {Signature(ex)}");
            }
        }

        // tyres.ini VERSION + compound-section keys — the M7 grip-transform input.
        if (fileChecks["tyres.ini"] == "ok")
        {
            var tyres = IniDocument.Parse(files["tyres.ini"], "tyres.ini");
            tyresVersion = tyres.TryGetValue("HEADER", "VERSION", out var v) ? v : "none";
            tyreKeys = tyres.SectionNames
                .Where(s => CompoundSection().IsMatch(s))
                .SelectMany(tyres.KeysOf)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ui_car.json readability (read from the car folder, never written).
        var uiPath = Path.Combine(carFolder, "ui", "ui_car.json");
        string uiStatus;
        var uiMissing = new List<string>();
        if (!File.Exists(uiPath))
        {
            uiStatus = "missing";
        }
        else
        {
            uiMissing = UiCarPatcher.ProbeMissing(File.ReadAllBytes(uiPath)).ToList();
            uiStatus = uiMissing.Count == 0 ? "ok" : "partial";
        }

        return new CarSurveyEntry
        {
            Car = car,
            Classification = classification,
            FileChecks = fileChecks,
            RoundTripMismatches = roundTripMismatches,
            CoreBugs = coreBugs,
            UiStatus = uiStatus,
            UiMissingFields = uiMissing,
            TyresVersion = tyresVersion,
            TyreCompoundKeys = tyreKeys,
            FailureSignature = coreBugs.Count > 0 ? coreBugs[0]
                : fileChecks.Values.FirstOrDefault(v => v.StartsWith("error", StringComparison.Ordinal)),
        };
    }

    private static string CheckIni(IReadOnlyDictionary<string, byte[]> files, string name, out IniDocument? doc)
    {
        doc = null;
        if (!files.TryGetValue(name, out var bytes))
            return "missing";
        try
        {
            doc = IniDocument.Parse(bytes, name);
            return "ok";
        }
        catch (NotSupportedException)
        {
            return "utf-16 (unsupported encoding)";
        }
    }

    private static string CheckLut(IReadOnlyDictionary<string, byte[]> files, string name)
    {
        if (!files.TryGetValue(name, out var bytes))
            return "missing";
        try
        {
            var lut = PowerLut.Parse(bytes, name);
            return lut.RowCount > 0 ? "ok" : "error: no data rows";
        }
        catch (NotSupportedException)
        {
            return "utf-16 (unsupported encoding)";
        }
    }

    private static string Signature(Exception ex)
    {
        var message = ex.Message.ReplaceLineEndings(" ");
        return $"{ex.GetType().Name}: {(message.Length > 140 ? message[..140] + "…" : message)}";
    }

    [GeneratedRegex(@"^(FRONT|REAR)(_\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CompoundSection();
}
