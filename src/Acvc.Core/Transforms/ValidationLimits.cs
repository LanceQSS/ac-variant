namespace Acvc.Core.Transforms;

/// <summary>
/// Every validation threshold in one place, repriceable without hunting.
/// Failures block a build; warnings inform.
/// </summary>
public static class ValidationLimits
{
    /// <summary>Fail: post-tune mass below source × this (−60%).</summary>
    public const double MassMinRatio = 0.4;

    /// <summary>Fail: post-tune mass above source × this (+60%).</summary>
    public const double MassMaxRatio = 1.6;

    /// <summary>Warn: any LUT torque value effectively multiplied beyond this.</summary>
    public const double PowerScaleWarnFactor = 3.0;

    /// <summary>Warn: limiter raised above source × this (+20%).</summary>
    public const double LimiterRaiseWarnRatio = 1.2;
}
