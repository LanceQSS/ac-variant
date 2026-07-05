using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>drivetrain.final — sets the final drive ratio in drivetrain.ini [GEARS].</summary>
public static class DrivetrainFinalTransform
{
    public static void Apply(DrivetrainIni drivetrain, double finalRatio)
    {
        if (!double.IsFinite(finalRatio) || finalRatio <= 0)
            throw new TransformException($"drivetrain.final must be a positive ratio, got {finalRatio}.");
        drivetrain.FinalRatio = finalRatio;
    }
}
