using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>power.scale — multiplies every power.lut torque value by a factor.</summary>
public static class PowerScaleTransform
{
    public static void Apply(PowerLut lut, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
            throw new TransformException($"power.scale factor must be a positive number, got {factor}.");
        for (var i = 0; i < lut.RowCount; i++)
            lut.SetValue(i, lut.GetRow(i).Value * factor);
    }
}
