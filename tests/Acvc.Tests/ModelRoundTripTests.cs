using Acvc.Core.Model;

namespace Acvc.Tests;

/// <summary>
/// Milestone 2 gate part 1: read→write with no mutation is byte-identical (compared
/// by SHA-256) for engine.ini, drivetrain.ini, car.ini and power.lut of both fixture
/// cars — and, beyond the required four, for every .ini/.lut in both archives.
/// </summary>
public class ModelRoundTripTests
{
    public static TheoryData<string> CarNames => new() { "abarth500", "bmw_m3_e30" };

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void Required_files_roundtrip_byte_identical(string carName)
    {
        var data = ModelTestUtil.TryLoadFixtureCar(carName);
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        foreach (var name in new[] { "engine.ini", "drivetrain.ini", "car.ini" })
        {
            var original = data!.Files[name];
            var emitted = IniDocument.Parse(original, name).ToBytes();
            Assert.True(ModelTestUtil.Sha256(original) == ModelTestUtil.Sha256(emitted),
                $"{carName}/{name}: round-trip is not byte-identical.");
        }

        var lutOriginal = data!.Files["power.lut"];
        var lutEmitted = PowerLut.Parse(lutOriginal, "power.lut").ToBytes();
        Assert.True(ModelTestUtil.Sha256(lutOriginal) == ModelTestUtil.Sha256(lutEmitted),
            $"{carName}/power.lut: round-trip is not byte-identical.");
    }

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void Every_ini_and_lut_in_the_archive_roundtrips_byte_identical(string carName)
    {
        var data = ModelTestUtil.TryLoadFixtureCar(carName);
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        var failures = new List<string>();
        foreach (var (name, original) in data!.Files)
        {
            byte[] emitted;
            if (name.EndsWith(".lut", StringComparison.OrdinalIgnoreCase))
                emitted = PowerLut.Parse(original, name).ToBytes();
            else if (name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                emitted = IniDocument.Parse(original, name).ToBytes();
            else
                continue;

            if (ModelTestUtil.Sha256(original) != ModelTestUtil.Sha256(emitted))
                failures.Add(name);
        }

        Assert.True(failures.Count == 0,
            $"{carName}: files not byte-identical after round-trip: {string.Join(", ", failures)}");
    }
}
