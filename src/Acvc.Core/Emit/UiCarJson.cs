using System.Text;

namespace Acvc.Core.Emit;

/// <summary>
/// Byte-level surgical edit of ui_car.json: append a suffix to the "name" value and
/// change nothing else. No JSON library round-trips formatting losslessly, so the
/// string literal is located and spliced at the byte level — BOM, indentation, key
/// order, trailing commas, everything outside the inserted bytes stays identical.
/// Full "specs"/curve regeneration is Milestone 5; this is deliberately only the name.
/// </summary>
public static class UiCarJson
{
    /// <summary>The " — " (em-dash) marker that introduces a tune suffix in a display name.</summary>
    private static readonly byte[] SuffixMarker = Encoding.UTF8.GetBytes(" — ");

    /// <summary>
    /// Appends <paramref name="suffix"/> to the "name" value — but a variant built
    /// FROM a variant must not stack suffixes ("… — mod — street"), so any existing
    /// " — …" tail in the name is replaced instead of concatenated (M8 amendment;
    /// M6's chained builds exposed the stacking).
    /// </summary>
    public static byte[] AppendToName(byte[] json, string suffix)
    {
        var valueEnd = FindNameValueEnd(json)
            ?? throw new EmitException("ui_car.json has no \"name\" string property to rename.");
        var valueStart = FindNameValueStart(json, valueEnd);

        // Truncate at the first existing suffix marker inside the name value, if any.
        var insertAt = valueEnd;
        for (var i = valueStart; i <= valueEnd - SuffixMarker.Length; i++)
        {
            if (json.AsSpan(i, SuffixMarker.Length).SequenceEqual(SuffixMarker))
            {
                insertAt = i;
                break;
            }
        }

        var escaped = suffix.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var suffixBytes = Encoding.UTF8.GetBytes(escaped);

        var result = new byte[insertAt + suffixBytes.Length + (json.Length - valueEnd)];
        json.AsSpan(0, insertAt).CopyTo(result);
        suffixBytes.CopyTo(result, insertAt);
        json.AsSpan(valueEnd).CopyTo(result.AsSpan(insertAt + suffixBytes.Length));
        return result;
    }

    /// <summary>Byte offset just after the opening quote of the name value that closes at <paramref name="valueEnd"/>.</summary>
    private static int FindNameValueStart(byte[] json, int valueEnd)
    {
        // Walk back to the opening quote; the value scanner guarantees one exists.
        var i = valueEnd - 1;
        while (i >= 0)
        {
            if (json[i] == (byte)'"' && (i == 0 || json[i - 1] != (byte)'\\'))
                return i + 1;
            i--;
        }
        return 0;
    }

    /// <summary>
    /// Byte offset of the closing quote of the first "name": "..." value, or null.
    /// Scanning is ASCII-safe: UTF-8 continuation bytes are ≥ 0x80 and never collide
    /// with the quote/backslash/colon bytes being matched.
    /// </summary>
    private static int? FindNameValueEnd(byte[] json)
    {
        var key = "\"name\""u8;
        for (var i = 0; i + key.Length < json.Length; i++)
        {
            if (!json.AsSpan(i, key.Length).SequenceEqual(key))
                continue;

            var p = i + key.Length;
            while (p < json.Length && IsJsonWhitespace(json[p]))
                p++;
            if (p >= json.Length || json[p] != (byte)':')
                continue;
            p++;
            while (p < json.Length && IsJsonWhitespace(json[p]))
                p++;
            if (p >= json.Length || json[p] != (byte)'"')
                continue;
            p++;

            while (p < json.Length)
            {
                if (json[p] == (byte)'\\')
                    p += 2;
                else if (json[p] == (byte)'"')
                    return p;
                else
                    p++;
            }
            return null; // unterminated string — treat as not found, caller fails loudly
        }
        return null;
    }

    private static bool IsJsonWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
