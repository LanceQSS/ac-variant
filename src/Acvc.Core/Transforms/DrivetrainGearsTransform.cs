using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>
/// drivetrain.gears — sets every forward gear ratio. The list must cover exactly the
/// car's [GEARS] COUNT: a partial gear set is almost certainly a spec mistake.
/// </summary>
public static class DrivetrainGearsTransform
{
    public static void Apply(DrivetrainIni drivetrain, IReadOnlyList<double> ratios)
    {
        var count = drivetrain.GearCount;
        if (ratios.Count != count)
            throw new TransformException(
                $"drivetrain.gears has {ratios.Count} ratios but the car has COUNT={count} forward gears; provide exactly {count}.");

        for (var i = 0; i < ratios.Count; i++)
            if (!double.IsFinite(ratios[i]) || ratios[i] <= 0)
                throw new TransformException($"drivetrain.gears[{i}] must be a positive ratio, got {ratios[i]}.");

        for (var gear = 1; gear <= count; gear++)
            drivetrain.SetGearRatio(gear, ratios[gear - 1]);
    }
}
