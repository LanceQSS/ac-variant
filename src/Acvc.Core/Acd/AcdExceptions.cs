namespace Acvc.Core.Acd;

/// <summary>The data.acd container structure is not what the Kunos format specifies.</summary>
public class AcdFormatException : Exception
{
    public AcdFormatException(string message) : base(message) { }
}

/// <summary>
/// The container parsed but the decrypted content failed the plausibility check —
/// the archive is almost certainly protected with CSP/x4fab-era mod encryption,
/// which is separate from the standard Kunos cipher and not supported.
/// </summary>
public class ProtectedDataException : Exception
{
    public ProtectedDataException(string message) : base(message) { }
}
