using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;

namespace Acvc.Tests;

/// <summary>
/// Numeric writes are invariant-culture always and preserve the source value's
/// decimal-place style without ever losing precision.
/// </summary>
public partial class NumericFormatTests
{
    [Theory]
    [InlineData("3.353000", 3.9, "3.900000")]   // pad to the source's six places
    [InlineData("1.38", 1.4, "1.40")]           // pad to two
    [InlineData("3.15", 3.907, "3.907")]        // precision beats style — never round to 3.91
    [InlineData("50", 67.5, "67.5")]            // integer source, fractional value: shortest
    [InlineData("1100", 1234.0, "1234")]        // integer source, integral value
    [InlineData("0.120", 0.12, "0.120")]        // three places kept
    public void Set_double_formats_in_the_style_of_the_replaced_text(string sourceText, double value, string expected)
    {
        var doc = IniDocument.Parse($"[S]\nK={sourceText}\n");
        doc.SetDouble("S", "K", value);
        Assert.Equal($"[S]\nK={expected}\n", Encoding.Latin1.GetString(doc.ToBytes()));
    }

    [Fact]
    public void Float_noise_is_suppressed()
    {
        // 40 × 1.35 is 54.000000000000004 in doubles; the file must say 54.
        var lut = PowerLut.Parse("1000|40\n");
        lut.SetValue(0, 40 * 1.35);
        Assert.Equal("1000|54\n", Encoding.Latin1.GetString(lut.ToBytes()));
    }

    [SkippableFact]
    public void Under_german_culture_all_emitted_numbers_use_dots_not_commas()
    {
        Skip.If(ModelTestUtil.TryLoadFixtureCar("abarth500") is null, ModelTestUtil.FixtureSkipReason);

        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            var files = ModelTestUtil.TryLoadFixtureCar("abarth500")!.Files;
            var models = CarModelSet.FromFiles(files);
            var plan = new TunePlan
            {
                SourceCar = "abarth500",
                TuneName = "de_check",
                PowerScale = 1.35,
                Limiter = 7400,
                Boost = new BoostSpec(1.4, 1.4),
                FinalDrive = 3.9,
                MassTotal = 1420.5,  // fractional on purpose: "1420,5" must never appear
            };
            var result = TunePipeline.Apply(plan, models);
            Assert.False(result.HasFailures);

            // Stock AC data legitimately contains digit,digit — commas are the INI
            // list separator (car.ini GRAPHICS_OFFSET=0,-0.56,0.038). The real
            // invariant: the tune introduces no NEW comma patterns over the source.
            foreach (var (name, mutated) in new (string, byte[])[]
                     {
                         ("car.ini", models.Car.ToBytes()),
                         ("engine.ini", models.Engine.ToBytes()),
                         ("drivetrain.ini", models.Drivetrain.ToBytes()),
                         ("power.lut", models.PowerLut.ToBytes()),
                     })
            {
                var before = DecimalComma().Count(Encoding.Latin1.GetString(files[name]));
                var after = DecimalComma().Count(Encoding.Latin1.GetString(mutated));
                Assert.True(after == before,
                    $"{name}: decimal-comma patterns went from {before} to {after} under de-DE.");
            }

            // The values this tune wrote are dot-formatted.
            Assert.Contains("TOTALMASS=1420.5", Encoding.Latin1.GetString(models.Car.ToBytes()));
            Assert.Contains("FINAL=3.900000", Encoding.Latin1.GetString(models.Drivetrain.ToBytes()));
            Assert.Contains("MAX_BOOST=1.40", Encoding.Latin1.GetString(models.Engine.ToBytes()));
            Assert.DoesNotMatch(DecimalComma(), Encoding.Latin1.GetString(models.PowerLut.ToBytes()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [GeneratedRegex(@"\d,\d")]
    private static partial Regex DecimalComma();
}
