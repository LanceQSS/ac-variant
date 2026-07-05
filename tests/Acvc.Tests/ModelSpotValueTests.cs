using Acvc.Core.Model;

namespace Acvc.Tests;

/// <summary>
/// Milestone 2 gate part 2: typed reads return values hand-verified against the
/// actual decrypted files of both fixture cars (read by eye from the unpacked data,
/// not derived from this code).
/// </summary>
public class ModelSpotValueTests
{
    [SkippableFact]
    public void Abarth500_typed_reads_match_hand_verified_values()
    {
        var data = ModelTestUtil.TryLoadFixtureCar("abarth500");
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        var car = CarIni.Parse(data!.Files["car.ini"]);
        Assert.Equal(1100.0, car.TotalMass);

        var engine = EngineIni.Parse(data.Files["engine.ini"]);
        Assert.Equal(6500, engine.Limiter);
        Assert.Equal(850, engine.Minimum);
        Assert.Equal(0.165, engine.Inertia);
        Assert.Equal("power.lut", engine.PowerCurveFile);
        Assert.True(engine.HasTurbo);
        var turbo = Assert.Single(engine.Turbos);
        Assert.Equal("TURBO_0", turbo.SectionName);
        Assert.Equal(1.38, turbo.MaxBoost);
        Assert.Equal(1.18, turbo.Wastegate);

        var drivetrain = DrivetrainIni.Parse(data.Files["drivetrain.ini"]);
        Assert.Equal(3.353, drivetrain.FinalRatio);  // written as "3.353000" in the file
        Assert.Equal(5, drivetrain.GearCount);
        Assert.Equal(3.909, drivetrain.GetGearRatio(1));
        Assert.Equal(0.872, drivetrain.GetGearRatio(5));

        var lut = PowerLut.Parse(data.Files["power.lut"]);
        Assert.Equal(34, lut.RowCount);
        Assert.Equal((-3000.0, 50.0), lut.GetRow(0));
        Assert.Equal((7750.0, 0.0), lut.GetRow(33));
    }

    [SkippableFact]
    public void Bmw_m3_e30_typed_reads_match_hand_verified_values()
    {
        var data = ModelTestUtil.TryLoadFixtureCar("bmw_m3_e30");
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        var car = CarIni.Parse(data!.Files["car.ini"]);
        Assert.Equal(1275.0, car.TotalMass);

        var engine = EngineIni.Parse(data.Files["engine.ini"]);
        Assert.Equal(7250, engine.Limiter);
        Assert.Equal(980, engine.Minimum);
        Assert.Equal(0.12, engine.Inertia);  // written as "0.120"
        Assert.Equal("power.lut", engine.PowerCurveFile);
        Assert.False(engine.HasTurbo);
        Assert.Empty(engine.Turbos);

        var drivetrain = DrivetrainIni.Parse(data.Files["drivetrain.ini"]);
        Assert.Equal(3.15, drivetrain.FinalRatio);
        Assert.Equal(5, drivetrain.GearCount);
        Assert.Equal(3.72, drivetrain.GetGearRatio(1));
        Assert.Equal(1.0, drivetrain.GetGearRatio(5));

        var lut = PowerLut.Parse(data.Files["power.lut"]);
        Assert.Equal(21, lut.RowCount);
        Assert.Equal((0.0, 100.0), lut.GetRow(0));
        Assert.Equal((9000.0, 0.0), lut.GetRow(20));
    }
}
