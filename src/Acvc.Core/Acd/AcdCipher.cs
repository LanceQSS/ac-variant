using System.Text;

namespace Acvc.Core.Acd;

/// <summary>
/// The data.acd content cipher: a byte-wise ROT keyed on the ASCII characters of the
/// key string produced by <see cref="AcdKey"/>. Decryption subtracts the key bytes
/// cyclically (stored − key, mod 256); confirmed against a stock Kunos car by the
/// milestone gate test. The key stream restarts at index 0 for every file in the
/// archive — the reference .bms script re-arms the cipher per entry.
/// Encryption (plain + key) is deliberately absent: repacking is v2.
/// </summary>
public sealed class AcdCipher
{
    private readonly byte[] _key;

    public AcdCipher(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Cipher key must be non-empty.", nameof(key));
        _key = Encoding.ASCII.GetBytes(key);
    }

    public static AcdCipher ForFolderName(string folderName) => new(AcdKey.Generate(folderName));

    /// <summary>Decrypts one file's content in place and returns the same array.</summary>
    public byte[] Decrypt(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(data[i] - _key[i % _key.Length]);
        return data;
    }
}
