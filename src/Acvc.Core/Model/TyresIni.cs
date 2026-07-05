using System.Globalization;
using System.Text.RegularExpressions;

namespace Acvc.Core.Model;

/// <summary>Typed accessor view over tyres.ini. The document stays the source of truth.</summary>
public sealed partial class TyresIni
{
    /// <summary>
    /// The grip keys tyres.grip_scale operates on — both families, per the M6 survey:
    /// all 184 surveyed V10 cars carry DX_REF/DY_REF and the legacy DX0/DX1/DY0/DY1
    /// side by side. Scaling both keys of a family scales the whole grip curve, so a
    /// uniform factor is correct regardless of which family the sim reads.
    /// FRICTION_LIMIT_ANGLE is deliberately not here.
    /// </summary>
    public static readonly string[] GripKeys = { "DX_REF", "DY_REF", "DX0", "DX1", "DY0", "DY1" };

    public TyresIni(IniDocument document) => Document = document;

    public IniDocument Document { get; }

    public static TyresIni Parse(byte[] bytes, string? sourceName = "tyres.ini")
        => new(IniDocument.Parse(bytes, sourceName));

    /// <summary>[HEADER] VERSION as written, or null when absent (pre-versioned models).</summary>
    public string? Version
        => Document.TryGetValue("HEADER", "VERSION", out var version) ? version : null;

    /// <summary>Compound sections (FRONT, REAR, FRONT_1, ...); THERMAL_* are not compounds.</summary>
    public IReadOnlyList<string> CompoundSections
        => Document.SectionNames.Where(s => CompoundName().IsMatch(s)).ToArray();

    /// <summary>
    /// Compounds using curve-based grip: DX_CURVE/DY_CURVE with a real value.
    /// "0" or empty counts as disabled (CM convention) — only an actual curve
    /// reference (e.g. DX_CURVE=tire_PS4S_265_DX+1.lut) makes REF-key scaling
    /// meaningless.
    /// </summary>
    public IReadOnlyList<string> CurveGripSections
        => CompoundSections.Where(section =>
            IsActiveCurve(section, "DX_CURVE") || IsActiveCurve(section, "DY_CURVE")).ToArray();

    /// <summary>Values of every present grip key in <paramref name="section"/>, keyed by key name.</summary>
    public IReadOnlyDictionary<string, double> GripValues(string section)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in GripKeys)
        {
            if (Document.TryGetValue(section, key, out var text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                values[key] = value;
        }
        return values;
    }

    public void SetGripValue(string section, string key, double value)
        => Document.SetDouble(section, key, value);

    private bool IsActiveCurve(string section, string key)
        => Document.TryGetValue(section, key, out var value) &&
           value.Trim() is not ("" or "0");

    public byte[] ToBytes() => Document.ToBytes();

    [GeneratedRegex(@"^(FRONT|REAR)(_\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CompoundName();
}
