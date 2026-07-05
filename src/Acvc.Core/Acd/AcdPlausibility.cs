using System.Text;

namespace Acvc.Core.Acd;

/// <summary>
/// Sanity check on decrypted archive content. Kunos data files are plain ASCII text
/// (INI/LUT), so a correct decryption is overwhelmingly printable and contains the
/// well-known car files. A CSP/x4fab-protected mod decrypted with the Kunos cipher
/// yields uniform garbage and fails both checks.
///
/// Limitation, by construction: this distinguishes Kunos-vs-foreign encryption, not
/// right-vs-wrong Kunos key. Key strings consist solely of ASCII digits and dashes
/// (bytes 45–57), so decrypting with the wrong car's key shifts each byte by at most
/// ±12 and the result still looks like text.
/// </summary>
public static class AcdPlausibility
{
    private const double MinPrintableRatio = 0.85;

    /// <summary>Returns null when content is plausible, otherwise a human-readable reason.</summary>
    public static string? FindImplausibility(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.Count == 0)
            return "the archive contains no files";

        long printable = 0, total = 0;
        foreach (var content in files.Values)
        {
            total += content.Length;
            foreach (var b in content)
                if (IsTextByte(b))
                    printable++;
        }
        if (total > 0)
        {
            var ratio = (double)printable / total;
            if (ratio < MinPrintableRatio)
                return $"only {ratio:P0} of decrypted bytes are printable text " +
                       $"(expected ≥ {MinPrintableRatio:P0} for Kunos INI/LUT data)";
        }

        var known = files.Keys.FirstOrDefault(k =>
            k.Equals("car.ini", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("engine.ini", StringComparison.OrdinalIgnoreCase));
        if (known is null)
            return "neither car.ini nor engine.ini is present in the archive";

        var text = Encoding.ASCII.GetString(files[known]);
        if (!text.Contains('[') || !text.Contains('='))
            return $"{known} decrypted to something that is not INI-shaped (no section header or key=value)";

        return null;
    }

    private static bool IsTextByte(byte b) =>
        b is >= 0x20 and <= 0x7E or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
