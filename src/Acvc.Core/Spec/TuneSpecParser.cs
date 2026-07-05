using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Acvc.Core.Spec;

/// <summary>
/// Parses tune-spec TOML text into a <see cref="TunePlan"/>. Schema per CLAUDE.md:
/// [meta] required (source_car, tune_name); [power], [engine], [drivetrain], [mass]
/// optional. Unknown tables or keys are hard errors naming the offender — typos must
/// never be silently ignored. Takes text, not paths: parsing is pure.
/// </summary>
public static partial class TuneSpecParser
{
    private static readonly string[] KnownTables = { "meta", "power", "engine", "drivetrain", "mass" };

    public static TunePlan Parse(string tomlText)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(tomlText)
                ?? throw new TuneSpecException("Tune spec is empty.");
        }
        catch (TomlException ex)
        {
            throw new TuneSpecException($"Tune spec is not valid TOML: {ex.Message}");
        }

        foreach (var (name, value) in root)
        {
            if (!KnownTables.Contains(name, StringComparer.Ordinal))
                throw new TuneSpecException(
                    $"Unknown table [{name}] in tune spec; expected one of: {string.Join(", ", KnownTables)}.");
            if (value is not TomlTable)
                throw new TuneSpecException($"'{name}' must be a table (e.g. [{name}]), not a value.");
        }

        var meta = GetTable(root, "meta")
            ?? throw new TuneSpecException("Tune spec is missing the required [meta] table.");
        CheckKeys(meta, "[meta]", "source_car", "tune_name");
        var sourceCar = RequireString(meta, "meta", "source_car");
        var tuneName = RequireString(meta, "meta", "tune_name");
        if (!TuneNamePattern().IsMatch(tuneName))
            throw new TuneSpecException(
                $"[meta] tune_name '{tuneName}' is invalid: use letters, digits, '_' or '-' only (it becomes part of the variant folder name).");

        double? powerScale = null;
        IReadOnlyList<PowerCurveRange>? powerCurve = null;
        if (GetTable(root, "power") is { } power)
        {
            CheckKeys(power, "[power]", "scale", "curve");
            powerScale = OptionalDouble(power, "power", "scale");
            powerCurve = OptionalCurve(power);
        }

        int? limiter = null;
        BoostSpec? boost = null;
        if (GetTable(root, "engine") is { } engine)
        {
            CheckKeys(engine, "[engine]", "limiter", "boost");
            limiter = OptionalInt(engine, "engine", "limiter");
            boost = OptionalBoost(engine);
        }

        double? final = null;
        IReadOnlyList<double>? gears = null;
        if (GetTable(root, "drivetrain") is { } drivetrain)
        {
            CheckKeys(drivetrain, "[drivetrain]", "final", "gears");
            final = OptionalDouble(drivetrain, "drivetrain", "final");
            gears = OptionalGears(drivetrain);
        }

        double? massTotal = null;
        if (GetTable(root, "mass") is { } mass)
        {
            CheckKeys(mass, "[mass]", "total");
            massTotal = OptionalDouble(mass, "mass", "total");
        }

        return new TunePlan
        {
            SourceCar = sourceCar,
            TuneName = tuneName,
            PowerScale = powerScale,
            PowerCurve = powerCurve,
            Limiter = limiter,
            Boost = boost,
            FinalDrive = final,
            Gears = gears,
            MassTotal = massTotal,
        };
    }

    // ---- table plumbing -----------------------------------------------------

    private static TomlTable? GetTable(TomlTable root, string name)
        => root.TryGetValue(name, out var value) ? (TomlTable)value : null;

    private static void CheckKeys(TomlTable table, string display, params string[] allowed)
    {
        foreach (var (key, _) in table)
            if (!allowed.Contains(key, StringComparer.Ordinal))
                throw new TuneSpecException(
                    $"Unknown key '{key}' in {display}; allowed keys: {string.Join(", ", allowed)}.");
    }

    private static string RequireString(TomlTable table, string tableName, string key)
    {
        if (!table.TryGetValue(key, out var value))
            throw new TuneSpecException($"[{tableName}] is missing required key '{key}'.");
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            throw new TuneSpecException($"[{tableName}] {key} must be a non-empty string.");
        return s;
    }

    private static double? OptionalDouble(TomlTable table, string tableName, string key)
        => table.TryGetValue(key, out var value) ? AsDouble(value, $"[{tableName}] {key}") : null;

    private static int? OptionalInt(TomlTable table, string tableName, string key)
    {
        if (!table.TryGetValue(key, out var value))
            return null;
        if (value is not long l)
            throw new TuneSpecException($"[{tableName}] {key} must be an integer, got '{value}'.");
        if (l is < int.MinValue or > int.MaxValue)
            throw new TuneSpecException($"[{tableName}] {key} value {l} is out of range.");
        return (int)l;
    }

    private static double AsDouble(object value, string context) => value switch
    {
        long l => l,
        double d => d,
        _ => throw new TuneSpecException($"{context} must be a number, got '{value}' ({value.GetType().Name})."),
    };

    private static BoostSpec? OptionalBoost(TomlTable engine)
    {
        if (!engine.TryGetValue("boost", out var value))
            return null;
        if (value is not TomlTable boost)
            throw new TuneSpecException("[engine] boost must be a table like: boost = { max = 1.4, wastegate = 1.4 }.");
        CheckKeys(boost, "[engine] boost", "max", "wastegate");
        if (!boost.TryGetValue("max", out var max))
            throw new TuneSpecException("[engine] boost is missing required key 'max'.");
        if (!boost.TryGetValue("wastegate", out var wastegate))
            throw new TuneSpecException("[engine] boost is missing required key 'wastegate' — both values are explicit, no defaults.");
        return new BoostSpec(AsDouble(max, "[engine] boost.max"), AsDouble(wastegate, "[engine] boost.wastegate"));
    }

    private static IReadOnlyList<PowerCurveRange>? OptionalCurve(TomlTable power)
    {
        if (!power.TryGetValue("curve", out var value))
            return null;

        var tables = value switch
        {
            TomlTableArray tableArray => tableArray.ToList(),
            TomlArray array => array.Select((item, i) => item as TomlTable
                ?? throw new TuneSpecException(
                    $"[power] curve[{i}] must be a table like {{ from = 3000, to = 5000, factor = 1.1 }}.")).ToList(),
            _ => throw new TuneSpecException("[power] curve must be an array of { from, to, factor } tables."),
        };
        if (tables.Count == 0)
            throw new TuneSpecException("[power] curve must not be empty — omit the key instead.");

        var ranges = new List<PowerCurveRange>();
        for (var i = 0; i < tables.Count; i++)
        {
            var entry = tables[i];
            CheckKeys(entry, $"[power] curve[{i}]", "from", "to", "factor");
            ranges.Add(new PowerCurveRange(
                RequireNumber(entry, $"[power] curve[{i}]", "from"),
                RequireNumber(entry, $"[power] curve[{i}]", "to"),
                RequireNumber(entry, $"[power] curve[{i}]", "factor")));
        }
        return ranges;
    }

    private static IReadOnlyList<double>? OptionalGears(TomlTable drivetrain)
    {
        if (!drivetrain.TryGetValue("gears", out var value))
            return null;
        if (value is not TomlArray array)
            throw new TuneSpecException("[drivetrain] gears must be an array of ratios like [3.2, 2.1, 1.5, 1.1, 0.9].");
        if (array.Count == 0)
            throw new TuneSpecException("[drivetrain] gears must not be empty — omit the key instead.");
        return array.Select((item, i) => AsDouble(item!, $"[drivetrain] gears[{i}]")).ToList();
    }

    private static double RequireNumber(TomlTable table, string context, string key)
    {
        if (!table.TryGetValue(key, out var value))
            throw new TuneSpecException($"{context} is missing required key '{key}'.");
        return AsDouble(value, $"{context} {key}");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TuneNamePattern();
}
