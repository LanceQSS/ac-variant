using System.Text;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;

namespace Acvc.Tests;

/// <summary>Every validation rule fires at least once; stock data validates clean.</summary>
public class TuneValidatorTests
{
    public static TheoryData<string> CarNames => new() { "abarth500", "bmw_m3_e30" };

    private static CarModelSet LoadModels(string carName)
        => CarModelSet.FromFiles(ModelTestUtil.TryLoadFixtureCar(carName)!.Files);

    private static TunePlan Plan(string car = "abarth500") => new() { SourceCar = car, TuneName = "t" };

    /// <summary>Synthetic minimal car for rules easier to trigger with crafted data.</summary>
    private static CarModelSet SyntheticModels(string lutText)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["car.ini"] = Encoding.Latin1.GetBytes("[BASIC]\nTOTALMASS=1000\n"),
            ["engine.ini"] = Encoding.Latin1.GetBytes(
                "[HEADER]\nPOWER_CURVE=power.lut\n[ENGINE_DATA]\nLIMITER=7000\nMINIMUM=900\nINERTIA=0.1\n"),
            ["drivetrain.ini"] = Encoding.Latin1.GetBytes("[GEARS]\nCOUNT=2\nGEAR_1=3.0\nGEAR_2=2.0\nFINAL=4.0\n"),
            ["power.lut"] = Encoding.Latin1.GetBytes(lutText),
        };
        return CarModelSet.FromFiles(files);
    }

    // ---- clean pass ----------------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void Stock_noop_tune_validates_clean(string carName)
    {
        // Deliberate regression guard for the limiter rule: bmw_m3_e30's LUT has
        // overrev rows to 9000 whose raw torque×rpm peaks at 8000, above its 7250
        // limiter — untouched factory data must still pass.
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);

        var result = TunePipeline.Apply(Plan(carName), models);

        Assert.Empty(result.Issues);
        Assert.False(result.HasFailures);
    }

    // ---- mass rules ----------------------------------------------------------

    [SkippableFact]
    public void Non_positive_mass_fails_without_restating_the_range_rule()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var result = TunePipeline.Apply(Plan() with { MassTotal = -5 }, LoadModels("abarth500"));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Failure, issue.Severity);
        Assert.Equal("mass.positive", issue.Rule);
        Assert.Equal(-5, issue.Value);
        Assert.Equal(0, issue.Limit);
    }

    [SkippableTheory]
    [InlineData(1900, 1760)]  // above 1100 × 1.6
    [InlineData(400, 440)]    // below 1100 × 0.4
    public void Mass_outside_60_percent_of_source_fails_with_value_and_limit(double mass, double limit)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var result = TunePipeline.Apply(Plan() with { MassTotal = mass }, LoadModels("abarth500"));

        var issue = Assert.Single(result.Failures);
        Assert.Equal("mass.range", issue.Rule);
        Assert.Equal(mass, issue.Value);
        Assert.Equal(limit, issue.Limit, 6);
    }

    // ---- LUT rules -----------------------------------------------------------

    [Fact]
    public void Non_increasing_lut_rpm_fails_with_offending_values()
    {
        var models = SyntheticModels("0|10\n100|20\n100|30\n");
        var result = TuneValidator.Validate(models, SourceSnapshot.Capture(models));

        var issue = Assert.Single(result.Failures);
        Assert.Equal("lut.monotonic", issue.Rule);
        Assert.Equal(100, issue.Value);
        Assert.Equal(100, issue.Limit);
    }

    [Fact]
    public void Negative_first_rpm_is_legal()
    {
        // abarth500 itself starts at -3000; the rule is strict increase, not positivity.
        var models = SyntheticModels("-3000|50\n0|60\n100|70\n");
        var result = TuneValidator.Validate(models, SourceSnapshot.Capture(models));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Non_finite_lut_value_fails()
    {
        // "1e400" parses to +Infinity under IEEE-conformant .NET parsing.
        var models = SyntheticModels("0|10\n100|1e400\n");
        var result = TuneValidator.Validate(models, SourceSnapshot.Capture(models));

        var issue = Assert.Single(result.Failures, i => i.Rule == "lut.finite");
        Assert.True(double.IsInfinity(issue.Value));
    }

    // ---- limiter vs peak power -----------------------------------------------

    [SkippableFact]
    public void Limiter_below_peak_power_rpm_fails_with_both_numbers()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        // abarth500 usable peak power (torque×rpm over rows ≤ source limiter 6500)
        // sits at 5500 rpm (85 Nm) — hand-computed from the fixture LUT.
        var result = TunePipeline.Apply(Plan() with { Limiter = 5000 }, LoadModels("abarth500"));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Failure, issue.Severity);
        Assert.Equal("limiter.peak", issue.Rule);
        Assert.Equal(5000, issue.Value);
        Assert.Equal(5500, issue.Limit);
    }

    [SkippableFact]
    public void Explicitly_restating_the_stock_limiter_is_not_a_failure()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("bmw_m3_e30") is null, ModelTestUtil.FixtureSkipReason);
        // bmw_m3_e30's usable peak is exactly at its 7250 limiter row.
        var result = TunePipeline.Apply(
            Plan("bmw_m3_e30") with { Limiter = 7250 }, LoadModels("bmw_m3_e30"));
        Assert.Empty(result.Failures);
    }

    // ---- warnings --------------------------------------------------------------

    [SkippableFact]
    public void Power_scale_beyond_3x_warns_with_effective_ratio()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var result = TunePipeline.Apply(Plan() with { PowerScale = 3.5 }, LoadModels("abarth500"));

        Assert.False(result.HasFailures);
        var issue = Assert.Single(result.Warnings);
        Assert.Equal("power.scale", issue.Rule);
        Assert.Equal(3.5, issue.Value, 6);
        Assert.Equal(3.0, issue.Limit);
    }

    [SkippableFact]
    public void Stacked_scale_and_curve_warn_on_the_combined_effect()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        // 2.0 × 1.8 = 3.6 in [4000..6000] — each factor alone is under the threshold.
        var plan = Plan() with
        {
            PowerScale = 2.0,
            PowerCurve = new[] { new PowerCurveRange(4000, 6000, 1.8) },
        };
        var result = TunePipeline.Apply(plan, LoadModels("abarth500"));

        var issue = Assert.Single(result.Warnings, i => i.Rule == "power.scale");
        Assert.Equal(3.6, issue.Value, 6);
    }

    [SkippableFact]
    public void Limiter_raised_over_20_percent_warns()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var result = TunePipeline.Apply(Plan() with { Limiter = 8000 }, LoadModels("abarth500"));

        Assert.False(result.HasFailures);  // 8000 is above the 5500 peak, so no failure
        var issue = Assert.Single(result.Warnings);
        Assert.Equal("limiter.raise", issue.Rule);
        Assert.Equal(8000, issue.Value);
        Assert.Equal(7800, issue.Limit, 6);  // 6500 × 1.2
    }
}
