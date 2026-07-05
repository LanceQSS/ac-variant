using System.Text;
using Acvc.Core.Acd;

namespace Acvc.Tests;

public class AcdPlausibilityTests
{
    private static Dictionary<string, byte[]> Files(params (string Name, byte[] Content)[] files)
        => files.ToDictionary(f => f.Name, f => f.Content, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Kunos_shaped_text_is_plausible()
    {
        var files = Files(
            ("car.ini", Encoding.ASCII.GetBytes("[BASIC]\r\nTOTALMASS=1050\r\n")),
            ("power.lut", Encoding.ASCII.GetBytes("1000|110\n2000|180\n")));
        Assert.Null(AcdPlausibility.FindImplausibility(files));
    }

    [Fact]
    public void Pseudorandom_bytes_are_implausible()
    {
        // Deterministic pseudo-garbage — what a CSP/x4fab archive looks like after a
        // Kunos-cipher decrypt.
        var noise = new byte[4096];
        for (var i = 0; i < noise.Length; i++)
            noise[i] = (byte)(i * 197 + 31);
        var reason = AcdPlausibility.FindImplausibility(Files(("car.ini", noise)));
        Assert.NotNull(reason);
        Assert.Contains("printable", reason);
    }

    [Fact]
    public void Missing_known_files_are_implausible()
    {
        var files = Files(("something.ini", Encoding.ASCII.GetBytes("[A]\nB=1\n")));
        var reason = AcdPlausibility.FindImplausibility(files);
        Assert.NotNull(reason);
        Assert.Contains("car.ini", reason);
    }

    [Fact]
    public void Non_ini_shaped_known_file_is_implausible()
    {
        var files = Files(("car.ini", Encoding.ASCII.GetBytes("just some prose, no sections")));
        Assert.NotNull(AcdPlausibility.FindImplausibility(files));
    }
}
