using Acvc.Core.Model;
using Acvc.Core.Spec;

namespace Acvc.Core.Transforms;

/// <summary>
/// engine.boost — sets MAX_BOOST and WASTEGATE in every [TURBO_n] section.
/// A car with no turbo section is a hard error (CLAUDE.md rule: no silent fallbacks).
/// </summary>
public static class EngineBoostTransform
{
    public static void Apply(EngineIni engine, BoostSpec boost)
    {
        if (!double.IsFinite(boost.Max) || boost.Max <= 0)
            throw new TransformException($"engine.boost max must be a positive number, got {boost.Max}.");
        if (!double.IsFinite(boost.Wastegate) || boost.Wastegate <= 0)
            throw new TransformException($"engine.boost wastegate must be a positive number, got {boost.Wastegate}.");

        var turbos = engine.Turbos;
        if (turbos.Count == 0)
            throw new TransformException(
                "engine.boost cannot apply: engine.ini has no [TURBO_n] section — the source car is naturally aspirated.");

        foreach (var turbo in turbos)
        {
            turbo.MaxBoost = boost.Max;
            turbo.Wastegate = boost.Wastegate;
        }
    }
}
