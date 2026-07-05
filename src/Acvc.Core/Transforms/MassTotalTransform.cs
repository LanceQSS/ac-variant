using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>
/// mass.total — sets TOTALMASS in car.ini [BASIC]. Range sanity (positive, within
/// ±60% of source) is the validator's job so the report carries value and limit.
/// </summary>
public static class MassTotalTransform
{
    public static void Apply(CarIni car, double totalKg)
    {
        if (!double.IsFinite(totalKg))
            throw new TransformException($"mass.total must be a finite number, got {totalKg}.");
        car.TotalMass = totalKg;
    }
}
