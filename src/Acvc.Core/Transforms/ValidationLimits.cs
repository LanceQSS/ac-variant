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

    /// <summary>Warn: effective grip ratio further than this from 1.0 (±15%).</summary>
    public const double GripScaleWarnDelta = 0.15;

    /// <summary>Fail: effective grip ratio further than this from 1.0 (±40%).</summary>
    public const double GripScaleFailDelta = 0.40;

    /// <summary>Warn: brake torque effectively scaled above this.</summary>
    public const double BrakeTorqueScaleWarnHigh = 1.5;

    /// <summary>Warn: brake torque effectively scaled below this. (Fail at ≤ 0.)</summary>
    public const double BrakeTorqueScaleWarnLow = 0.5;

    /// <summary>Fail: differential lock fractions outside [Min, Max].</summary>
    public const double DiffLockMin = 0;
    public const double DiffLockMax = 1;
}
