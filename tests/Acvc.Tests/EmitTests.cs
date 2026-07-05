using System.Text;
using Acvc.Core.Acd;
using Acvc.Core.Emit;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;

namespace Acvc.Tests;

/// <summary>
/// Milestone 4 gate: variant folders emitted to temp dirs around both fixture cars'
/// real data.acd, wrapped in a synthetic-but-realistic car folder scaffold (kn5, sfx
/// with GUIDs/banks, two skins, ui) so every structural rule is assertable.
/// </summary>
public class EmitTests : IDisposable
{
    private const string SpecMarker = "# spec-marker-3f9c";
    private readonly string _root;

    public EmitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "acvc-emit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true); // does not follow junctions
        }
        catch
        {
            // best effort
        }
    }

    public static TheoryData<string> CarNames => new() { "abarth500", "bmw_m3_e30" };

    // ---- scaffold -------------------------------------------------------------

    private static string UiJsonText(string carName)
        => "{\n\t\"name\": \"Fake " + carName + "\",\n\t\"brand\": \"Kunos\",\n\t\"class\": \"street\"\n}";

    /// <summary>Realistic source car folder: real fixture data.acd + fake everything else.</summary>
    private string CreateSourceCar(string carName)
    {
        var fixture = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals(carName, StringComparison.OrdinalIgnoreCase));
        Skip.If(fixture is null, ModelTestUtil.FixtureSkipReason);

        var car = Path.Combine(_root, "cars", carName);
        Directory.CreateDirectory(car);
        File.Copy(Path.Combine(fixture!, "data.acd"), Path.Combine(car, "data.acd"));

        File.WriteAllBytes(Path.Combine(car, $"{carName}.kn5"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(car, "collider.kn5"), new byte[] { 4, 5 });
        File.WriteAllBytes(Path.Combine(car, "body_shadow.png"), new byte[] { 6 });

        Directory.CreateDirectory(Path.Combine(car, "animations"));
        File.WriteAllBytes(Path.Combine(car, "animations", "car_door_L.ksanim"), new byte[] { 7 });

        Directory.CreateDirectory(Path.Combine(car, "sfx"));
        File.WriteAllText(Path.Combine(car, "sfx", "GUIDs.txt"),
            $"{{1111}} event:/cars/{carName}/engine_int\n" +
            $"{{2222}} event:/cars/{carName}/engine_ext\n" +
            $"{{3333}} event:/cars/{carName}/gear\n" +
            $"{{4444}} bank:/{carName}\n");
        File.WriteAllBytes(Path.Combine(car, "sfx", $"{carName}.bank"), new byte[] { 8, 9 });
        File.WriteAllBytes(Path.Combine(car, "sfx", "unrelated.bank"), new byte[] { 10 });

        Directory.CreateDirectory(Path.Combine(car, "skins", "alpha_skin"));
        File.WriteAllText(Path.Combine(car, "skins", "alpha_skin", "skin.ini"), "[SKIN]\nNAME=Alpha\n");
        File.WriteAllBytes(Path.Combine(car, "skins", "alpha_skin", "preview.jpg"), new byte[] { 11 });
        Directory.CreateDirectory(Path.Combine(car, "skins", "beta_skin"));
        File.WriteAllText(Path.Combine(car, "skins", "beta_skin", "skin.ini"), "[SKIN]\nNAME=Beta\n");

        Directory.CreateDirectory(Path.Combine(car, "ui"));
        File.WriteAllText(Path.Combine(car, "ui", "ui_car.json"), UiJsonText(carName), new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(car, "ui", "badge.png"), new byte[] { 12 });

        return car;
    }

    private static Dictionary<string, byte[]> TransformedFiles(string sourceCar, double mass = 1234)
    {
        var files = AcdUnpacker.Load(sourceCar).Files;
        var models = CarModelSet.FromFiles(files);
        var plan = new TunePlan
        {
            SourceCar = Path.GetFileName(sourceCar),
            TuneName = "test_tune",
            MassTotal = mass,
        };
        var validation = TunePipeline.Apply(plan, models);
        Assert.False(validation.HasFailures);
        return models.MergedInto(files);
    }

    private EmitOptions Options(bool force = false, SkinsMode skins = SkinsMode.CopyFirst) => new()
    {
        OutRoot = Path.Combine(_root, "out"),
        Force = force,
        SkinsMode = skins,
        UiNameSuffix = " — test_tune",
        SpecText = $"[meta]\nsource_car = \"x\"\ntune_name = \"test_tune\"\n{SpecMarker}\n",
        SpecFileName = "test_tune.toml",
    };

    private static Dictionary<string, string> HashTree(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(root, f),
                f => ModelTestUtil.Sha256(File.ReadAllBytes(f)),
                StringComparer.OrdinalIgnoreCase);

    // ---- structure -------------------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void Variant_structure_is_complete_and_correct(string carName)
    {
        var source = CreateSourceCar(carName);
        var data = TransformedFiles(source);
        var variantName = $"{carName}_test_tune";

        var result = VariantEmitter.Emit(source, variantName, data, Options());
        var variant = result.VariantPath;

        // No data.acd; loose transformed data/ only.
        Assert.False(File.Exists(Path.Combine(variant, "data.acd")));
        Assert.Equal(data.Count, Directory.GetFiles(Path.Combine(variant, "data")).Length);
        Assert.Contains("TOTALMASS=1234",
            Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(variant, "data", "car.ini"))));

        // Model/collision/graphics assets came along, names unchanged (lods.ini refers to them).
        Assert.True(File.Exists(Path.Combine(variant, $"{carName}.kn5")));
        Assert.True(File.Exists(Path.Combine(variant, "collider.kn5")));
        Assert.True(File.Exists(Path.Combine(variant, "animations", "car_door_L.ksanim")));
        Assert.True(File.Exists(Path.Combine(variant, "ui", "badge.png")));

        // Audio identity: zero source-name occurrences left in GUIDs.txt (the variant
        // name embeds the source name, so strip variant-name hits first), bank renamed.
        var guids = File.ReadAllText(Path.Combine(variant, "sfx", "GUIDs.txt"));
        Assert.DoesNotContain(carName, guids.Replace(variantName, ""));
        Assert.Equal(4, guids.Split(variantName).Length - 1);
        Assert.Contains("4 occurrence", result.AudioNote);
        Assert.True(File.Exists(Path.Combine(variant, "sfx", $"{variantName}.bank")));
        Assert.False(File.Exists(Path.Combine(variant, "sfx", $"{carName}.bank")));
        Assert.True(File.Exists(Path.Combine(variant, "sfx", "unrelated.bank")));

        // Exactly one skin, the alphabetical first.
        var skins = Directory.GetDirectories(Path.Combine(variant, "skins"));
        var skin = Assert.Single(skins);
        Assert.Equal("alpha_skin", Path.GetFileName(skin));
        Assert.True(File.Exists(Path.Combine(skin, "skin.ini")));

        // Readme: source car, spec text, tool version.
        var readme = File.ReadAllText(Path.Combine(variant, "readme.txt"));
        Assert.Contains(carName, readme);
        Assert.Contains(SpecMarker, readme);
        Assert.Contains("0.4.0", readme);

        // ui_car.json: name suffixed, everything else byte-identical.
        var expectedUi = Encoding.UTF8.GetBytes(
            UiJsonText(carName).Replace($"Fake {carName}\"", $"Fake {carName} — test_tune\""));
        Assert.Equal(expectedUi, File.ReadAllBytes(Path.Combine(variant, "ui", "ui_car.json")));
    }

    [SkippableFact]
    public void Source_folder_is_untouched_by_an_emit()
    {
        var source = CreateSourceCar("abarth500");
        var before = HashTree(source);

        VariantEmitter.Emit(source, "abarth500_test_tune", TransformedFiles(source), Options());

        var after = HashTree(source);
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
            Assert.Equal(hash, after[path]);
    }

    // ---- collision & force -------------------------------------------------------

    [SkippableFact]
    public void Existing_variant_is_refused_without_force()
    {
        var source = CreateSourceCar("abarth500");
        var data = TransformedFiles(source);
        var first = VariantEmitter.Emit(source, "abarth500_test_tune", data, Options());
        var readmeBefore = File.ReadAllBytes(Path.Combine(first.VariantPath, "readme.txt"));

        var ex = Assert.Throws<EmitException>(
            () => VariantEmitter.Emit(source, "abarth500_test_tune", data, Options()));

        Assert.Contains("--force", ex.Message);
        Assert.Equal(readmeBefore, File.ReadAllBytes(Path.Combine(first.VariantPath, "readme.txt")));
    }

    [SkippableFact]
    public void Force_replaces_the_whole_folder_never_merges()
    {
        var source = CreateSourceCar("abarth500");
        var first = VariantEmitter.Emit(source, "abarth500_test_tune", TransformedFiles(source), Options());
        File.WriteAllText(Path.Combine(first.VariantPath, "stale.txt"), "left over");

        var second = VariantEmitter.Emit(source, "abarth500_test_tune",
            TransformedFiles(source, mass: 1300), Options(force: true));

        Assert.False(File.Exists(Path.Combine(second.VariantPath, "stale.txt")));
        Assert.Contains("TOTALMASS=1300",
            Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(second.VariantPath, "data", "car.ini"))));
    }

    [SkippableFact]
    public void Failed_build_leaves_no_target_and_no_temp_residue()
    {
        var source = CreateSourceCar("abarth500");
        var bad = TransformedFiles(source);
        bad["bad|name.ini"] = new byte[] { 1 }; // '|' is invalid on Windows — fails mid-build

        Assert.Throws<EmitException>(
            () => VariantEmitter.Emit(source, "abarth500_test_tune", bad, Options()));

        var outRoot = Path.Combine(_root, "out");
        Assert.False(Directory.Exists(Path.Combine(outRoot, "abarth500_test_tune")));
        Assert.Empty(Directory.GetFileSystemEntries(outRoot));
    }

    [SkippableFact]
    public void Failed_force_rebuild_preserves_the_previous_variant_intact()
    {
        var source = CreateSourceCar("abarth500");
        var good = VariantEmitter.Emit(source, "abarth500_test_tune", TransformedFiles(source), Options());
        var before = HashTree(good.VariantPath);

        var bad = TransformedFiles(source, mass: 1300);
        bad["bad|name.ini"] = new byte[] { 1 };
        Assert.Throws<EmitException>(
            () => VariantEmitter.Emit(source, "abarth500_test_tune", bad, Options(force: true)));

        var after = HashTree(good.VariantPath);
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
            Assert.Equal(hash, after[path]);
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "out"))); // no .acvc-* residue
    }

    // ---- junctions -----------------------------------------------------------------

    [SkippableFact]
    public void Junction_mode_links_every_source_skin()
    {
        var source = CreateSourceCar("abarth500");

        var result = VariantEmitter.Emit(source, "abarth500_test_tune",
            TransformedFiles(source), Options(skins: SkinsMode.Junction));

        var skins = Directory.GetDirectories(Path.Combine(result.VariantPath, "skins"))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(2, skins.Length);

        foreach (var skin in skins)
        {
            var info = new DirectoryInfo(skin);
            Assert.True(info.Attributes.HasFlag(FileAttributes.ReparsePoint), $"{skin} is not a reparse point");
            var target = Directory.ResolveLinkTarget(skin, returnFinalTarget: false);
            Assert.NotNull(target);
            Assert.Equal(
                Path.Combine(source, "skins", Path.GetFileName(skin)),
                target!.FullName,
                ignoreCase: true);
            // Content is reachable through the junction.
            Assert.True(File.Exists(Path.Combine(skin, "skin.ini")));
        }
    }

    // ---- Kunos-style audio (no per-car GUIDs.txt) --------------------------------

    [SkippableFact]
    public void Kunos_style_source_gets_guids_generated_from_the_global_file()
    {
        var source = CreateSourceCar("abarth500");
        File.Delete(Path.Combine(source, "sfx", "GUIDs.txt")); // Kunos cars have none

        // Install-global map at <root>/sfx/GUIDs.txt (source is <root>/cars/abarth500),
        // with prefix-collision decoys that exact-token matching must NOT capture.
        Directory.CreateDirectory(Path.Combine(_root, "sfx"));
        File.WriteAllText(Path.Combine(_root, "sfx", "GUIDs.txt"),
            "{aaaa} bank:/abarth500\n" +
            "{aaaa} bank:/abarth500_s1\n" +
            "{bbbb} bank:/ks_abarth500_assetto_corse\n" +
            "{cccc} event:/cars/abarth500/engine_ext\n" +
            "{dddd} event:/cars/abarth500/gear\n" +
            "{eeee} event:/cars/abarth500_s1/engine_ext\n" +
            "{ffff} event:/cars/other_car/engine_ext\n");

        var result = VariantEmitter.Emit(source, "abarth500_test_tune", TransformedFiles(source), Options());

        Assert.Contains("generated", result.AudioNote);
        var generated = File.ReadAllText(Path.Combine(result.VariantPath, "sfx", "GUIDs.txt"));
        Assert.Equal(
            "{aaaa} bank:/abarth500_test_tune\n" +
            "{cccc} event:/cars/abarth500_test_tune/engine_ext\n" +
            "{dddd} event:/cars/abarth500_test_tune/gear\n",
            generated);
        Assert.True(File.Exists(Path.Combine(result.VariantPath, "sfx", "abarth500_test_tune.bank")));
    }

    [SkippableFact]
    public void No_guids_source_anywhere_refuses_to_emit_a_silent_car()
    {
        var source = CreateSourceCar("abarth500");
        File.Delete(Path.Combine(source, "sfx", "GUIDs.txt"));
        // No <root>/sfx/GUIDs.txt either.

        var ex = Assert.Throws<EmitException>(
            () => VariantEmitter.Emit(source, "abarth500_test_tune", TransformedFiles(source), Options()));
        Assert.Contains("silent", ex.Message);

        var outRoot = Path.Combine(_root, "out");
        Assert.False(Directory.Exists(Path.Combine(outRoot, "abarth500_test_tune")));
        Assert.Empty(Directory.GetFileSystemEntries(outRoot));
    }

    // ---- guards ---------------------------------------------------------------------

    [SkippableFact]
    public void Variant_name_equal_to_source_name_is_refused()
    {
        var source = CreateSourceCar("abarth500");
        var ex = Assert.Throws<EmitException>(
            () => VariantEmitter.Emit(source, "abarth500", TransformedFiles(source), Options()));
        Assert.Contains("source", ex.Message);
    }
}
