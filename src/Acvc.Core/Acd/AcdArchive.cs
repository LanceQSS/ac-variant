using System.Buffers.Binary;
using System.Text;

namespace Acvc.Core.Acd;

/// <summary>One file inside a data.acd container. Content is still encrypted.</summary>
public sealed record AcdFileEntry(string Name, byte[] EncryptedContent);

/// <summary>
/// Parses the data.acd container. Layout, per <c>CarTuner/files/getdata.bms</c> and
/// the companion rebuilder script:
///   - optional 8-byte header, present iff the first int32 (LE) is negative
///     (e.g. 0xFFFFFBA9 followed by 0x000A4691 for post-Dream-Pack archives);
///   - then repeated entries: int32 name length, ASCII name bytes, int32 content
///     size, then <c>size</c> × int32 fields where the low byte of each little-endian
///     field is one (encrypted) content byte — packed size ≈ 4× content.
/// Parsing is structural only; decryption is <see cref="AcdCipher"/>'s job.
/// </summary>
public static class AcdArchive
{
    private const int MaxNameLength = 255;

    public static IReadOnlyList<AcdFileEntry> Read(string path) => Read(File.ReadAllBytes(path));

    public static IReadOnlyList<AcdFileEntry> Read(byte[] acd)
    {
        if (acd.Length < 8)
            throw new AcdFormatException($"File is only {acd.Length} bytes — too small to be a data.acd.");

        var pos = 0;
        var first = BinaryPrimitives.ReadInt32LittleEndian(acd);
        if (first < 0)
            pos = 8; // versioned header: negative marker int32 + one extra int32

        var entries = new List<AcdFileEntry>();
        while (pos < acd.Length)
        {
            var entryStart = pos;
            var nameLength = ReadInt32(acd, ref pos, "entry name length");
            if (nameLength <= 0 || nameLength > MaxNameLength)
                throw new AcdFormatException(
                    $"Entry at offset {entryStart} has name length {nameLength}, which is not a plausible " +
                    "file name. The archive is corrupt or uses non-Kunos (CSP/x4fab) protection.");

            if (pos + nameLength > acd.Length)
                throw new AcdFormatException(
                    $"Entry at offset {entryStart}: name ({nameLength} bytes) runs past end of file.");
            var name = DecodeName(acd, pos, nameLength, entryStart);
            pos += nameLength;

            var size = ReadInt32(acd, ref pos, $"content size of '{name}'");
            if (size < 0)
                throw new AcdFormatException(
                    $"Entry '{name}' at offset {entryStart} declares negative content size {size}.");
            var packed = (long)size * 4;
            if (pos + packed > acd.Length)
                throw new AcdFormatException(
                    $"Entry '{name}' at offset {entryStart} declares {size} content bytes ({packed} packed), " +
                    $"but only {acd.Length - pos} bytes remain in the file.");

            // Each original byte sits in the low byte of a 32-bit little-endian field.
            var content = new byte[size];
            for (var i = 0; i < size; i++)
                content[i] = acd[pos + i * 4];
            pos += (int)packed;

            entries.Add(new AcdFileEntry(name, content));
        }

        if (entries.Count == 0)
            throw new AcdFormatException("Archive contains no entries.");
        return entries;
    }

    private static int ReadInt32(byte[] acd, ref int pos, string what)
    {
        if (pos + 4 > acd.Length)
            throw new AcdFormatException($"Truncated file: expected 4 bytes for {what} at offset {pos}.");
        var value = BinaryPrimitives.ReadInt32LittleEndian(acd.AsSpan(pos));
        pos += 4;
        return value;
    }

    private static string DecodeName(byte[] acd, int pos, int length, int entryStart)
    {
        for (var i = 0; i < length; i++)
        {
            var b = acd[pos + i];
            if (b < 0x20 || b > 0x7E)
                throw new AcdFormatException(
                    $"Entry at offset {entryStart} has a non-printable byte (0x{b:X2}) in its name. " +
                    "The archive is corrupt or uses non-Kunos (CSP/x4fab) protection.");
        }
        return Encoding.ASCII.GetString(acd, pos, length);
    }
}
