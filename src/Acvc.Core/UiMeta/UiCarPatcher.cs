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

    /// <summary>
    /// Applies each field independently (M6 degradation policy): a ui_car.json
    /// missing some or all keys still gets everything that IS present regenerated;
    /// the returned list names exactly the fields that had to be skipped. Never
    /// throws for missing keys — ui cosmetics must not block a build.
    /// </summary>
    public static (byte[] Json, IReadOnlyList<string> SkippedFields) Apply(byte[] json, UiSpecsPatch patch)
    {
        var skipped = new List<string>();
        json = TryReplaceString(json, "bhp", patch.Bhp, skipped);
        json = TryReplaceString(json, "torque", patch.Torque, skipped);
        json = TryReplaceString(json, "weight", patch.Weight, skipped);
        json = TryReplaceString(json, "pwratio", patch.PwRatio, skipped);
        json = TryReplaceArray(json, "torqueCurve", patch.TorqueCurveJson, skipped);
        json = TryReplaceArray(json, "powerCurve", patch.PowerCurveJson, skipped);
        return (json, skipped);
    }

    /// <summary>The regenerable ui fields NOT present in <paramref name="json"/> (survey + build warnings).</summary>
    public static IReadOnlyList<string> ProbeMissing(byte[] json)
    {
        var missing = new List<string>();
        foreach (var (key, isString) in new[]
                 {
                     ("name", true), ("bhp", true), ("torque", true), ("weight", true),
                     ("pwratio", true), ("torqueCurve", false), ("powerCurve", false),
                 })
        {
            if (FindValueSpanOrNull(json, key, isString) is null)
                missing.Add(key);
        }
        return missing;
    }

    private static byte[] TryReplaceString(byte[] json, string key, string newValue, List<string> skipped)
    {
        if (FindValueSpanOrNull(json, key, expectString: true) is not { } span)
        {
            skipped.Add(key);
            return json;
        }
        return Splice(json, span.Start, span.End, Encoding.UTF8.GetBytes(
            newValue.Replace("\\", "\\\\").Replace("\"", "\\\"")));
    }

    private static byte[] TryReplaceArray(byte[] json, string key, string newArrayJson, List<string> skipped)
    {
        if (FindValueSpanOrNull(json, key, expectString: false) is not { } span)
        {
            skipped.Add(key);
            return json;
        }
        return Splice(json, span.Start, span.End, Encoding.UTF8.GetBytes(newArrayJson));
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

    private static byte[] Splice(byte[] json, int start, int end, byte[] replacement)
    {
        var result = new byte[json.Length - (end - start) + replacement.Length];
        json.AsSpan(0, start).CopyTo(result);
        replacement.CopyTo(result, start);
        json.AsSpan(end).CopyTo(result.AsSpan(start + replacement.Length));
        return result;
    }

    /// <summary>
    /// Locates the value of the first `"key":` occurrence, or null when absent. For
    /// strings, returns the span of the content between the quotes; for arrays, the
    /// span of the whole [...] including brackets. Key matching includes both
    /// quotes, so "torque" never matches the prefix of "torqueCurve".
    /// </summary>
    private static (int Start, int End)? FindValueSpanOrNull(byte[] json, string key, bool expectString)
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
        return null;
    }

    private static bool IsWs(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
