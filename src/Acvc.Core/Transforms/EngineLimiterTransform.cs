using Acvc.Core.Model;

namespace Acvc.Core.Transforms;

/// <summary>engine.limiter — sets the rev limiter in engine.ini [ENGINE_DATA].</summary>
public static class EngineLimiterTransform
{
    public static void Apply(EngineIni engine, int limiter)
    {
        if (limiter <= 0)
            throw new TransformException(
                $"engine.limiter must be a positive rpm, got {limiter}. (Removing the limiter is not a v1 transform.)");
        engine.Limiter = limiter;
    }
}
