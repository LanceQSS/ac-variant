namespace Acvc.Core.Model;

/// <summary>
/// One physical line of an INI file. Every node keeps its exact source text split
/// into spans plus its own line terminator, so a document renders back byte-for-byte
/// even with mixed line endings. Rendering is pure concatenation of the spans —
/// losslessness holds no matter how a line was classified.
/// </summary>
public abstract class IniLine
{
    /// <summary>"\r\n", "\n", "\r", or "" for a final line with no newline.</summary>
    public string Terminator { get; internal set; } = "";

    /// <summary>The line's text without its terminator.</summary>
    public abstract string RenderContent();

    public string Render() => RenderContent() + Terminator;
}

/// <summary>Blank line, full-line comment, or anything unrecognized — kept verbatim.</summary>
public sealed class RawIniLine : IniLine
{
    public string Text { get; internal set; } = "";
    public override string RenderContent() => Text;
}

/// <summary>A "[NAME]" line. Trailing trivia is everything after ']' verbatim.</summary>
public sealed class SectionHeaderIniLine : IniLine
{
    public string LeadingTrivia { get; internal set; } = "";
    public string Name { get; internal set; } = "";
    public string TrailingTrivia { get; internal set; } = "";
    public override string RenderContent() => $"{LeadingTrivia}[{Name}]{TrailingTrivia}";
}

/// <summary>
/// A "KEY=VALUE" line. The value is isolated from surrounding whitespace and any
/// inline comment so a mutation replaces only the value's bytes.
/// </summary>
public sealed class KeyValueIniLine : IniLine
{
    public string LeadingTrivia { get; internal set; } = "";
    public string Key { get; internal set; } = "";
    public string PreEqualsTrivia { get; internal set; } = "";
    public string PostEqualsTrivia { get; internal set; } = "";
    public string Value { get; internal set; } = "";
    /// <summary>Whitespace after the value plus any inline comment, verbatim.</summary>
    public string TrailingTrivia { get; internal set; } = "";

    public override string RenderContent()
        => $"{LeadingTrivia}{Key}{PreEqualsTrivia}={PostEqualsTrivia}{Value}{TrailingTrivia}";
}
