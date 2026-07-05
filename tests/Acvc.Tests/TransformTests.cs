using System.Text;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;

namespace Acvc.Tests;

/// <summary>Table-driven per-transform tests over both fixture cars' in-memory models.</summary>
public class TransformTests
{
    public static TheoryData<string> CarNames => new() { "abarth500", "bmw_m3_e30" };

    private static CarModelSet LoadModels(string carName)
        => CarModelSet.FromFiles(ModelTestUtil.TryLoadFixtureCar(carName)!.Files);

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---- power.scale ---------------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void PowerScale_multiplies_every_row_and_preserves_lut_trivia(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);
        var source = models.PowerLut.Rows.ToList();
        var tail = Latin1(models.PowerLut.ToBytes())[^8..];

        PowerScaleTransform.Apply(models.PowerLut, 1.35);

        Assert.Equal(source.Count, models.PowerLut.RowCount);
        for (var i = 0; i < source.Count; i++)
        {
            Assert.Equal(source[i].Rpm, models.PowerLut.GetRow(i).Rpm);
            Assert.Equal(source[i].Value * 1.35, models.PowerLut.GetRow(i).Value, 6);
        }
        // Trailing blank lines and terminators survive (last row is torque 0 in both cars).
        Assert.EndsWith(tail, Latin1(models.PowerLut.ToBytes()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    [InlineData(double.NaN)]
    public void PowerScale_rejects_non_positive_or_non_finite_factors(double factor)
    {
        var lut = PowerLut.Parse("0|100\n1000|110\n");
        Assert.Throws<TransformException>(() => PowerScaleTransform.Apply(lut, factor));
    }

    // ---- power.curve ---------------------------------------------------------

    [Fact]
    public void PowerCurve_applies_factors_inclusively_within_each_range()
    {
        var lut = PowerLut.Parse("1000|10\n2000|20\n3000|30\n4000|40\n");
        PowerCurveTransform.Apply(lut, new[] { new PowerCurveRange(2000, 3000, 2.0) });
        Assert.Equal(new[] { (1000.0, 10.0), (2000.0, 40.0), (3000.0, 60.0), (4000.0, 40.0) }, lut.Rows);
    }

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void PowerCurve_shapes_only_rows_inside_ranges(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);
        var source = models.PowerLut.Rows.ToList();
        var ranges = new[] { new PowerCurveRange(0, 3000, 1.1), new PowerCurveRange(5000, 6000, 1.2) };

        PowerCurveTransform.Apply(models.PowerLut, ranges);

        for (var i = 0; i < source.Count; i++)
        {
            var rpm = source[i].Rpm;
            var factor = rpm is >= 0 and <= 3000 ? 1.1 : rpm is >= 5000 and <= 6000 ? 1.2 : 1.0;
            Assert.Equal(source[i].Value * factor, models.PowerLut.GetRow(i).Value, 6);
        }
        // abarth500's -3000 row sits outside [0..3000] and must be untouched.
        if (carName == "abarth500")
            Assert.Equal(source[0].Value, models.PowerLut.GetRow(0).Value);
    }

    [Fact]
    public void PowerCurve_row_exactly_on_a_range_endpoint_gets_the_factor()
    {
        // Semantics (documented in CLAUDE.md): from/to are both inclusive.
        var lut = PowerLut.Parse("1000|10\n2000|20\n3000|30\n");
        PowerCurveTransform.Apply(lut, new[] { new PowerCurveRange(1000, 3000, 2.0) });
        Assert.Equal(new[] { (1000.0, 20.0), (2000.0, 40.0), (3000.0, 60.0) }, lut.Rows);
    }

    [Fact]
    public void PowerCurve_row_between_two_listed_ranges_is_untouched()
    {
        var lut = PowerLut.Parse("1000|10\n2000|20\n3000|30\n4000|40\n5000|50\n");
        PowerCurveTransform.Apply(lut, new[]
        {
            new PowerCurveRange(1000, 2000, 2.0),
            new PowerCurveRange(4000, 5000, 3.0),
        });
        // 3000 sits in the gap: factor 1.0, untouched.
        Assert.Equal(new[] { (1000.0, 20.0), (2000.0, 40.0), (3000.0, 30.0), (4000.0, 120.0), (5000.0, 150.0) }, lut.Rows);
    }

    [Theory]
    [InlineData(3000, 5000, 1.1, 5000, 6000, 1.2)]  // shared boundary = overlap
    [InlineData(1000, 4000, 1.1, 2000, 3000, 1.2)]  // nested
    public void PowerCurve_rejects_overlapping_ranges(
        double from1, double to1, double f1, double from2, double to2, double f2)
    {
        var lut = PowerLut.Parse("1000|10\n");
        var ranges = new[] { new PowerCurveRange(from1, to1, f1), new PowerCurveRange(from2, to2, f2) };
        var ex = Assert.Throws<TransformException>(() => PowerCurveTransform.Apply(lut, ranges));
        Assert.Contains("overlap", ex.Message);
    }

    [Theory]
    [InlineData(5000, 3000, 1.1)]  // from >= to
    [InlineData(3000, 3000, 1.1)]
    [InlineData(1000, 2000, 0)]    // factor must be positive
    [InlineData(1000, 2000, -2)]
    public void PowerCurve_rejects_malformed_ranges(double from, double to, double factor)
    {
        var lut = PowerLut.Parse("1000|10\n");
        Assert.Throws<TransformException>(
            () => PowerCurveTransform.Apply(lut, new[] { new PowerCurveRange(from, to, factor) }));
    }

    // ---- engine.limiter ------------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void EngineLimiter_sets_the_value_in_place(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);

        EngineLimiterTransform.Apply(models.Engine, 7000);

        Assert.Equal(7000, models.Engine.Limiter);
        Assert.Contains("LIMITER=7000", Latin1(models.Engine.ToBytes()));
    }

    [Fact]
    public void EngineLimiter_rejects_non_positive_rpm()
    {
        var engine = EngineIni.Parse(Encoding.Latin1.GetBytes("[ENGINE_DATA]\nLIMITER=6000\n"));
        Assert.Throws<TransformException>(() => EngineLimiterTransform.Apply(engine, 0));
        Assert.Throws<TransformException>(() => EngineLimiterTransform.Apply(engine, -100));
    }

    // ---- engine.boost --------------------------------------------------------

    [SkippableFact]
    public void EngineBoost_sets_max_and_wastegate_in_turbo_section()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels("abarth500");

        EngineBoostTransform.Apply(models.Engine, new BoostSpec(1.5, 1.3));

        var turbo = Assert.Single(models.Engine.Turbos);
        Assert.Equal(1.5, turbo.MaxBoost);
        Assert.Equal(1.3, turbo.Wastegate);
        // Source texts are 1.38/1.18 — two decimal places are preserved.
        var text = Latin1(models.Engine.ToBytes());
        Assert.Contains("MAX_BOOST=1.50", text);
        Assert.Contains("WASTEGATE=1.30", text);
    }

    [SkippableFact]
    public void EngineBoost_on_a_naturally_aspirated_car_is_a_hard_error()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("bmw_m3_e30") is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels("bmw_m3_e30");

        var ex = Assert.Throws<TransformException>(
            () => EngineBoostTransform.Apply(models.Engine, new BoostSpec(1.4, 1.4)));
        Assert.Contains("TURBO", ex.Message);
        Assert.Contains("naturally aspirated", ex.Message);
    }

    // ---- drivetrain.final ----------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void DrivetrainFinal_sets_ratio_preserving_source_decimal_style(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);

        DrivetrainFinalTransform.Apply(models.Drivetrain, 4.1);

        Assert.Equal(4.1, models.Drivetrain.FinalRatio);
        var text = Latin1(models.Drivetrain.ToBytes());
        // abarth500 writes FINAL=3.353000 (6 places), bmw_m3_e30 writes FINAL=3.15 (2).
        Assert.Contains(carName == "abarth500" ? "FINAL=4.100000" : "FINAL=4.10", text);
    }

    [Fact]
    public void DrivetrainFinal_rejects_non_positive_ratio()
    {
        var dt = DrivetrainIni.Parse(Encoding.Latin1.GetBytes("[GEARS]\nCOUNT=1\nGEAR_1=3.0\nFINAL=4.0\n"));
        Assert.Throws<TransformException>(() => DrivetrainFinalTransform.Apply(dt, 0));
    }

    // ---- drivetrain.gears ----------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void DrivetrainGears_sets_every_forward_ratio(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);
        var ratios = new[] { 3.2, 2.1, 1.5, 1.1, 0.9 };  // both fixture cars have COUNT=5

        DrivetrainGearsTransform.Apply(models.Drivetrain, ratios);

        for (var gear = 1; gear <= 5; gear++)
            Assert.Equal(ratios[gear - 1], models.Drivetrain.GetGearRatio(gear));
    }

    [SkippableFact]
    public void DrivetrainGears_count_mismatch_is_a_hard_error()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels("abarth500");

        var ex = Assert.Throws<TransformException>(
            () => DrivetrainGearsTransform.Apply(models.Drivetrain, new[] { 3.2, 2.1 }));
        Assert.Contains("COUNT=5", ex.Message);
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void DrivetrainGears_rejects_non_positive_ratios()
    {
        var dt = DrivetrainIni.Parse(Encoding.Latin1.GetBytes("[GEARS]\nCOUNT=2\nGEAR_1=3.0\nGEAR_2=2.0\nFINAL=4.0\n"));
        Assert.Throws<TransformException>(() => DrivetrainGearsTransform.Apply(dt, new[] { 3.0, -2.0 }));
    }

    // ---- mass.total ----------------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void MassTotal_sets_totalmass(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);

        MassTotalTransform.Apply(models.Car, 1400);

        Assert.Equal(1400.0, models.Car.TotalMass);
        Assert.Contains("TOTALMASS=1400", Latin1(models.Car.ToBytes()));
    }

    [Fact]
    public void MassTotal_rejects_non_finite()
    {
        var car = CarIni.Parse(Encoding.Latin1.GetBytes("[BASIC]\nTOTALMASS=1000\n"));
        Assert.Throws<TransformException>(() => MassTotalTransform.Apply(car, double.PositiveInfinity));
    }
}
