using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeBoundaryClearance
{
    readonly struct Interval
    {
        internal Interval(float minimum, float maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        internal float Minimum { get; }
        internal float Maximum { get; }
        internal float Width => Maximum - Minimum;
        internal bool Contains(float value) => value >= Minimum && value <= Maximum;
    }

    readonly struct Candidate
    {
        internal Candidate(
            MeshCollider collider,
            Bounds bounds,
            int depth,
            Vector3 forward,
            Vector3 transverse)
        {
            Collider = collider;
            Bounds = bounds;
            Depth = depth;
            Forward = Project(bounds, forward);
            Transverse = Project(bounds, transverse);
        }

        internal MeshCollider Collider { get; }
        internal Bounds Bounds { get; }
        internal int Depth { get; }
        internal Interval Forward { get; }
        internal Interval Transverse { get; }
    }

    sealed class Surface
    {
        internal Surface(
            MeshCollider collider,
            Bounds bounds,
            float contactOffset,
            NativeMeshGeometry geometry)
        {
            Name = collider.name;
            Bounds = bounds;
            ContactOffset = contactOffset;
            Geometry = geometry;
        }

        internal string Name { get; }
        internal Bounds Bounds { get; }
        internal float ContactOffset { get; }
        internal NativeMeshGeometry Geometry { get; }
    }

    sealed class Wall
    {
        internal Wall(Surface[] Surfaces, Vector3 Anchor)
        {
            this.Surfaces = Surfaces;
            this.Anchor = Anchor;
        }

        internal Surface[] Surfaces { get; }
        internal Vector3 Anchor { get; }
    }

    readonly struct Boundary
    {
        internal Boundary(Vector3 point, float contactOffset)
        {
            Point = point;
            ContactOffset = contactOffset;
        }

        internal Vector3 Point { get; }
        internal float ContactOffset { get; }
    }

    readonly struct PeakEntryProfile
    {
        internal PeakEntryProfile(Surface surface, NativeMeshGeometry geometry, Vector3 point)
        {
            Surface = surface;
            Geometry = geometry;
            Point = point;
        }

        internal Surface Surface { get; }
        internal NativeMeshGeometry Geometry { get; }
        internal Vector3 Point { get; }
    }

    readonly struct PeakSuccessorProfile
    {
        internal PeakSuccessorProfile(Vector3 localContactCorrection, Vector3 localUp, double requiredDrop)
        {
            LocalContactCorrection = localContactCorrection;
            LocalUp = localUp;
            RequiredDrop = requiredDrop;
        }

        internal Vector3 LocalContactCorrection { get; }
        internal Vector3 LocalUp { get; }
        internal double RequiredDrop { get; }
    }

    readonly struct PeakProfileKey : IEquatable<PeakProfileKey>
    {
        readonly string peakId;
        readonly string peakRevision;
        readonly string peakSegmentTypeId;
        readonly string peakBiomeId;
        readonly string nextId;
        readonly string nextRevision;
        readonly string nextSegmentTypeId;
        readonly string nextBiomeId;
        readonly string connector;
        readonly Vector3 entryPosition;
        readonly Quaternion entryRotation;
        readonly Vector3 boundaryPosition;
        readonly Quaternion boundaryRotation;

        internal PeakProfileKey(
            PeakAttachmentSource Peak,
            LayoutSegmentSource Next,
            string connector,
            Pose entry,
            Pose boundary)
        {
            peakId = Peak.Id;
            peakRevision = Peak.Revision;
            peakSegmentTypeId = Peak.SegmentTypeId;
            peakBiomeId = Peak.BiomeId;
            nextId = Next.Id;
            nextRevision = Next.Revision;
            nextSegmentTypeId = Next.SegmentTypeId;
            nextBiomeId = Next.BiomeId;
            this.connector = connector;
            entryPosition = entry.position;
            entryRotation = entry.rotation;
            boundaryPosition = boundary.position;
            boundaryRotation = boundary.rotation;
        }

        public bool Equals(PeakProfileKey other) =>
            string.Equals(peakId, other.peakId, StringComparison.Ordinal)
            && string.Equals(peakRevision, other.peakRevision, StringComparison.Ordinal)
            && string.Equals(peakSegmentTypeId, other.peakSegmentTypeId, StringComparison.Ordinal)
            && string.Equals(peakBiomeId, other.peakBiomeId, StringComparison.Ordinal)
            && string.Equals(nextId, other.nextId, StringComparison.Ordinal)
            && string.Equals(nextRevision, other.nextRevision, StringComparison.Ordinal)
            && string.Equals(nextSegmentTypeId, other.nextSegmentTypeId, StringComparison.Ordinal)
            && string.Equals(nextBiomeId, other.nextBiomeId, StringComparison.Ordinal)
            && string.Equals(connector, other.connector, StringComparison.Ordinal)
            && entryPosition.Equals(other.entryPosition)
            && entryRotation.Equals(other.entryRotation)
            && boundaryPosition.Equals(other.boundaryPosition)
            && boundaryRotation.Equals(other.boundaryRotation);

        public override bool Equals(object? value) => value is PeakProfileKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(peakId);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(peakRevision);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(peakSegmentTypeId);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(peakBiomeId);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(nextId);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(nextRevision);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(nextSegmentTypeId);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(nextBiomeId);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(connector);
                hash = hash * 397 ^ entryPosition.GetHashCode();
                hash = hash * 397 ^ entryRotation.GetHashCode();
                hash = hash * 397 ^ boundaryPosition.GetHashCode();
                return hash * 397 ^ boundaryRotation.GetHashCode();
            }
        }
    }

    readonly Dictionary<MeshCollider, Surface> surfaces = new();
    readonly Dictionary<LayoutSegmentSource, Wall> Walls = new();
    readonly Dictionary<LayoutSegmentSource, Surface> Entries = new();
    readonly Dictionary<PeakProfileKey, PeakSuccessorProfile> peakProfiles = new();
    readonly NativeMeshAssets meshes = new();
    readonly Transform[] exclusions;

    internal NativeBoundaryClearance(params Transform[] Exclusions) =>
        exclusions = Exclusions ?? Array.Empty<Transform>();

    internal Vector3 Resolve(
        IReadOnlyList<LayoutSegmentSource> Previous,
        Vector3 previousOffset,
        LayoutSegmentSource Next,
        Vector3 baseOffset,
        LayoutTransition Transition)
    {
        if (Next is CustomLayoutSegmentSource) return baseOffset;
        if (Previous.Count == 0)
        {
            if (Next is not INativeLayoutSegmentSource { NativeRole: Segment.Beach }) return baseOffset;
            throw new InvalidOperationException("Native boundary clearance has no preceding segment source.");
        }

        var Source = Previous[Previous.Count - 1];
        if (Source is not NadirLayoutSegmentSource && Next is not INativeLayoutSegmentSource { NativeRole: Segment.Beach } && Next is not NadirLayoutSegmentSource) return baseOffset;
        return ResolveBoundary(Source, previousOffset, Next, Transition);
    }

    Vector3 ResolveBoundary(
        LayoutSegmentSource Source,
        Vector3 previousOffset,
        LayoutSegmentSource Next,
        LayoutTransition Transition)
    {
        var Forward = Next.ProgressionAxis;
        if (!UnityValueContract.Finite(Forward) || Forward.sqrMagnitude <= 0f)
            throw new InvalidOperationException("Native boundary clearance has no valid progression axis.");
        Forward.Normalize();
        var Transverse = Source.AuthoredRotation(Transition.Exit) * Vector3.right;
        Transverse -= Forward * Vector3.Dot(Transverse, Forward);
        if (!UnityValueContract.Finite(Transverse) || Transverse.sqrMagnitude <= 0f)
            throw new InvalidOperationException("Native boundary clearance has no valid transverse axis.");
        Transverse.Normalize();
        var Up = Vector3.Cross(Forward, Transverse);
        if (!UnityValueContract.Finite(Up) || Up.sqrMagnitude <= 0f)
            throw new InvalidOperationException("Native boundary clearance has no valid up axis.");
        Up.Normalize();

        var Wall = GetWall(Source, Transition.Exit, Forward, Transverse);
        var EntryPosition = Next.AuthoredPosition(Transition.Entry);
        var Entry = GetEntry(Next, EntryPosition, Forward, Transverse);
        var WallBoundary = ResolveWallBoundary(Wall, previousOffset, Forward, Transverse, Up);
        if (!Entry.Geometry.TryUniqueGroundableBoundaryPoint(
                Transverse, Vector3.Dot(Transverse, EntryPosition), -Forward, Up, out var EntryBoundary))
            throw new InvalidOperationException(
                $"Native entry collider '{Entry.Name}' has no unique groundable boundary through its entry.");

        var Result = WallBoundary.Point - EntryBoundary
            - Up * (WallBoundary.ContactOffset + Entry.ContactOffset);
        if (!UnityValueContract.Finite(Result))
            throw new InvalidOperationException("Native boundary clearance produced an invalid placement offset.");
        return Result;
    }

    internal Vector3 ResolvePeak(
        PeakAttachmentSource Peak,
        Pose placedBoundary,
        LayoutSegmentSource Next,
        Vector3 baseOffset,
        LayoutPort entry)
    {
        if (Next is CustomLayoutSegmentSource || FeatureGeneration.NativeCaldera(Next)) return baseOffset;
        var Profile = GetPeakSuccessorProfile(Peak, Next, entry);
        var ContactCorrection = baseOffset + placedBoundary.rotation * Profile.LocalContactCorrection;
        var Up = (placedBoundary.rotation * Profile.LocalUp).normalized;
        return RealizePeakCorrection(ContactCorrection, Up, Profile.RequiredDrop);
    }

    PeakSuccessorProfile GetPeakSuccessorProfile(
        PeakAttachmentSource Peak,
        LayoutSegmentSource Next,
        LayoutPort entry)
    {
        var BoundaryPort = Peak.BridgeSuccessorBoundary;
        if (!Peak.BridgeRoot)
            throw new InvalidOperationException("Native Peak successor clearance has no physical bridge boundary.");
        var BoundaryPose = BoundaryPort.Pose;
        var Boundary = new Pose(Peak.AuthoredPosition(BoundaryPose), Peak.AuthoredRotation(BoundaryPose));
        var NextEntry = new Pose(Next.AuthoredPosition(entry.Pose), Next.AuthoredRotation(entry.Pose));
        if (!UnityValueContract.Finite(Boundary.position)
            || !UnityValueContract.Finite(NextEntry.position)
            || !UnityValueContract.Finite(Boundary.rotation)
            || Boundary.rotation.x == 0f && Boundary.rotation.y == 0f
                && Boundary.rotation.z == 0f && Boundary.rotation.w == 0f
            || !UnityValueContract.Finite(NextEntry.rotation)
            || NextEntry.rotation.x == 0f && NextEntry.rotation.y == 0f
                && NextEntry.rotation.z == 0f && NextEntry.rotation.w == 0f)
            throw new InvalidOperationException("Native Peak successor clearance has an invalid boundary or entry pose.");
        Boundary.rotation = Boundary.rotation.normalized;
        NextEntry.rotation = NextEntry.rotation.normalized;
        var Key = new PeakProfileKey(Peak, Next, entry.Connector, NextEntry, Boundary);
        if (peakProfiles.TryGetValue(Key, out var result)) return result;

        var Forward = (Boundary.rotation * Vector3.forward).normalized;
        var Transverse = (Boundary.rotation * Vector3.right).normalized;
        var Up = Vector3.Cross(Forward, Transverse).normalized;
        if (!UnityValueContract.Finite(Forward) || !UnityValueContract.Finite(Transverse)
            || !UnityValueContract.Finite(Up)
            || Forward.sqrMagnitude <= 0f || Transverse.sqrMagnitude <= 0f || Up.sqrMagnitude <= 0f)
            throw new InvalidOperationException("Native Peak successor clearance has an invalid boundary frame.");

        var Bridge = GetPeakSurface(Peak);
        var PassiveBeach = Next is INativeLayoutSegmentSource { NativeRole: Segment.Beach };
        var entryProfile = GetPeakEntryProfile(
            Next, NextEntry, Boundary, Forward, Transverse, Up, PassiveBeach);
        var Clearance = Bridge.ContactOffset + entryProfile.Surface.ContactOffset;
        var ContactCorrection = PassiveBeach
            ? new Vector3(0f, Boundary.position.y - entryProfile.Point.y - Clearance, 0f)
            : Boundary.position - entryProfile.Point - Up * Clearance;
        Bridge.Geometry.GroundableTopEnvelopePenetration(
            entryProfile.Geometry,
            ContactCorrection,
            Transverse,
            Forward,
            Up,
            Clearance,
            out var requiredDrop);
        var InverseBoundaryRotation = Quaternion.Inverse(Boundary.rotation);
        var LocalContactCorrection = InverseBoundaryRotation * ContactCorrection;
        var LocalUp = InverseBoundaryRotation * Up;
        if (!UnityValueContract.Finite(LocalContactCorrection)
            || !UnityValueContract.Finite(LocalUp)
            || !double.IsFinite(requiredDrop) || requiredDrop < 0d || requiredDrop > float.MaxValue)
            throw new InvalidOperationException("Native Peak successor clearance produced an invalid boundary profile.");
        result = new PeakSuccessorProfile(LocalContactCorrection, LocalUp, requiredDrop);
        peakProfiles.Add(Key, result);
        return result;
    }

    PeakEntryProfile GetPeakEntryProfile(
        LayoutSegmentSource Source,
        Pose entry,
        Pose boundary,
        Vector3 forward,
        Vector3 transverse,
        Vector3 up,
        bool entryLocal)
    {
        var SurfaceCandidates = Candidates(Source.SegmentParent.transform, forward, transverse);
        SurfaceCandidates.Sort((Left, Right) =>
            NativePlacement.BoundsSquaredDistance(entry.position, Left.Bounds)
                .CompareTo(NativePlacement.BoundsSquaredDistance(entry.position, Right.Bounds)));
        var entryRotation = (boundary.rotation * Quaternion.Inverse(entry.rotation)).normalized;
        var entryOffset = boundary.position - entry.position;
        var coordinate = Vector3.Dot(transverse, boundary.position);
        var distance = double.PositiveInfinity;
        var ambiguous = false;
        var result = default(PeakEntryProfile);
        for (var i = 0; i < SurfaceCandidates.Count; i++)
        {
            var Candidate = SurfaceCandidates[i];
            if (NativePlacement.BoundsSquaredDistance(entry.position, Candidate.Bounds) > distance) break;
            var Surface = GetSurface(Candidate);
            var closest = Surface.Geometry.ClosestPoint(entry.position, Vector3.zero);
            var CandidateDistance = NativePlacement.SquaredDistance(entry.position, closest);
            if (CandidateDistance > distance) continue;
            var placed = Surface.Geometry.Transform(entry.position, entryOffset, entryRotation);
            Vector3 point;
            var found = entryLocal
                ? placed.TryUniqueNearestGroundableIntersection(
                    boundary.position, Vector3.up, Vector3.up, out point)
                : placed.TryGroundableEntryPoint(
                    transverse, coordinate, forward, up, out point);
            if (!found) continue;
            if (CandidateDistance < distance)
            {
                result = new PeakEntryProfile(Surface, placed, point);
                distance = CandidateDistance;
                ambiguous = false;
            }
            else if (!ReferenceEquals(result.Surface, Surface)) ambiguous = true;
        }
        if (result.Surface is null)
            throw new InvalidOperationException(
                $"Native segment '{Source.Id}' has no groundable mesh intersection at its Peak successor entry.");
        if (ambiguous)
            throw new InvalidOperationException(
                $"Native segment '{Source.Id}' has multiple equally near groundable Peak successor entry surfaces.");
        return result;
    }

    Surface GetPeakSurface(PeakAttachmentSource Peak)
    {
        var Bridge = Peak.BridgeRoot;
        var SurfaceCandidates = Candidates(Bridge, Vector3.forward, Vector3.right);
        if (SurfaceCandidates.Count != 1)
            throw new InvalidOperationException(
                $"Peak Bridge candidate count is {SurfaceCandidates.Count}; expected exactly one active static Terrain/Map mesh.");
        return GetSurface(SurfaceCandidates[0]);
    }

    Wall GetWall(
        LayoutSegmentSource Source,
        Pose exit,
        Vector3 forward,
        Vector3 transverse)
    {
        if (Walls.TryGetValue(Source, out var result)) return result;
        var WallNext = Source.WallNext;
        var Anchor = Source.AuthoredPosition(exit);
        List<Candidate> selected;
        if (Source is NadirLayoutSegmentSource Nadir)
        {
            var Bridge = Nadir.BridgeRoot;
            selected = Candidates(Bridge, forward, transverse);
            if (selected.Count != 1)
                throw new InvalidOperationException(
                    $"Nadir Peak Bridge candidate count is {selected.Count}; expected exactly one active static Terrain/Map mesh.");
        }
        else
        {
            var WallNextTransform = WallNext is { } liveWallNext && liveWallNext
                ? liveWallNext.transform
                : throw new InvalidOperationException($"Segment '{Source.Id}' has no WallNext anchor.");
            var seedTransverse = Vector3.Dot(transverse, Anchor);
            var seedForward = Vector3.Dot(forward, WallNextTransform.position);
            selected = Source.SegmentCampfire
                ? SelectWall(
                    Source.SegmentCampfire.transform, seedTransverse, seedForward, forward, transverse)
                : new List<Candidate>();
            if (selected.Count == 0)
                selected = SelectWall(
                    Source.Container, seedTransverse, seedForward, forward, transverse);
            if (selected.Count == 0)
                throw new InvalidOperationException(
                    $"Segment '{Source.Id}' has no active static Terrain/Map mesh wall containing WallNext.");
        }

        var selectedSurfaces = new Surface[selected.Count];
        for (var i = 0; i < selected.Count; i++)
            selectedSurfaces[i] = GetSurface(selected[i]);
        result = new Wall(selectedSurfaces, Anchor);
        Walls.Add(Source, result);
        return result;
    }

    Surface GetEntry(
        LayoutSegmentSource Source,
        Vector3 entry,
        Vector3 forward,
        Vector3 transverse)
    {
        if (Entries.TryGetValue(Source, out var result)) return result;
        var SurfaceCandidates = Candidates(Source.SegmentParent.transform, forward, transverse);
        SurfaceCandidates.Sort((Left, Right) =>
            NativePlacement.BoundsSquaredDistance(entry, Left.Bounds)
                .CompareTo(NativePlacement.BoundsSquaredDistance(entry, Right.Bounds)));

        var distance = double.PositiveInfinity;
        var ambiguous = false;
        for (var i = 0; i < SurfaceCandidates.Count; i++)
        {
            var Candidate = SurfaceCandidates[i];
            if (NativePlacement.BoundsSquaredDistance(entry, Candidate.Bounds) > distance) break;
            var Surface = GetSurface(Candidate);
            var point = Surface.Geometry.ClosestPoint(entry, Vector3.zero);
            var CandidateDistance = NativePlacement.SquaredDistance(entry, point);
            if (CandidateDistance < distance)
            {
                result = Surface;
                distance = CandidateDistance;
                ambiguous = false;
            }
            else if (CandidateDistance == distance && !ReferenceEquals(result, Surface)) ambiguous = true;
        }

        if (result is null)
            throw new InvalidOperationException(
                $"Native segment '{Source.Id}' has no active static Terrain/Map mesh beneath its entry.");
        if (ambiguous)
            throw new InvalidOperationException(
                $"Native segment '{Source.Id}' has multiple mesh surfaces equally near its entry.");
        Entries.Add(Source, result);
        return result;
    }

    List<Candidate> SelectWall(
        Transform root,
        float seedTransverse,
        float seedForward,
        Vector3 forward,
        Vector3 transverse)
    {
        var candidates = Candidates(root, forward, transverse);
        var result = new List<Candidate>();
        var depth = int.MaxValue;
        var width = float.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            if (!candidate.Transverse.Contains(seedTransverse)
                || !candidate.Forward.Contains(seedForward)) continue;
            if (candidate.Depth < depth)
            {
                result.Clear();
                depth = candidate.Depth;
                width = candidate.Transverse.Width;
                result.Add(candidate);
            }
            else if (candidate.Depth == depth)
            {
                result.Add(candidate);
                width = Math.Max(width, candidate.Transverse.Width);
            }
        }
        for (var i = result.Count - 1; i >= 0; i--)
            if (!Mathf.Approximately(result[i].Transverse.Width, width)) result.RemoveAt(i);
        return result;
    }

    List<Candidate> Candidates(
        Transform root,
        Vector3 forward,
        Vector3 transverse)
    {
        var terrainMap = HelperFunctions.terrainMapMask.value;
        var result = new List<Candidate>();
        foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!collider || !collider.enabled || collider.isTrigger || collider.convex || !collider.sharedMesh
                || collider.attachedRigidbody || !NativePlacement.ActiveWithin(collider.transform, root)
                || NativeCollision.IsGeneratedOutput(root, collider.transform) || Excluded(collider.transform)
                || (terrainMap & (1 << collider.gameObject.layer)) == 0
                || !NativePlacement.TryWorldBounds(collider, out var bounds)
                || bounds.size.sqrMagnitude <= 0f) continue;
            result.Add(new Candidate(
                collider, bounds, Depth(collider.transform, root), forward, transverse));
        }
        return result;
    }

    Surface GetSurface(Candidate candidate)
    {
        var collider = candidate.Collider;
        if (surfaces.TryGetValue(collider, out var result)) return result;
        var contactOffset = collider.contactOffset;
        if (!float.IsFinite(contactOffset) || contactOffset < 0f)
            throw new InvalidOperationException(
                $"Collider '{collider.name}' has an invalid native boundary contact offset.");
        var geometry = meshes.Resolve(collider);
        result = new Surface(collider, candidate.Bounds, contactOffset, geometry);
        surfaces.Add(collider, result);
        return result;
    }

    static Boundary ResolveWallBoundary(
        Wall Wall,
        Vector3 Offset,
        Vector3 Forward,
        Vector3 Transverse,
        Vector3 Up)
    {
        var Found = false;
        var Projection = float.NegativeInfinity;
        var Point = default(Vector3);
        var ContactOffset = 0f;
        foreach (var Surface in Wall.Surfaces)
        {
            if (!Surface.Geometry.TryUniqueGroundableBoundaryPoint(
                    Transverse, Vector3.Dot(Transverse, Wall.Anchor), Forward, Up, out var Candidate)) continue;
            var CandidateProjection = Vector3.Dot(Candidate, Forward);
            if (!Found || CandidateProjection > Projection
                || CandidateProjection == Projection && Vector3.Dot(Candidate, Up) > Vector3.Dot(Point, Up))
            {
                Found = true;
                Projection = CandidateProjection;
                Point = Candidate;
                ContactOffset = Surface.ContactOffset;
            }
            else if (Candidate.Equals(Point)) ContactOffset = Math.Max(ContactOffset, Surface.ContactOffset);
        }
        return Found
            ? new Boundary(Point + Offset, ContactOffset)
            : throw new InvalidOperationException(
                "The selected native wall has no unique groundable boundary through its exit.");
    }

    bool Excluded(Transform value)
    {
        for (var i = 0; i < exclusions.Length; i++)
            if (exclusions[i] && TransformOwnership.Contains(exclusions[i], value)) return true;
        return false;
    }

    static int Depth(Transform value, Transform root)
    {
        var result = 0;
        while (value != root)
        {
            value = value.parent;
            if (!value)
                throw new InvalidOperationException("Native boundary clearance found a collider outside its search root.");
            result++;
        }
        return result;
    }

    static Interval Project(Bounds bounds, Vector3 axis)
    {
        var middle = Vector3.Dot(axis, bounds.center);
        var Extent = Vector3.Dot(NativePlacement.Abs(axis), bounds.extents);
        if (!float.IsFinite(middle) || !float.IsFinite(Extent))
            throw new InvalidOperationException("Native boundary clearance produced an invalid mesh projection.");
        return new Interval(middle - Extent, middle + Extent);
    }

    static Vector3 RealizePeakCorrection(Vector3 contactCorrection, Vector3 up, double requiredDrop)
    {
        if (!UnityValueContract.Finite(contactCorrection)
            || !UnityValueContract.Finite(up) || up.sqrMagnitude <= 0f
            || !double.IsFinite(requiredDrop) || requiredDrop < 0d || requiredDrop > float.MaxValue)
            throw new InvalidOperationException("Native Peak successor clearance produced an invalid placement offset.");

        var lower = BitConverter.SingleToInt32Bits((float)requiredDrop);
        if ((double)BitConverter.Int32BitsToSingle(lower) < requiredDrop) lower++;
        var result = contactCorrection - up * BitConverter.Int32BitsToSingle(lower);
        if (UnityValueContract.Finite(result)
            && ProjectedDrop(contactCorrection, result, up) >= requiredDrop) return result;

        var upper = BitConverter.SingleToInt32Bits(float.MaxValue);
        while (lower < upper)
        {
            var middle = lower + ((upper - lower) >> 1);
            result = contactCorrection - up * BitConverter.Int32BitsToSingle(middle);
            if (UnityValueContract.Finite(result)
                && ProjectedDrop(contactCorrection, result, up) < requiredDrop)
            {
                lower = middle + 1;
                continue;
            }
            upper = middle;
        }
        result = contactCorrection - up * BitConverter.Int32BitsToSingle(lower);
        if (!UnityValueContract.Finite(result)
            || ProjectedDrop(contactCorrection, result, up) < requiredDrop)
            throw new InvalidOperationException(
                "Native Peak successor clearance cannot represent its required vertical drop.");
        return result;
    }

    static double ProjectedDrop(Vector3 contactCorrection, Vector3 result, Vector3 up) =>
        ((double)contactCorrection.x - result.x) * up.x
        + ((double)contactCorrection.y - result.y) * up.y
        + ((double)contactCorrection.z - result.z) * up.z;
}
