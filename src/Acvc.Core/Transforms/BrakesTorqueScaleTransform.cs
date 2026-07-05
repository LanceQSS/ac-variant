using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>
/// brakes.torque_scale — scales [DATA] MAX_TORQUE in brakes.ini. Range sanity
/// (warn &gt;1.5 / &lt;0.5, fail ≤ 0) is the validator's job so reports carry numbers.
/// </summary>
public static class BrakesTorqueScaleTransform
{
    public static void Apply(BrakesIni? brakes, double factor)
    {
        if (brakes is null)
            throw new TransformException("brakes.torque_scale cannot apply: the car data has no brakes.ini.");
        if (!double.IsFinite(factor))
            throw new TransformException($"brakes.torque_scale must be a finite number, got {factor}.");
        if (!brakes.HasMaxTorque)
            throw new TransformException("brakes.torque_scale cannot apply: brakes.ini has no [DATA] MAX_TORQUE key.");

        brakes.MaxTorque *= factor;
    }
}
