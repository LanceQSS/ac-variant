using System.Text;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;

namespace Acvc.Tests;

/// <summary>
/// The full CLAUDE.md example spec (source car adjusted to a fixture car), parsed
/// from TOML and pushed through the whole pipeline; every resulting value asserted.
/// </summary>
public class TuneIntegrationTests
{
    private const string Spec = """
        [meta]
        source_car = "abarth500"
        tune_name  = "street_600"

        [power]
        scale = 1.35

        [engine]
        limiter = 7400
        boost = { max = 1.4, wastegate = 1.4 }

        [drivetrain]
        final = 3.90

        [mass]
        total = 1420
        """;

    [SkippableFact]
    public void Example_spec_applies_end_to_end_with_every_value_correct()
    {
        var data = ModelTestUtil.TryLoadFixtureCar("abarth500");
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        var plan = TuneSpecParser.Parse(Spec);
        var models = CarModelSet.FromFiles(data!.Files);
        var sourceRows = models.PowerLut.Rows.ToList();

        var result = TunePipeline.Apply(plan, models);

        // Validation: comfortably inside every threshold — no findings at all.
        Assert.Empty(result.Issues);

        // mass.total: 1100 -> 1420.
        Assert.Equal(1420.0, models.Car.TotalMass);
        Assert.Contains("TOTALMASS=1420\t", Encoding.Latin1.GetString(models.Car.ToBytes()));

        // engine.limiter: 6500 -> 7400 (a +13.8% raise — under the 20% warning line).
        Assert.Equal(7400, models.Engine.Limiter);

        // engine.boost: 1.38/1.18 -> 1.40/1.40, two decimal places preserved.
        var turbo = Assert.Single(models.Engine.Turbos);
        Assert.Equal(1.4, turbo.MaxBoost);
        Assert.Equal(1.4, turbo.Wastegate);
        var engineText = Encoding.Latin1.GetString(models.Engine.ToBytes());
        Assert.Contains("MAX_BOOST=1.40", engineText);
        Assert.Contains("WASTEGATE=1.40", engineText);

        // drivetrain.final: 3.353000 -> 3.900000, six places preserved, comment intact.
        Assert.Equal(3.9, models.Drivetrain.FinalRatio);
        var drivetrainText = Encoding.Latin1.GetString(models.Drivetrain.ToBytes());
        Assert.Contains("FINAL=3.900000\t\t; final gear ratio", drivetrainText);

        // power.scale: all 34 rows × 1.35, rpm column untouched.
        Assert.Equal(34, models.PowerLut.RowCount);
        for (var i = 0; i < sourceRows.Count; i++)
        {
            var (rpm, value) = models.PowerLut.GetRow(i);
            Assert.Equal(sourceRows[i].Rpm, rpm);
            Assert.Equal(sourceRows[i].Value * 1.35, value, 6);
        }
        // Hand-checked spots: first row -3000|50 -> 67.5; last row 7750|0 stays 0.
        Assert.Equal((-3000.0, 67.5), models.PowerLut.GetRow(0));
        Assert.Equal((7750.0, 0.0), models.PowerLut.GetRow(33));

        // Untouched files stay untouched: gears were not in the spec.
        Assert.Equal(3.909, models.Drivetrain.GetGearRatio(1));
    }

    private const string AllTenSpec = """
        [meta]
        source_car = "abarth500"
        tune_name  = "full_house"

        [power]
        scale = 1.35
        curve = [ { from = 3000, to = 5000, factor = 1.1 } ]

        [engine]
        limiter = 7400
        boost = { max = 1.4, wastegate = 1.4 }

        [drivetrain]
        final = 3.90
        gears = [3.2, 2.1, 1.5, 1.1, 0.9]

        [mass]
        total = 1420

        [tyres]
        grip_scale = 1.10

        [brakes]
        torque_scale = 1.15

        [diff]
        power = 0.45
        coast = 0.25
        """;

    [SkippableFact]
    public void All_ten_transforms_apply_from_one_spec_with_clean_validation()
    {
        var data = ModelTestUtil.TryLoadFixtureCar("abarth500");
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        var plan = TuneSpecParser.Parse(AllTenSpec);
        var models = CarModelSet.FromFiles(data!.Files);
        var sourceRows = models.PowerLut.Rows.ToList();
        var sourceGrip = models.Tyres!.CompoundSections
            .ToDictionary(s => s, s => models.Tyres.GripValues(s));
        var sourceBrake = models.Brakes!.MaxTorque;

        var result = TunePipeline.Apply(plan, models);
        Assert.Empty(result.Issues); // every value chosen inside all thresholds

        // 1+2: power.scale × power.curve (inclusive 3000..5000).
        for (var i = 0; i < sourceRows.Count; i++)
        {
            var factor = sourceRows[i].Rpm is >= 3000 and <= 5000 ? 1.35 * 1.1 : 1.35;
            Assert.Equal(sourceRows[i].Value * factor, models.PowerLut.GetRow(i).Value, 6);
        }
        // 3: limiter. 4: boost.
        Assert.Equal(7400, models.Engine.Limiter);
        var turbo = Assert.Single(models.Engine.Turbos);
        Assert.Equal(1.4, turbo.MaxBoost);
        Assert.Equal(1.4, turbo.Wastegate);
        // 5: final. 6: gears.
        Assert.Equal(3.9, models.Drivetrain.FinalRatio);
        Assert.Equal(new[] { 3.2, 2.1, 1.5, 1.1, 0.9 },
            Enumerable.Range(1, 5).Select(models.Drivetrain.GetGearRatio));
        // 7: mass.
        Assert.Equal(1420.0, models.Car.TotalMass);
        // 8: grip — both families, every compound, ×1.10.
        foreach (var (section, before) in sourceGrip)
        {
            var after = models.Tyres.GripValues(section);
            foreach (var (key, value) in before)
                Assert.Equal(value * 1.10, after[key], 6);
        }
        // 9: brakes — 2400 × 1.15.
        Assert.Equal(sourceBrake * 1.15, models.Brakes!.MaxTorque, 6);
        // 10: diff.
        Assert.Equal(0.45, models.Drivetrain.DiffPower);
        Assert.Equal(0.25, models.Drivetrain.DiffCoast);
    }
}
