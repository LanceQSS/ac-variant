using System.Text;
using System.Text.RegularExpressions;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;
using Acvc.Core.UiMeta;

namespace Acvc.Tests;

/// <summary>
/// Milestone 5 gate: ui_car.json regeneration against both fixture cars. Expected
/// values were derived independently (script over the raw LUTs with the documented
/// model: torque × (1 + ΣMAX_BOOST), bhp = T·rpm·2π/60 ÷ 745.7, grid 0..limiter
/// step 500 + limiter, weight = TOTALMASS − 75, pwratio truncated to 2 decimals).
/// </summary>
public partial class UiRegenTests
{
    private static (byte[] UiJson, CarModelSet Models) LoadFixture(string carName, TunePlan? plan)
    {
        var folder = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals(carName, StringComparison.OrdinalIgnoreCase));
        Skip.If(folder is null, ModelTestUtil.FixtureSkipReason);
        var uiPath = Path.Combine(folder!, "ui_car.json");
        Skip.If(!File.Exists(uiPath),
            "fixture ui_car.json missing — re-run scripts/make-fixtures.ps1 (M5 added it)");

        var models = CarModelSet.FromFiles(Acvc.Core.Acd.AcdUnpacker.Load(folder!).Files);
        if (plan is not null)
        {
            var validation = TunePipeline.Apply(plan, models);
            Assert.False(validation.HasFailures);
        }
        return (File.ReadAllBytes(uiPath), models);
    }

    [GeneratedRegex("""\["\d+","\d+"\]""")]
    private static partial Regex StringPair();

    // ---- stock anchors: locks the boost/weight model against real Kunos data ----

    [SkippableFact]
    public void Stock_abarth500_lut_derived_specs_reconcile_with_ui_within_tolerance()
    {
        // ui claims 160bhp/230Nm (brochure); LUT × (1 + MAX_BOOST 1.38) gives 156/224
        // — within the 10% tripwire. Weight delta (TOTALMASS 1100 → 1025kg) is 75.
        var (_, models) = LoadFixture("abarth500", null);
        var patch = UiCarPatcher.BuildPatch(models);
        Assert.Equal("156bhp", patch.Bhp);
        Assert.Equal("224Nm", patch.Torque);
        Assert.Equal("1025kg", patch.Weight);
    }

    [SkippableFact]
    public void Stock_bmw_m3_e30_documents_the_marketing_gap()
    {
        // The tripwire case: stock ui claims 238bhp (real Sport Evo brochure), the
        // LUT peaks at 209 within the limiter — 12.3% apart. LUT is truth (CLAUDE.md).
        var (_, models) = LoadFixture("bmw_m3_e30", null);
        var patch = UiCarPatcher.BuildPatch(models);
        Assert.Equal("209bhp", patch.Bhp);
        Assert.Equal("211Nm", patch.Torque);
        Assert.Equal("1200kg", patch.Weight);
    }

    // ---- transformed regeneration -------------------------------------------------

    [SkippableFact]
    public void Abarth500_street_600_regen_matches_hand_computed_values()
    {
        var (ui, models) = LoadFixture("abarth500", new TunePlan
        {
            SourceCar = "abarth500",
            TuneName = "street_600",
            PowerScale = 1.35,
            Limiter = 7400,
            Boost = new BoostSpec(1.4, 1.4),
            MassTotal = 1420,
        });

        var patch = UiCarPatcher.BuildPatch(models);
        Assert.Equal("213bhp", patch.Bhp);
        Assert.Equal("305Nm", patch.Torque);
        Assert.Equal("1345kg", patch.Weight);
        Assert.Equal("6.31kg/hp", patch.PwRatio);

        var patched = UiCarPatcher.Apply(ui, patch);
        var text = Encoding.UTF8.GetString(patched);

        Assert.Contains("\"213bhp\"", text);
        Assert.DoesNotContain("160bhp", text);
        Assert.DoesNotContain("\"1025kg\"", text);

        // Curve shape: string pairs, 16 points per curve (0, 500..7000, 7400).
        Assert.Equal(32, StringPair().Count(text));
        Assert.Contains("[\"0\",\"0\"]", text);
        Assert.Contains("[\"500\",\"194\"]", text);   // torque near idle
        Assert.Contains("[\"3000\",\"305\"]", text);  // peak torque point
        Assert.Contains("[\"7400\",\"156\"]", text);  // torque at the new limiter
        Assert.Contains("[\"5500\",\"213\"]", text);  // peak power point
        Assert.Contains("[\"7400\",\"162\"]", text);  // power at the new limiter

        // Everything before the first replaced value is byte-identical (name,
        // description with its raw control characters, tags...).
        var firstChange = Encoding.UTF8.GetString(ui).IndexOf("\"bhp\"", StringComparison.Ordinal);
        Assert.True(firstChange > 0);
        Assert.Equal(ui.AsSpan(0, firstChange).ToArray(), patched.AsSpan(0, firstChange).ToArray());
    }

    [SkippableFact]
    public void Bmw_m3_e30_regen_matches_hand_computed_values()
    {
        var (ui, models) = LoadFixture("bmw_m3_e30", new TunePlan
        {
            SourceCar = "bmw_m3_e30",
            TuneName = "evo_plus",
            PowerScale = 1.2,
            MassTotal = 1300,
        });

        var patch = UiCarPatcher.BuildPatch(models);
        Assert.Equal("250bhp", patch.Bhp);
        Assert.Equal("253Nm", patch.Torque);
        Assert.Equal("1225kg", patch.Weight);
        Assert.Equal("4.90kg/hp", patch.PwRatio);

        var text = Encoding.UTF8.GetString(UiCarPatcher.Apply(ui, patch));
        Assert.Contains("\"250bhp\"", text);
        Assert.DoesNotContain("238bhp", text);
        Assert.Equal(32, StringPair().Count(text));      // 16 points per curve
        Assert.Contains("[\"7250\",\"246\"]", text);     // torque at the stock limiter
        Assert.Contains("[\"7250\",\"250\"]", text);     // power at the stock limiter
    }

    [Fact]
    public void Missing_specs_key_fails_loudly()
    {
        var json = Encoding.UTF8.GetBytes("{\"name\": \"X\", \"torqueCurve\": [], \"powerCurve\": []}");
        var patch = new UiSpecsPatch("1bhp", "1Nm", "1kg", "1.00kg/hp", "[]", "[]");
        var ex = Assert.Throws<Acvc.Core.Emit.EmitException>(() => UiCarPatcher.Apply(json, patch));
        Assert.Contains("bhp", ex.Message);
    }
}
