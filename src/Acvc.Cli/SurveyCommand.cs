using System.CommandLine;
using System.Text.Json;
using Acvc.Core.Survey;

namespace Acvc.Cli;

/// <summary>
/// acvc survey — classify and health-check every car in content/cars, in memory
/// only. Writes nothing inside any car folder; the JSON report goes wherever
/// --report points (default ./acvc-survey.json). Exit 1 when core bugs are found —
/// the M6 gate is a survey clean of category-(a) failures.
/// </summary>
internal static class SurveyCommand
{
    public static Command Create()
    {
        var acPathOption = new Option<string?>("--ac-path")
        {
            Description = "Assetto Corsa install root. Overrides ac_path in ./acvc.config.toml.",
        };
        var reportOption = new Option<string?>("--report")
        {
            Description = "JSON report path. Default: ./acvc-survey.json",
        };

        var survey = new Command("survey",
            "Classify every installed car (packed/loose/encrypted/broken) and health-check the buildable ones. Read-only.");
        survey.Options.Add(acPathOption);
        survey.Options.Add(reportOption);
        survey.SetAction(parseResult => Run(
            parseResult.GetValue(acPathOption),
            parseResult.GetValue(reportOption)));
        return survey;
    }

    private static int Run(string? acPathOverride, string? reportOverride)
    {
        try
        {
            var acPath = Program.ResolveAcPath(acPathOverride);
            var carsRoot = Path.Combine(acPath, "content", "cars");
            if (!Directory.Exists(carsRoot))
                throw new InvalidOperationException(
                    $"'{acPath}' does not look like an Assetto Corsa install: {carsRoot} does not exist.");

            var report = CarSurveyor.Survey(carsRoot);

            var reportPath = Path.GetFullPath(reportOverride ?? "acvc-survey.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));

            PrintSummary(report, reportPath);
            return report.CoreBugCount > 0 ? Program.ExitUsage : Program.ExitOk;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Program.ExitUsage;
        }
    }

    private static void PrintSummary(SurveyReport report, string reportPath)
    {
        Console.WriteLine($"Surveyed {report.Cars.Count} car folders under {report.CarsRoot}");
        Console.WriteLine("  " + string.Join("   ",
            report.ClassificationCounts.OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}: {kv.Value}")));
        Console.WriteLine($"  buildable: {report.BuildableCount}   CORE BUGS: {report.CoreBugCount}");

        if (report.CoreBugCount > 0)
        {
            Console.WriteLine("Core bugs (category a — tool defects):");
            foreach (var entry in report.Cars.Where(c => c.CoreBugs.Count > 0))
                foreach (var bug in entry.CoreBugs)
                    Console.WriteLine($"  {entry.Car}: {bug}");
        }

        if (report.TyresVersionCounts.Count > 0)
            Console.WriteLine("tyres.ini VERSION values: " + string.Join(", ",
                report.TyresVersionCounts.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} ×{kv.Value}")));

        var topFailures = report.FailureSignatureCounts.Take(10).ToList();
        if (topFailures.Count > 0)
        {
            Console.WriteLine("Top failure signatures:");
            foreach (var (sig, count) in topFailures.Select(kv => (kv.Key, kv.Value)))
                Console.WriteLine($"  {count} × {sig}");
        }

        Console.WriteLine($"Report: {reportPath}");
    }
}
