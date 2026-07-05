namespace Acvc.Core.Model;

/// <summary>Typed accessor view over drivetrain.ini. The document stays the source of truth.</summary>
public sealed class DrivetrainIni
{
    public DrivetrainIni(IniDocument document) => Document = document;

    public IniDocument Document { get; }

    public static DrivetrainIni Parse(byte[] bytes, string? sourceName = "drivetrain.ini")
        => new(IniDocument.Parse(bytes, sourceName));

    /// <summary>[GEARS] FINAL — final drive ratio.</summary>
    public double FinalRatio
    {
        get => Document.GetDouble("GEARS", "FINAL");
        set => Document.SetDouble("GEARS", "FINAL", value);
    }

    /// <summary>[GEARS] COUNT — number of forward gears.</summary>
    public int GearCount => Document.GetInt("GEARS", "COUNT");

    /// <summary>[GEARS] GEAR_n ratio, 1-based up to <see cref="GearCount"/>.</summary>
    public double GetGearRatio(int gear)
    {
        ValidateGearNumber(gear);
        return Document.GetDouble("GEARS", $"GEAR_{gear}");
    }

    public void SetGearRatio(int gear, double ratio)
    {
        ValidateGearNumber(gear);
        Document.SetDouble("GEARS", $"GEAR_{gear}", ratio);
    }

    private void ValidateGearNumber(int gear)
    {
        var count = GearCount;
        if (gear < 1 || gear > count)
            throw new ArgumentOutOfRangeException(nameof(gear),
                $"Gear {gear} is out of range; {Document.SourceName ?? "drivetrain.ini"} declares COUNT={count}.");
    }

    public byte[] ToBytes() => Document.ToBytes();
}
