using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeFogCutoutTable
{
    readonly (FogCutoutZone Zone, float Threshold)[] Entries;

    NativeFogCutoutTable((FogCutoutZone Zone, float Threshold)[] Entries) => this.Entries = Entries;

    internal static NativeFogCutoutTable Capture(FogCutoutHandler Handler)
    {
        var Zones = Handler.cutoutZones;
        if (Zones is null || Zones.Length == 0)
            throw new InvalidOperationException("PEAK's native fog cutout catalog is empty.");
        var Entries = new (FogCutoutZone Zone, float Threshold)[Zones.Length];
        for (var Index = 0; Index < Zones.Length; Index++)
        {
            var Zone = Zones[Index];
            if (!Zone || !Zone.enabled || Zone.gameObject.scene != Handler.gameObject.scene
                || Zone.transform.parent != Handler.transform || Zone.GetComponents<FogCutoutZone>().Length != 1
                || !UnityValueContract.Finite(Zone.transform.position) || !float.IsFinite(Zone.transitionPoint))
                throw new InvalidOperationException("PEAK's native fog cutout catalog contains an invalid zone.");
            var Threshold = Zone.transform.position.z + Zone.transitionPoint;
            if (!float.IsFinite(Threshold) || Index != 0 && Threshold <= Entries[Index - 1].Threshold)
                throw new InvalidOperationException("PEAK's native fog cutout thresholds are not strictly increasing.");
            Entries[Index] = (Zone, Threshold);
        }
        return new NativeFogCutoutTable(Entries);
    }

    internal FogCutoutZone Select(LayoutSegmentSource Source)
    {
        var Entry = Source is PeakAttachmentSource Peak
            ? Peak.BridgeExit
            : Source.EntryPorts.Count == 1
                ? Source.EntryPorts[0]
                : throw new InvalidOperationException($"Native source '{Source.Id}' has no unique fog cutout entry.");
        var Position = Source.AuthoredPosition(Entry.Pose).z;
        if (!float.IsFinite(Position))
            throw new InvalidOperationException($"Native source '{Source.Id}' has a non-finite fog cutout entry.");
        var Index = 0;
        while (Index + 1 < Entries.Length && Position > Entries[Index].Threshold) Index++;
        return Entries[Index].Zone;
    }
}

internal static class NativeFogRuntime
{
    static NativeFogSource? Source;

    internal static void Publish(NativeFogSource? Fog) => Source = Fog;

    internal static NativeFogSource Require() => Source
        ?? throw new InvalidOperationException("PEAK's native Caldera fog source was not sealed.");

    internal static void Clear() => Source = null;
}

internal sealed class NativeFogSource
{
    readonly bool OriginEnabled;
    readonly float OriginSize;
    readonly float OriginMoveOnHeight;
    readonly float OriginMoveOnForward;
    readonly bool OriginDisablesFog;

    NativeFogSource(FogSphereOrigin Origin, FogCutoutZone Cutout, SpecialDayZone SpecialDay)
    {
        OriginEnabled = Origin.enabled;
        OriginSize = Origin.size;
        OriginMoveOnHeight = Origin.moveOnHeight;
        OriginMoveOnForward = Origin.moveOnForward;
        OriginDisablesFog = Origin.disableFog;
        CutoutMin = Cutout.min;
        CutoutMax = Cutout.max;
        CutoutAmount = Cutout.amount;
        OverrideFog = SpecialDay.overrideFog;
        FogDensity = OverrideFog ? SpecialDay.fogDensity : 0f;
    }

    internal float CutoutMin { get; }
    internal float CutoutMax { get; }
    internal float CutoutAmount { get; }
    internal bool OverrideFog { get; }
    internal float FogDensity { get; }

    internal static NativeFogSource Capture(
        FogSphereOrigin Origin, FogCutoutZone Cutout, SpecialDayZone SpecialDay)
    {
        if (!Origin || !Origin.enabled || !Origin.gameObject.activeSelf || !Origin.disableFog
            || !ManifestEncoding.CanEncode(Origin.size) || !ManifestEncoding.CanEncode(Origin.moveOnHeight) || !ManifestEncoding.CanEncode(Origin.moveOnForward))
            throw new InvalidOperationException("PEAK's native Caldera fog origin contract is invalid.");
        if (!Cutout || !Cutout.enabled || !ManifestEncoding.CanEncode(Cutout.min) || !ManifestEncoding.CanEncode(Cutout.max) || !ManifestEncoding.CanEncode(Cutout.amount))
            throw new InvalidOperationException("PEAK's native Caldera fog cutout contract is invalid.");
        if (!SpecialDay || !SpecialDay.enabled || !SpecialDay.gameObject.activeSelf || SpecialDay.globalZone
            || SpecialDay.overrideFog && !ManifestEncoding.CanEncode(SpecialDay.fogDensity))
            throw new InvalidOperationException("PEAK's native Caldera SpecialDay fog contract is invalid.");
        return new NativeFogSource(Origin, Cutout, SpecialDay);
    }

    internal void ApplyCutout(FogCutoutZone Target)
    {
        if (!Target) throw new ArgumentNullException(nameof(Target));
        Target.min = CutoutMin;
        Target.max = CutoutMax;
        Target.amount = CutoutAmount;
    }

    internal void ApplyFog(SpecialDayZone Target)
    {
        if (!Target) throw new ArgumentNullException(nameof(Target));
        Target.overrideFog = OverrideFog;
        Target.fogDensity = FogDensity;
    }

    internal bool Matches(NativeFogSource Other) =>
        OriginEnabled == Other.OriginEnabled
        && OriginSize.Equals(Other.OriginSize)
        && OriginMoveOnHeight.Equals(Other.OriginMoveOnHeight)
        && OriginMoveOnForward.Equals(Other.OriginMoveOnForward)
        && OriginDisablesFog == Other.OriginDisablesFog
        && CutoutMin.Equals(Other.CutoutMin)
        && CutoutMax.Equals(Other.CutoutMax)
        && CutoutAmount.Equals(Other.CutoutAmount)
        && OverrideFog == Other.OverrideFog
        && FogDensity.Equals(Other.FogDensity);

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(OriginEnabled);
        ManifestEncoding.WriteScalar(Writer, OriginSize);
        ManifestEncoding.WriteScalar(Writer, OriginMoveOnHeight);
        ManifestEncoding.WriteScalar(Writer, OriginMoveOnForward);
        Writer.Write(OriginDisablesFog);
        ManifestEncoding.WriteScalar(Writer, CutoutMin);
        ManifestEncoding.WriteScalar(Writer, CutoutMax);
        ManifestEncoding.WriteScalar(Writer, CutoutAmount);
        Writer.Write(OverrideFog);
        ManifestEncoding.WriteScalar(Writer, FogDensity);
    }

}
