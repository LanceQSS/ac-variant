using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>
/// tyres.grip_scale — scales every grip key of every compound section by one factor:
/// both the DX_REF/DY_REF family and the legacy DX0/DX1/DY0/DY1 family (M6 survey:
/// all V10 cars carry both; scaling both keys of a family scales the whole grip
/// curve, so a uniform factor is correct regardless of which family the sim reads).
/// FRICTION_LIMIT_ANGLE untouched. Only tyre model VERSION=10 is supported, and
/// curve-based grip (an active DX_CURVE/DY_CURVE) is refused — on such cars the REF
/// keys are dead and scaling them would be a silent no-op, the worst kind of result.
/// </summary>
public static class TyresGripScaleTransform
{
    public const string SupportedVersion = "10";

    public static void Apply(TyresIni? tyres, double factor)
    {
        if (tyres is null)
            throw new TransformException("tyres.grip_scale cannot apply: the car data has no tyres.ini.");
        if (!double.IsFinite(factor))
            throw new TransformException($"tyres.grip_scale must be a finite number, got {factor}.");

        var version = tyres.Version ?? "none";
        if (version != SupportedVersion)
            throw new TransformException(
                $"tyres.grip_scale supports tyre model VERSION={SupportedVersion} only; this car's tyres.ini " +
                $"declares VERSION={version}. Other transforms still work on this car.");

        var curveSections = tyres.CurveGripSections;
        if (curveSections.Count > 0)
            throw new TransformException(
                $"tyres.grip_scale cannot apply: compound section(s) {string.Join(", ", curveSections.Select(s => $"[{s}]"))} " +
                "use curve-based grip (DX_CURVE/DY_CURVE point at .lut files), so the reference grip keys are not " +
                "what the sim reads. Other transforms still work on this car.");

        var compounds = tyres.CompoundSections;
        if (compounds.Count == 0)
            throw new TransformException("tyres.grip_scale cannot apply: tyres.ini has no compound sections (FRONT/REAR).");

        var scaledKeys = 0;
        foreach (var section in compounds)
        {
            foreach (var (key, value) in tyres.GripValues(section))
            {
                tyres.SetGripValue(section, key, value * factor);
                scaledKeys++;
            }
        }
        if (scaledKeys == 0)
            throw new TransformException(
                $"tyres.grip_scale found none of the V{SupportedVersion} grip keys " +
                $"({string.Join(", ", TyresIni.GripKeys)}) in any compound section — refusing a silent no-op.");
    }
}
