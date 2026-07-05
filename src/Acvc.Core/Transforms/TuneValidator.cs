using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>Pre-transform snapshot of the source values validation compares against.</summary>
public sealed record SourceSnapshot(
    double Mass,
    int Limiter,
    IReadOnlyList<(double Rpm, double Value)> LutRows)
{
    public static SourceSnapshot Capture(CarModelSet models) => new(
        models.Car.TotalMass,
        models.Engine.Limiter,
        models.PowerLut.Rows.ToList());
}

/// <summary>
/// Post-transform validation (CLAUDE.md: mass bounds, LUT monotonic, no NaN, limiter
/// vs peak power; warnings past sanity thresholds). All thresholds live in
/// <see cref="ValidationLimits"/>.
/// </summary>
public static class TuneValidator
{
    public static ValidationResult Validate(CarModelSet models, SourceSnapshot source)
    {
        var issues = new List<ValidationIssue>();

        CheckMass(models, source, issues);
        CheckLutShape(models, issues);
        CheckLimiterVsPeakPower(models, source, issues);
        CheckEffectivePowerScale(models, source, issues);
        CheckLimiterRaise(models, source, issues);

        return new ValidationResult(issues);
    }

    private static void CheckMass(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        var mass = models.Car.TotalMass;
        if (!double.IsFinite(mass) || mass <= 0)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Failure, "mass.positive", mass, 0,
                $"TOTALMASS is {mass} kg; mass must be greater than 0."));
            return; // the range rule would only restate the same defect
        }

        var min = source.Mass * ValidationLimits.MassMinRatio;
        var max = source.Mass * ValidationLimits.MassMaxRatio;
        if (mass < min)
            issues.Add(new ValidationIssue(ValidationSeverity.Failure, "mass.range", mass, min,
                $"TOTALMASS {mass} kg is below {min} kg (source {source.Mass} kg − 60%)."));
        else if (mass > max)
            issues.Add(new ValidationIssue(ValidationSeverity.Failure, "mass.range", mass, max,
                $"TOTALMASS {mass} kg is above {max} kg (source {source.Mass} kg + 60%)."));
    }

    private static void CheckLutShape(CarModelSet models, List<ValidationIssue> issues)
    {
        var rows = models.PowerLut.Rows.ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var (rpm, value) = rows[i];
            if (!double.IsFinite(rpm) || !double.IsFinite(value))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Failure, "lut.finite", value, 0,
                    $"{models.PowerLutFileName} row {i} ({rpm}|{value}) contains a non-finite number."));
                continue;
            }
            // Strictly increasing rpm; a negative first rpm is legal (abarth500 starts at -3000).
            if (i > 0 && rpm <= rows[i - 1].Rpm)
                issues.Add(new ValidationIssue(ValidationSeverity.Failure, "lut.monotonic", rpm, rows[i - 1].Rpm,
                    $"{models.PowerLutFileName} rpm must be strictly increasing: row {i} has {rpm} after {rows[i - 1].Rpm}."));
        }
    }

    /// <summary>
    /// The tuned limiter must not sit below the rpm where the engine makes peak power
    /// (power = torque × rpm, not peak torque). Peak is computed over the usable rev
    /// range — rows at or below the SOURCE limiter — because Kunos LUTs carry overrev
    /// padding rows past the factory limiter (stock bmw_m3_e30: LIMITER=7250 with LUT
    /// rows to 9000 whose raw torque×rpm peaks at 8000; the literal "limiter above
    /// global LUT peak" rule would fail an untouched factory car).
    /// </summary>
    private static void CheckLimiterVsPeakPower(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        var limiter = models.Engine.Limiter;
        if (limiter <= 0)
            return; // LIMITER=0 means no limiter; nothing to compare

        var usable = models.PowerLut.Rows
            .Where(r => source.Limiter <= 0 || r.Rpm <= source.Limiter)
            .Where(r => double.IsFinite(r.Rpm) && double.IsFinite(r.Value))
            .ToList();
        if (usable.Count == 0)
            return; // lut.finite / archive checks already cover degenerate data

        var peak = usable.MaxBy(r => r.Rpm * r.Value);
        if (limiter < peak.Rpm)
            issues.Add(new ValidationIssue(ValidationSeverity.Failure, "limiter.peak", limiter, peak.Rpm,
                $"LIMITER {limiter} rpm is below the peak-power rpm {peak.Rpm} " +
                $"(torque×rpm peaks there); the tune could never reach its own peak power."));
    }

    private static void CheckEffectivePowerScale(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        // Judge the effective multiplier per row (covers scale, curve, and stacking)
        // rather than trusting the spec's numbers.
        var rows = models.PowerLut.Rows.ToList();
        if (rows.Count != source.LutRows.Count)
            return; // v1 transforms never add/remove rows; nothing meaningful to compare

        double maxRatio = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var before = source.LutRows[i].Value;
            var after = rows[i].Value;
            if (before > 0 && double.IsFinite(after))
                maxRatio = Math.Max(maxRatio, after / before);
        }

        if (maxRatio > ValidationLimits.PowerScaleWarnFactor)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "power.scale", maxRatio,
                ValidationLimits.PowerScaleWarnFactor,
                $"Torque is scaled up to {maxRatio:0.##}× the source — past the {ValidationLimits.PowerScaleWarnFactor}× sanity threshold."));
    }

    private static void CheckLimiterRaise(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        var limiter = models.Engine.Limiter;
        if (source.Limiter <= 0 || limiter <= 0)
            return;
        var warnAbove = source.Limiter * ValidationLimits.LimiterRaiseWarnRatio;
        if (limiter > warnAbove)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "limiter.raise", limiter, warnAbove,
                $"LIMITER {limiter} rpm is more than {(ValidationLimits.LimiterRaiseWarnRatio - 1) * 100:0}% above the source {source.Limiter} rpm."));
    }
}
