namespace Acvc.Core.Model;

/// <summary>
/// The parsed models a tune operates on, built purely from in-memory bytes (e.g. the
/// decrypted file map from AcdUnpacker) — no filesystem involvement. The torque LUT
/// is resolved via engine.ini's POWER_CURVE key rather than assuming "power.lut".
/// </summary>
public sealed class CarModelSet
{
    public required CarIni Car { get; init; }
    public required EngineIni Engine { get; init; }
    public required DrivetrainIni Drivetrain { get; init; }
    public required PowerLut PowerLut { get; init; }
    /// <summary>File name the LUT came from (engine.ini POWER_CURVE).</summary>
    public required string PowerLutFileName { get; init; }

    public static CarModelSet FromFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        var engine = new EngineIni(IniDocument.Parse(Require(files, "engine.ini"), "engine.ini"));
        var lutName = engine.PowerCurveFile;
        return new CarModelSet
        {
            Car = new CarIni(IniDocument.Parse(Require(files, "car.ini"), "car.ini")),
            Engine = engine,
            Drivetrain = new DrivetrainIni(IniDocument.Parse(Require(files, "drivetrain.ini"), "drivetrain.ini")),
            PowerLut = PowerLut.Parse(Require(files, lutName), lutName),
            PowerLutFileName = lutName,
        };
    }

    private static byte[] Require(IReadOnlyDictionary<string, byte[]> files, string name)
        => files.TryGetValue(name, out var bytes)
            ? bytes
            : throw new FileNotFoundException($"Car data does not contain required file '{name}'.");
}
