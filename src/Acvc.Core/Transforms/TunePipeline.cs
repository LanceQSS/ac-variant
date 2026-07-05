using Acvc.Core.Model;
using Acvc.Core.Spec;

namespace Acvc.Core.Transforms;

/// <summary>
/// Applies a <see cref="TunePlan"/> to in-memory car models in a fixed order, then
/// runs the validation pass (CLAUDE.md pipeline: load → transform (pure) → validate
/// → emit; emit is not this class's business). Models are mutated through the
/// lossless tree; nothing here touches the filesystem.
/// </summary>
public static class TunePipeline
{
    public static ValidationResult Apply(TunePlan plan, CarModelSet models)
    {
        var source = SourceSnapshot.Capture(models);

        if (plan.PowerScale is { } scale)
            PowerScaleTransform.Apply(models.PowerLut, scale);
        if (plan.PowerCurve is { } curve)
            PowerCurveTransform.Apply(models.PowerLut, curve);
        if (plan.Limiter is { } limiter)
            EngineLimiterTransform.Apply(models.Engine, limiter);
        if (plan.Boost is { } boost)
            EngineBoostTransform.Apply(models.Engine, boost);
        if (plan.FinalDrive is { } final)
            DrivetrainFinalTransform.Apply(models.Drivetrain, final);
        if (plan.Gears is { } gears)
            DrivetrainGearsTransform.Apply(models.Drivetrain, gears);
        if (plan.MassTotal is { } mass)
            MassTotalTransform.Apply(models.Car, mass);

        return TuneValidator.Validate(models, source);
    }
}
