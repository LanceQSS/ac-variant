using System.Text;
using Acvc.Core.Model;

namespace Acvc.Tests;

/// <summary>Synthetic coverage for the lossless LUT model — no fixtures needed.</summary>
public class PowerLutTests
{
    private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

    [Theory]
    [InlineData("-3000|50\n-250|40\n0|60\n")]
    [InlineData("0|100\n9000|0\n\n\n\n")]              // real files end with blank lines
    [InlineData("; comment\n100 | 60.5 ; note\nnot a row\nabc|def\n1000|55")]
    [InlineData("100|50\r\n200|60\n300|70\r")]         // mixed EOLs
    [InlineData("")]
    public void Roundtrip_is_byte_identical(string text)
    {
        var original = Bytes(text);
        Assert.Equal(original, PowerLut.Parse(original).ToBytes());
    }

    [Fact]
    public void Rows_expose_typed_values_and_skip_non_data_lines()
    {
        var lut = PowerLut.Parse("; torque\n-3000|50\n\n100 | 60.5 ; note\nabc|def\n7750|0\n\n\n");
        Assert.Equal(3, lut.RowCount);
        Assert.Equal((-3000.0, 50.0), lut.GetRow(0));
        Assert.Equal((100.0, 60.5), lut.GetRow(1));
        Assert.Equal((7750.0, 0.0), lut.GetRow(2));
    }

    [Fact]
    public void SetValue_touches_only_that_rows_value_bytes()
    {
        var original = Bytes("0|100\n500|110\t; mid\n1000|130\n");
        var lut = PowerLut.Parse(original);
        lut.SetValue(1, 143);
        var mutated = lut.ToBytes();

        var before = ModelTestUtil.SplitKeepingTerminators(original);
        var after = ModelTestUtil.SplitKeepingTerminators(mutated);
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before[0], after[0]);
        Assert.Equal("500|143\t; mid\n", after[1]);
        Assert.Equal(before[2], after[2]);

        Assert.Equal((500.0, 143.0), PowerLut.Parse(mutated).GetRow(1));
    }

    [Fact]
    public void Decimal_values_write_invariant_and_integral_values_write_without_point()
    {
        var lut = PowerLut.Parse("100|50\n200|60\n");
        lut.SetValue(0, 67.5);
        lut.SetValue(1, 81.0);
        Assert.Equal(Bytes("100|67.5\n200|81\n"), lut.ToBytes());
    }

    [Fact]
    public void Out_of_range_row_fails_loudly()
    {
        var lut = PowerLut.Parse("100|50\n", "power.lut");
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => lut.SetValue(3, 1));
        Assert.Contains("power.lut", ex.Message);
    }

    [Fact]
    public void Utf16_input_fails_loudly()
        => Assert.Throws<NotSupportedException>(() => PowerLut.Parse(new byte[] { 0xFF, 0xFE, 0x31, 0x00 }));
}
