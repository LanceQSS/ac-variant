using System.Text;
using System.Text.Json;
using Acvc.Core.Acd;
using Acvc.Core.Survey;

namespace Acvc.Tests;

/// <summary>
/// Survey classification and read-only guarantees over a synthetic content/cars
/// root built around the real fixture archives.
/// </summary>
public class SurveyTests : IDisposable
{
    private readonly string _root;
    private readonly string _carsRoot;

    public SurveyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "acvc-survey-tests", Guid.NewGuid().ToString("N"));
        _carsRoot = Path.Combine(_root, "cars");
        Directory.CreateDirectory(_carsRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CarDir(string name)
    {
        var dir = Path.Combine(_carsRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string BuildSyntheticRoot()
    {
        var abarth = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("abarth500", StringComparison.OrdinalIgnoreCase));
        var bmw = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("bmw_m3_e30", StringComparison.OrdinalIgnoreCase));
        Skip.If(abarth is null || bmw is null, ModelTestUtil.FixtureSkipReason);

        // kunos-packed (with a ui_car.json so ui probing has something to read)
        var packed = CarDir("abarth500");
        File.Copy(Path.Combine(abarth!, "data.acd"), Path.Combine(packed, "data.acd"));
        Directory.CreateDirectory(Path.Combine(packed, "ui"));
        File.WriteAllText(Path.Combine(packed, "ui", "ui_car.json"),
            "{\"name\": \"A\", \"specs\": {\"bhp\": \"1bhp\"}}", new UTF8Encoding(false));

        // both: acd + loose data — acd must win
        var both = CarDir("bmw_m3_e30");
        File.Copy(Path.Combine(bmw!, "data.acd"), Path.Combine(both, "data.acd"));
        Directory.CreateDirectory(Path.Combine(both, "data"));
        File.WriteAllText(Path.Combine(both, "data", "stray.txt"), "loose leftover");

        // loose-data (derived from the unpacked fixture)
        var loose = CarDir("mod_loose");
        Directory.CreateDirectory(Path.Combine(loose, "data"));
        foreach (var (name, bytes) in AcdUnpacker.Load(abarth!).Files)
            File.WriteAllBytes(Path.Combine(loose, "data", name), bytes);

        // encrypted: container parses, content is garbage
        var noise = new byte[2048];
        for (var i = 0; i < noise.Length; i++)
            noise[i] = (byte)(i * 197 + 31);
        File.WriteAllBytes(Path.Combine(CarDir("enc_mod"), "data.acd"),
            SyntheticAcd.Build(true, ("car.ini", noise), ("engine.ini", noise)));

        // broken container
        File.WriteAllBytes(Path.Combine(CarDir("broken_mod"), "data.acd"),
            new byte[] { 0xEF, 0xBE, 0xAD, 0x7E, 1, 2, 3, 4, 5, 6, 7, 8 });

        // no data at all
        CarDir("graphics_only_mod");

        return _carsRoot;
    }

    [SkippableFact]
    public void Classifies_all_six_categories_and_finds_no_core_bugs()
    {
        var carsRoot = BuildSyntheticRoot();
        var before = HashTree(carsRoot);

        var report = CarSurveyor.Survey(carsRoot);

        Assert.Equal(6, report.Cars.Count);
        Assert.Equal("kunos-packed", Entry(report, "abarth500").Classification);
        Assert.Equal("both", Entry(report, "bmw_m3_e30").Classification);
        Assert.Equal("loose-data", Entry(report, "mod_loose").Classification);
        Assert.Equal("encrypted", Entry(report, "enc_mod").Classification);
        Assert.Equal("broken-container", Entry(report, "broken_mod").Classification);
        Assert.Equal("no-data", Entry(report, "graphics_only_mod").Classification);

        Assert.Equal(3, report.BuildableCount);
        Assert.Equal(0, report.CoreBugCount);

        // Buildable cars: core files ok, tyres surveyed with a VERSION and keys.
        foreach (var name in new[] { "abarth500", "bmw_m3_e30", "mod_loose" })
        {
            var entry = Entry(report, name);
            Assert.Equal("ok", entry.FileChecks["car.ini"]);
            Assert.Equal("ok", entry.FileChecks["engine.ini"]);
            Assert.Equal("ok", entry.FileChecks["model"]);
            Assert.Empty(entry.RoundTripMismatches);
            Assert.NotNull(entry.TyresVersion);
            Assert.NotEmpty(entry.TyreCompoundKeys);
        }

        // ui probing: packed car has a partial ui_car.json; loose car has none.
        Assert.Equal("partial", Entry(report, "abarth500").UiStatus);
        Assert.Contains("torqueCurve", Entry(report, "abarth500").UiMissingFields);
        Assert.Equal("missing", Entry(report, "mod_loose").UiStatus);

        // M7 input: tyre keys aggregated per VERSION.
        Assert.NotEmpty(report.TyreCompoundKeysByVersion);

        // Absolute rule: the survey wrote nothing inside any car folder.
        var after = HashTree(carsRoot);
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
            Assert.Equal(hash, after[path]);

        // The report serializes (what the CLI writes).
        var json = JsonSerializer.Serialize(report);
        Assert.Contains("kunos-packed", json);
    }

    private static CarSurveyEntry Entry(SurveyReport report, string car)
        => report.Cars.Single(c => c.Car == car);

    private static Dictionary<string, string> HashTree(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(root, f),
                f => ModelTestUtil.Sha256(File.ReadAllBytes(f)),
                StringComparer.OrdinalIgnoreCase);
}
