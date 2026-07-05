using System.Security.Cryptography;
using System.Text;
using Acvc.Core.Acd;

namespace Acvc.Tests;

internal static class ModelTestUtil
{
    public const string FixtureSkipReason =
        "tests/fixtures/ has no car with data.acd — run scripts/make-fixtures.ps1 against a local AC install";

    /// <summary>Decrypted files of a fixture car, or null when that fixture is absent.</summary>
    public static UnpackedData? TryLoadFixtureCar(string carName)
    {
        var folder = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals(carName, StringComparison.OrdinalIgnoreCase));
        return folder is null ? null : AcdUnpacker.Load(folder);
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Splits into physical lines, each including its own terminator, so positional
    /// comparison shows exactly which lines changed. Latin-1: byte-true.
    /// </summary>
    public static List<string> SplitKeepingTerminators(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var lines = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                i++;
            if (i < text.Length)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i += 2;
                else
                    i += 1;
            }
            lines.Add(text[start..i]);
        }
        return lines;
    }
}
