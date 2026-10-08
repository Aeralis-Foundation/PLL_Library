using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class FogZoneState
{
    PllPresentationCoverage Transition;
    long Revision = -1;

    internal FogZoneState(ResolvedFogZone Source, string StableInstanceIdentity)
    {
        Root = Source.Root; Settings = Source.Settings;
        StableTargetKey = Source.StableTargetKey;
        this.StableInstanceIdentity = StableInstanceIdentity;
    }

    internal GameObject Root { get; }
    internal FogZoneSettings Settings { get; }
    internal string StableTargetKey { get; }
    internal string StableInstanceIdentity { get; }
    internal bool DesiredEnabled => Transition.Target;
    internal float Coverage => Transition.Coverage;

    internal void Adopt(bool Target, float StartCoverage, double StartClock,
        float CoverDelaySeconds, long Revision, double Now)
    {
        if (Revision <= this.Revision) return;
        this.Revision = Revision;
        Transition.Adopt(Target, StartCoverage, StartClock, CoverDelaySeconds, Now);
    }

    internal void Tick(double Now) => Transition.Tick(Now);

    internal float Influence(Vector3 WorldViewpoint)
    {
        var Coverage = Transition.Coverage;
        if (Coverage == 0f || !Root || !Root.activeInHierarchy) return 0f;
        var Transform = Root.transform;
        var Point = Transform.InverseTransformPoint(WorldViewpoint) - Settings.Volume.Center;
        float InwardMeters;
        if (Settings.Volume.Shape == PllZoneShape.Sphere)
        {
            var InwardLocal = Settings.Volume.Radius * Coverage - Point.magnitude;
            if (InwardLocal < 0f) return 0f;
            InwardMeters = InwardLocal * Transform.TransformVector(Vector3.right).magnitude;
        }
        else
        {
            var Inward = Settings.Volume.Size * (0.5f * Coverage);
            Inward.x -= Mathf.Abs(Point.x);
            Inward.y -= Mathf.Abs(Point.y);
            Inward.z -= Mathf.Abs(Point.z);
            if (Inward.x < 0f || Inward.y < 0f || Inward.z < 0f) return 0f;
            var Inverse = Transform.worldToLocalMatrix;
            InwardMeters = Mathf.Min(
                Inward.x / new Vector3(Inverse.m00, Inverse.m01, Inverse.m02).magnitude,
                Mathf.Min(Inward.y / new Vector3(Inverse.m10, Inverse.m11, Inverse.m12).magnitude,
                    Inward.z / new Vector3(Inverse.m20, Inverse.m21, Inverse.m22).magnitude));
        }
        return Settings.BoundaryBlendDistance == 0f ? Coverage
            : Coverage * Mathf.Clamp01(InwardMeters / Settings.BoundaryBlendDistance);
    }
}

internal readonly struct FogZoneContribution
{
    internal FogZoneContribution(float VisibilityDistance, Color Color, float Influence)
    {
        this.VisibilityDistance = VisibilityDistance;
        this.Color = Color; this.Influence = Influence;
    }

    internal float VisibilityDistance { get; }
    internal Color Color { get; }
    internal float Influence { get; }

    internal float Opacity(float ViewpointDistance)
    {
        var x = Mathf.Clamp01(ViewpointDistance / VisibilityDistance);
        var Smooth = x * x * (3f - 2f * x);
        return Influence * (1f - (1f - Smooth) * (1f - Smooth));
    }
}

internal sealed class FogZoneCompositor
{
    readonly Func<double?> ReadClock;
    FogZoneState[] Sources;
    readonly List<FogZoneContribution> Contributions = new();

    internal FogZoneCompositor(Func<double?> ReadClock, FogZoneState[] Sources)
    {
        this.ReadClock = ReadClock;
        this.Sources = Sources;
        Array.Sort(Sources, Compare);
    }

    internal void ReplaceSources(FogZoneState[] Sources)
    {
        this.Sources = Sources;
        Array.Sort(this.Sources, Compare);
    }

    internal IReadOnlyList<FogZoneContribution> Sample(Vector3 WorldViewpoint)
    {
        Contributions.Clear();
        if (ReadClock() is not { } Now) return Contributions;
        foreach (var Source in Sources)
        {
            Source.Tick(Now);
            var Influence = Source.Influence(WorldViewpoint);
            if (Influence != 0f)
                Contributions.Add(new FogZoneContribution(
                    Source.Settings.VisibilityDistance, Source.Settings.Color, Influence));
        }
        return Contributions;
    }

    internal Color Composite(Color NativeSceneColor, float ViewpointDistance)
    {
        var Alpha = NativeSceneColor.a;
        foreach (var Contribution in Contributions)
            NativeSceneColor = Color.LerpUnclamped(NativeSceneColor,
                Contribution.Color, Contribution.Opacity(ViewpointDistance));
        NativeSceneColor.a = Alpha;
        return NativeSceneColor;
    }

    static int Compare(FogZoneState Left, FogZoneState Right)
    {
        var Priority = Left.Settings.Priority.CompareTo(Right.Settings.Priority);
        if (Priority != 0) return Priority;
        var Source = StringComparer.Ordinal.Compare(Left.StableTargetKey, Right.StableTargetKey);
        return Source != 0 ? Source
            : StringComparer.Ordinal.Compare(Left.StableInstanceIdentity, Right.StableInstanceIdentity);
    }
}
