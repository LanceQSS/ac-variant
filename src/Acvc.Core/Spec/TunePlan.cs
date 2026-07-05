namespace Acvc.Core.Spec;

/// <summary>A tune spec failed to parse or violated the spec schema.</summary>
public class TuneSpecException : Exception
{
    public TuneSpecException(string message) : base(message) { }
}

/// <summary>[engine] boost — both values are explicit; there are no silent defaults.</summary>
public sealed record BoostSpec(double Max, double Wastegate);

/// <summary>[power] curve entry: factor applied to LUT rows with FromRpm ≤ rpm ≤ ToRpm.</summary>
public sealed record PowerCurveRange(double FromRpm, double ToRpm, double Factor);

/// <summary>
/// Typed result of parsing a tune spec. Null means "table/key absent — leave the car
/// alone"; the v1 transform set (CLAUDE.md) is the complete surface.
/// </summary>
public sealed record TunePlan
{
    public required string SourceCar { get; init; }
    public required string TuneName { get; init; }

    public double? PowerScale { get; init; }
    public IReadOnlyList<PowerCurveRange>? PowerCurve { get; init; }
    public int? Limiter { get; init; }
    public BoostSpec? Boost { get; init; }
    public double? FinalDrive { get; init; }
    public IReadOnlyList<double>? Gears { get; init; }
    public double? MassTotal { get; init; }
}
