using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zorro.Core;

namespace PLL.Library;

internal static class PlacementPlan
{
    internal sealed class Placement
    {
        internal Placement(LayoutSegmentSource[] Sources, Transform Container, PlacementFrame Frame, int Index)
        {
            this.Sources = Sources;
            this.Container = Container;
            this.Frame = Frame;
            this.Index = Index;
            SupplementalRoots = NativeFeatureSourceContract.SupplementalRootContract.Create(Container, Sources);
        }

        internal LayoutSegmentSource[] Sources { get; }
        internal Transform Container { get; }
        internal NativeFeatureSourceContract.SupplementalRootContract[] SupplementalRoots { get; }
        internal PlacementFrame Frame { get; }
        internal Vector3 Offset => Frame.Offset;
        internal int Index { get; }
        internal bool Reuse { get; set; }
        internal MapSlot[] Slots { get; set; } = Array.Empty<MapSlot>();
        internal Transform? AttachmentRespawn { get; set; }
    }
    internal sealed class LayoutLink
    {
        internal LayoutLink(Placement Previous, Placement Next, LayoutTransition Transition)
        {
            this.Previous = Previous;
            this.Next = Next;
            this.Transition = Transition;
        }

        internal Placement Previous { get; }
        internal Placement Next { get; }
        internal LayoutTransition Transition { get; }
    }
    internal static List<Placement> CreatePlacements(
        IReadOnlyList<LayoutSegmentSource> Plan,
        IEnumerable<LayoutSegmentSource> Catalog,
        OriginalMapSources Originals,
        Transform StartPlane,
        Transform StartSpawns,
        out List<LayoutLink> Links,
        out int SegmentCount)
    {
        var Placements = new List<Placement>(Plan.Count);
        var BoundaryClearance = new NativeBoundaryClearance(StartPlane, StartSpawns);
        Links = new List<LayoutLink>(Math.Max(0, Plan.Count - 1));
        SegmentCount = 0;
        for (var i = 0; i < Plan.Count; i++)
        {
            LayoutSegmentSource[] Selected;
            Transform Container;
            if (IsNativeMountainPair(Plan, i))
            {
                var Count = IsPeakGroup(Plan, i + 1) ? 3 : 2;
                Selected = new LayoutSegmentSource[Count];
                for (var j = 0; j < Count; j++) Selected[j] = Plan[i + j];
                Container = Count == 3 ? PeakGroupContainer(Selected) : Selected[0].Container;
                if (Selected[1].Container != Container)
                    throw new InvalidOperationException(
                        $"PEAK's native {Selected[0].Id}/{Selected[1].Id} placement lost its shared container.");
                if (!Originals.Active.ContainsKey(Container.gameObject))
                    Originals.Active.Add(Container.gameObject, Container.gameObject.activeSelf);
                i += Count - 1;
            }
            else if (IsPeakGroup(Plan, i))
            {
                Selected = new[] { Plan[i], Plan[i + 1] };
                Container = PeakGroupContainer(Selected);
                if (!Originals.Active.ContainsKey(Container.gameObject))
                    Originals.Active.Add(Container.gameObject, Container.gameObject.activeSelf);
                i++;
            }
            else
            {
                if (Plan[i] is PeakAttachmentSource)
                    throw new InvalidOperationException("Peak must remain attached to its preceding ordinary segment.");
                Selected = new[] { Plan[i] };
                Container = Plan[i].Container;
            }

            var Offset = Vector3.zero;
            if (Placements.Count != 0)
            {
                var Previous = Placements[Placements.Count - 1];
                var PreviousSource = LastSource(Previous.Sources);
                var Transition = ResolvePlacementTransition(Previous, Selected[0]);
                var Entry = Selected[0].AuthoredPosition(Transition.Entry);
                var FullPeak = PreviousSource is PeakAttachmentSource;
                var PeakSuccessor = TryPeakSuccessor(Previous.Sources, PreviousSource, out var peak);
                var PlacedExit = PeakSuccessor || FullPeak
                    ? PlacedPose(Previous, PreviousSource, Transition.Exit)
                    : default;
                Offset = (PeakSuccessor || FullPeak
                    ? PlacedExit.position
                    : PlacedPosition(Previous, PreviousSource, Transition.Exit)) - Entry;
                if (PeakSuccessor)
                    Offset = BoundaryClearance.ResolvePeak(
                        peak,
                        PlacedExit,
                        Selected[0],
                        Offset,
                        new LayoutPort(Transition.Connector, Transition.Entry));
                else
                    Offset = BoundaryClearance.Resolve(
                        Previous.Sources, Previous.Offset, Selected[0], Offset, Transition);
                var Rotation = PeakSuccessor || FullPeak
                    ? PlacedExit.rotation * Quaternion.Inverse(Selected[0].AuthoredRotation(Transition.Entry))
                    : Previous.Frame.Rotate(Transition.Rotation);
                var Rotated = Math.Abs(Quaternion.Dot(Rotation.normalized, Quaternion.identity)) < 0.999999f;
                if (Rotated && (PreviousSource is not PeakAttachmentSource || Selected[0] is not NadirLayoutSegmentSource))
                    throw new InvalidOperationException(
                        "A nonidentity placement frame is only valid from native Peak to terminal Nadir.");
                var Frame = Rotated
                    ? new PlacementFrame(Entry, Offset, Rotation)
                    : new PlacementFrame(Offset);
                var Placement = new Placement(Selected, Container, Frame, SegmentCount);
                Placements.Add(Placement);
                Links.Add(new LayoutLink(Previous, Placement, Transition));
            }
            else Placements.Add(new Placement(Selected, Container, new PlacementFrame(Offset), SegmentCount));

            for (var j = 0; j < Selected.Length; j++)
                if (Selected[j] is not PeakAttachmentSource) SegmentCount++;
        }
        return Placements;
    }
    internal static LayoutTransition ResolvePlacementTransition(
        Placement PreviousPlacement,
        LayoutSegmentSource Next)
    {
        var Previous = LastSource(PreviousPlacement.Sources);
        var Transition = LayoutTransition.Resolve(Previous, Next);
        if (!TryPeakSuccessor(PreviousPlacement.Sources, Previous, out var peak)) return Transition;
        var Exit = peak.BridgeSuccessorBoundary;
        if (!string.Equals(Exit.Connector, Transition.Connector, StringComparison.Ordinal))
            throw new InvalidOperationException("PEAK's current Peak Bridge boundary connector is invalid.");
        LayoutPort Entry;
        if (Next is NativeOrdinarySegmentSource { PeakSuccessorEntry: { } CalderaEntry })
        {
            var Pose = CalderaEntry.Pose;
            var Lift = Pose.rotation * (Vector3.up * peak.BridgeWalkingClearance);
            var Scale = Next.RootScale;
            Pose.position += new Vector3(Lift.x / Scale.x, Lift.y / Scale.y, Lift.z / Scale.z);
            Entry = new LayoutPort(CalderaEntry.Connector, Pose);
        }
        else Entry = new LayoutPort(Transition.Connector, Transition.Entry);
        var Rotation = peak.AuthoredRotation(Exit.Pose)
            * Quaternion.Inverse(Next.AuthoredRotation(Entry.Pose));
        return LayoutTransition.Create(Exit, Entry, Rotation.normalized);
    }
    internal static bool TryPeakSuccessor(
        IReadOnlyList<LayoutSegmentSource> Sources,
        LayoutSegmentSource Source,
        out PeakAttachmentSource Peak)
    {
        if (Source is PeakAttachmentSource)
        {
            Peak = (PeakAttachmentSource)Source;
            return true;
        }
        if (Source is NativeOrdinarySegmentSource { ContextualPeak: { } PassivePeak }
            && PassivePeak.BridgeRoot && !Selected(Sources, PassivePeak))
        {
            Peak = PassivePeak;
            return true;
        }
        Peak = null!;
        return false;
    }
    internal static LayoutSegmentSource LastSource(IReadOnlyList<LayoutSegmentSource> Sources) =>
        Sources.Count != 0
            ? Sources[Sources.Count - 1]
            : throw new InvalidOperationException("A placement contains no map source.");
    internal static bool AlwaysClone(Placement Placement)
    {
        for (var i = 0; i < Placement.Sources.Length; i++)
            if (Placement.Sources[i] is CustomLayoutSegmentSource or NadirLayoutSegmentSource) return true;
        return false;
    }
    internal static bool CanReuseAuthoredFeatures(
        Placement Placement,
        HashSet<int> AuthoredTerrain,
        HashSet<int> AuthoredCampfires,
        bool ReplaceGenerated,
        BiomeLayoutConfigSnapshot Configuration)
    {
        if (ReplaceGenerated) return false;
        foreach (var Source in Placement.Sources)
        {
            if (Configuration.NativeVariations.TryGetValue(Source.Id, out var Selected) && !Selected.IsRandom) return false;
            if (Source is PeakAttachmentSource) continue;
            if (!AuthoredTerrain.Contains(Source.SegmentParent.GetInstanceID())) return false;
            if (!Source.SegmentCampfire)
            {
                if (!FeatureGeneration.NativeCampfireOptional(Source)) return false;
                continue;
            }
            if (!AuthoredCampfires.Contains(Source.SegmentCampfire.GetInstanceID())) return false;
        }
        return true;
    }
    internal static LayoutSegmentSource FullPeakOwner(
        IReadOnlyList<LayoutSegmentSource> Sources,
        PeakAttachmentSource Source)
    {
        if (Source is not PeakAttachmentSource || Sources.Count < 2
            || !ReferenceEquals(Sources[Sources.Count - 1], Source))
            throw new InvalidOperationException("A full Peak requires its immediately preceding ordinary owner.");
        var Owner = Sources[Sources.Count - 2];
        if (Owner is PeakAttachmentSource)
            throw new InvalidOperationException("A full Peak cannot attach to another attachment.");
        if (Owner is NadirLayoutSegmentSource)
            throw new InvalidOperationException("A full Peak does not support a Nadir owner exit.");
        return Owner;
    }
    internal static LayoutPort LogicalPeakExit(PeakAttachmentSource Source, LayoutPort Exit)
    {
        var Boundary = Source.BridgeSuccessorBoundary;
        return new LayoutPort(Exit.Connector, new Pose(Exit.Pose.position, Boundary.Pose.rotation));
    }
    internal static Quaternion PeakOwnerRouteRotation(PeakAttachmentSource Source, Pose OwnerBoundary)
    {
        if (Source.ExitPorts.Count != 1)
            throw new InvalidOperationException("PEAK's full Peak route requires one summit port.");
        var Direction = Source.AuthoredPosition(Source.ExitPorts[0].Pose)
            - Source.AuthoredPosition(OwnerBoundary);
        Direction.y = 0f;
        if (float.IsNaN(Direction.x) || float.IsInfinity(Direction.x)
            || float.IsNaN(Direction.z) || float.IsInfinity(Direction.z)
            || Direction.sqrMagnitude <= 0f)
            throw new InvalidOperationException("PEAK's full Peak owner-to-summit route is invalid.");
        var Rotation = Quaternion.LookRotation(Direction, Vector3.up);
        if (float.IsNaN(Rotation.x) || float.IsInfinity(Rotation.x)
            || float.IsNaN(Rotation.y) || float.IsInfinity(Rotation.y)
            || float.IsNaN(Rotation.z) || float.IsInfinity(Rotation.z)
            || float.IsNaN(Rotation.w) || float.IsInfinity(Rotation.w))
            throw new InvalidOperationException("PEAK's full Peak owner-to-summit route rotation is invalid.");
        return Rotation;
    }
    internal static PlacementFrame FullPeakFrame(
        IReadOnlyList<LayoutSegmentSource> Sources,
        PlacementFrame OwnerFrame,
        PeakAttachmentSource Source)
    {
        var Owner = FullPeakOwner(Sources, Source);
        return FullPeakFrame(Owner, OwnerFrame, Source);
    }
    internal static PlacementFrame FullPeakFrame(
        LayoutSegmentSource Owner,
        PlacementFrame OwnerFrame,
        PeakAttachmentSource Source)
    {
        if (Owner is NativeOrdinarySegmentSource { ContextualPeak: { } SelectedContextualPeak }
            && ReferenceEquals(SelectedContextualPeak, Source)) return OwnerFrame;
        if (Owner.ExitPorts.Count == 0)
            throw new InvalidOperationException("The full Peak owner has no ExitPorts[0] pose.");
        if (Source.ExitPorts.Count == 0)
            throw new InvalidOperationException("PEAK's full Peak route is unavailable.");
        var Anchor = Source.BridgeOwnerBoundary;

        var OwnerExit = Owner.ExitPorts[0].Pose;
        var Exit = OwnerFrame.Pose(new Pose(
            Owner.AuthoredPosition(OwnerExit),
            Owner.AuthoredRotation(OwnerExit)));
        if (Owner is CustomLayoutSegmentSource
            && (Vector3.Dot(Exit.rotation * Vector3.forward, Vector3.forward) < 0.999999f
                || Vector3.Dot(Exit.rotation * Vector3.up, Vector3.up) < 0.999999f))
            throw new InvalidOperationException(
                "A custom full Peak owner requires ExitPorts[0] aligned to world forward and up.");

        var AnchorPosition = Source.AuthoredPosition(Anchor);
        var RouteRotation = PeakOwnerRouteRotation(Source, Anchor);
        var Rotation = Exit.rotation * Quaternion.Inverse(RouteRotation);
        return new PlacementFrame(AnchorPosition, Exit.position - AnchorPosition, Rotation);
    }
    internal static Vector3 PlacedPosition(Placement Placement, LayoutSegmentSource Source, Pose Pose) =>
        Source is PeakAttachmentSource Peak
            ? FullPeakFrame(Placement.Sources, Placement.Frame, Peak).Point(Peak.AuthoredPosition(Pose))
            : Placement.Frame.Point(Source.AuthoredPosition(Pose));
    internal static Vector3 PeakBridgeOwnerBoundaryLocalPosition(PeakAttachmentSource Source)
    {
        var Boundary = Source.BridgeOwnerBoundary;
        if (!Source.BridgeRoot)
            throw new InvalidOperationException("PEAK's native Peak Bridge owner boundary is unavailable.");
        return Source.SegmentParent.transform.InverseTransformPoint(
            Source.AuthoredPosition(Boundary));
    }
    internal static void PlaceFullPeakRoot(
        PeakAttachmentSource Source,
        Transform Attachment,
        PlacementFrame Frame)
    {
        if (!Attachment)
            throw new InvalidOperationException("A full Peak placement lost its root or authored root pose.");
        Attachment.SetPositionAndRotation(
            Frame.Point(Source.AuthoredPosition(Source.HashPose)),
            Frame.Rotate(Source.AuthoredRotation(Source.HashPose)));
    }
    internal static PlacementFrame PeakFrame(PeakAttachmentSource Source, Transform Attachment)
    {
        if (!Attachment)
            throw new InvalidOperationException("A placed Peak frame lost its live attachment or authored root pose.");
        var AuthoredPosition = Source.AuthoredPosition(Source.HashPose);
        var Rotation = (Attachment.rotation * Quaternion.Inverse(Source.AuthoredRotation(Source.HashPose))).normalized;
        var Offset = Attachment.position - AuthoredPosition;
        return Math.Abs(Quaternion.Dot(Rotation, Quaternion.identity)) >= 0.999999f
            ? new PlacementFrame(Offset)
            : new PlacementFrame(AuthoredPosition, Offset, Rotation);
    }

    internal static PlacementFrame PlacedPeakFrame(MapSlot Slot)
    {
        var Attachment = Slot.PeakAttachment
            ?? throw new InvalidOperationException("A placed Peak frame lost its attachment.");
        var Source = Slot.PeakSource
            ?? throw new InvalidOperationException("A placed Peak frame lost its source.");
        return PeakFrame(Source, Attachment);
    }

    internal static Pose PlacedPeakExitPose(MapSlot Slot)
    {
        var Source = Slot.PeakSource
            ?? throw new InvalidOperationException("A placed Peak exit lost its source.");
        if (Source.ExitPorts.Count != 1)
            throw new InvalidOperationException("A placed Peak exit requires one summit port.");
        var Exit = LogicalPeakExit(Source, Source.ExitPorts[0]);
        var Authored = new Pose(Source.AuthoredPosition(Exit.Pose), Source.AuthoredRotation(Exit.Pose));
        return FullPeakFrame(Slot.Source, Slot.Frame, Source).Pose(Authored);
    }
    internal static Pose PlacedPose(Placement Placement, LayoutSegmentSource Source, Pose Pose)
    {
        var Authored = new Pose(Source.AuthoredPosition(Pose), Source.AuthoredRotation(Pose));
        return Source is PeakAttachmentSource Peak
            ? FullPeakFrame(Placement.Sources, Placement.Frame, Peak).Pose(Authored)
            : Placement.Frame.Pose(Authored);
    }
    internal static void BindIncomingAnchors(IReadOnlyList<LayoutLink> Links, List<GameObject> CreatedObjects)
    {
        foreach (var Link in Links)
        {
            var Root = Link.Next.Slots[0].Container;
            var Next = Link.Next.Sources[0];
            var Anchor = NativeFixtures.CreatePoseAnchor(
                Root,
                NativeFixtures.Rebase(Root, PlacedPose(Link.Next, Next, Link.Transition.Entry)),
                $"PLL_Entry_{Link.Next.Index:D3}");
            Link.Next.Slots[0].Segment.reconnectSpawnPos = Anchor;
            CreatedObjects.Add(Anchor.gameObject);
        }
    }
    internal static List<LayoutSegmentSource> CatalogSources(LayoutSegmentCatalog Catalog)
    {
        var Result = new List<LayoutSegmentSource>(Catalog.AllValues);
        foreach (var Source in Catalog.Native.Sources.Values)
            if (Source is NativeOrdinarySegmentSource { ContextualPeak: { } Peak }
                && !Result.Contains(Peak)) Result.Add(Peak);
        return Result;
    }
    internal static List<LayoutSegmentSource> CurrentLayout(
        LayoutSegmentCatalog Catalog,
        MapHandler Handler)
    {
        var OrdinaryRoles = (int)Segment.TheKiln + 1;
        if (Handler.segments is null || Handler.segments.Length < OrdinaryRoles)
            throw new InvalidOperationException("PEAK's ordinary native segment roles are unavailable.");
        var Current = new List<LayoutSegmentSource>(OrdinaryRoles);
        for (var i = 0; i < OrdinaryRoles; i++)
        {
            var Segment = Handler.segments[i];
            var Parent = Segment.segmentParent;
            var Campfire = Segment.segmentCampfire;
            var Biome = Segment.biome;
            LayoutSegmentSource? Selected = null;
            foreach (var Source in Catalog.AllValues)
            {
                if (Source is not NativeOrdinarySegmentSource { NativeRole: var role } Native
                    || (int)role != i) continue;
                if (Native.SegmentParent != Parent
                    || Native.SegmentCampfire != Campfire
                    || Native.NativeBiome != Biome) continue;
                Selected = Native;
                break;
            }
            Current.Add(Selected ?? throw new InvalidOperationException(
                $"The effective map segment at index {i} did not exactly match PEAK's segment catalog."));
        }
        var Result = new List<LayoutSegmentSource>(Current.Count);
        for (var i = 0; i < Current.Count; i++)
        {
            var Source = Current[i];
            if (Source is NativeOrdinarySegmentSource { NativeRole: Segment.Caldera })
            {
                if (++i >= Current.Count || !NativeSegmentCatalog.IsCompositePair(Source, Current[i]))
                    throw new InvalidOperationException(
                        $"PEAK's current map layout splits native composite prefix '{Source.Id}'.");
                Result.Add(Current[i]);
                continue;
            }
            if (Source is NativeOrdinarySegmentSource { NativeRole: Segment.TheKiln })
                throw new InvalidOperationException(
                    $"PEAK's current map layout exposes native composite owner '{Source.Id}' without its prefix.");
            Result.Add(Source);
        }
        return Result;
    }
    internal static bool IsPeakGroup(IReadOnlyList<LayoutSegmentSource> Plan, int Index) =>
        Index + 1 < Plan.Count
        && LayoutPlan.CanAttachPeak(Plan[Index], Plan[Index + 1]);
    internal static bool IsNativeMountainPair(IReadOnlyList<LayoutSegmentSource> Plan, int Index) =>
        Index + 1 < Plan.Count
        && NativeSegmentCatalog.IsCompositePair(Plan[Index], Plan[Index + 1]);
    internal static NativeProgressionBoundaryKind ProgressionBoundary(
        IReadOnlyList<LayoutSegmentSource> SelectedSources,
        LayoutSegmentSource Source,
        bool HasSuccessor)
    {
        for (var i = 0; i < SelectedSources.Count; i++)
            if (SelectedSources[i] is PeakAttachmentSource) return NativeProgressionBoundaryKind.None;
        if (!HasSuccessor) return NativeProgressionBoundaryKind.None;
        if (Source is NadirLayoutSegmentSource) return NativeProgressionBoundaryKind.NadirGate;
        return Source is NativeOrdinarySegmentSource { ContextualPeak: { } Peak }
            && Peak.BridgeRoot && !Selected(SelectedSources, Peak)
            ? NativeProgressionBoundaryKind.PeakBridge
            : NativeProgressionBoundaryKind.None;
    }
    internal static void ApplyMiniRunTerminal(List<LayoutSegmentSource> Plan)
    {
        if (!RunSettings.isMiniRun) return;
        var Role = (Segment)RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRunBiome, false);
        for (var i = 0; i < Plan.Count; i++)
        {
            var Terminal = Plan[i];
            if (Terminal is not NativeOrdinarySegmentSource Native || Native.NativeRole != Role) continue;
            var TerminalIndex = i;
            if (Native.NativeRole == Segment.Caldera)
            {
                if (++TerminalIndex >= Plan.Count
                    || !NativeSegmentCatalog.IsCompositePair(Terminal, Plan[TerminalIndex]))
                    throw new InvalidOperationException(
                        $"Mini-run segment {Role} resolved to an incomplete native composite.");
            }
            Plan.RemoveRange(TerminalIndex + 1, Plan.Count - TerminalIndex - 1);
            return;
        }
        throw new InvalidOperationException($"The map layout does not contain the selected mini-run segment {Role}.");
    }
    internal static Transform PeakGroupContainer(IReadOnlyList<LayoutSegmentSource> Group)
    {
        var Owner = Group[Group.Count - 2];
        var Peak = Group[Group.Count - 1];
        var Container = Owner.Container;
        if (!Container || !LayoutPlan.CanAttachPeak(Owner, Peak)
            || !TransformOwnership.Contains(Container, Owner.SegmentParent.transform))
            throw new InvalidOperationException("The ordinary segment cannot own its Peak attachment.");
        if (Owner is NativeOrdinarySegmentSource { ContextualPeak: { } SelectedContextualPeak }
            && ReferenceEquals(SelectedContextualPeak, Peak))
            return Container == Peak.Container && TransformOwnership.Contains(Container, Peak.SegmentParent.transform)
                ? Container
                : throw new InvalidOperationException(
                    "The Volcano or Citadel owner lost its native Peak attachment.");
        return Owner is OrdinaryLayoutSegmentSource
            ? Container
            : throw new InvalidOperationException(
                "The ordinary segment has no pose for its external Peak attachment.");
    }
    internal static bool Selected(IReadOnlyList<LayoutSegmentSource> Sources, LayoutSegmentSource Source)
    {
        for (var i = 0; i < Sources.Count; i++)
            if (ReferenceEquals(Sources[i], Source)) return true;
        return false;
    }
}
