namespace Acvc.Core.Transforms;

/// <summary>
/// Every validation threshold in one place, repriceable without hunting.
/// Principle (repriced post-beta.1): failures exist only for output the sim cannot
/// consume — integrity. Realism departures are warnings and never block a build;
/// the tool judges validity, not taste.
/// </summary>
public static class ValidationLimits
{
    /// <summary>Warn: post-tune mass below source × this (−60%).</summary>
    public const double MassMinRatio = 0.4;

    /// <summary>Warn: post-tune mass above source × this (+60%).</summary>
    public const double MassMaxRatio = 1.6;

    /// <summary>Warn: any LUT torque value effectively multiplied beyond this.</summary>
    public const double PowerScaleWarnFactor = 3.0;

    /// <summary>Warn: limiter raised above source × this (+20%).</summary>
    public const double LimiterRaiseWarnRatio = 1.2;

    /// <summary>Warn (inner tier): effective grip ratio further than this from 1.0 (±15%).</summary>
    public const double GripScaleWarnDelta = 0.15;

    /// <summary>Warn (outer tier): grip beyond this is outside the realistic tire envelope (±40%). Fail only at ≤ 0.</summary>
    public const double GripScaleEnvelopeDelta = 0.40;

    /// <summary>Warn: brake torque effectively scaled above this.</summary>
    public const double BrakeTorqueScaleWarnHigh = 1.5;

    /// <summary>Warn: brake torque effectively scaled below this. (Fail at ≤ 0.)</summary>
    public const double BrakeTorqueScaleWarnLow = 0.5;

    /// <summary>Fail below Min (the sim needs non-negative lock); warn above Max (factory mod data ships POWER=1.5 and runs).</summary>
    public const double DiffLockMin = 0;
    public const double DiffLockMax = 1;
}
