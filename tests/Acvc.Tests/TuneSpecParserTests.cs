using Acvc.Core.Spec;

namespace Acvc.Tests;

public class TuneSpecParserTests
{
    private const string FullSpec = """
        [meta]
        source_car = "abarth500"
        tune_name  = "street_600"

        [power]
        scale = 1.35
        curve = [ { from = 3000, to = 5000, factor = 1.1 } ]

        [engine]
        limiter = 7400
        boost = { max = 1.4, wastegate = 1.4 }

        [drivetrain]
        final = 3.90
        gears = [3.2, 2.1, 1.5, 1.1, 0.9]

        [mass]
        total = 1420
        """;

    [Fact]
    public void Full_spec_parses_to_typed_plan()
    {
        var plan = TuneSpecParser.Parse(FullSpec);

        Assert.Equal("abarth500", plan.SourceCar);
        Assert.Equal("street_600", plan.TuneName);
        Assert.Equal(1.35, plan.PowerScale);
        var range = Assert.Single(plan.PowerCurve!);
        Assert.Equal(new PowerCurveRange(3000, 5000, 1.1), range);
        Assert.Equal(7400, plan.Limiter);
        Assert.Equal(new BoostSpec(1.4, 1.4), plan.Boost);
        Assert.Equal(3.90, plan.FinalDrive);
        Assert.Equal(new[] { 3.2, 2.1, 1.5, 1.1, 0.9 }, plan.Gears!);
        Assert.Equal(1420.0, plan.MassTotal);
    }

    [Fact]
    public void Minimal_spec_meta_only_is_a_valid_noop_plan()
    {
        var plan = TuneSpecParser.Parse("[meta]\nsource_car = \"abarth500\"\ntune_name = \"stock\"\n");
        Assert.Equal("abarth500", plan.SourceCar);
        Assert.Null(plan.PowerScale);
        Assert.Null(plan.PowerCurve);
        Assert.Null(plan.Limiter);
        Assert.Null(plan.Boost);
        Assert.Null(plan.FinalDrive);
        Assert.Null(plan.Gears);
        Assert.Null(plan.MassTotal);
    }

    [Theory]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[powr]\nscale = 1.1\n", "powr")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\nstray = 1\n", "stray")]
    public void Unknown_table_or_root_key_is_a_hard_error_naming_it(string toml, string offender)
    {
        var ex = Assert.Throws<TuneSpecException>(() => TuneSpecParser.Parse(toml));
        Assert.Contains(offender, ex.Message);
    }

    [Theory]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\ntypo = 1\n", "typo", "[meta]")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[power]\nscal = 1.3\n", "scal", "[power]")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[engine]\nlimit = 7000\n", "limit", "[engine]")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[engine]\nboost = { max = 1.4, wastegate = 1.4, gate = 1 }\n", "gate", "boost")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[drivetrain]\nfinale = 3.9\n", "finale", "[drivetrain]")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[mass]\nweight = 1000\n", "weight", "[mass]")]
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[power]\ncurve = [ { from = 1, to = 2, factor = 1.1, upto = 3 } ]\n", "upto", "curve")]
    public void Unknown_key_is_a_hard_error_naming_key_and_table(string toml, string key, string table)
    {
        var ex = Assert.Throws<TuneSpecException>(() => TuneSpecParser.Parse(toml));
        Assert.Contains($"'{key}'", ex.Message);
        Assert.Contains(table, ex.Message);
    }

    [Theory]
    [InlineData("[power]\nscale = 1.35\n", "[meta]")]                                        // missing meta entirely
    [InlineData("[meta]\ntune_name = \"t\"\n", "source_car")]                                // missing source_car
    [InlineData("[meta]\nsource_car = \"a\"\n", "tune_name")]                                // missing tune_name
    [InlineData("[meta]\nsource_car = \"\"\ntune_name = \"t\"\n", "source_car")]             // empty
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"has space\"\n", "tune_name")]     // unsafe name
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[power]\nscale = \"1.35\"\n", "scale")]      // string, not number
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[engine]\nlimiter = 7400.5\n", "limiter")]   // not an integer
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[engine]\nboost = { max = 1.4 }\n", "wastegate")] // half a boost
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[engine]\nboost = 1.4\n", "boost")]          // wrong shape
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[drivetrain]\ngears = [3.2, \"x\"]\n", "gears")] // bad element
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[drivetrain]\ngears = []\n", "gears")]       // empty array
    [InlineData("[meta]\nsource_car = \"a\"\ntune_name = \"t\"\n[power]\ncurve = [ { from = 1, to = 2 } ]\n", "factor")] // missing factor
    public void Schema_violations_fail_loudly(string toml, string expectedInMessage)
    {
        var ex = Assert.Throws<TuneSpecException>(() => TuneSpecParser.Parse(toml));
        Assert.Contains(expectedInMessage, ex.Message);
    }

    [Fact]
    public void Invalid_toml_syntax_reports_as_spec_error()
    {
        var ex = Assert.Throws<TuneSpecException>(() => TuneSpecParser.Parse("[meta\nsource_car = \"a\""));
        Assert.Contains("not valid TOML", ex.Message);
    }
}
