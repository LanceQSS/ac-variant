using System.Globalization;

namespace Acvc.Core.Model;

/// <summary>Invariant-culture number formatting for values written into AC data files.</summary>
internal static class IniNumber
{
    /// <summary>
    /// Values are rounded to 6 decimals before formatting: AC data never uses more,
    /// and this suppresses binary float noise (40 × 1.35 must emit "54", not
    /// "54.000000000000004").
    /// </summary>
    private const int MaxDecimals = 6;

    /// <summary>
    /// Integral doubles render without a decimal point (mass 1420 → "1420"), everything
    /// else uses .NET's shortest round-trippable representation (1.38 → "1.38").
    /// </summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException($"Cannot write non-finite value '{value}' into a data file.", nameof(value));
        value = Math.Round(value, MaxDecimals);
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats <paramref name="value"/> in the style of the text it replaces: when the
    /// source had N decimal places and the shortest form has fewer, pad to N
    /// ("3.353000" ← 3.9 → "3.900000"; "1.38" ← 1.4 → "1.40"). When the new value
    /// genuinely needs more places than the source, precision wins over style
    /// ("3.15" ← 3.907 → "3.907", never a silently rounded "3.91").
    /// </summary>
    public static string FormatLike(string sourceText, double value)
    {
        var shortest = Format(value);
        var sourceDecimals = CountDecimals(sourceText);
        if (sourceDecimals > 0 && CountDecimals(shortest) < sourceDecimals)
            return Math.Round(value, MaxDecimals)
                .ToString("F" + Math.Min(sourceDecimals, 15), CultureInfo.InvariantCulture);
        return shortest;
    }

    private static int CountDecimals(string text)
    {
        var dot = text.IndexOf('.');
        if (dot < 0)
            return 0;
        var count = 0;
        for (var i = dot + 1; i < text.Length && char.IsAsciiDigit(text[i]); i++)
            count++;
        return count;
    }
}
