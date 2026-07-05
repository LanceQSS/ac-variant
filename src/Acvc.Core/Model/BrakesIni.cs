namespace Acvc.Core.Model;

/// <summary>Typed accessor view over brakes.ini. The document stays the source of truth.</summary>
public sealed class BrakesIni
{
    public BrakesIni(IniDocument document) => Document = document;

    public IniDocument Document { get; }

    public static BrakesIni Parse(byte[] bytes, string? sourceName = "brakes.ini")
        => new(IniDocument.Parse(bytes, sourceName));

    public bool HasMaxTorque => Document.TryGetValue("DATA", "MAX_TORQUE", out _);

    /// <summary>[DATA] MAX_TORQUE — maximum brake torque in Nm.</summary>
    public double MaxTorque
    {
        get => Document.GetDouble("DATA", "MAX_TORQUE");
        set => Document.SetDouble("DATA", "MAX_TORQUE", value);
    }

    public byte[] ToBytes() => Document.ToBytes();
}
