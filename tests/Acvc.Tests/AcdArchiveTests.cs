using System.Text;
using Acvc.Core.Acd;

namespace Acvc.Tests;

public class AcdArchiveTests
{
    private const string Folder = "abarth500";

    private static (string Name, byte[] EncryptedContent) Entry(string name, string plainText, string key)
        => (name, SyntheticAcd.EncryptReference(Encoding.ASCII.GetBytes(plainText), key));

    [Theory]
    [InlineData(false)] // pre-Dream-Pack layout: entries start at offset 0
    [InlineData(true)]  // versioned layout: negative int32 marker + one extra int32
    public void Roundtrip_parse_and_decrypt_recovers_plaintext(bool withHeader)
    {
        var key = AcdKey.Generate(Folder);
        const string carIni = "[BASIC]\nTOTALMASS=1050\n";
        const string engineIni = "[HEADER]\nVERSION=1\n[ENGINE_DATA]\nLIMITER=6500\n";
        var acd = SyntheticAcd.Build(withHeader,
            Entry("car.ini", carIni, key),
            Entry("engine.ini", engineIni, key));

        var entries = AcdArchive.Read(acd);

        Assert.Equal(new[] { "car.ini", "engine.ini" }, entries.Select(e => e.Name));
        // The key stream must restart for every file — decrypt each independently.
        var cipher = new AcdCipher(key);
        Assert.Equal(carIni, Encoding.ASCII.GetString(cipher.Decrypt(entries[0].EncryptedContent)));
        Assert.Equal(engineIni, Encoding.ASCII.GetString(cipher.Decrypt(entries[1].EncryptedContent)));
    }

    [Fact]
    public void Content_bytes_come_from_low_byte_of_each_32bit_field()
    {
        var acd = SyntheticAcd.Build(false, ("a.ini", new byte[] { 0x41, 0x42 }));
        // Corrupt the upper three bytes of each field; the parser must ignore them.
        // Entry layout: 4 (nameLen) + 5 (name) + 4 (size) = 13 bytes before fields.
        acd[14] = 0xDE; acd[15] = 0xAD; acd[16] = 0xBE;
        acd[18] = 0xEF; acd[19] = 0x01; acd[20] = 0x02;

        var entries = AcdArchive.Read(acd);
        Assert.Equal(new byte[] { 0x41, 0x42 }, entries.Single().EncryptedContent);
    }

    [Fact]
    public void Truncated_content_fails_loudly()
    {
        var acd = SyntheticAcd.Build(false, ("car.ini", new byte[] { 1, 2, 3, 4 }));
        var truncated = acd.Take(acd.Length - 6).ToArray();
        var ex = Assert.Throws<AcdFormatException>(() => AcdArchive.Read(truncated));
        Assert.Contains("car.ini", ex.Message);
    }

    [Fact]
    public void Implausible_name_length_fails_loudly()
    {
        // Random-looking positive garbage: first int32 = 0x7EADBEEF => insane name length.
        var garbage = new byte[] { 0xEF, 0xBE, 0xAD, 0x7E, 1, 2, 3, 4, 5, 6, 7, 8 };
        var ex = Assert.Throws<AcdFormatException>(() => AcdArchive.Read(garbage));
        Assert.Contains("name length", ex.Message);
    }

    [Fact]
    public void Tiny_file_fails_loudly()
        => Assert.Throws<AcdFormatException>(() => AcdArchive.Read(new byte[] { 1, 2, 3 }));
}
