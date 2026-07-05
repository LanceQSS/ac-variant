using System.CommandLine;
using Acvc.Core.Acd;
using Acvc.Core.Emit;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;
using Acvc.Core.UiMeta;

namespace Acvc.Cli;

/// <summary>
/// acvc build &lt;spec.toml&gt; — the full pipeline: load source car → unpack in memory →
/// apply TunePlan → validate (failures abort before any write; warnings print and
/// continue) → emit the variant folder &lt;source_car&gt;_&lt;tune_name&gt;.
/// </summary>
internal static class BuildCommand
{
    public static Command Create()
    {
        var specArg = new Argument<string>("spec")
        {
            Description = "Path to the tune spec (.toml)",
        };
        var acPathOption = new Option<string?>("--ac-path")
        {
            Description = "Assetto Corsa install root. Overrides ac_path in ./acvc.config.toml.",
        };
        var outOption = new Option<string?>("--out")
        {
            Description = "Directory to create the variant folder in. Default: <ac-path>/content/cars",
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Replace an existing variant folder (whole-folder swap, never a merge).",
        };
        var skinsOption = new Option<string>("--skins")
        {
            Description = "Skin handling: 'junction' = NTFS junctions to all source skins (default; CM and AC follow them); 'copy' = copy only the first skin.",
            DefaultValueFactory = _ => "junction",
        };
        skinsOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not ("copy" or "junction"))
                result.AddError("--skins must be 'copy' or 'junction'.");
        });

        var build = new Command("build",
            "Compile a tune spec into a new variant car folder. The source car is never modified.");
        build.Arguments.Add(specArg);
        build.Options.Add(acPathOption);
        build.Options.Add(outOption);
        build.Options.Add(forceOption);
        build.Options.Add(skinsOption);
        build.SetAction(parseResult => Run(
            parseResult.GetValue(specArg)!,
            parseResult.GetValue(acPathOption),
            parseResult.GetValue(outOption),
            parseResult.GetValue(forceOption),
            parseResult.GetValue(skinsOption)!));
        return build;
    }

    private static int Run(string specPath, string? acPathOverride, string? outOverride, bool force, string skins)
    {
        try
        {
            if (!File.Exists(specPath))
                throw new InvalidOperationException($"Tune spec not found: {Path.GetFullPath(specPath)}");
            var specText = File.ReadAllText(specPath);
            var plan = TuneSpecParser.Parse(specText);

            var acPath = Program.ResolveAcPath(acPathOverride);
            var carsRoot = Path.Combine(acPath, "content", "cars");
            var sourceFolder = Path.Combine(carsRoot, plan.SourceCar);
            if (!Directory.Exists(sourceFolder))
                throw new InvalidOperationException(
                    $"Source car '{plan.SourceCar}' not found at {sourceFolder}.");

            var files = LoadSourceData(sourceFolder);
            var models = CarModelSet.FromFiles(files);
            var validation = TunePipeline.Apply(plan, models);

            foreach (var warning in validation.Warnings)
                Console.WriteLine($"warning [{warning.Rule}]: {warning.Message} (value {warning.Value}, limit {warning.Limit})");
            if (validation.HasFailures)
            {
                foreach (var failure in validation.Failures)
                    Console.Error.WriteLine($"FAIL [{failure.Rule}]: {failure.Message} (value {failure.Value}, limit {failure.Limit})");
                Console.Error.WriteLine("Validation failed — nothing was written.");
                return Program.ExitValidation;
            }

            var variantName = $"{plan.SourceCar}_{plan.TuneName}";
            var uiPatch = UiCarPatcher.BuildPatch(models);
            var result = VariantEmitter.Emit(sourceFolder, variantName, models.MergedInto(files), new EmitOptions
            {
                OutRoot = outOverride ?? carsRoot,
                Force = force,
                SkinsMode = skins == "copy" ? SkinsMode.CopyFirst : SkinsMode.Junction,
                UiNameSuffix = $" — {plan.TuneName}",
                UiPatch = uiPatch,
                SpecText = specText,
                SpecFileName = Path.GetFileName(specPath),
            });

            Console.WriteLine($"Built {result.VariantName}");
            Console.WriteLine($"  -> {result.VariantPath}");
            Console.WriteLine($"  data/: {result.DataFileCount} files (loose; no data.acd by design)");
            Console.WriteLine($"  sfx: {result.AudioNote}" +
                              (result.RenamedBanks.Count > 0 ? $"; banks: {string.Join(", ", result.RenamedBanks)}" : ""));
            Console.WriteLine($"  skins: {result.SkinsNote}");
            Console.WriteLine($"  ui specs: {uiPatch.Bhp}, {uiPatch.Torque}, {uiPatch.Weight}, {uiPatch.PwRatio} (LUT-derived)");
            return Program.ExitOk;
        }
        catch (TuneSpecException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitUsage;
        }
        catch (TransformException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitValidation;
        }
        catch (EmitException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitEmit;
        }
        catch (ProtectedDataException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitProtected;
        }
        catch (AcdFormatException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitFormat;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException
                                       or UnauthorizedAccessException or KeyNotFoundException or FormatException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitUsage;
        }
    }

    /// <summary>
    /// Source data files, in memory: from data.acd when packed, else from a loose
    /// data/ folder (common for mods). Neither path writes anything.
    /// </summary>
    internal static IReadOnlyDictionary<string, byte[]> LoadSourceData(string sourceFolder)
    {
        if (File.Exists(Path.Combine(sourceFolder, "data.acd")))
            return AcdUnpacker.Load(sourceFolder).Files;

        var dataDir = Path.Combine(sourceFolder, "data");
        if (!Directory.Exists(dataDir))
            throw new InvalidOperationException(
                $"{sourceFolder} has neither data.acd nor a loose data/ folder — nothing to tune.");

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dataDir))
            files[Path.GetFileName(file)] = File.ReadAllBytes(file);
        if (files.Count == 0)
            throw new InvalidOperationException($"{dataDir} is empty — nothing to tune.");
        return files;
    }
}
