using System.Globalization;

namespace Acvc.Core.Model;

/// <summary>Invariant-culture number formatting for values written into AC data files.</summary>
internal static class IniNumber
{
    /// <summary>
    /// Integral doubles render without a decimal point (mass 1420 → "1420"), everything
    /// else uses .NET's shortest round-trippable representation (1.38 → "1.38").
    /// </summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException($"Cannot write non-finite value '{value}' into a data file.", nameof(value));
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
