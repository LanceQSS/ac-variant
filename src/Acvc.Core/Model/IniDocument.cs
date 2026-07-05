using System.Text;

namespace Acvc.Core.Model;

/// <summary>
/// Lossless INI document: an ordered list of line nodes preserving all trivia —
/// whitespace, per-line line endings, comments, duplicate keys, unknown sections.
/// Bytes are decoded via Latin-1 (a 1:1 byte↔char mapping), so
/// <c>Parse(bytes).ToBytes()</c> is byte-identical by construction; a UTF-8 BOM is
/// captured separately and re-emitted. Typed lookups are case-insensitive and
/// last-occurrence-wins (matching INI readers that overwrite on re-read); setters
/// replace only the value span of the located line.
/// </summary>
public sealed class IniDocument
{
    private readonly List<IniLine> _lines;

    private IniDocument(List<IniLine> lines, bool hasUtf8Bom, string? sourceName)
    {
        _lines = lines;
        HasUtf8Bom = hasUtf8Bom;
        SourceName = sourceName;
    }

    public IReadOnlyList<IniLine> Lines => _lines;
    public bool HasUtf8Bom { get; }
    /// <summary>Optional label (e.g. file name) used in error messages.</summary>
    public string? SourceName { get; }

    /// <summary>
    /// The document's own dominant line terminator — for callers that later need to
    /// synthesize new lines in this file's style. Existing lines keep theirs verbatim.
    /// </summary>
    public string DominantNewLine
    {
        get
        {
            int crlf = 0, lf = 0, cr = 0;
            foreach (var line in _lines)
            {
                switch (line.Terminator)
                {
                    case "\r\n": crlf++; break;
                    case "\n": lf++; break;
                    case "\r": cr++; break;
                }
            }
            if (crlf >= lf && crlf >= cr && crlf > 0) return "\r\n";
            if (cr > lf) return "\r";
            return "\n";
        }
    }

    public static IniDocument Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

    public static IniDocument Parse(byte[] bytes, string? sourceName = null)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            throw new NotSupportedException(
                $"{sourceName ?? "INI data"} is UTF-16 encoded; AC data files are ASCII/UTF-8 and UTF-16 is not supported.");

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.Latin1.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        var lines = new List<IniLine>();
        foreach (var (content, terminator) in SplitLines(text))
        {
            var line = ParseLine(content);
            line.Terminator = terminator;
            lines.Add(line);
        }
        return new IniDocument(lines, hasBom, sourceName);
    }

    /// <summary>Convenience for synthetic content; equivalent to parsing Latin-1 bytes of <paramref name="text"/>.</summary>
    public static IniDocument Parse(string text, string? sourceName = null)
        => Parse(Encoding.Latin1.GetBytes(text), sourceName);

    public byte[] ToBytes()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
            sb.Append(line.Render());
        var body = Encoding.Latin1.GetBytes(sb.ToString());
        if (!HasUtf8Bom)
            return body;
        var result = new byte[body.Length + 3];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }

    // ---- typed access -------------------------------------------------------

    public bool HasSection(string section)
        => _lines.OfType<SectionHeaderIniLine>()
            .Any(s => string.Equals(s.Name.Trim(), section, StringComparison.OrdinalIgnoreCase));

    /// <summary>Distinct section names in document order.</summary>
    public IReadOnlyList<string> SectionNames
        => _lines.OfType<SectionHeaderIniLine>()
            .Select(s => s.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGetValue(string section, string key, out string value)
    {
        var line = FindLast(section, key);
        value = line?.Value ?? "";
        return line is not null;
    }

    public string GetValue(string section, string key)
        => FindLast(section, key)?.Value
           ?? throw new KeyNotFoundException($"{Describe()}: [{section}] {key} not found.");

    public double GetDouble(string section, string key)
    {
        var raw = GetValue(section, key);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"{Describe()}: [{section}] {key} value '{raw}' is not a number.");
        return value;
    }

    public int GetInt(string section, string key)
    {
        var raw = GetValue(section, key);
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"{Describe()}: [{section}] {key} value '{raw}' is not an integer.");
        return value;
    }

    /// <summary>
    /// Replaces the value span of the last occurrence of [section] key. Everything
    /// else on the line — indentation, alignment tabs, inline comment — is untouched.
    /// Throws if the key does not exist: the model layer never invents lines silently.
    /// </summary>
    public void SetValue(string section, string key, string newValue)
    {
        ArgumentNullException.ThrowIfNull(newValue);
        if (newValue.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new ArgumentException($"New value for [{section}] {key} must not contain line breaks.", nameof(newValue));
        if (newValue.Contains(';') || newValue.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException(
                $"New value for [{section}] {key} must not contain comment markers (';' or '//').", nameof(newValue));

        var line = FindLast(section, key)
            ?? throw new KeyNotFoundException($"{Describe()}: cannot set [{section}] {key} — key not found.");
        line.Value = newValue;
    }

    /// <summary>
    /// Like <see cref="SetValue"/> but formats the number in the style of the value it
    /// replaces (see <see cref="IniNumber.FormatLike"/>): FINAL=3.353000 set to 3.9
    /// becomes FINAL=3.900000. Always invariant culture.
    /// </summary>
    public void SetDouble(string section, string key, double value)
    {
        var line = FindLast(section, key)
            ?? throw new KeyNotFoundException($"{Describe()}: cannot set [{section}] {key} — key not found.");
        line.Value = IniNumber.FormatLike(line.Value, value);
    }

    private KeyValueIniLine? FindLast(string section, string key)
    {
        KeyValueIniLine? found = null;
        var current = "";
        foreach (var line in _lines)
        {
            switch (line)
            {
                case SectionHeaderIniLine header:
                    current = header.Name.Trim();
                    break;
                case KeyValueIniLine kv when
                    string.Equals(current, section, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase):
                    found = kv;
                    break;
            }
        }
        return found;
    }

    private string Describe() => SourceName ?? "INI document";

    // ---- parsing ------------------------------------------------------------

    private static IEnumerable<(string Content, string Terminator)> SplitLines(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && text[i] != '\r' && text[i] != '\n')
                i++;
            var content = text[start..i];
            string terminator;
            if (i >= text.Length)
                terminator = "";
            else if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                terminator = "\r\n";
                i += 2;
            }
            else
            {
                terminator = text[i].ToString();
                i += 1;
            }
            yield return (content, terminator);
        }
    }

    private static IniLine ParseLine(string content)
    {
        var ws = 0;
        while (ws < content.Length && content[ws] is ' ' or '\t')
            ws++;
        if (ws == content.Length)
            return new RawIniLine { Text = content };

        var c = content[ws];
        if (c == ';' || (c == '/' && ws + 1 < content.Length && content[ws + 1] == '/'))
            return new RawIniLine { Text = content };

        if (c == '[')
        {
            var close = content.IndexOf(']', ws);
            if (close < 0)
                return new RawIniLine { Text = content };
            return new SectionHeaderIniLine
            {
                LeadingTrivia = content[..ws],
                Name = content[(ws + 1)..close],
                TrailingTrivia = content[(close + 1)..],
            };
        }

        var eq = content.IndexOf('=');
        if (eq <= ws)
            return new RawIniLine { Text = content };

        var keyEnd = eq;
        while (keyEnd > ws && content[keyEnd - 1] is ' ' or '\t')
            keyEnd--;

        var right = content[(eq + 1)..];
        var semicolon = right.IndexOf(';');
        var slashes = right.IndexOf("//", StringComparison.Ordinal);
        var commentStart = (semicolon, slashes) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => slashes,
            (_, < 0) => semicolon,
            _ => Math.Min(semicolon, slashes),
        };
        var valueRegion = commentStart < 0 ? right : right[..commentStart];
        var comment = commentStart < 0 ? "" : right[commentStart..];

        var vStart = 0;
        while (vStart < valueRegion.Length && valueRegion[vStart] is ' ' or '\t')
            vStart++;
        var vEnd = valueRegion.Length;
        while (vEnd > vStart && valueRegion[vEnd - 1] is ' ' or '\t')
            vEnd--;

        return new KeyValueIniLine
        {
            LeadingTrivia = content[..ws],
            Key = content[ws..keyEnd],
            PreEqualsTrivia = content[keyEnd..eq],
            PostEqualsTrivia = valueRegion[..vStart],
            Value = valueRegion[vStart..vEnd],
            TrailingTrivia = valueRegion[vEnd..] + comment,
        };
    }
}
