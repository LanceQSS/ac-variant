using System.Text;
using Acvc.Core.Emit;
using Acvc.Core.Install;
using Acvc.Core.Spec;
using Acvc.Core.Survey;

namespace Acvc.Tests;

public class TuneSpecWriterTests
{
    private static readonly TunePlan FullPlan = new()
    {
        SourceCar = "abarth500",
        TuneName = "full_house",
        PowerScale = 1.35,
        PowerCurve = new[] { new PowerCurveRange(3000, 5000, 1.1) },
        Limiter = 7400,
        Boost = new BoostSpec(1.4, 1.4),
        FinalDrive = 3.9,
        Gears = new[] { 3.2, 2.1, 1.5, 1.1, 0.9 },
        MassTotal = 1420,
        GripScale = 1.1,
        BrakeTorqueScale = 1.15,
        DiffPower = 0.45,
        DiffCoast = 0.25,
    };

    [Fact]
    public void Full_plan_roundtrips_through_write_and_parse()
    {
        var reparsed = TuneSpecParser.Parse(TuneSpecWriter.Write(FullPlan));

        Assert.Equal(FullPlan.SourceCar, reparsed.SourceCar);
        Assert.Equal(FullPlan.TuneName, reparsed.TuneName);
        Assert.Equal(FullPlan.PowerScale, reparsed.PowerScale);
        Assert.Equal(FullPlan.PowerCurve!, reparsed.PowerCurve!);
        Assert.Equal(FullPlan.Limiter, reparsed.Limiter);
        Assert.Equal(FullPlan.Boost, reparsed.Boost);
        Assert.Equal(FullPlan.FinalDrive, reparsed.FinalDrive);
        Assert.Equal(FullPlan.Gears!, reparsed.Gears!);
        Assert.Equal(FullPlan.MassTotal, reparsed.MassTotal);
        Assert.Equal(FullPlan.GripScale, reparsed.GripScale);
        Assert.Equal(FullPlan.BrakeTorqueScale, reparsed.BrakeTorqueScale);
        Assert.Equal(FullPlan.DiffPower, reparsed.DiffPower);
        Assert.Equal(FullPlan.DiffCoast, reparsed.DiffCoast);
    }

    [Fact]
    public void Minimal_plan_writes_meta_only()
    {
        var text = TuneSpecWriter.Write(new TunePlan { SourceCar = "a", TuneName = "t" });
        Assert.Contains("[meta]", text);
        Assert.DoesNotContain("[power]", text);
        Assert.DoesNotContain("[diff]", text);

        var reparsed = TuneSpecParser.Parse(text);
        Assert.Null(reparsed.PowerScale);
        Assert.Null(reparsed.DiffCoast);
    }

    [Theory]
    [InlineData("street_600", true)]
    [InlineData("Street-600", true)]
    [InlineData("has space", false)]
    [InlineData("dots.bad", false)]
    [InlineData("", false)]
    public void Tune_name_rule_is_exposed_for_live_validation(string name, bool valid)
        => Assert.Equal(valid, TuneSpecParser.IsValidTuneName(name));
}

public class SteamVdfTests
{
    [Fact]
    public void Parses_library_paths_with_unescaping()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                }
            }
            """;
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" },
            SteamVdf.ParseLibraryPaths(vdf));
    }
}

public class CarCatalogTests : IDisposable
{
    private readonly string _root;

    public CarCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "acvc-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "content", "cars"));
        Directory.CreateDirectory(Path.Combine(_root, "content", "sfx"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [SkippableFact]
    public void Classifies_and_detects_kunos_via_global_guids()
    {
        var fixture = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("abarth500", StringComparison.OrdinalIgnoreCase));
        Skip.If(fixture is null, ModelTestUtil.FixtureSkipReason);

        var cars = Path.Combine(_root, "content", "cars");
        Directory.CreateDirectory(Path.Combine(cars, "abarth500"));
        File.Copy(Path.Combine(fixture!, "data.acd"), Path.Combine(cars, "abarth500", "data.acd"));

        Directory.CreateDirectory(Path.Combine(cars, "some_mod", "data"));
        File.WriteAllText(Path.Combine(cars, "some_mod", "data", "car.ini"), "[BASIC]\nTOTALMASS=1000\n");

        var noise = new byte[2048];
        for (var i = 0; i < noise.Length; i++)
            noise[i] = (byte)(i * 197 + 31);
        Directory.CreateDirectory(Path.Combine(cars, "enc_mod"));
        File.WriteAllBytes(Path.Combine(cars, "enc_mod", "data.acd"),
            SyntheticAcd.Build(true, ("car.ini", noise), ("engine.ini", noise)));

        // Kunos marker: abarth500's events registered in the global map, mods absent.
        File.WriteAllText(Path.Combine(_root, "content", "sfx", "GUIDs.txt"),
            "{aaaa} event:/cars/abarth500/engine_ext\n{bbbb} event:/cars/abarth500/gear\n");

        var list = CarCatalog.List(_root);

        var abarth = list.Single(c => c.Name == "abarth500");
        Assert.Equal("kunos-packed", abarth.Classification);
        Assert.True(abarth.IsKunos);
        Assert.True(abarth.IsBuildable);

        var mod = list.Single(c => c.Name == "some_mod");
        Assert.Equal("loose-data", mod.Classification);
        Assert.False(mod.IsKunos);
        Assert.True(mod.IsBuildable);

        var encrypted = list.Single(c => c.Name == "enc_mod");
        Assert.Equal("encrypted", encrypted.Classification);
        Assert.False(encrypted.IsBuildable);
        Assert.Contains("CSP/x4fab", encrypted.Reason!);
    }

    [SkippableFact]
    public void Cache_short_circuits_matching_acd_and_invalidates_on_change()
    {
        var fixture = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("abarth500", StringComparison.OrdinalIgnoreCase));
        Skip.If(fixture is null, ModelTestUtil.FixtureSkipReason);

        var cars = Path.Combine(_root, "content", "cars");
        var carDir = Path.Combine(cars, "abarth500");
        Directory.CreateDirectory(carDir);
        var acdPath = Path.Combine(carDir, "data.acd");
        File.Copy(Path.Combine(fixture!, "data.acd"), acdPath);
        var cachePath = Path.Combine(_root, "cache.json");

        // First scan: classifies for real and writes the cache.
        var first = CarCatalog.List(_root, cachePath).Single();
        Assert.Equal("kunos-packed", first.Classification);
        Assert.True(File.Exists(cachePath));

        // Plant a fake verdict with the CORRECT size/mtime key: a hit must be
        // trusted without re-decrypting — this proves the short-circuit is real.
        var info = new FileInfo(acdPath);
        File.WriteAllText(cachePath,
            $"{{\"abarth500\":{{\"Size\":{info.Length},\"MtimeTicks\":{info.LastWriteTimeUtc.Ticks}," +
            "\"Verdict\":\"encrypted\",\"Reason\":\"planted-by-test\"}}");
        var cached = CarCatalog.List(_root, cachePath).Single();
        Assert.Equal("encrypted", cached.Classification);
        Assert.Equal("planted-by-test", cached.Reason);

        // Touch the archive: the key no longer matches, so it re-classifies honestly.
        File.SetLastWriteTimeUtc(acdPath, DateTime.UtcNow.AddMinutes(1));
        var refreshed = CarCatalog.List(_root, cachePath).Single();
        Assert.Equal("kunos-packed", refreshed.Classification);

        // Corrupt cache is discarded, never fatal.
        File.WriteAllText(cachePath, "{not json");
        Assert.Equal("kunos-packed", CarCatalog.List(_root, cachePath).Single().Classification);
    }
}

public class VariantBuilderTests : IDisposable
{
    private readonly string _root;

    public VariantBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "acvc-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [SkippableFact]
    public void Validation_failure_returns_outcome_without_writing_anything()
    {
        var fixture = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("abarth500", StringComparison.OrdinalIgnoreCase));
        Skip.If(fixture is null, ModelTestUtil.FixtureSkipReason);

        // Minimal buildable source car; data.acd only is enough for the failure path.
        var source = Path.Combine(_root, "abarth500");
        Directory.CreateDirectory(source);
        File.Copy(Path.Combine(fixture!, "data.acd"), Path.Combine(source, "data.acd"));

        var outRoot = Path.Combine(_root, "out");
        var plan = new TunePlan { SourceCar = "abarth500", TuneName = "bad", MassTotal = -5 };

        var outcome = VariantBuilder.Build(source, plan, outRoot, force: false,
            SkinsMode.CopyFirst, "[meta]\n", "bad.toml");

        Assert.True(outcome.Validation.HasFailures);
        Assert.Null(outcome.Emit);
        Assert.Null(outcome.UiPatch);
        Assert.False(Directory.Exists(outRoot) && Directory.GetFileSystemEntries(outRoot).Length > 0);
    }
}
