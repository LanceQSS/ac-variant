using Acvc.Core.Model;
using Acvc.Core.Spec;

namespace Acvc.Core.Transforms;

/// <summary>
/// power.curve — per-range torque shaping: each factor applies to LUT rows with
/// FromRpm ≤ rpm ≤ ToRpm (inclusive bounds). Overlapping ranges are ambiguous and
/// therefore an error, not a silently-defined precedence.
/// </summary>
public static class PowerCurveTransform
{
    public static void Apply(PowerLut lut, IReadOnlyList<PowerCurveRange> ranges)
    {
        if (ranges.Count == 0)
            throw new TransformException("power.curve requires at least one range.");

        foreach (var range in ranges)
        {
            if (!double.IsFinite(range.FromRpm) || !double.IsFinite(range.ToRpm))
                throw new TransformException($"power.curve range {Describe(range)} has a non-finite bound.");
            if (range.FromRpm >= range.ToRpm)
                throw new TransformException($"power.curve range {Describe(range)}: 'from' must be below 'to'.");
            if (!double.IsFinite(range.Factor) || range.Factor <= 0)
                throw new TransformException($"power.curve range {Describe(range)}: factor must be a positive number.");
        }

        var ordered = ranges.OrderBy(r => r.FromRpm).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].FromRpm <= ordered[i - 1].ToRpm)
                throw new TransformException(
                    $"power.curve ranges {Describe(ordered[i - 1])} and {Describe(ordered[i])} overlap; ranges must be disjoint.");

        for (var i = 0; i < lut.RowCount; i++)
        {
            var (rpm, value) = lut.GetRow(i);
            var range = ordered.FirstOrDefault(r => rpm >= r.FromRpm && rpm <= r.ToRpm);
            if (range is not null)
                lut.SetValue(i, value * range.Factor);
        }
    }

    private static string Describe(PowerCurveRange r) => $"[{r.FromRpm}..{r.ToRpm}]×{r.Factor}";
}
