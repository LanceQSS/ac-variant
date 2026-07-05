using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>Pre-transform snapshot of the source values validation compares against.</summary>
public sealed record SourceSnapshot(
    double Mass,
    int Limiter,
    IReadOnlyList<(double Rpm, double Value)> LutRows,
    IReadOnlyDictionary<string, double> GripRefs,
    double? BrakeMaxTorque,
    double? DiffPower,
    double? DiffCoast)
{
    public static SourceSnapshot Capture(CarModelSet models)
    {
        var gripRefs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (models.Tyres is { } tyres)
            foreach (var section in tyres.CompoundSections)
                foreach (var (key, value) in tyres.GripValues(section))
                    gripRefs[$"{section}/{key}"] = value;

        double? brakeTorque = null;
        if (models.Brakes is { HasMaxTorque: true } brakes)
            brakeTorque = SafeRead(() => brakes.MaxTorque);

        double? diffPower = null, diffCoast = null;
        if (models.Drivetrain.HasDifferential)
        {
            diffPower = SafeRead(() => models.Drivetrain.DiffPower);
            diffCoast = SafeRead(() => models.Drivetrain.DiffCoast);
        }

        return new SourceSnapshot(
            models.Car.TotalMass,
            models.Engine.Limiter,
            models.PowerLut.Rows.ToList(),
            gripRefs,
            brakeTorque,
            diffPower,
            diffCoast);
    }

    private static double? SafeRead(Func<double> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException)
        {
            return null; // absent/unparseable source value: nothing to compare against
        }
    }
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
        CheckGripScale(models, source, issues);
        CheckBrakeTorqueScale(models, source, issues);
        CheckDiffLock(models, source, issues);

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
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "mass.range", mass, min,
                $"TOTALMASS {mass} kg is {(1 - mass / source.Mass):P0} below source {source.Mass} kg — outside ±60% of stock."));
        else if (mass > max)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "mass.range", mass, max,
                $"TOTALMASS {mass} kg is {(mass / source.Mass - 1):P0} above source {source.Mass} kg — outside ±60% of stock."));
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
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "limiter.peak", limiter, peak.Rpm,
                $"LIMITER {limiter} rpm sits below the usable-range peak-power rpm {peak.Rpm} — restrictor-style build."));
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
                $"torque up to {maxRatio:0.##}× source — beyond {ValidationLimits.PowerScaleWarnFactor}× of stock."));
    }

    /// <summary>
    /// Judges the EFFECTIVE grip ratio per key against the pre-transform snapshot
    /// (same philosophy as the power rule: measure what was written, don't trust the
    /// spec). One issue for the worst deviation. No-op tunes are ratio 1 and clean.
    /// </summary>
    private static void CheckGripScale(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        if (models.Tyres is not { } tyres || source.GripRefs.Count == 0)
            return;

        double worstRatio = 1;
        foreach (var section in tyres.CompoundSections)
        {
            foreach (var (key, value) in tyres.GripValues(section))
            {
                if (!source.GripRefs.TryGetValue($"{section}/{key}", out var before) || Math.Abs(before) < 1e-12)
                    continue;
                var ratio = value / before;
                if (Math.Abs(ratio - 1) > Math.Abs(worstRatio - 1))
                    worstRatio = ratio;
            }
        }

        // Zero/negative grip is an integrity failure — the tyre model cannot consume
        // it. Everything else, however extreme, is a factual warning.
        if (worstRatio <= 0)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Failure, "tyres.grip", worstRatio, 0,
                $"grip {worstRatio:0.##}× source — zero or negative grip cannot be consumed by the sim."));
            return;
        }

        var deviation = Math.Abs(worstRatio - 1);
        if (deviation > ValidationLimits.GripScaleEnvelopeDelta)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "tyres.grip", worstRatio,
                worstRatio > 1 ? 1 + ValidationLimits.GripScaleEnvelopeDelta : 1 - ValidationLimits.GripScaleEnvelopeDelta,
                $"grip {worstRatio:0.##}× source — outside the realistic tire envelope (±{ValidationLimits.GripScaleEnvelopeDelta:P0})."));
        else if (deviation > ValidationLimits.GripScaleWarnDelta)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "tyres.grip", worstRatio,
                worstRatio > 1 ? 1 + ValidationLimits.GripScaleWarnDelta : 1 - ValidationLimits.GripScaleWarnDelta,
                $"grip {worstRatio:0.##}× source — beyond ±{ValidationLimits.GripScaleWarnDelta:P0} of stock."));
    }

    private static void CheckBrakeTorqueScale(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        if (models.Brakes is not { HasMaxTorque: true } brakes || source.BrakeMaxTorque is not { } before || before <= 0)
            return;

        double after;
        try
        {
            after = brakes.MaxTorque;
        }
        catch (FormatException)
        {
            return; // unreadable value would have been caught by the file checks
        }

        var ratio = after / before;
        if (ratio <= 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Failure, "brakes.torque", ratio, 0,
                $"brake torque {ratio:0.##}× source — zero or negative braking cannot be consumed by the sim."));
        else if (ratio > ValidationLimits.BrakeTorqueScaleWarnHigh)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "brakes.torque", ratio,
                ValidationLimits.BrakeTorqueScaleWarnHigh,
                $"brake torque {ratio:0.##}× source — above {ValidationLimits.BrakeTorqueScaleWarnHigh}× of stock."));
        else if (ratio < ValidationLimits.BrakeTorqueScaleWarnLow)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "brakes.torque", ratio,
                ValidationLimits.BrakeTorqueScaleWarnLow,
                $"brake torque {ratio:0.##}× source — below {ValidationLimits.BrakeTorqueScaleWarnLow}× of stock."));
    }

    /// <summary>
    /// Lock fractions must land in [0,1] — but only values the tune CHANGED are
    /// judged, so a stock car that already ships odd diff numbers still builds
    /// no-op tunes (the tool judges tunes, not Kunos/mod authors).
    /// </summary>
    private static void CheckDiffLock(CarModelSet models, SourceSnapshot source, List<ValidationIssue> issues)
    {
        if (!models.Drivetrain.HasDifferential)
            return;

        CheckOne("diff.power", source.DiffPower, () => models.Drivetrain.DiffPower);
        CheckOne("diff.coast", source.DiffCoast, () => models.Drivetrain.DiffCoast);

        void CheckOne(string rule, double? before, Func<double> read)
        {
            double after;
            try
            {
                after = read();
            }
            catch (Exception ex) when (ex is KeyNotFoundException or FormatException)
            {
                return;
            }
            var changed = before is not { } b || Math.Abs(after - b) > 1e-9;
            if (!changed)
                return;
            if (after < ValidationLimits.DiffLockMin)
                issues.Add(new ValidationIssue(ValidationSeverity.Failure, rule, after, ValidationLimits.DiffLockMin,
                    $"{rule} = {after} — a negative lock fraction cannot be consumed by the sim."));
            else if (after > ValidationLimits.DiffLockMax)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, rule, after, ValidationLimits.DiffLockMax,
                    $"{rule} = {after} — above the nominal 0–1 lock-fraction range (factory mod data ships values above 1 and runs)."));
        }
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
