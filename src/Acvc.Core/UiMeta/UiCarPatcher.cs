using System.Globalization;
using System.Text;
using Acvc.Core.Emit;
using Acvc.Core.Model;

namespace Acvc.Core.UiMeta;

/// <summary>Replacement values for the regenerated parts of ui_car.json.</summary>
public sealed record UiSpecsPatch(
    string Bhp,
    string Torque,
    string Weight,
    string PwRatio,
    string TorqueCurveJson,
    string PowerCurveJson);

/// <summary>
/// Regenerates ui_car.json specs and curves from transformed data via byte-level
/// splices. Kunos ui files are not even strict JSON (raw control characters inside
/// description strings), so no parser round-trip is safe — each value is located and
/// replaced in place; everything outside the six replaced spans stays byte-identical.
/// Formats match the stock files exactly: "213bhp", "305Nm", "1345kg", "6.31kg/hp",
/// and curves as arrays of ["rpm","value"] STRING pairs from 0 to the limiter.
/// </summary>
public static class UiCarPatcher
{
    /// <summary>
    /// Stock ui "weight" is TOTALMASS minus the driver: both fixture cars ship
    /// exactly 75 kg less in ui_car.json than car.ini TOTALMASS.
    /// </summary>
    public const double DriverMassDeltaKg = 75;

    public static UiSpecsPatch BuildPatch(CarModelSet models)
    {
        var points = PowerCurves.SampleGrid(models.Engine, models.PowerLut);
        var (peakTorque, peakPower, _) = PowerCurves.Peaks(points);

        var bhp = (int)Math.Round(peakPower);
        var torque = (int)Math.Round(peakTorque);
        var totalMass = models.Car.TotalMass;
        var weight = totalMass - DriverMassDeltaKg;
        if (weight <= 0)
            weight = totalMass;
        // Truncation, not rounding: stock abarth500 shows 1025/160 = 6.406 as "6.40".
        var pwRatio = Math.Floor(weight / bhp * 100) / 100;

        return new UiSpecsPatch(
            $"{bhp}bhp",
            $"{torque}Nm",
            $"{(int)Math.Round(weight)}kg",
            pwRatio.ToString("F2", CultureInfo.InvariantCulture) + "kg/hp",
            FormatCurve(points, p => p.TorqueNm),
            FormatCurve(points, p => p.PowerBhp));
    }

    public static byte[] Apply(byte[] json, UiSpecsPatch patch)
    {
        json = ReplaceStringValue(json, "bhp", patch.Bhp);
        json = ReplaceStringValue(json, "torque", patch.Torque);
        json = ReplaceStringValue(json, "weight", patch.Weight);
        json = ReplaceStringValue(json, "pwratio", patch.PwRatio);
        json = ReplaceArrayValue(json, "torqueCurve", patch.TorqueCurveJson);
        json = ReplaceArrayValue(json, "powerCurve", patch.PowerCurveJson);
        return json;
    }

    private static string FormatCurve(IReadOnlyList<CurvePoint> points, Func<CurvePoint, double> value)
    {
        var sb = new StringBuilder("[\n");
        for (var i = 0; i < points.Count; i++)
        {
            sb.Append("\t\t[\"")
              .Append(((int)Math.Round(points[i].Rpm)).ToString(CultureInfo.InvariantCulture))
              .Append("\",\"")
              .Append(((int)Math.Round(value(points[i]))).ToString(CultureInfo.InvariantCulture))
              .Append("\"]");
            sb.Append(i < points.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("\t]");
        return sb.ToString();
    }

    // ---- byte splicing ----------------------------------------------------------

    private static byte[] ReplaceStringValue(byte[] json, string key, string newValue)
    {
        var (valueStart, valueEnd) = FindValueSpan(json, key, expectString: true);
        return Splice(json, valueStart, valueEnd, Encoding.UTF8.GetBytes(
            newValue.Replace("\\", "\\\\").Replace("\"", "\\\"")));
    }

    private static byte[] ReplaceArrayValue(byte[] json, string key, string newArrayJson)
    {
        var (valueStart, valueEnd) = FindValueSpan(json, key, expectString: false);
        return Splice(json, valueStart, valueEnd, Encoding.UTF8.GetBytes(newArrayJson));
    }

    private static byte[] Splice(byte[] json, int start, int end, byte[] replacement)
    {
        var result = new byte[json.Length - (end - start) + replacement.Length];
        json.AsSpan(0, start).CopyTo(result);
        replacement.CopyTo(result, start);
        json.AsSpan(end).CopyTo(result.AsSpan(start + replacement.Length));
        return result;
    }

    /// <summary>
    /// Locates the value of the first `"key":` occurrence. For strings, returns the
    /// span of the content between the quotes; for arrays, the span of the whole
    /// [...] including brackets. Key matching includes both quotes, so "torque"
    /// never matches the prefix of "torqueCurve".
    /// </summary>
    private static (int Start, int End) FindValueSpan(byte[] json, string key, bool expectString)
    {
        var pattern = Encoding.ASCII.GetBytes($"\"{key}\"");
        for (var i = 0; i + pattern.Length < json.Length; i++)
        {
            if (!json.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                continue;
            var p = i + pattern.Length;
            while (p < json.Length && IsWs(json[p])) p++;
            if (p >= json.Length || json[p] != (byte)':')
                continue;
            p++;
            while (p < json.Length && IsWs(json[p])) p++;
            if (p >= json.Length)
                break;

            if (expectString)
            {
                if (json[p] != (byte)'"')
                    continue;
                var start = p + 1;
                var q = start;
                while (q < json.Length)
                {
                    if (json[q] == (byte)'\\') q += 2;
                    else if (json[q] == (byte)'"') return (start, q);
                    else q++;
                }
                break;
            }

            if (json[p] != (byte)'[')
                continue;
            var depth = 0;
            for (var q = p; q < json.Length; q++)
            {
                var b = json[q];
                if (b == (byte)'"')
                {
                    q++;
                    while (q < json.Length && json[q] != (byte)'"')
                        q += json[q] == (byte)'\\' ? 2 : 1;
                }
                else if (b == (byte)'[')
                {
                    depth++;
                }
                else if (b == (byte)']')
                {
                    depth--;
                    if (depth == 0)
                        return (p, q + 1);
                }
            }
            break;
        }
        throw new EmitException($"ui_car.json has no '{key}' value to regenerate.");
    }

    private static bool IsWs(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
