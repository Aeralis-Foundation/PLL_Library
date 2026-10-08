using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal readonly struct LayoutPort
{
    internal LayoutPort(string connector, Pose pose)
    {
        Connector = connector;
        Pose = pose;
    }

    internal string Connector { get; }
    internal Pose Pose { get; }
}

internal readonly struct PeakBridgeProfile
{
    internal PeakBridgeProfile(
        Transform source,
        LayoutPort exit,
        Pose contact,
        Pose ownerBoundary,
        LayoutPort successorBoundary,
        Pose BridgeCampfireValue,
        float WalkingClearance)
    {
        Source = source;
        Exit = exit;
        Contact = contact;
        OwnerBoundary = ownerBoundary;
        SuccessorBoundary = successorBoundary;
        BridgeCampfire = BridgeCampfireValue;
        this.WalkingClearance = WalkingClearance;
    }

    internal Transform Source { get; }
    internal LayoutPort Exit { get; }
    internal Pose Contact { get; }
    internal Pose OwnerBoundary { get; }
    internal LayoutPort SuccessorBoundary { get; }
    internal Pose BridgeCampfire { get; }
    internal float WalkingClearance { get; }
}

internal sealed class NativePortProfile
{
    internal NativePortProfile(Pose? start, LayoutPort[] entries, LayoutPort[] exits)
    {
        Start = start;
        Entries = entries;
        Exits = exits;
    }

    internal Pose? Start { get; }
    internal IReadOnlyList<LayoutPort> Entries { get; }
    internal IReadOnlyList<LayoutPort> Exits { get; }
}

internal static class NativeSegmentPorts
{
    static readonly LayoutPort[] NoPorts = Array.Empty<LayoutPort>();

    internal static NativePortProfile Create(
        MapHandler handler,
        Segment role,
        MapHandler.MapSegment source,
        Transform container)
    {
        if (!handler) throw new ArgumentNullException(nameof(handler));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!container) throw new ArgumentNullException(nameof(container));
        var index = (int)role;
        var last = (int)Segment.TheKiln;
        if (index < (int)Segment.Beach || index > last || handler.segments is null || handler.segments.Length <= last)
            throw new InvalidOperationException($"PEAK's native {role} role boundaries are unavailable.");

        var authored = handler.segments[index]
            ?? throw new InvalidOperationException($"PEAK's native segment role {index} is unavailable.");
        var reconnect = source.reconnectSpawnPos ? source.reconnectSpawnPos : Reconnect(authored, index);
        var start = index == (int)Segment.Beach ? LocalPose(container, reconnect) : (Pose?)null;
        var wallPrevious = source.wallPrevious ? source.wallPrevious : authored.wallPrevious;
        var wallNext = source.wallNext ? source.wallNext : authored.wallNext;
        var entryAnchor = wallPrevious
            ? FogOrigin(wallPrevious, role, "entry")
            : index == (int)Segment.Beach
                ? reconnect
                : throw new InvalidOperationException($"PEAK's native {role} entry boundary is unavailable.");
        var entries = new[] { Port(TranslationPose(container, entryAnchor)) };
        var exits = wallNext
            ? new[] { Port(TranslationPose(container, FogOrigin(wallNext, role, "exit"))) }
            : index == last
                ? NoPorts
                : throw new InvalidOperationException($"PEAK's native {role} exit boundary is unavailable.");
        return new NativePortProfile(start, entries, exits);
    }

    internal static IReadOnlyList<LayoutPort> Empty => NoPorts;

    internal static LayoutPort CreateCalderaPeakEntry(Transform Container, Transform SegmentRoot)
    {
        var Shoreline = SegmentRoot.Find("Rocks/Beach");
        var Spawner = Shoreline ? Shoreline.GetComponent<PropSpawner>() : null;
        if (!Spawner || Spawner.GetType() != typeof(PropSpawner)
            || Spawner.raycastPosition || Spawner.nrOfSpawns <= 0
            || Spawner.layerType != HelperFunctions.LayerType.Terrain)
            throw new InvalidOperationException("PEAK's Caldera shoreline terrain provider is unavailable.");
        var River = SegmentRoot.Find("River");
        var Lava = River ? River.GetComponent<Lava>() : null;
        var Animator = River ? River.GetComponent<Animator>() : null;
        var Controller = Animator ? Animator.runtimeAnimatorController : null;
        if (!Lava || Lava.GetType() != typeof(Lava) || !Controller
            || Controller is AnimatorOverrideController)
            throw new InvalidOperationException("PEAK's Caldera tide is unsupported. Update PLL_Library.");
        var HeatHeight = Lava.height;
        if (!float.IsFinite(HeatHeight) || HeatHeight <= 0f)
            throw new InvalidOperationException("PEAK's Caldera heat height must be positive and finite.");
        var Clips = Controller.animationClips;
        if (Clips.Length != 1 || !Clips[0])
            throw new InvalidOperationException("PEAK's Caldera tide is unsupported. Update PLL_Library.");
        var Clip = Clips[0];
        var TideCeiling = CalderaTideCeiling((Application.buildGUID,
            typeof(Lava).Assembly.ManifestModule.ModuleVersionId,
            Controller.name, Clip.name, Clip.length, Clip.frameRate));
        var RiverPosition = River!.localPosition;
        RiverPosition.y = TideCeiling;
        var Position = Shoreline!.position;
        Position.y = Math.Max(Position.y, SegmentRoot.TransformPoint(RiverPosition).y + HeatHeight);
        return Port(new Pose(Container.InverseTransformPoint(Position), Quaternion.Inverse(Container.rotation)));
    }

    internal static float CalderaTideCeiling(
        (string BuildGUID, Guid NativeModule, string Controller, string Clip, float Duration, float FrameRate) Target)
    {
        if (Target.BuildGUID != "8e03f211030843c599c5dacb8e0ac142"
            || Target.NativeModule != new Guid("60e20675-8e5a-4ad9-9b5a-c31f273b85fc")
            || Target.Controller != "River" || Target.Clip != "LavaRiseAndFall"
            || Target.Duration != 71.75f || Target.FrameRate != 60f)
            throw new InvalidOperationException("PEAK's Caldera tide is unsupported. Update PLL_Library.");
        return 785.26691f;
    }

    internal static IReadOnlyList<LayoutPort> CreatePeakExit(
        Transform container,
        Transform summit,
        Pose bridgeContact,
        out Vector3 progressionAxis)
    {
        if (!container) throw new ArgumentNullException(nameof(container));
        if (!summit) throw new ArgumentNullException(nameof(summit));
        progressionAxis = HorizontalDirection(
            container.TransformPoint(bridgeContact.position), summit.position, "Peak");
        return new[] { Port(DirectionalPose(container, summit, progressionAxis)) };
    }

    internal static PeakBridgeProfile CreatePeakBridge(
        Transform container, Transform peak, global::Peak.ScoutStatue Statue)
    {
        if (!container) throw new ArgumentNullException(nameof(container));
        if (!peak) throw new ArgumentNullException(nameof(peak));
        if (!Statue || Statue.transform.parent != peak || !Statue.gemSpot
            || !TransformOwnership.Contains(Statue.transform, Statue.gemSpot))
            throw new InvalidOperationException("PEAK's Peak Bridge statue has no owned gem anchor.");
        Transform? bridge = null;
        for (var i = 0; i < peak.childCount; i++)
        {
            var child = peak.GetChild(i);
            if (!child.name.Equals("Peak Bridge", StringComparison.Ordinal)) continue;
            if (bridge)
                throw new InvalidOperationException("PEAK's Peak attachment owns multiple Peak Bridge children.");
            bridge = child;
        }
        if (!bridge)
            throw new InvalidOperationException("PEAK's Peak attachment has no direct Peak Bridge child.");

        var filters = bridge.GetComponents<MeshFilter>();
        var renderers = bridge.GetComponents<MeshRenderer>();
        var colliders = bridge.GetComponents<MeshCollider>();
        if (bridge.childCount != 0 || filters.Length != 1 || renderers.Length != 1
            || colliders.Length != 1 || bridge.GetComponents<MonoBehaviour>().Length != 0)
            throw new InvalidOperationException("PEAK's Peak Bridge component contract is ambiguous.");
        var mesh = filters[0].sharedMesh;
        if (!mesh || colliders[0].sharedMesh != mesh
            || !mesh.name.Equals("Peak Bridge", StringComparison.Ordinal) || !mesh.isReadable)
            throw new InvalidOperationException("PEAK's Peak Bridge mesh contract is invalid.");

        var vertices = mesh.vertices;
        if (vertices.Length == 0)
            throw new InvalidOperationException("PEAK's Peak Bridge mesh has no vertices.");
        var minZ = float.PositiveInfinity;
        var maxZ = float.NegativeInfinity;
        foreach (var vertex in vertices)
        {
            if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                throw new InvalidOperationException("PEAK's Peak Bridge mesh contains a non-finite vertex.");
            minZ = Math.Min(minZ, vertex.z);
            maxZ = Math.Max(maxZ, vertex.z);
        }
        if (minZ >= maxZ)
            throw new InvalidOperationException("PEAK's Peak Bridge longitudinal bounds are degenerate.");

        var minLower = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var minUpper = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var maxLower = minLower;
        var maxUpper = minUpper;
        var minVertices = 0;
        var maxVertices = 0;
        foreach (var vertex in vertices)
        {
            if (vertex.z == minZ)
            {
                minLower = Vector2.Min(minLower, vertex);
                minUpper = Vector2.Max(minUpper, vertex);
                minVertices++;
            }
            else if (vertex.z == maxZ)
            {
                maxLower = Vector2.Min(maxLower, vertex);
                maxUpper = Vector2.Max(maxUpper, vertex);
                maxVertices++;
            }
        }
        var minimumBoundary = minVertices >= 2 && minLower.x < minUpper.x && minLower.y < minUpper.y;
        var maximumBoundary = maxVertices >= 2 && maxLower.x < maxUpper.x && maxLower.y < maxUpper.y;
        if (minimumBoundary == maximumBoundary)
            throw new InvalidOperationException("PEAK's Peak Bridge far boundary is ambiguous.");
        var lower = minimumBoundary ? minLower : maxLower;
        var upper = minimumBoundary ? minUpper : maxUpper;
        var faceCenter = new Vector3(
            (float)(((double)lower.x + upper.x) * 0.5),
            (float)(((double)lower.y + upper.y) * 0.5),
            minimumBoundary ? minZ : maxZ);
        var exitRotation = Quaternion.Inverse(container.rotation);
        var faceCenterWorld = bridge.TransformPoint(faceCenter);
        var forward = ((container.rotation * exitRotation) * Vector3.forward).normalized;
        var forwardExtent = float.NegativeInfinity;
        foreach (var vertex in vertices)
            if (vertex.z == faceCenter.z)
                forwardExtent = Math.Max(forwardExtent, Vector3.Dot(bridge.TransformPoint(vertex) - faceCenterWorld, forward));
        if (!float.IsFinite(forwardExtent) || forwardExtent <= 0f)
            throw new InvalidOperationException("PEAK's Peak Bridge contact extent is invalid.");
        var exit = Port(new Pose(container.InverseTransformPoint(faceCenterWorld), exitRotation));
        var contactWorld = faceCenterWorld + forward * forwardExtent;
        var contact = new Pose(container.InverseTransformPoint(contactWorld), exitRotation);
        var transverse = ((container.rotation * exitRotation) * Vector3.right).normalized;
        var up = Vector3.Cross(forward, transverse).normalized;
        var geometry = new NativeMeshGeometry(vertices, mesh.triangles, bridge.localToWorldMatrix);
        var coordinate = Vector3.Dot(transverse, contactWorld);
        if (!geometry.TryUniqueGroundableBoundaryPoint(
                transverse, coordinate, -forward, up, out var ownerBoundaryWorld))
            throw new InvalidOperationException(
                "PEAK's Peak Bridge has no unique groundable center-transverse owner boundary.");
        if (!geometry.TryUniqueNearestGroundableIntersection(
                contactWorld, Vector3.up, Vector3.up, out var successorBoundaryWorld))
            throw new InvalidOperationException(
                "PEAK's Peak Bridge has no unique nearest groundable successor boundary through its contact.");
        if (Vector3.Dot(successorBoundaryWorld - ownerBoundaryWorld, forward) <= 0f)
            throw new InvalidOperationException("PEAK's Peak Bridge owner and successor boundaries are not distinct.");
        var Front = HorizontalDirection(Statue.transform.position, Statue.gemSpot.position, "Peak statue");
        var FrontTransverse = Vector3.Cross(Vector3.up, Front).normalized;
        if (!geometry.TryUniqueGroundableBoundaryPoint(
                FrontTransverse, Vector3.Dot(FrontTransverse, Statue.transform.position),
                Front, Vector3.up, out var FrontBoundary)
            || Vector3.Dot(FrontBoundary - Statue.transform.position, Front) <= 0f)
            throw new InvalidOperationException("PEAK's Peak Bridge has no unique groundable boundary ahead of its statue.");
        var CampfireLine = (Statue.transform.position + FrontBoundary) * 0.5f;
        if (!geometry.TryUniqueNearestGroundableIntersection(
                CampfireLine, Vector3.up, Vector3.up, out var CampfireWorld))
            throw new InvalidOperationException(
                "PEAK's Peak Bridge has no unique groundable campfire point ahead of its statue.");
        var ownerBoundary = new Pose(container.InverseTransformPoint(ownerBoundaryWorld), exitRotation);
        var successorBoundary = new LayoutPort(
            exit.Connector, new Pose(container.InverseTransformPoint(successorBoundaryWorld), exitRotation));
        var BridgeCampfire = new Pose(container.InverseTransformPoint(CampfireWorld),
            Quaternion.Inverse(container.rotation) * Quaternion.LookRotation(Front, Vector3.up));
        var ContactOffset = colliders[0].contactOffset;
        if (!float.IsFinite(ContactOffset) || ContactOffset < 0f)
            throw new InvalidOperationException("PEAK's Peak Bridge contact offset is invalid.");
        var WalkingClearance = Math.Max(0f,
            successorBoundaryWorld.y - geometry.MinimumGroundableWorldY() + ContactOffset);
        return new PeakBridgeProfile(
            bridge, exit, contact, ownerBoundary, successorBoundary, BridgeCampfire, WalkingClearance);
    }

    internal static NativePortProfile CreateNadir(
        MapHandler.MapSegment source,
        Transform container,
        Transform gate,
        out LayoutPort peakSuccessorEntry)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!container) throw new ArgumentNullException(nameof(container));
        if (!gate) throw new ArgumentNullException(nameof(gate));
        var reconnect = source.reconnectSpawnPos
            ? source.reconnectSpawnPos
            : throw new InvalidOperationException("PEAK's native Nadir reconnect anchor is unavailable.");
        if (!TransformOwnership.Contains(container, reconnect) || !TransformOwnership.Contains(container, gate))
            throw new InvalidOperationException("PEAK's native Nadir ports are outside its segment container.");
        var peakSuccessorAxis = HorizontalDirection(reconnect.position, gate.position, "Nadir");
        peakSuccessorEntry = Port(DirectionalPose(container, reconnect, peakSuccessorAxis));
        return new NativePortProfile(
            LocalPose(container, reconnect),
            new[] { Port(TranslationPose(container, reconnect)) },
            new[] { Port(TranslationPose(container, gate)) });
    }

    internal static Transform CreateNadirBridge(Transform nadirPeak)
    {
        if (!nadirPeak) throw new ArgumentNullException(nameof(nadirPeak));
        Transform? bridge = null;
        for (var i = 0; i < nadirPeak.childCount; i++)
        {
            var child = nadirPeak.GetChild(i);
            if (!child.name.Equals("Peak Bridge.001", StringComparison.Ordinal)) continue;
            if (bridge)
                throw new InvalidOperationException("PEAK's Nadir Peak fixture owns multiple Peak Bridge.001 children.");
            bridge = child;
        }
        if (!bridge)
            throw new InvalidOperationException("PEAK's Nadir Peak fixture has no direct Peak Bridge.001 child.");

        var filters = bridge.GetComponents<MeshFilter>();
        var renderers = bridge.GetComponents<MeshRenderer>();
        var colliders = bridge.GetComponents<MeshCollider>();
        if (bridge.childCount != 0 || bridge.GetComponents<Component>().Length != 4
            || filters.Length != 1 || renderers.Length != 1 || colliders.Length != 1)
            throw new InvalidOperationException("PEAK's Nadir Peak Bridge component contract is ambiguous.");
        var collider = colliders[0];
        var mesh = filters[0].sharedMesh;
        if (!mesh || collider.sharedMesh != mesh || !mesh.name.Equals("Peak Bridge.001", StringComparison.Ordinal)
            || !mesh.isReadable || mesh.vertexCount == 0 || !collider.enabled || collider.isTrigger || collider.convex
            || collider.attachedRigidbody || !NativePlacement.ActiveWithin(bridge, nadirPeak)
            || (HelperFunctions.terrainMapMask.value & (1 << bridge.gameObject.layer)) == 0)
            throw new InvalidOperationException("PEAK's Nadir Peak Bridge mesh contract is invalid.");
        return bridge;
    }

    internal static Pose LocalPose(Transform container, Transform value)
    {
        if (!container) throw new ArgumentNullException(nameof(container));
        if (!value) throw new ArgumentNullException(nameof(value));
        return new Pose(
            container.InverseTransformPoint(value.position),
            Quaternion.Inverse(container.rotation) * value.rotation);
    }

    internal static void ValidateSource(
        MapHandler handler,
        string name,
        Transform container,
        Transform segmentRoot,
        Transform? campfireRoot)
    {
        if (!handler || !handler.transform || !container || !segmentRoot
            || !TransformOwnership.Contains(container, segmentRoot) || !TransformOwnership.Contains(handler.transform, container)
            || campfireRoot && !TransformOwnership.Contains(container, campfireRoot))
            throw new InvalidOperationException($"PEAK's native {name} container/root ownership is invalid.");
    }

    static Transform Reconnect(MapHandler.MapSegment source, int role) =>
        source.reconnectSpawnPos
            ? source.reconnectSpawnPos
            : throw new InvalidOperationException($"PEAK's native segment role {role} has no reconnect anchor.");

    static LayoutPort Port(Pose pose) => new(PLLLibrary.LayoutConnector, pose);

    static Transform FogOrigin(GameObject wall, Segment role, string boundary)
    {
        FogSphereOrigin? result = null;
        for (var value = wall.transform; value; value = value.parent)
        {
            var origins = value.GetComponents<FogSphereOrigin>();
            if (origins.Length == 0) continue;
            if (origins.Length != 1 || result)
                throw new InvalidOperationException($"PEAK's native {role} {boundary} fog origin is ambiguous.");
            result = origins[0];
        }
        return result
            ? result.transform
            : throw new InvalidOperationException($"PEAK's native {role} {boundary} fog origin is unavailable.");
    }

    static Pose TranslationPose(Transform container, Transform value) => new(
        container.InverseTransformPoint(value.position),
        Quaternion.Inverse(container.rotation));

    static Pose DirectionalPose(Transform container, Transform value, Vector3 worldAxis) => new(
        container.InverseTransformPoint(value.position),
        Quaternion.Inverse(container.rotation) * Quaternion.LookRotation(worldAxis, Vector3.up));

    static Vector3 HorizontalDirection(Vector3 from, Vector3 to, string name)
    {
        var direction = to - from;
        direction.y = 0f;
        if (!float.IsFinite(direction.x) || !float.IsFinite(direction.z) || direction.sqrMagnitude <= 0f)
            throw new InvalidOperationException($"PEAK's native {name} progression direction is invalid.");
        return direction.normalized;
    }

}
