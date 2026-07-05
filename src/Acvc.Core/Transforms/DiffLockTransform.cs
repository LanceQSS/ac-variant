using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>
/// diff.power / diff.coast — sets [DIFFERENTIAL] POWER and/or COAST lock fractions
/// in drivetrain.ini. A car without a [DIFFERENTIAL] section is a hard error (no
/// silent fallbacks); the [0,1] range rule belongs to the validator.
/// </summary>
public static class DiffLockTransform
{
    public static void Apply(DrivetrainIni drivetrain, double? power, double? coast)
    {
        if (power is null && coast is null)
            return;
        if (!drivetrain.HasDifferential)
            throw new TransformException(
                "diff.power/diff.coast cannot apply: drivetrain.ini has no [DIFFERENTIAL] section.");

        if (power is { } p)
        {
            if (!double.IsFinite(p))
                throw new TransformException($"diff.power must be a finite number, got {p}.");
            drivetrain.DiffPower = p;
        }
        if (coast is { } c)
        {
            if (!double.IsFinite(c))
                throw new TransformException($"diff.coast must be a finite number, got {c}.");
            drivetrain.DiffCoast = c;
        }
    }
}
