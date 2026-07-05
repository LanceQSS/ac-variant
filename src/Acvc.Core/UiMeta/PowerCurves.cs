using Acvc.Core.Model;

namespace Acvc.Core.UiMeta;

public sealed record CurvePoint(double Rpm, double TorqueNm, double PowerBhp);

/// <summary>
/// Effective torque/power curves computed from the (possibly transformed) data —
/// the LUT is truth, not the marketing numbers in stock ui files (verified stock
/// bmw_m3_e30 ships hand-authored brochure curves ~12% above its LUT).
///
/// Model, validated against stock ui_car.json files in-session:
///   effective torque = power.lut torque × (1 + Σ MAX_BOOST over [TURBO_n]) — the
///   multiplier stated in engine.ini's own MAX_BOOST comment; reconciles the stock
///   abarth500 LUT to its 160bhp ui claim within 2.4%;
///   power[bhp] = torque × rpm × 2π/60 ÷ 745.7 — exact identity between the stock
///   torqueCurve and powerCurve arrays of both fixture cars.
/// </summary>
public static class PowerCurves
{
    private const double WattsPerBhp = 745.699872;

    /// <summary>Kunos ui curve grid: a forced (0,0) origin, then every <paramref name="step"/> rpm, then the limiter itself.</summary>
    public const int UiGridStep = 500;

    public static double BoostTorqueMultiplier(EngineIni engine)
        => 1 + engine.Turbos.Sum(t => t.MaxBoost);

    /// <summary>
    /// Samples the effective curves on a 0..limiter grid (falls back to the last LUT
    /// rpm when LIMITER=0). The origin point is (0, 0, 0) — the stock ui convention.
    /// </summary>
    public static IReadOnlyList<CurvePoint> SampleGrid(EngineIni engine, PowerLut lut, int step = UiGridStep)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        var rows = lut.Rows.ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("Cannot sample curves from an empty LUT.");

        var limiter = engine.Limiter;
        var top = limiter > 0 ? limiter : (int)rows[^1].Rpm;
        var multiplier = BoostTorqueMultiplier(engine);

        var points = new List<CurvePoint> { new(0, 0, 0) };
        for (var rpm = step; rpm < top; rpm += step)
            points.Add(PointAt(rows, rpm, multiplier));
        points.Add(PointAt(rows, top, multiplier));
        return points;
    }

    public static (double TorqueNm, double PowerBhp, double PowerRpm) Peaks(IReadOnlyList<CurvePoint> points)
    {
        var peakTorque = points.Max(p => p.TorqueNm);
        var peakPower = points.MaxBy(p => p.PowerBhp)!;
        return (peakTorque, peakPower.PowerBhp, peakPower.Rpm);
    }

    private static CurvePoint PointAt(IReadOnlyList<(double Rpm, double Value)> rows, double rpm, double multiplier)
    {
        var torque = Interpolate(rows, rpm) * multiplier;
        var powerBhp = torque * rpm * (2 * Math.PI / 60) / WattsPerBhp;
        return new CurvePoint(rpm, torque, powerBhp);
    }

    /// <summary>Linear interpolation over the LUT; zero outside its rpm range.</summary>
    private static double Interpolate(IReadOnlyList<(double Rpm, double Value)> rows, double rpm)
    {
        if (rpm < rows[0].Rpm || rpm > rows[^1].Rpm)
            return 0;
        for (var i = 1; i < rows.Count; i++)
        {
            var (r1, v1) = rows[i - 1];
            var (r2, v2) = rows[i];
            if (rpm <= r2)
                return r2 == r1 ? v1 : v1 + (v2 - v1) * (rpm - r1) / (r2 - r1);
        }
        return rows[^1].Value;
    }
}
