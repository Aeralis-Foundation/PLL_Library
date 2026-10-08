using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal static class NativePlacement
{
    static readonly float[] FormationSamples = { 0.25f, 0.5f, 0.75f };

    enum ColliderCopyKind : byte
    {
        Unsupported,
        Mesh,
        Box,
        Sphere,
        Capsule,
        Terrain
    }

    sealed class StartSurface
    {
        readonly MeshCollider source;
        GameObject? proxyObject;
        Collider? proxy;

        internal StartSurface(MeshCollider source, Bounds bounds)
        {
            this.source = source;
            Bounds = bounds;
        }

        internal Bounds Bounds { get; }

        internal bool Raycast(Ray ray, out RaycastHit hit, float distance)
        {
            if (source.gameObject.activeInHierarchy) return source.Raycast(ray, out hit, distance);
            if (!proxy)
            {
                var value = proxyObject = new GameObject("PLL Native Start Surface");
                try
                {
                    value.hideFlags = HideFlags.HideAndDontSave;
                    value.layer = source.gameObject.layer;
                    value.SetActive(false);
                    value.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    value.transform.localScale = source.transform.lossyScale;
                    proxy = CopyCollider(source, value);
                    value.SetActive(true);
                    Physics.SyncTransforms();
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(value);
                    throw;
                }
            }
            return proxy!.Raycast(ray, out hit, distance);
        }

        internal void Destroy()
        {
            if (proxyObject) UnityEngine.Object.DestroyImmediate(proxyObject);
            proxyObject = null;
            proxy = null;
        }
    }

    sealed class StartSupport
    {
        readonly List<StartSurface> surfaces = new();

        internal StartSupport(StartSurface surface, int order)
        {
            Bounds = surface.Bounds;
            Order = order;
            surfaces.Add(surface);
        }

        internal Bounds Bounds { get; private set; }
        internal int Order { get; }

        internal void Add(StartSurface surface)
        {
            var bounds = Bounds;
            bounds.Encapsulate(surface.Bounds);
            Bounds = bounds;
            surfaces.Add(surface);
        }

        internal bool Raycast(Ray ray, float distance, out RaycastHit result)
        {
            result = default;
            var found = false;
            foreach (var surface in surfaces)
            {
                var bounds = surface.Bounds;
                if (ray.origin.x < bounds.min.x || ray.origin.x > bounds.max.x
                    || ray.origin.z < bounds.min.z || ray.origin.z > bounds.max.z
                    || !surface.Raycast(ray, out var hit, distance)
                    || Vector3.Dot(hit.normal, Vector3.up) <= 0.5f
                    || found && hit.point.y <= result.point.y) continue;
                result = hit;
                found = true;
            }
            return found;
        }
    }

    internal sealed class StartFormation
    {
        readonly int[] indices;
        readonly Vector3[] offsets;

        internal StartFormation(Pose start, int[] indices, Vector3[] offsets)
        {
            Start = start;
            this.indices = indices;
            this.offsets = offsets;
        }

        internal Pose Start { get; }

        internal void Place(Transform spawnRoot, Pose target)
        {
            if (!spawnRoot) throw new ArgumentNullException(nameof(spawnRoot));
            var spawns = spawnRoot.GetComponentsInChildren<SpawnPoint>(true);
            Array.Sort(spawns, (left, right) => left.index.CompareTo(right.index));
            if (spawns.Length != indices.Length)
                throw new InvalidOperationException("PEAK's placed native start formation does not match its resolved SpawnPoints.");
            for (var i = 0; i < spawns.Length; i++)
            {
                if (spawns[i].index != indices[i])
                    throw new InvalidOperationException("PEAK's placed native start formation has different SpawnPoint indices.");
                spawns[i].transform.position = target.position + target.rotation * offsets[i];
            }
        }
    }

    internal sealed class ResolvedLayoutStart
    {
        internal ResolvedLayoutStart(Pose Pose, StartFormation? NativeFormation)
        {
            this.Pose = Pose;
            this.NativeFormation = NativeFormation;
        }

        internal Pose Pose { get; }
        internal StartFormation? NativeFormation { get; }
    }

    internal static ResolvedLayoutStart ResolveLayoutStart(
        LayoutSegmentSource Source, Transform SpawnRoot, Transform PrimarySpawn)
    {
        if (Source is PeakAttachmentSource)
            throw new InvalidOperationException("Peak cannot begin a layout.");
        if (Source.Start is { } Start) return new ResolvedLayoutStart(Start, null);
        if (Source is not NativeOrdinarySegmentSource)
            throw new InvalidOperationException($"Segment '{Source.Id}' has no explicit Start port.");
        var Formation = ResolveStart(Source, SpawnRoot, PrimarySpawn);
        return new ResolvedLayoutStart(Formation.Start, Formation);
    }

    internal static StartFormation ResolveStart(
        LayoutSegmentSource Source,
        Transform spawnRoot,
        Transform primarySpawn)
    {
        Physics.SyncTransforms();
        if (!spawnRoot || !primarySpawn || !TransformOwnership.Contains(spawnRoot, primarySpawn))
            throw new InvalidOperationException("PEAK's native start formation is unavailable.");
        var spawns = spawnRoot.GetComponentsInChildren<SpawnPoint>(true);
        Array.Sort(spawns, (left, right) => left.index.CompareTo(right.index));
        if (spawns.Length != 4 || spawns[0].transform != primarySpawn)
            throw new InvalidOperationException("PEAK's native four-player start formation is invalid.");
        for (var i = 1; i < spawns.Length; i++)
            if (spawns[i - 1].index == spawns[i].index)
                throw new InvalidOperationException("PEAK's native start formation has duplicate SpawnPoint indices.");

        var offsets = new Vector3[spawns.Length];
        var minimumOffset = Vector3.zero;
        var maximumOffset = Vector3.zero;
        for (var i = 0; i < spawns.Length; i++)
        {
            offsets[i] = spawns[i].transform.position - primarySpawn.position;
            minimumOffset = Vector3.Min(minimumOffset, offsets[i]);
            maximumOffset = Vector3.Max(maximumOffset, offsets[i]);
        }

        var Root = Source.SegmentParent.transform;
        var terrainMask = HelperFunctions.terrainMapMask.value;
        var surfaces = new List<StartSurface>();
        var supports = new List<StartSupport>();
        var supportByOwner = new Dictionary<Transform, StartSupport>();

        try
        {
            var order = 0;
            foreach (var Collider in Root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!Collider || !Collider.enabled || Collider.isTrigger || Collider.convex || !Collider.sharedMesh
                    || Collider.attachedRigidbody || !ActiveWithin(Collider.transform, Root)
                    || NativeCollision.IsGeneratedOutput(Root, Collider.transform)
                    || (terrainMask & (1 << Collider.gameObject.layer)) == 0
                    || !TryWorldBounds(Collider, out var bounds) || bounds.size.sqrMagnitude <= 0f) continue;
                var Surface = new StartSurface(Collider, bounds);
                surfaces.Add(Surface);
                var Owner = SupportOwner(Root, Collider.transform);
                if (supportByOwner.TryGetValue(Owner, out var support)) support.Add(Surface);
                else
                {
                    support = new StartSupport(Surface, order++);
                    supportByOwner.Add(Owner, support);
                    supports.Add(support);
                }
            }
            if (supports.Count == 0)
                throw new InvalidOperationException($"PEAK's native {Source.Id} has no usable Terrain/Map collision surface for its Start port.");

            var width = maximumOffset.x - minimumOffset.x;
            var depth = maximumOffset.z - minimumOffset.z;
            supports.RemoveAll(value => value.Bounds.size.x < width || value.Bounds.size.z < depth);
            if (supports.Count == 0)
                throw new InvalidOperationException($"PEAK's native {Source.Id} has no collision surface large enough for its four-player Start formation.");
            var Entry = Source.AuthoredPosition(Source.EntryPorts[0].Pose);
            supports.Sort((left, right) =>
            {
                var Compared = BoundsSquaredDistance(Entry, left.Bounds).CompareTo(
                    BoundsSquaredDistance(Entry, right.Bounds));
                return Compared != 0 ? Compared : left.Order.CompareTo(right.Order);
            });

            StartFormation? result = null;
            var squaredDistance = double.PositiveInfinity;
            var ambiguous = false;
            foreach (var support in supports)
            {
                if (BoundsSquaredDistance(Entry, support.Bounds) > squaredDistance) break;
                if (!TryFormation(
                        Source, support, surfaces, spawns, primarySpawn, offsets, minimumOffset, maximumOffset,
                        Entry, out var formation, out var candidateSquaredDistance)
                    || candidateSquaredDistance > squaredDistance) continue;
                if (candidateSquaredDistance < squaredDistance)
                {
                    result = formation;
                    squaredDistance = candidateSquaredDistance;
                    ambiguous = false;
                }
                else if (result is not null) ambiguous = true;
            }
            if (result is null)
                throw new InvalidOperationException($"PEAK's native {Source.Id} has no grounded four-player Start formation.");
            if (ambiguous)
                throw new InvalidOperationException($"PEAK's native {Source.Id} has multiple equally near Start support surfaces.");
            return result;
        }
        finally
        {
            foreach (var surface in surfaces) surface.Destroy();
        }
    }

    static bool TryFormation(
        LayoutSegmentSource Source,
        StartSupport support,
        List<StartSurface> surfaces,
        SpawnPoint[] spawns,
        Transform primarySpawn,
        Vector3[] sourceOffsets,
        Vector3 minimumOffset,
        Vector3 maximumOffset,
        Vector3 entry,
        out StartFormation result,
        out double squaredDistance)
    {
        var clearance = Math.Max(Physics.defaultContactOffset, 0f);
        var bounds = support.Bounds;
        var distance = bounds.size.y + clearance * 2f;
        var top = bounds.max.y + clearance;
        var hits = new RaycastHit[sourceOffsets.Length];
        var bestHits = new RaycastHit[sourceOffsets.Length];
        squaredDistance = double.PositiveInfinity;
        foreach (var z in FormationSamples)
            foreach (var x in FormationSamples)
            {
                var center = new Vector3(
                    Mathf.Lerp(bounds.min.x - minimumOffset.x, bounds.max.x - maximumOffset.x, x),
                    top,
                    Mathf.Lerp(bounds.min.z - minimumOffset.z, bounds.max.z - maximumOffset.z, z));
                var valid = true;
                for (var i = 0; i < sourceOffsets.Length; i++)
                {
                    var origin = center + new Vector3(sourceOffsets[i].x, 0f, sourceOffsets[i].z);
                    if (support.Raycast(new Ray(origin, Vector3.down), distance, out hits[i])) continue;
                    valid = false;
                    break;
                }
                if (!valid) continue;
                if (HasHorizontalHit(hits, surfaces)) continue;
                var candidateSquaredDistance = SquaredDistance(entry, hits[0].point);
                if (candidateSquaredDistance >= squaredDistance) continue;
                squaredDistance = candidateSquaredDistance;
                Array.Copy(hits, bestHits, hits.Length);
            }
        if (double.IsPositiveInfinity(squaredDistance))
        {
            result = null!;
            return false;
        }

        var primary = bestHits[0].point;
        var inverseRotation = Quaternion.Inverse(primarySpawn.rotation);
        var indices = new int[spawns.Length];
        var offsets = new Vector3[spawns.Length];
        for (var i = 0; i < spawns.Length; i++)
        {
            indices[i] = spawns[i].index;
            offsets[i] = inverseRotation * (bestHits[i].point - primary);
        }
        result = new StartFormation(
            new Pose(
                Source.Container.InverseTransformPoint(primary),
                Quaternion.Inverse(Source.Container.rotation) * primarySpawn.rotation),
            indices,
            offsets);
        return true;
    }

    static bool HasHorizontalHit(RaycastHit[] hits, List<StartSurface> surfaces)
    {
        foreach (var hit in hits)
        {
            var origin = hit.point + Vector3.up * 1f;
            for (var z = -1; z <= 1; z++)
                for (var x = -1; x <= 1; x++)
                {
                    if (x == 0 && z == 0) continue;
                    var ray = new Ray(origin, new Vector3(x, 0f, z).normalized);
                    foreach (var surface in surfaces)
                    {
                        if (!surface.Bounds.IntersectRay(ray, out var boundsDistance) || boundsDistance > 1f) continue;
                        if (surface.Raycast(ray, out _, 1f)) return true;
                    }
                }
        }
        return false;
    }

    static Transform SupportOwner(Transform root, Transform value)
    {
        for (var current = value; current != root; current = current.parent)
        {
            if (!current)
                throw new InvalidOperationException("PEAK's native Start surface is outside its segment root.");
            if (current.GetComponent<Ashley.MeshSplitter.MeshSplitter>()) return current;
        }
        return value;
    }

    internal static double BoundsSquaredDistance(Vector3 Point, Bounds BoundsValue)
    {
        static double Gap(float Value, float Minimum, float Maximum) =>
            Value < Minimum ? Minimum - Value : Value > Maximum ? Value - Maximum : 0d;
        var X = Gap(Point.x, BoundsValue.min.x, BoundsValue.max.x);
        var Y = Gap(Point.y, BoundsValue.min.y, BoundsValue.max.y);
        var Z = Gap(Point.z, BoundsValue.min.z, BoundsValue.max.z);
        return X * X + Y * Y + Z * Z;
    }

    internal static double SquaredDistance(Vector3 Left, Vector3 Right)
    {
        var X = (double)Left.x - Right.x;
        var Y = (double)Left.y - Right.y;
        var Z = (double)Left.z - Right.z;
        return X * X + Y * Y + Z * Z;
    }

    internal static bool CanCopyCollider(Collider Value) =>
        CopyKind(Value) != ColliderCopyKind.Unsupported;

    internal static Collider CopyCollider(Collider source, GameObject target)
    {
        switch (CopyKind(source))
        {
            case ColliderCopyKind.Mesh:
                var mesh = (MeshCollider)source;
                var meshCopy = target.AddComponent<MeshCollider>();
                meshCopy.cookingOptions = mesh.cookingOptions;
                meshCopy.convex = mesh.convex;
                meshCopy.sharedMesh = mesh.sharedMesh;
                return meshCopy;
            case ColliderCopyKind.Box:
                var box = (BoxCollider)source;
                var boxCopy = target.AddComponent<BoxCollider>();
                boxCopy.center = box.center;
                boxCopy.size = box.size;
                return boxCopy;
            case ColliderCopyKind.Sphere:
                var sphere = (SphereCollider)source;
                var sphereCopy = target.AddComponent<SphereCollider>();
                sphereCopy.center = sphere.center;
                sphereCopy.radius = sphere.radius;
                return sphereCopy;
            case ColliderCopyKind.Capsule:
                var capsule = (CapsuleCollider)source;
                var capsuleCopy = target.AddComponent<CapsuleCollider>();
                capsuleCopy.center = capsule.center;
                capsuleCopy.radius = capsule.radius;
                capsuleCopy.height = capsule.height;
                capsuleCopy.direction = capsule.direction;
                return capsuleCopy;
            case ColliderCopyKind.Terrain:
                var terrain = (TerrainCollider)source;
                var terrainCopy = target.AddComponent<TerrainCollider>();
                terrainCopy.terrainData = terrain.terrainData;
                return terrainCopy;
            default:
                throw new InvalidOperationException(
                    $"PEAK's native collider '{source.GetType().FullName}' on '{source.name}' cannot be mirrored for deterministic Start placement.");
        }
    }

    internal static bool TryWorldBounds(Collider collider, out Bounds bounds)
    {
        if (collider.gameObject.activeInHierarchy)
        {
            bounds = collider.bounds;
            return Valid(bounds);
        }
        if (collider is SphereCollider sphere) return TryWorldBounds(sphere, out bounds);
        if (collider is CapsuleCollider capsule) return TryWorldBounds(capsule, out bounds);
        if (!TryLocalBounds(collider, out var local))
        {
            bounds = default;
            return false;
        }

        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var center = local.center;
        var extent = local.extents;
        for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var point = collider.transform.TransformPoint(
                        center + Vector3.Scale(extent, new Vector3(x, y, z)));
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
        bounds = new Bounds();
        bounds.SetMinMax(minimum, maximum);
        return Valid(bounds);
    }

    static bool TryWorldBounds(SphereCollider collider, out Bounds bounds)
    {
        var scale = Abs(collider.transform.lossyScale);
        var radius = Math.Abs(collider.radius) * Math.Max(scale.x, Math.Max(scale.y, scale.z));
        bounds = new Bounds(collider.transform.TransformPoint(collider.center), Vector3.one * (radius * 2f));
        return Valid(bounds);
    }

    static bool TryWorldBounds(CapsuleCollider collider, out Bounds bounds)
    {
        var scale = Abs(collider.transform.lossyScale);
        var direction = collider.direction;
        var radius = Math.Abs(collider.radius)
            * Math.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
        var axis = Vector3.zero;
        axis[direction] = 1f;
        axis = Abs(collider.transform.TransformDirection(axis).normalized);
        var halfSegment = Math.Max(Math.Abs(collider.height) * scale[direction] * 0.5f - radius, 0f);
        var extents = Vector3.one * radius + axis * halfSegment;
        bounds = new Bounds(collider.transform.TransformPoint(collider.center), extents * 2f);
        return Valid(bounds);
    }

    static bool TryLocalBounds(Collider collider, out Bounds bounds)
    {
        switch (CopyKind(collider))
        {
            case ColliderCopyKind.Mesh:
                bounds = ((MeshCollider)collider).sharedMesh.bounds;
                return Valid(bounds);
            case ColliderCopyKind.Box:
                var box = (BoxCollider)collider;
                bounds = new Bounds(box.center, box.size);
                return Valid(bounds);
            case ColliderCopyKind.Sphere:
                var sphere = (SphereCollider)collider;
                bounds = new Bounds(
                    sphere.center, Vector3.one * (sphere.radius * 2f));
                return Valid(bounds);
            case ColliderCopyKind.Capsule:
                var capsule = (CapsuleCollider)collider;
                var size = Vector3.one * (capsule.radius * 2f);
                size[capsule.direction] = Math.Max(
                    capsule.height, capsule.radius * 2f);
                bounds = new Bounds(capsule.center, size);
                return Valid(bounds);
            case ColliderCopyKind.Terrain:
                bounds = ((TerrainCollider)collider).terrainData.bounds;
                return Valid(bounds);
            default:
                bounds = default;
                return false;
        }
    }

    static ColliderCopyKind CopyKind(Collider Value) => Value switch
    {
        MeshCollider Mesh when Mesh.sharedMesh => ColliderCopyKind.Mesh,
        BoxCollider => ColliderCopyKind.Box,
        SphereCollider => ColliderCopyKind.Sphere,
        CapsuleCollider => ColliderCopyKind.Capsule,
        TerrainCollider Terrain when Terrain.terrainData => ColliderCopyKind.Terrain,
        _ => ColliderCopyKind.Unsupported
    };

    static bool Valid(Bounds Value) =>
        UnityValueContract.Finite(Value.center)
        && UnityValueContract.Finite(Value.extents)
        && Value.extents.x >= 0f && Value.extents.y >= 0f && Value.extents.z >= 0f;

    internal static Vector3 Abs(Vector3 Value) =>
        new(Math.Abs(Value.x), Math.Abs(Value.y), Math.Abs(Value.z));

    internal static bool ActiveWithin(Transform value, Transform root)
    {
        while (value != root)
        {
            if (!value || !value.gameObject.activeSelf) return false;
            value = value.parent;
        }
        return true;
    }
}
