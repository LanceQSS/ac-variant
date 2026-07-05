using System.Text;
using Acvc.Core.Model;
using Acvc.Core.Transforms;

namespace Acvc.Tests;

/// <summary>M7: the three handling transforms, table-driven over both fixture cars.</summary>
public class HandlingTransformTests
{
    public static TheoryData<string> CarNames => new() { "abarth500", "bmw_m3_e30" };

    private static CarModelSet LoadModels(string carName)
        => CarModelSet.FromFiles(ModelTestUtil.TryLoadFixtureCar(carName)!.Files);

    // ---- tyres.grip_scale ------------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void GripScale_scales_both_families_in_every_compound(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);
        var tyres = models.Tyres!;
        Assert.NotEmpty(tyres.CompoundSections);
        var before = tyres.CompoundSections.ToDictionary(s => s, s => tyres.GripValues(s));
        var flaBefore = tyres.CompoundSections
            .ToDictionary(s => s, s => tyres.Document.GetValue(s, "FRICTION_LIMIT_ANGLE"));

        TyresGripScaleTransform.Apply(tyres, 1.25);

        foreach (var section in tyres.CompoundSections)
        {
            var scaled = tyres.GripValues(section);
            // Both families were present (M6 survey fact) and both got the factor.
            Assert.Contains("DX_REF", scaled.Keys);
            Assert.Contains("DX0", scaled.Keys);
            Assert.Equal(before[section].Count, scaled.Count);
            foreach (var (key, value) in before[section])
                Assert.Equal(value * 1.25, scaled[key], 6);
            // FRICTION_LIMIT_ANGLE untouched, byte-for-byte.
            Assert.Equal(flaBefore[section], tyres.Document.GetValue(section, "FRICTION_LIMIT_ANGLE"));
        }
    }

    [SkippableFact]
    public void GripScale_spot_value_and_negative_slope_scaling()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels("abarth500");

        TyresGripScaleTransform.Apply(models.Tyres, 1.25);

        // Hand-verified stock values: [FRONT] DX_REF=1.26, DX1=-0.046.
        Assert.Equal("1.575", models.Tyres!.Document.GetValue("FRONT", "DX_REF"));
        Assert.Equal(-0.046 * 1.25, models.Tyres.GripValues("FRONT")["DX1"], 6);
    }

    [Theory]
    [InlineData("[HEADER]\nVERSION=7\n[FRONT]\nDX_REF=1.2\n[REAR]\nDX_REF=1.2\n", "VERSION=7")]
    [InlineData("[HEADER]\nNAME=x\n[FRONT]\nDX_REF=1.2\n", "VERSION=none")]
    public void GripScale_refuses_non_v10_tyre_models_naming_the_version(string tyresText, string expected)
    {
        var tyres = TyresIni.Parse(Encoding.Latin1.GetBytes(tyresText));
        var ex = Assert.Throws<TransformException>(() => TyresGripScaleTransform.Apply(tyres, 1.1));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void GripScale_refuses_curve_based_grip()
    {
        // Shape synthesized from nohesi_realistic_audi_rs3: V10, DX_CURVE pointing at
        // a lut, REF keys present but dead ("не используется" in the real file).
        var tyres = TyresIni.Parse(Encoding.Latin1.GetBytes(
            "[HEADER]\nVERSION=10\n" +
            "[FRONT]\nDX_REF=0.5\nDY_REF=0.5\nDX_CURVE=tire_PS4S_265_DX+1.lut\nDY_CURVE=tire_PS4S_265_DY+1.lut\n" +
            "[REAR]\nDX_REF=0.5\nDY_REF=0.5\nDX_CURVE=tire_PS4S_245_DX-1.lut\nDY_CURVE=tire_PS4S_245_DY-1.lut\n"));

        var ex = Assert.Throws<TransformException>(() => TyresGripScaleTransform.Apply(tyres, 1.1));
        Assert.Contains("DX_CURVE", ex.Message);
        Assert.Contains("[FRONT]", ex.Message);
    }

    [Fact]
    public void GripScale_treats_disabled_curve_keys_as_absent()
    {
        // DX_CURVE=0 / empty means "curve grip off" (CM convention) — must not refuse.
        var tyres = TyresIni.Parse(Encoding.Latin1.GetBytes(
            "[HEADER]\nVERSION=10\n[FRONT]\nDX_REF=1.2\nDX_CURVE=0\nDY_CURVE=\n[REAR]\nDX_REF=1.2\n"));

        TyresGripScaleTransform.Apply(tyres, 1.5);

        Assert.Equal(1.8, tyres.GripValues("FRONT")["DX_REF"], 6);
    }

    [Fact]
    public void GripScale_without_tyres_ini_is_a_hard_error()
    {
        var ex = Assert.Throws<TransformException>(() => TyresGripScaleTransform.Apply(null, 1.1));
        Assert.Contains("tyres.ini", ex.Message);
    }

    [Fact]
    public void GripScale_refuses_v10_file_with_no_grip_keys_instead_of_a_silent_noop()
    {
        var tyres = TyresIni.Parse(Encoding.Latin1.GetBytes("[HEADER]\nVERSION=10\n[FRONT]\nRADIUS=0.3\n"));
        var ex = Assert.Throws<TransformException>(() => TyresGripScaleTransform.Apply(tyres, 1.1));
        Assert.Contains("no-op", ex.Message);
    }

    // ---- brakes.torque_scale ----------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void BrakesTorqueScale_scales_max_torque(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);
        var before = models.Brakes!.MaxTorque;

        BrakesTorqueScaleTransform.Apply(models.Brakes, 0.6);

        Assert.Equal(before * 0.6, models.Brakes!.MaxTorque, 6);
    }

    [SkippableFact]
    public void BrakesTorqueScale_spot_value()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels("abarth500");

        BrakesTorqueScaleTransform.Apply(models.Brakes, 0.6);

        // Hand-verified stock: [DATA] MAX_TORQUE=2400 → 1440.
        Assert.Equal(1440.0, models.Brakes!.MaxTorque);
        Assert.Contains("MAX_TORQUE=1440", Encoding.Latin1.GetString(models.Brakes.ToBytes()));
    }

    [Fact]
    public void BrakesTorqueScale_without_brakes_ini_or_key_is_a_hard_error()
    {
        Assert.Contains("brakes.ini",
            Assert.Throws<TransformException>(() => BrakesTorqueScaleTransform.Apply(null, 1.1)).Message);

        var noKey = BrakesIni.Parse(Encoding.Latin1.GetBytes("[DATA]\nFRONT_SHARE=0.7\n"));
        Assert.Contains("MAX_TORQUE",
            Assert.Throws<TransformException>(() => BrakesTorqueScaleTransform.Apply(noKey, 1.1)).Message);
    }

    // ---- diff.power / diff.coast --------------------------------------------------

    [SkippableTheory]
    [MemberData(nameof(CarNames))]
    public void DiffLock_sets_power_and_coast(string carName)
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar(carName) is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels(carName);

        DiffLockTransform.Apply(models.Drivetrain, 0.9, 0.6);

        Assert.Equal(0.9, models.Drivetrain.DiffPower);
        Assert.Equal(0.6, models.Drivetrain.DiffCoast);
    }

    [SkippableFact]
    public void DiffLock_setting_only_power_leaves_coast_untouched()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("bmw_m3_e30") is null, ModelTestUtil.FixtureSkipReason);
        var models = LoadModels("bmw_m3_e30");
        // Hand-verified stock: POWER=0.25, COAST=0.25.
        DiffLockTransform.Apply(models.Drivetrain, 0.9, null);

        Assert.Equal(0.9, models.Drivetrain.DiffPower);
        Assert.Equal(0.25, models.Drivetrain.DiffCoast);
    }

    [Fact]
    public void DiffLock_without_differential_section_is_a_hard_error()
    {
        var dt = DrivetrainIni.Parse(Encoding.Latin1.GetBytes("[GEARS]\nCOUNT=2\nGEAR_1=3.0\nGEAR_2=2.0\nFINAL=4.0\n"));
        var ex = Assert.Throws<TransformException>(() => DiffLockTransform.Apply(dt, 0.5, null));
        Assert.Contains("DIFFERENTIAL", ex.Message);
    }
}
