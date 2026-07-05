using System.Globalization;
using System.Text;

namespace Acvc.Core.Spec;

/// <summary>
/// Serializes a <see cref="TunePlan"/> back to tune-spec TOML — the GUI's Save
/// button (the spec is the shareable artifact; the sharing story is specs, never
/// data). Output round-trips through <see cref="TuneSpecParser.Parse"/>.
/// </summary>
public static class TuneSpecWriter
{
    public static string Write(TunePlan plan)
    {
        var sb = new StringBuilder();
        sb.Append("[meta]\n");
        sb.Append($"source_car = \"{plan.SourceCar}\"\n");
        sb.Append($"tune_name  = \"{plan.TuneName}\"\n");

        if (plan.PowerScale is not null || plan.PowerCurve is not null)
        {
            sb.Append("\n[power]\n");
            if (plan.PowerScale is { } scale)
                sb.Append($"scale = {Num(scale)}\n");
            if (plan.PowerCurve is { } curve)
                sb.Append("curve = [ " + string.Join(", ", curve.Select(r =>
                    $"{{ from = {Num(r.FromRpm)}, to = {Num(r.ToRpm)}, factor = {Num(r.Factor)} }}")) + " ]\n");
        }

        if (plan.Limiter is not null || plan.Boost is not null)
        {
            sb.Append("\n[engine]\n");
            if (plan.Limiter is { } limiter)
                sb.Append($"limiter = {limiter.ToString(CultureInfo.InvariantCulture)}\n");
            if (plan.Boost is { } boost)
                sb.Append($"boost = {{ max = {Num(boost.Max)}, wastegate = {Num(boost.Wastegate)} }}\n");
        }

        if (plan.FinalDrive is not null || plan.Gears is not null)
        {
            sb.Append("\n[drivetrain]\n");
            if (plan.FinalDrive is { } final)
                sb.Append($"final = {Num(final)}\n");
            if (plan.Gears is { } gears)
                sb.Append("gears = [" + string.Join(", ", gears.Select(Num)) + "]\n");
        }

        if (plan.MassTotal is { } mass)
            sb.Append($"\n[mass]\ntotal = {Num(mass)}\n");
        if (plan.GripScale is { } grip)
            sb.Append($"\n[tyres]\ngrip_scale = {Num(grip)}\n");
        if (plan.BrakeTorqueScale is { } brake)
            sb.Append($"\n[brakes]\ntorque_scale = {Num(brake)}\n");

        if (plan.DiffPower is not null || plan.DiffCoast is not null)
        {
            sb.Append("\n[diff]\n");
            if (plan.DiffPower is { } power)
                sb.Append($"power = {Num(power)}\n");
            if (plan.DiffCoast is { } coast)
                sb.Append($"coast = {Num(coast)}\n");
        }

        return sb.ToString();
    }

    /// <summary>TOML floats need a decimal point or exponent to stay floats on re-parse — but our
    /// parser accepts integers as doubles, so plain shortest invariant works for both.</summary>
    private static string Num(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}
