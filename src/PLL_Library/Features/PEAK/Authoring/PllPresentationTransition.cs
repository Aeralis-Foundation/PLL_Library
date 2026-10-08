using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public struct PllPresentationTransition
{
    [SerializeField, Min(0f), InspectorName("Cover Delay (seconds)")] float SecondsValue;

    internal PllPresentationTransition(float CoverDelaySeconds) => SecondsValue = CoverDelaySeconds;

    public float CoverDelaySeconds => SecondsValue;
}

internal struct PllPresentationCoverage
{
    internal bool Target { get; private set; }
    internal float Coverage { get; private set; }
    internal float StartCoverage { get; private set; }
    internal double StartClock { get; private set; }
    internal float CoverDelaySeconds { get; private set; }
    internal bool Active { get; private set; }

    internal bool SetTarget(bool Target, double Now, float CoverDelaySeconds)
    {
        if (this.Target == Target) return false;
        Tick(Now);
        Adopt(Target, Coverage, Now, CoverDelaySeconds, Now);
        return true;
    }

    internal void Adopt(
        bool Target, float StartCoverage, double StartClock,
        float CoverDelaySeconds, double Now)
    {
        this.Target = Target;
        this.StartCoverage = StartCoverage;
        this.StartClock = StartClock;
        this.CoverDelaySeconds = CoverDelaySeconds;
        Coverage = StartCoverage;
        Active = StartCoverage != (Target ? 1f : 0f);
        Tick(Now);
    }

    internal bool Tick(double Now)
    {
        if (!Active) return false;
        var Endpoint = Target ? 1f : 0f;
        var RemainingSeconds = CoverDelaySeconds * Math.Abs(Endpoint - StartCoverage);
        var u = RemainingSeconds == 0f
            ? 1d : Math.Max(0d, Math.Min(1d, (Now - StartClock) / RemainingSeconds));
        Coverage = u == 1d ? Endpoint
            : StartCoverage + (Endpoint - StartCoverage) * (float)(u * u * (3d - 2d * u));
        if (u == 1d) Active = false;
        return true;
    }

    internal void Complete(bool Target)
    {
        this.Target = Target;
        Coverage = StartCoverage = Target ? 1f : 0f;
        StartClock = 0d;
        CoverDelaySeconds = 0f;
        Active = false;
    }
}
