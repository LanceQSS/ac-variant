using System.CommandLine;
using Acvc.Core.Acd;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;
using Acvc.Core.UiMeta;

namespace Acvc.Cli;

/// <summary>
/// acvc dyno &lt;spec.toml|car&gt; — renders the torque/power curves to PNG. Given a spec,
/// the stock curves are overlaid with the tuned ones; given a car name, stock only.
/// Purely analytical: nothing is written except the PNG.
/// </summary>
internal static class DynoCommand
{
    private const int PlotStep = 100; // denser than the 500-rpm ui grid for smooth lines

    public static Command Create()
    {
        var targetArg = new Argument<string>("target")
        {
            Description = "Tune spec path (.toml) for stock-vs-tuned, or a car folder name for stock only",
        };
        var acPathOption = new Option<string?>("--ac-path")
        {
            Description = "Assetto Corsa install root. Overrides ac_path in ./acvc.config.toml.",
        };
        var outOption = new Option<string?>("--out")
        {
            Description = "Output PNG path. Default: ./dyno_<car>[_<tune>].png",
        };

        var dyno = new Command("dyno", "Render a car's (or tune's) torque and power curves to a PNG.");
        dyno.Arguments.Add(targetArg);
        dyno.Options.Add(acPathOption);
        dyno.Options.Add(outOption);
        dyno.SetAction(parseResult => Run(
            parseResult.GetValue(targetArg)!,
            parseResult.GetValue(acPathOption),
            parseResult.GetValue(outOption)));
        return dyno;
    }

    private static int Run(string target, string? acPathOverride, string? outOverride)
    {
        try
        {
            var isSpec = target.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);
            if (isSpec && !File.Exists(target))
                throw new InvalidOperationException($"Tune spec not found: {Path.GetFullPath(target)}");

            var plan = isSpec ? TuneSpecParser.Parse(File.ReadAllText(target)) : null;
            var carName = plan?.SourceCar ?? target;

            var acPath = Program.ResolveAcPath(acPathOverride);
            var sourceFolder = Path.Combine(acPath, "content", "cars", carName);
            if (!Directory.Exists(sourceFolder))
                throw new InvalidOperationException($"Car '{carName}' not found at {sourceFolder}.");

            var files = BuildCommand.LoadSourceData(sourceFolder);
            var stockModels = CarModelSet.FromFiles(files);
            var stock = PowerCurves.SampleGrid(stockModels.Engine, stockModels.PowerLut, PlotStep);
            var (stockTorque, stockPower, _) = PowerCurves.Peaks(stock);
            Console.WriteLine($"stock: {stockPower:0} bhp, {stockTorque:0} Nm");

            IReadOnlyList<CurvePoint>? tuned = null;
            var title = carName;
            if (plan is not null)
            {
                var tunedModels = CarModelSet.FromFiles(files);
                var validation = TunePipeline.Apply(plan, tunedModels);
                foreach (var issue in validation.Issues)
                    Console.WriteLine($"{(issue.Severity == ValidationSeverity.Failure ? "FAIL" : "warning")} " +
                                      $"[{issue.Rule}]: {issue.Message} (value {issue.Value}, limit {issue.Limit})");

                tuned = PowerCurves.SampleGrid(tunedModels.Engine, tunedModels.PowerLut, PlotStep);
                var (tunedTorque, tunedPower, _) = PowerCurves.Peaks(tuned);
                Console.WriteLine($"tuned: {tunedPower:0} bhp, {tunedTorque:0} Nm");
                title = $"{carName} — {plan.TuneName}";
            }

            var outPath = outOverride ?? Path.Combine(
                Environment.CurrentDirectory,
                plan is null ? $"dyno_{carName}.png" : $"dyno_{carName}_{plan.TuneName}.png");
            DynoRenderer.Render(title, stock, tuned, outPath);
            Console.WriteLine($"dyno chart -> {Path.GetFullPath(outPath)}");
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
}
