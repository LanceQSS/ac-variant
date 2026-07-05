using System.Text;

namespace Acvc.Tests;

/// <summary>
/// Test-only reference builder for data.acd containers, written from the rebuilder
/// script (CarTuner/files/makedata.bms) rather than from the code under test:
/// encryption is (plain + keyByte) mod 256 restarting per file, and every encrypted
/// byte is stored as a 32-bit little-endian field.
/// </summary>
internal static class SyntheticAcd
{
    public static byte[] EncryptReference(byte[] plain, string key)
    {
        var keyBytes = Encoding.ASCII.GetBytes(key);
        var result = new byte[plain.Length];
        for (var i = 0; i < plain.Length; i++)
            result[i] = (byte)(plain[i] + keyBytes[i % keyBytes.Length]);
        return result;
    }

    public static byte[] Build(bool withHeader, params (string Name, byte[] EncryptedContent)[] entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        if (withHeader)
        {
            w.Write(unchecked((int)0xFFFFFBA9)); // negative marker per makedata.bms
            w.Write(0x000A4691);
        }
        foreach (var (name, content) in entries)
        {
            w.Write(name.Length);
            w.Write(Encoding.ASCII.GetBytes(name));
            w.Write(content.Length);
            foreach (var b in content)
                w.Write((int)b);
        }
        w.Flush();
        return ms.ToArray();
    }
}
