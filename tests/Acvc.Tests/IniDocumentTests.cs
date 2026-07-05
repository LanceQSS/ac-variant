using System.Text;
using Acvc.Core.Model;

namespace Acvc.Tests;

/// <summary>Synthetic trivia coverage for the lossless INI model — no fixtures needed.</summary>
public class IniDocumentTests
{
    private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

    [Theory]
    [InlineData("A=1\r\nB=2\nC=3\rD=4")]              // mixed EOLs, no trailing newline
    [InlineData("[S]\nK=1\n\n\n")]                    // trailing blank lines
    [InlineData("\t [ S ] ; c\n\tK = 1 \t; x\n")]     // exotic padding everywhere
    [InlineData("junk without equals\n[NOCLOSE\nK=1")]// garbage + malformed header
    [InlineData("")]                                   // empty file
    [InlineData("; only a comment, no newline")]
    public void Roundtrip_is_byte_identical(string text)
    {
        var original = Bytes(text);
        Assert.Equal(original, IniDocument.Parse(original).ToBytes());
    }

    [Fact]
    public void Mixed_line_endings_are_preserved_per_line_and_dominant_detected()
    {
        var doc = IniDocument.Parse("A=1\r\nB=2\r\nC=3\n");
        Assert.Equal(new[] { "\r\n", "\r\n", "\n" }, doc.Lines.Select(l => l.Terminator));
        Assert.Equal("\r\n", doc.DominantNewLine);
        Assert.Equal("\n", IniDocument.Parse("A=1\nB=2\n").DominantNewLine);
    }

    [Fact]
    public void Utf8_bom_is_detected_and_preserved()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes("[S]\nK=1\n")).ToArray();
        var doc = IniDocument.Parse(original);
        Assert.True(doc.HasUtf8Bom);
        Assert.Equal("1", doc.GetValue("S", "K"));
        Assert.Equal(original, doc.ToBytes());
    }

    [Fact]
    public void Utf16_input_fails_loudly()
    {
        Assert.Throws<NotSupportedException>(() => IniDocument.Parse(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }));
        Assert.Throws<NotSupportedException>(() => IniDocument.Parse(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }));
    }

    [Fact]
    public void Non_ascii_bytes_roundtrip_via_latin1()
    {
        var original = Bytes("; temp \xB0C \xE9\n[S]\nK=1\n");
        Assert.Equal(original, IniDocument.Parse(original).ToBytes());
    }

    [Fact]
    public void Duplicate_keys_last_wins_for_get_and_set_and_both_survive_emit()
    {
        var doc = IniDocument.Parse("[S]\nK=1\nK=2\n");
        Assert.Equal("2", doc.GetValue("S", "K"));

        doc.SetValue("S", "K", "9");
        Assert.Equal(Bytes("[S]\nK=1\nK=9\n"), doc.ToBytes());
    }

    [Fact]
    public void Repeated_sections_merge_with_last_wins()
    {
        var doc = IniDocument.Parse("[S]\nA=1\n[T]\nB=2\n[S]\nA=3\nC=4\n");
        Assert.Equal("3", doc.GetValue("S", "A"));
        Assert.Equal("4", doc.GetValue("S", "C"));

        doc.SetValue("S", "A", "8");
        Assert.Equal(Bytes("[S]\nA=1\n[T]\nB=2\n[S]\nA=8\nC=4\n"), doc.ToBytes());
    }

    [Fact]
    public void Set_preserves_alignment_tabs_and_inline_comment()
    {
        var doc = IniDocument.Parse("[S]\nK=5\t\t; comment\n");
        doc.SetValue("S", "K", "77");
        Assert.Equal(Bytes("[S]\nK=77\t\t; comment\n"), doc.ToBytes());
    }

    [Fact]
    public void Value_with_trailing_whitespace_and_no_comment_is_read_clean_and_set_in_place()
    {
        // Real case: abarth500 drivetrain.ini has "GEAR_1=3.909\t\t" with no comment.
        var doc = IniDocument.Parse("[GEARS]\nGEAR_1=3.909\t\t\n");
        Assert.Equal("3.909", doc.GetValue("GEARS", "GEAR_1"));

        doc.SetValue("GEARS", "GEAR_1", "4.1");
        Assert.Equal(Bytes("[GEARS]\nGEAR_1=4.1\t\t\n"), doc.ToBytes());
    }

    [Fact]
    public void Slash_slash_comments_are_recognized()
    {
        var doc = IniDocument.Parse("// header comment\n[S]\nK=5 // note\n");
        Assert.Equal("5", doc.GetValue("S", "K"));
        doc.SetValue("S", "K", "6");
        Assert.Equal(Bytes("// header comment\n[S]\nK=6 // note\n"), doc.ToBytes());
    }

    [Fact]
    public void Value_may_contain_spaces()
    {
        var doc = IniDocument.Parse("[INFO]\nSCREEN_NAME=Abarth 500 EsseEsse\n");
        Assert.Equal("Abarth 500 EsseEsse", doc.GetValue("INFO", "SCREEN_NAME"));
    }

    [Fact]
    public void Lookup_is_case_insensitive_but_text_is_preserved()
    {
        var doc = IniDocument.Parse("[Basic]\nTotalMass=10\n");
        Assert.Equal("10", doc.GetValue("BASIC", "TOTALMASS"));
        doc.SetValue("basic", "totalmass", "11");
        Assert.Equal(Bytes("[Basic]\nTotalMass=11\n"), doc.ToBytes());
    }

    [Fact]
    public void Keys_before_any_section_live_in_the_empty_section()
    {
        var doc = IniDocument.Parse("ROOT=1\n[S]\nK=2\n");
        Assert.Equal("1", doc.GetValue("", "ROOT"));
    }

    [Fact]
    public void Missing_key_or_section_fails_loudly_with_context()
    {
        var doc = IniDocument.Parse("[S]\nK=1\n", "engine.ini");
        var get = Assert.Throws<KeyNotFoundException>(() => doc.GetValue("S", "NOPE"));
        Assert.Contains("engine.ini", get.Message);
        Assert.Contains("NOPE", get.Message);
        Assert.Throws<KeyNotFoundException>(() => doc.SetValue("TURBO_0", "MAX_BOOST", "1.5"));
    }

    [Fact]
    public void Set_rejects_values_that_would_change_meaning_on_reparse()
    {
        var doc = IniDocument.Parse("[S]\nK=1\n");
        Assert.Throws<ArgumentException>(() => doc.SetValue("S", "K", "1;x"));
        Assert.Throws<ArgumentException>(() => doc.SetValue("S", "K", "1//x"));
        Assert.Throws<ArgumentException>(() => doc.SetValue("S", "K", "1\n2"));
    }

    [Fact]
    public void Typed_getters_fail_loudly_on_non_numeric_values()
    {
        var doc = IniDocument.Parse("[S]\nK=abc\n", "car.ini");
        var ex = Assert.Throws<FormatException>(() => doc.GetDouble("S", "K"));
        Assert.Contains("abc", ex.Message);
        Assert.Throws<FormatException>(() => doc.GetInt("S", "K"));
    }
}
