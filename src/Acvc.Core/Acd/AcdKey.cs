using System.Globalization;

namespace Acvc.Core.Acd;

/// <summary>
/// Generates the data.acd cipher key from a car folder name.
///
/// Exact port of the key generation in <c>CarTuner/files/getdata.bms</c> (QuickBMS
/// "Assetto Corsa DATA.ACD extractor 0.1.1"). QuickBMS math is signed 32-bit with
/// C-style truncation on division; several script loops mutate the index inside the
/// body, so the effective strides differ from the literal <c>next</c> increments:
/// key1 stride 1, key2 stride 2, key3 start 1 stride 3, key4 start 1 stride 1,
/// key5 start 1 stride 4, key6/key7 stride 2, key8 stride 1.
/// The eight results are masked to a byte and joined as "%d-%d-%d-%d-%d-%d-%d-%d";
/// the ASCII characters of that string are the cipher key stream.
/// </summary>
public static class AcdKey
{
    /// <summary>
    /// Computes the key string for <paramref name="folderName"/> exactly as given —
    /// no case folding here. AC resolves folder names case-insensitively, so callers
    /// unpacking real cars should pass the name lowercased (all Kunos folders already
    /// are); <see cref="AcdUnpacker"/> does this.
    /// </summary>
    public static string Generate(string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            throw new ArgumentException("Car folder name must be non-empty.", nameof(folderName));

        var name = new byte[folderName.Length];
        for (var c = 0; c < folderName.Length; c++)
        {
            var ch = folderName[c];
            if (ch < 0x20 || ch > 0x7E)
                throw new ArgumentException(
                    $"Car folder name '{folderName}' contains non-ASCII character '{ch}' at position {c}; " +
                    "ACD key generation is only defined for printable-ASCII folder names.",
                    nameof(folderName));
            name[c] = (byte)ch;
        }

        var n = name.Length;

        unchecked
        {
            // KEY1: sum of all bytes.
            var key1 = 0;
            for (var i = 0; i < n; i++)
                key1 += name[i];

            // KEY2: body advances i by 1 without restoring it, so the loop strides 2.
            var key2 = 0;
            for (var i = 0; i < n - 1; i += 2)
            {
                key2 *= name[i];
                key2 -= name[i + 1];
            }

            // KEY3: body nets i to i-1, then `next i += 4` — stride 3 from start 1.
            var key3 = 0;
            for (var i = 1; i < n - 3; i += 3)
            {
                key3 *= name[i];
                key3 /= name[i + 1] + 0x1b;
                key3 += -0x1b - name[i - 1];
            }

            // KEY4: 0x1683 minus every byte after the first.
            var key4 = 0x1683;
            for (var i = 1; i < n; i++)
                key4 -= name[i];

            // KEY5: body restores i, `next i += 4` — stride 4 from start 1.
            var key5 = 0x42;
            for (var i = 1; i < n - 4; i += 4)
            {
                var tmp = (name[i] + 0xF) * key5;
                key5 = (name[i - 1] + 0xF) * tmp + 0x16;
            }

            // KEY6: 0x65 minus every second byte.
            var key6 = 0x65;
            for (var i = 0; i < n - 2; i += 2)
                key6 -= name[i];

            // KEY7: repeated modulo by every second byte.
            var key7 = 0xAB;
            for (var i = 0; i < n - 2; i += 2)
                key7 %= name[i];

            // KEY8: body restores i — stride 1, divides then adds the following byte.
            var key8 = 0xAB;
            for (var i = 0; i < n - 1; i++)
            {
                key8 /= name[i];
                key8 += name[i + 1];
            }

            Span<int> keys = stackalloc[] { key1, key2, key3, key4, key5, key6, key7, key8 };
            var parts = new string[8];
            for (var i = 0; i < 8; i++)
                parts[i] = (keys[i] & 0xFF).ToString(CultureInfo.InvariantCulture);
            return string.Join('-', parts);
        }
    }
}
