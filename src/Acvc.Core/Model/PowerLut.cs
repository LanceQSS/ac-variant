using System.Globalization;
using System.Text;

namespace Acvc.Core.Model;

/// <summary>
/// Lossless reader-writer for AC .lut files ("rpm|value" per line; power.lut is
/// torque-at-crank vs rpm). Same preservation strategy as <see cref="IniDocument"/>:
/// per-line nodes with exact spans and terminators, Latin-1 byte-true decoding,
/// blank/comment/garbage lines kept verbatim. Data rows are exposed as typed doubles;
/// <see cref="SetValue"/> rewrites only the value span of one row.
/// </summary>
public sealed class PowerLut
{
    private abstract class LutLine
    {
        public string Terminator = "";
        public abstract string RenderContent();
        public string Render() => RenderContent() + Terminator;
    }

    private sealed class RawLutLine : LutLine
    {
        public string Text = "";
        public override string RenderContent() => Text;
    }

    private sealed class DataLutLine : LutLine
    {
        public string LeadingTrivia = "";
        public string RpmText = "";
        public string PreBarTrivia = "";
        public string PostBarTrivia = "";
        public string ValueText = "";
        public string TrailingTrivia = "";  // trailing whitespace + inline comment, verbatim
        public double Rpm;
        public double Value;
        public override string RenderContent()
            => $"{LeadingTrivia}{RpmText}{PreBarTrivia}|{PostBarTrivia}{ValueText}{TrailingTrivia}";
    }

    private readonly List<LutLine> _lines;
    private readonly List<DataLutLine> _rows;

    private PowerLut(List<LutLine> lines, bool hasUtf8Bom, string? sourceName)
    {
        _lines = lines;
        _rows = lines.OfType<DataLutLine>().ToList();
        HasUtf8Bom = hasUtf8Bom;
        SourceName = sourceName;
    }

    public bool HasUtf8Bom { get; }
    public string? SourceName { get; }

    public static PowerLut Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

    public static PowerLut Parse(byte[] bytes, string? sourceName = null)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            throw new NotSupportedException(
                $"{sourceName ?? "LUT data"} is UTF-16 encoded; AC data files are ASCII/UTF-8 and UTF-16 is not supported.");

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.Latin1.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        var lines = new List<LutLine>();
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
            var line = ParseLine(content);
            line.Terminator = terminator;
            lines.Add(line);
        }
        return new PowerLut(lines, hasBom, sourceName);
    }

    /// <summary>Convenience for synthetic content; equivalent to parsing Latin-1 bytes.</summary>
    public static PowerLut Parse(string text, string? sourceName = null)
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

    public int RowCount => _rows.Count;

    public (double Rpm, double Value) GetRow(int index)
    {
        var row = RowAt(index);
        return (row.Rpm, row.Value);
    }

    public IEnumerable<(double Rpm, double Value)> Rows
        => _rows.Select(r => (r.Rpm, r.Value));

    /// <summary>
    /// Rewrites the value span of row <paramref name="index"/>; rpm and all trivia
    /// untouched. Formats in the style of the replaced text (decimal-place count
    /// preserved) and stores the value exactly as it will read back from the emitted
    /// bytes, so the typed view never disagrees with the file.
    /// </summary>
    public void SetValue(int index, double newValue)
    {
        var row = RowAt(index);
        row.ValueText = IniNumber.FormatLike(row.ValueText, newValue);
        row.Value = double.Parse(row.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private DataLutLine RowAt(int index)
    {
        if (index < 0 || index >= _rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"{SourceName ?? "LUT"} has {_rows.Count} data rows; index {index} is out of range.");
        return _rows[index];
    }

    // ---- parsing ------------------------------------------------------------

    private static LutLine ParseLine(string content)
    {
        var ws = 0;
        while (ws < content.Length && content[ws] is ' ' or '\t')
            ws++;
        if (ws == content.Length || content[ws] == ';'
            || (content[ws] == '/' && ws + 1 < content.Length && content[ws + 1] == '/'))
            return new RawLutLine { Text = content };

        var bar = content.IndexOf('|');
        if (bar < 0)
            return new RawLutLine { Text = content };

        // Left side: rpm.
        var rpmEnd = bar;
        while (rpmEnd > ws && content[rpmEnd - 1] is ' ' or '\t')
            rpmEnd--;
        var rpmText = content[ws..rpmEnd];

        // Right side: value, then optional whitespace + inline comment.
        var right = content[(bar + 1)..];
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
        var valueText = valueRegion[vStart..vEnd];

        if (!double.TryParse(rpmText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm) ||
            !double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return new RawLutLine { Text = content };

        return new DataLutLine
        {
            LeadingTrivia = content[..ws],
            RpmText = rpmText,
            PreBarTrivia = content[rpmEnd..bar],
            PostBarTrivia = valueRegion[..vStart],
            ValueText = valueText,
            TrailingTrivia = valueRegion[vEnd..] + comment,
            Rpm = rpm,
            Value = value,
        };
    }
}
