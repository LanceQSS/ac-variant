namespace Acvc.Core.Transforms;

/// <summary>
/// A transform cannot apply to this car's data at all (e.g. boost on a car with no
/// [TURBO_n] section, wrong gear count). Distinct from validation: validation judges
/// values after transforms succeed; this is a structural impossibility, reported
/// immediately and loudly.
/// </summary>
public class TransformException : Exception
{
    public TransformException(string message) : base(message) { }
}
