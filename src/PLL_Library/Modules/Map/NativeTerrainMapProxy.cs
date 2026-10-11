using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeTerrainMapProxy : IDisposable
{
    internal readonly struct LocalTransform
    {
        internal LocalTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        internal Vector3 Position { get; }
        internal Quaternion Rotation { get; }
        internal Vector3 Scale { get; }
    }

    internal readonly struct ExternalSupport
    {
        internal ExternalSupport(Collider source, Transform frame)
        {
            Source = source;
            FrameMatrix = frame.localToWorldMatrix;
            SourceMatrix = source.transform.localToWorldMatrix;
            var path = new List<Transform>();
            for (var current = source.transform; current; current = current.parent)
            {
                path.Add(current);
                var relative = frame.worldToLocalMatrix * current.localToWorldMatrix;
                var position = (Vector3)relative.GetColumn(3);
                var rotation = relative.rotation;
                var scale = relative.lossyScale;
                if (!Usable(position, rotation, scale)
                    || !Same(relative, Matrix4x4.TRS(position, rotation, scale))) continue;
                var transforms = new LocalTransform[path.Count];
                transforms[0] = new LocalTransform(position, rotation, scale);
                for (var i = path.Count - 2; i >= 0; i--)
                {
                    var value = path[i];
                    if (!Usable(value.localPosition, value.localRotation, value.localScale))
                        throw new InvalidOperationException(
                            "The native external campfire support collider has an invalid transform.");
                    transforms[path.Count - i - 1] =
                        new LocalTransform(value.localPosition, value.localRotation, value.localScale);
                }
                Transforms = transforms;
                return;
            }
            throw new InvalidOperationException(
                "The native external campfire support collider cannot be represented in its segment frame.");
        }

        internal Collider Source { get; }
        internal Matrix4x4 FrameMatrix { get; }
        internal Matrix4x4 SourceMatrix { get; }
        internal LocalTransform[] Transforms { get; }
    }

    static Collider? activeExclusion;
    static Transform? activeSpawnerRoot;
    GameObject? root;
    bool ownsExclusion;

    internal NativeTerrainMapProxy(
        Transform sourceRoot,
        Transform container,
        Transform spawnerRoot,
        ExternalSupport externalSupport)
    {
        if (!sourceRoot || !container || sourceRoot != container && !sourceRoot.IsChildOf(container))
            throw new InvalidOperationException("Native Terrain/Map collision cannot be mirrored outside its placed container.");
        if (!spawnerRoot || spawnerRoot != container && !spawnerRoot.IsChildOf(container))
            throw new InvalidOperationException("Native campfire generation left its placed container.");

        var value = new GameObject("PLL Native Terrain Map Proxy");
        root = value;
        try
        {
            value.hideFlags = HideFlags.HideAndDontSave;
            value.SetActive(false);
            value.transform.SetParent(container, false);
            var mirrors = new Dictionary<Transform, Transform> { [container] = value.transform };
            var copies = new List<Collider>();
            var terrainMask = HelperFunctions.terrainMapMask.value;
            var external = CopyExternal(externalSupport, value.transform);
            foreach (var collider in sourceRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!collider || !collider.enabled || collider.isTrigger
                    || collider.attachedRigidbody
                    || !NativePlacement.ActiveWithin(collider.transform, sourceRoot)
                    || NativeCollision.IsGeneratedOutput(sourceRoot, collider.transform)
                    || (terrainMask & (1 << collider.gameObject.layer)) == 0
                    || !NativePlacement.CanCopyCollider(collider)
                    || !NativePlacement.TryWorldBounds(collider, out var bounds) || !Nonempty(bounds)) continue;
                var target = Mirror(collider.transform, container, mirrors).gameObject;
                var copy = NativePlacement.CopyCollider(collider, target);
                copy.enabled = true;
                copies.Add(copy);
            }
            if (!external && copies.Count == 0)
                throw new InvalidOperationException("The native support root has no usable static Terrain/Map collision.");

            value.SetActive(true);
            Physics.SyncTransforms();
            if (external && (!external.enabled || !external.gameObject.activeInHierarchy
                || !NativePlacement.TryWorldBounds(external, out var externalBounds) || !Nonempty(externalBounds)))
                throw new InvalidOperationException("A native Terrain/Map collider proxy produced invalid collision bounds.");
            foreach (var copy in copies)
                if (!copy || !copy.enabled || !copy.gameObject.activeInHierarchy
                    || !NativePlacement.TryWorldBounds(copy, out var bounds) || !Nonempty(bounds))
                    throw new InvalidOperationException("A native Terrain/Map collider proxy produced invalid collision bounds.");
            NativePropSpawnerGeneration.RegisterExternalSupports(
                spawnerRoot, value.transform, external ? external : externalSupport.Source, copies);
            if (external)
            {
                if (activeExclusion || activeSpawnerRoot)
                    throw new InvalidOperationException("Native external support filtering overlapped another generation scope.");
                activeExclusion = externalSupport.Source;
                activeSpawnerRoot = spawnerRoot;
                ownsExclusion = true;
            }
        }
        catch
        {
            ReleaseExclusion();
            if (value) UnityEngine.Object.DestroyImmediate(value);
            root = null;
            Physics.SyncTransforms();
            throw;
        }
    }

    internal static ExternalSupport ResolveExternalSupport(
        LayoutSegmentSource Source,
        IReadOnlyCollection<Transform> authoredContainers)
    {
        var Frame = Source.Container;
        var Root = Source.SegmentCampfire;
        if (!Frame || !Root || Root.transform != Frame && !Root.transform.IsChildOf(Frame))
            throw new InvalidOperationException(
                $"PEAK's native {Source.Id} campfire root is outside its authored segment container.");

        PropSpawner? spawner = null;
        foreach (var Candidate in Root.GetComponentsInChildren<PropSpawner>(true))
        {
            if (!Candidate || Candidate.GetType() != typeof(PropSpawner)
                || !NativePlacement.ActiveWithin(Candidate.transform, Root.transform)
                || !SpawnsCampfire(Candidate)) continue;
            if (spawner)
                throw new InvalidOperationException(
                    $"PEAK's native {Source.Id} campfire root has multiple deterministic Campfire spawners.");
            spawner = Candidate;
        }
        if (!spawner || !spawner.raycastPosition
            || spawner.area.x != 0f || spawner.area.y != 0f)
            throw new InvalidOperationException(
                $"PEAK's native {Source.Id} Campfire spawner has no deterministic position ray.");

        var origin = spawner.transform.position;
        var forward = spawner.transform.forward;
        var direction =
            (forward + forward * spawner.rayNearCutoff + spawner.rayDirectionOffset).normalized;
        var length = spawner.rayLength - spawner.rayNearCutoff;
        if (!UnityValueContract.Finite(origin)
            || !UnityValueContract.Finite(direction) || direction.sqrMagnitude <= 0f
            || !float.IsFinite(length) || length <= 0f)
            throw new InvalidOperationException(
                $"PEAK's native {Source.Id} Campfire spawner has an invalid position ray.");

        Physics.SyncTransforms();
        var ray = new Ray(origin, direction);
        var rayMask = HelperFunctions.GetMask(spawner.layerType).value;
        var hits = Physics.RaycastAll(
            ray,
            length,
            rayMask,
            QueryTriggerInteraction.UseGlobal);
        Collider? Collider = null;
        var NearestDistance = float.PositiveInfinity;
        var NearestDistinctDistance = float.PositiveInfinity;
        foreach (var Hit in hits)
        {
            if (Hit.distance >= NearestDistinctDistance) continue;
            var Candidate = Hit.collider;
            if (!IsActiveSupport(Candidate)) continue;
            if (Hit.distance < NearestDistance)
            {
                if (Candidate != Collider) NearestDistinctDistance = NearestDistance;
                Collider = Candidate;
                NearestDistance = Hit.distance;
            }
            else if (Candidate != Collider) NearestDistinctDistance = Hit.distance;
        }
        if (Collider && Mathf.Approximately(NearestDistinctDistance, NearestDistance))
            throw new InvalidOperationException(
                $"PEAK's native {Source.Id} Campfire ray has ambiguous external support collision.");

        if (!Collider && Frame.parent is { } ExternalRoot)
            foreach (var Candidate in ExternalRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!Candidate || !Candidate.enabled || Candidate.isTrigger
                    || Candidate.attachedRigidbody || Candidate.gameObject.activeInHierarchy
                    || !NativePlacement.ActiveWithin(Candidate.transform, ExternalRoot)
                    || Candidate.gameObject.scene != Frame.gameObject.scene
                    || InsideAny(Candidate.transform, authoredContainers)
                    || NativeCollision.IsGeneratedOutput(ExternalRoot, Candidate.transform)
                    || (rayMask & (1 << Candidate.gameObject.layer)) == 0
                    || !NativePlacement.CanCopyCollider(Candidate)
                    || !NativePlacement.TryWorldBounds(Candidate, out var CandidateBounds)
                    || !Nonempty(CandidateBounds)
                    || !CandidateBounds.IntersectRay(ray, out var Distance) || Distance > length) continue;
                if (Collider)
                    throw new InvalidOperationException(
                        $"PEAK's native {Source.Id} Campfire ray has ambiguous inactive external support collision.");
                Collider = Candidate;
            }

        if (!Collider || !NativePlacement.CanCopyCollider(Collider)
            || !NativePlacement.TryWorldBounds(Collider, out var Bounds) || !Nonempty(Bounds))
            throw new InvalidOperationException(
                $"PEAK's native {Source.Id} Campfire ray has no usable external static support collider.");
        return new ExternalSupport(Collider, Frame);

        bool IsActiveSupport(Collider Candidate) => Candidate && Candidate.enabled && !Candidate.isTrigger
            && !Candidate.attachedRigidbody && Candidate.gameObject.activeInHierarchy
            && Candidate.gameObject.scene == Frame.gameObject.scene
            && !InsideAny(Candidate.transform, authoredContainers)
            && !NativeCollision.IsGeneratedOutput(Candidate.transform.root, Candidate.transform);
    }

    public void Dispose()
    {
        ReleaseExclusion();
        if (root) UnityEngine.Object.DestroyImmediate(root);
        root = null;
        Physics.SyncTransforms();
    }

    static Collider? CopyExternal(ExternalSupport support, Transform parent)
    {
        var source = support.Source;
        if (!source || source.isTrigger || source.attachedRigidbody
            || !NativePlacement.CanCopyCollider(source))
            throw new InvalidOperationException(
                "The native external campfire support collider is no longer usable.");
        if (source.gameObject.activeInHierarchy
            && Same(parent.localToWorldMatrix, support.FrameMatrix)
            && Same(source.transform.localToWorldMatrix, support.SourceMatrix)) return null;
        GameObject? target = null;
        foreach (var local in support.Transforms)
        {
            target = new GameObject(source.name);
            target.hideFlags = HideFlags.HideAndDontSave;
            var transform = target.transform;
            transform.SetParent(parent, false);
            transform.localPosition = local.Position;
            transform.localRotation = local.Rotation;
            transform.localScale = local.Scale;
            parent = transform;
        }
        if (!target)
            throw new InvalidOperationException(
                "The native external campfire support collider has no transform path.");
        target.layer = source.gameObject.layer;
        target.tag = source.tag;
        var copy = NativePlacement.CopyCollider(source, target);
        copy.enabled = true;
        return copy;
    }

    void ReleaseExclusion()
    {
        if (!ownsExclusion) return;
        activeExclusion = null;
        activeSpawnerRoot = null;
        ownsExclusion = false;
    }

    internal static Collider? ExcludedSupport(PropSpawner Spawner)
    {
        var Root = activeSpawnerRoot;
        return activeExclusion && Root
            && TransformOwnership.Contains(Root, Spawner.transform)
            ? activeExclusion
            : null;
    }

    static Transform Mirror(
        Transform source,
        Transform container,
        IDictionary<Transform, Transform> mirrors)
    {
        if (mirrors.TryGetValue(source, out var existing)) return existing;
        var sourceParent = source.parent;
        if (!sourceParent || source != container && !source.IsChildOf(container))
            throw new InvalidOperationException("Native Terrain/Map collision left its placed container.");
        var parent = Mirror(sourceParent, container, mirrors);
        var value = new GameObject(source.name);
        value.hideFlags = HideFlags.HideAndDontSave;
        value.layer = source.gameObject.layer;
        value.tag = source.tag;
        var transform = value.transform;
        transform.SetParent(parent, false);
        transform.localPosition = source.localPosition;
        transform.localRotation = source.localRotation;
        transform.localScale = source.localScale;
        mirrors.Add(source, transform);
        return transform;
    }

    static bool SpawnsCampfire(PropSpawner spawner)
    {
        var Props = NativePrefabContracts.EffectiveProps(spawner);
        if (Props is not { Length: > 0 }) return false;
        foreach (var Prop in Props)
            if (Prop && Prop.GetComponentInChildren<global::Campfire>(true)) return true;
        return false;
    }

    static bool InsideAny(Transform value, IReadOnlyCollection<Transform> roots)
    {
        foreach (var root in roots)
            if (TransformOwnership.Contains(root, value)) return true;
        return false;
    }

    static bool Nonempty(Bounds value) =>
        value.extents.x > 0f || value.extents.y > 0f || value.extents.z > 0f;
    static bool Usable(Vector3 position, Quaternion rotation, Vector3 scale) =>
        UnityValueContract.Finite(position)
        && UnityValueContract.Finite(rotation)
        && UnityValueContract.Finite(scale)
        && scale.x != 0f && scale.y != 0f && scale.z != 0f;
    static bool Same(Matrix4x4 left, Matrix4x4 right)
    {
        for (var i = 0; i < 16; i++)
            if (!Mathf.Approximately(left[i], right[i])) return false;
        return true;
    }
}
