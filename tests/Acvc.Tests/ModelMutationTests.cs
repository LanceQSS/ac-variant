using Acvc.Core.Model;

namespace Acvc.Tests;

/// <summary>
/// Milestone 2 gate part 3: one typed mutation changes only that value's bytes —
/// a positional line diff of output vs original touches exactly one line, and the
/// changed line differs only in the value text.
/// </summary>
public class ModelMutationTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        // car, original TOTALMASS text (hand-verified), new value
        { "abarth500", "1100", "1234" },
        { "bmw_m3_e30", "1275", "1399" },
    };

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public void Setting_TotalMass_touches_exactly_one_line(string carName, string oldMass, string newMass)
    {
        var data = ModelTestUtil.TryLoadFixtureCar(carName);
        Skip.If(data is null, ModelTestUtil.FixtureSkipReason);

        var original = data!.Files["car.ini"];
        var car = CarIni.Parse(original);
        car.TotalMass = double.Parse(newMass, System.Globalization.CultureInfo.InvariantCulture);
        var mutated = car.ToBytes();

        var originalLines = ModelTestUtil.SplitKeepingTerminators(original);
        var mutatedLines = ModelTestUtil.SplitKeepingTerminators(mutated);
        Assert.Equal(originalLines.Count, mutatedLines.Count);

        var changed = Enumerable.Range(0, originalLines.Count)
            .Where(i => originalLines[i] != mutatedLines[i])
            .ToList();
        var index = Assert.Single(changed);

        // The changed line is the TOTALMASS line, altered only in its value text:
        // replacing the value back must reproduce the original line byte-for-byte
        // (indentation, alignment tabs, inline comment, terminator all intact).
        Assert.StartsWith("TOTALMASS=", mutatedLines[index]);
        Assert.Equal(originalLines[index], mutatedLines[index].Replace($"={newMass}", $"={oldMass}"));

        // And the typed value reads back from the emitted bytes.
        Assert.Equal(double.Parse(newMass, System.Globalization.CultureInfo.InvariantCulture),
            CarIni.Parse(mutated).TotalMass);
    }
}
