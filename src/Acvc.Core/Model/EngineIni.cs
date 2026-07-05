using System.Text.RegularExpressions;

namespace Acvc.Core.Model;

/// <summary>Typed accessor view over engine.ini. The document stays the source of truth.</summary>
public sealed partial class EngineIni
{
    public EngineIni(IniDocument document) => Document = document;

    public IniDocument Document { get; }

    public static EngineIni Parse(byte[] bytes, string? sourceName = "engine.ini")
        => new(IniDocument.Parse(bytes, sourceName));

    /// <summary>[ENGINE_DATA] LIMITER — rev limiter rpm; 0 means no limiter.</summary>
    public int Limiter
    {
        get => Document.GetInt("ENGINE_DATA", "LIMITER");
        set => Document.SetValue("ENGINE_DATA", "LIMITER", IniNumber.Format(value));
    }

    /// <summary>[ENGINE_DATA] MINIMUM — idle rpm.</summary>
    public int Minimum => Document.GetInt("ENGINE_DATA", "MINIMUM");

    /// <summary>[ENGINE_DATA] INERTIA.</summary>
    public double Inertia => Document.GetDouble("ENGINE_DATA", "INERTIA");

    /// <summary>[HEADER] POWER_CURVE — the LUT file the torque curve lives in.</summary>
    public string PowerCurveFile => Document.GetValue("HEADER", "POWER_CURVE");

    /// <summary>[TURBO_n] sections in document order; empty for NA cars.</summary>
    public IReadOnlyList<TurboSection> Turbos
        => Document.SectionNames
            .Where(name => TurboSectionName().IsMatch(name))
            .Select(name => new TurboSection(Document, name))
            .ToArray();

    public bool HasTurbo => Turbos.Count > 0;

    public byte[] ToBytes() => Document.ToBytes();

    [GeneratedRegex(@"^TURBO_\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex TurboSectionName();
}

/// <summary>One [TURBO_n] section of engine.ini.</summary>
public sealed class TurboSection
{
    private readonly IniDocument _document;

    internal TurboSection(IniDocument document, string sectionName)
    {
        _document = document;
        SectionName = sectionName;
    }

    public string SectionName { get; }

    public double MaxBoost
    {
        get => _document.GetDouble(SectionName, "MAX_BOOST");
        set => _document.SetValue(SectionName, "MAX_BOOST", IniNumber.Format(value));
    }

    public double Wastegate
    {
        get => _document.GetDouble(SectionName, "WASTEGATE");
        set => _document.SetValue(SectionName, "WASTEGATE", IniNumber.Format(value));
    }
}
