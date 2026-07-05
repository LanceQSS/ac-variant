namespace Acvc.Core.Model;

/// <summary>Typed accessor view over car.ini. The document stays the source of truth.</summary>
public sealed class CarIni
{
    public CarIni(IniDocument document) => Document = document;

    public IniDocument Document { get; }

    public static CarIni Parse(byte[] bytes, string? sourceName = "car.ini")
        => new(IniDocument.Parse(bytes, sourceName));

    /// <summary>[BASIC] TOTALMASS — total vehicle mass in kg.</summary>
    public double TotalMass
    {
        get => Document.GetDouble("BASIC", "TOTALMASS");
        set => Document.SetValue("BASIC", "TOTALMASS", IniNumber.Format(value));
    }

    public byte[] ToBytes() => Document.ToBytes();
}
