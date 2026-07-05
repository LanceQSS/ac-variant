using System.Text;
using Acvc.Core.Emit;

namespace Acvc.Tests;

public class UiCarJsonTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Appends_suffix_inside_the_name_string_only()
    {
        var input = Utf8("{\"name\": \"Fake Car\",\n\"brand\": \"Kunos\"}");
        var output = UiCarJson.AppendToName(input, " — street_600");
        Assert.Equal(Utf8("{\"name\": \"Fake Car — street_600\",\n\"brand\": \"Kunos\"}"), output);
    }

    [Fact]
    public void Preserves_bom_spacing_and_everything_else_byte_for_byte()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = "{\r\n\t\"name\"  :   \"Foo\" ,\r\n\t\"power\": \"200 bhp\"\r\n}";
        var input = bom.Concat(Utf8(body)).ToArray();

        var output = UiCarJson.AppendToName(input, " — t");

        var expected = bom.Concat(Utf8(body.Replace("\"Foo\"", "\"Foo — t\""))).ToArray();
        Assert.Equal(expected, output);
    }

    [Fact]
    public void Handles_escaped_quotes_inside_the_name_value()
    {
        var input = Utf8("{\"name\": \"Fo\\\"o\", \"x\": 1}");
        var output = UiCarJson.AppendToName(input, "-t");
        Assert.Equal(Utf8("{\"name\": \"Fo\\\"o-t\", \"x\": 1}"), output);
    }

    [Fact]
    public void Uses_the_first_name_property()
    {
        var input = Utf8("{\"name\": \"Outer\", \"specs\": {\"name\": \"Inner\"}}");
        var output = UiCarJson.AppendToName(input, "!");
        Assert.Equal(Utf8("{\"name\": \"Outer!\", \"specs\": {\"name\": \"Inner\"}}"), output);
    }

    [Fact]
    public void Suffix_with_json_special_characters_is_escaped()
    {
        var input = Utf8("{\"name\": \"A\"}");
        var output = UiCarJson.AppendToName(input, " \"q\"");
        Assert.Equal(Utf8("{\"name\": \"A \\\"q\\\"\"}"), output);
    }

    [Fact]
    public void Missing_name_fails_loudly()
    {
        var ex = Assert.Throws<EmitException>(
            () => UiCarJson.AppendToName(Utf8("{\"brand\": \"Kunos\"}"), "x"));
        Assert.Contains("name", ex.Message);
    }
}
