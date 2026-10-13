using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal static class NativePlacement
{
    const float PlayerClearanceRadius = 1f;

    enum ColliderCopyKind : byte
    {
        Unsupported,
        Mesh,
        Box,
        Sphere,
        Capsule,
        Terrain
    }

    internal sealed class StartFormation
    {
        readonly Vector3[] Offsets;

        internal StartFormation(Pose Start, Vector3[] Offsets, Pose? PlanePose)
        {
            this.Start = Start;
            this.Offsets = Offsets;
            this.PlanePose = PlanePose;
        }

        internal Pose Start { get; }
        internal Pose? PlanePose { get; }

        internal void Place(Transform[] Spawns, Pose Target)
        {
            for (var i = 0; i < Spawns.Length; i++)
                Spawns[i].position = Target.position + Target.rotation * Offsets[i];
        }
    }

    internal static StartFormation ResolveStart(
        MapSlot First, Transform[] Spawns, Transform Plane, bool PlacePlane)
    {
        var Source = First.Source;
        if (Source is not NativeOrdinarySegmentSource)
            throw new InvalidOperationException($"Segment '{Source.Id}' has no explicit Start port.");
        Physics.SyncTransforms();
        var PrimarySpawn = Spawns[0];
        var Offsets = new Vector3[Spawns.Length];
        var MinimumOffset = Vector3.zero;
        var MaximumOffset = Vector3.zero;
        for (var i = 0; i < Spawns.Length; i++)
        {
            Offsets[i] = Spawns[i].position - PrimarySpawn.position;
            MinimumOffset = Vector3.Min(MinimumOffset, Offsets[i]);
            MaximumOffset = Vector3.Max(MaximumOffset, Offsets[i]);
        }
        var MeshAssets = new NativeMeshAssets();
        var Shape = new PlaneFootprint(Plane, PrimarySpawn.position, PlacePlane ? MeshAssets : null);
        MinimumOffset = Vector3.Min(MinimumOffset, Shape.MinimumWorldOffset);
        MaximumOffset = Vector3.Max(MaximumOffset, Shape.MaximumWorldOffset);

        var Root = First.Segment.segmentParent.transform;
        var CollisionMask = HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysicalExceptCharacter).value;
        var GroundByCollider = new Dictionary<int, bool>();
        var CollisionBounds = default(Bounds);
        var GroundGeometry = new Dictionary<int, NativeMeshGeometry>();
        var RayHits = new RaycastHit[NativeCollision.InitialQueryHits];
        var OverlapHits = new Collider[NativeCollision.InitialQueryHits];
        foreach (var Collider in Root.GetComponentsInChildren<Collider>())
        {
            if (!Collider.enabled || Collider.isTrigger
                || (CollisionMask & (1 << Collider.gameObject.layer)) == 0
                || !TryWorldBounds(Collider, out var bounds) || bounds.size.sqrMagnitude <= 0f) continue;
            var Ground = Collider is MeshCollider { convex: false } && !Collider.attachedRigidbody;
            if (GroundByCollider.Count == 0) CollisionBounds = bounds;
            else CollisionBounds.Encapsulate(bounds);
            GroundByCollider.Add(Collider.GetInstanceID(), Ground);
        }
        if (GroundByCollider.Count == 0)
            throw new InvalidOperationException($"No solid collision was found in '{Source.Id}'.");

        var Width = MaximumOffset.x - MinimumOffset.x;
        var Depth = MaximumOffset.z - MinimumOffset.z;
        var Entry = First.PlacedEntry;
        var Forward = First.PlacedProgressionAxis;
        var RearOffset = Forward.x * (Forward.x < 0f ? MaximumOffset.x : MinimumOffset.x)
            + Forward.z * (Forward.z < 0f ? MaximumOffset.z : MinimumOffset.z);
        var Anchor = Entry - Forward * Math.Min(0f, RearOffset);
        var Clearance = Math.Max(Physics.defaultContactOffset, 0f);
        var Distance = CollisionBounds.size.y + Clearance * 2f;
        var Top = CollisionBounds.max.y + Clearance;
        var Hits = new RaycastHit[Spawns.Length];
        var BestHits = new RaycastHit[Spawns.Length];
        var BestDistance = double.PositiveInfinity;
        Pose? BestPlane = null;
        var Ambiguous = false;
        var Columns = Mathf.CeilToInt(Width / PlayerClearanceRadius);
        var Rows = Mathf.CeilToInt(Depth / PlayerClearanceRadius);
        ConsiderFormation(new Vector3(Anchor.x, Top, Anchor.z));
        for (var z = 0; z <= Rows; z++)
            for (var x = 0; x <= Columns; x++)
                ConsiderFormation(new Vector3(
                    Anchor.x + Mathf.Lerp(-Width, Width, (float)x / Columns), Top,
                    Anchor.z + Mathf.Lerp(-Depth, Depth, (float)z / Rows)));
        if (double.IsPositiveInfinity(BestDistance))
            throw new InvalidOperationException($"No clear landing area was found near the entrance to '{Source.Id}'.");
        if (Ambiguous)
            throw new InvalidOperationException($"More than one landing area in '{Source.Id}' is equally close to the entrance.");
        var Primary = BestHits[0].point;
        var InverseRotation = Quaternion.Inverse(PrimarySpawn.rotation);
        for (var i = 0; i < Offsets.Length; i++)
            Offsets[i] = InverseRotation * (BestHits[i].point - Primary);
        return new StartFormation(new Pose(Primary, PrimarySpawn.rotation), Offsets, BestPlane);

        void ConsiderFormation(Vector3 Center)
        {
            if (Vector3.Dot(Center - new Vector3(Entry.x, Top, Entry.z), Forward) + RearOffset < 0f) return;
            var CandidateOffset = Center - Anchor;
            var CandidateDistance = (double)CandidateOffset.x * CandidateOffset.x + (double)CandidateOffset.z * CandidateOffset.z;
            if (CandidateDistance > BestDistance) return;
            for (var i = 0; i < Offsets.Length; i++)
            {
                var Origin = Center + new Vector3(Offsets[i].x, 0f, Offsets[i].z);
                if (!RaycastGround(new Ray(Origin, Vector3.down), out Hits[i])) return;
            }
            var LowestPlayer = Hits[0].point.y;
            var HighestPlayer = LowestPlayer;
            foreach (var Hit in Hits)
            {
                LowestPlayer = Mathf.Min(LowestPlayer, Hit.point.y);
                HighestPlayer = Mathf.Max(HighestPlayer, Hit.point.y);
            }
            if (HighestPlayer - LowestPlayer > PlayerClearanceRadius * 2f) return;
            foreach (var Hit in Hits)
                if (Intersects(new Bounds(Hit.point + Vector3.up * (PlayerClearanceRadius / Hit.normal.y + Clearance),
                        Vector3.one * (PlayerClearanceRadius * 2f)), true)) return;
            Pose? PlanePose = null;
            if (PlacePlane)
            {
                var Position = Center + Shape.PivotWorldOffset;
                var Translation = new Vector3(Center.x - PrimarySpawn.position.x, 0f, Center.z - PrimarySpawn.position.z);
                if (!GroundPlane(Translation, new Bounds(
                        new Vector3(Position.x + Shape.CenterWorldOffset.x, CollisionBounds.center.y,
                            Position.z + Shape.CenterWorldOffset.z),
                        new Vector3(Shape.Size.x, CollisionBounds.size.y + Clearance * 2f, Shape.Size.z)), out var Separation)) return;
                Position.y = Plane.position.y + Separation + Clearance;
                if (Position.y + Shape.CenterWorldOffset.y - Shape.Size.y * 0.5f - LowestPlayer > Shape.Size.y) return;
                if (Intersects(new Bounds(Position + Shape.CenterWorldOffset, Shape.Size), false)) return;
                PlanePose = new Pose(Position, Shape.Rotation);
            }
            if (CandidateDistance == BestDistance)
            {
                if (BestHits[0].point != Hits[0].point) Ambiguous = true;
                return;
            }
            BestDistance = CandidateDistance;
            BestPlane = PlanePose;
            Ambiguous = false;
            Array.Copy(Hits, BestHits, Hits.Length);
        }

        bool GroundPlane(Vector3 Translation, Bounds Bounds, out float Separation)
        {
            Separation = float.NegativeInfinity;
            while (true)
            {
                var Count = Physics.OverlapBoxNonAlloc(Bounds.center, Bounds.extents, OverlapHits,
                    Quaternion.identity, CollisionMask, QueryTriggerInteraction.Ignore);
                if (Count == OverlapHits.Length)
                {
                    if (OverlapHits.Length == NativeCollision.MaximumQueryHits)
                        throw new InvalidOperationException($"A landing clearance query reached its {NativeCollision.MaximumQueryHits}-hit limit.");
                    Array.Resize(ref OverlapHits, OverlapHits.Length * 2);
                    continue;
                }
                for (var i = 0; i < Count; i++)
                {
                    var Collider = OverlapHits[i];
                    var Id = Collider.GetInstanceID();
                    if (!GroundByCollider.TryGetValue(Id, out var Ground) || !Ground) continue;
                    if (!GroundGeometry.TryGetValue(Id, out var Geometry))
                    {
                        Geometry = MeshAssets.Resolve((MeshCollider)Collider);
                        GroundGeometry.Add(Id, Geometry);
                    }
                    foreach (var Model in Shape.Geometry)
                        if (Model.TryVerticalSeparation(Geometry, Translation, out var Required))
                            Separation = Mathf.Max(Separation, Required);
                }
                return !float.IsNegativeInfinity(Separation);
            }
        }

        bool RaycastGround(Ray Ray, out RaycastHit Result)
        {
            Result = default;
            while (true)
            {
                var Count = Physics.RaycastNonAlloc(Ray, RayHits, Distance, CollisionMask, QueryTriggerInteraction.Ignore);
                if (Count < RayHits.Length)
                {
                    var Found = false;
                    var Ground = false;
                    for (var i = 0; i < Count; i++)
                    {
                        var Hit = RayHits[i];
                        if (!GroundByCollider.TryGetValue(Hit.collider.GetInstanceID(), out var CandidateGround)
                            || Found && Hit.point.y < Result.point.y) continue;
                        if (Found && Hit.point.y == Result.point.y)
                        {
                            Ground &= CandidateGround && Hit.normal.y > 0.5f;
                            continue;
                        }
                        Result = Hit; Found = true; Ground = CandidateGround && Hit.normal.y > 0.5f;
                    }
                    return Found && Ground;
                }
                if (RayHits.Length == NativeCollision.MaximumQueryHits)
                    throw new InvalidOperationException($"A landing ray reached its {NativeCollision.MaximumQueryHits}-hit limit.");
                Array.Resize(ref RayHits, RayHits.Length * 2);
            }
        }

        bool Intersects(Bounds Bounds, bool Player)
        {
            while (true)
            {
                var Count = Player
                    ? Physics.OverlapSphereNonAlloc(Bounds.center, PlayerClearanceRadius, OverlapHits, CollisionMask, QueryTriggerInteraction.Ignore)
                    : Physics.OverlapBoxNonAlloc(Bounds.center, Bounds.extents, OverlapHits, Quaternion.identity, CollisionMask, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < Count; i++)
                    if (GroundByCollider.TryGetValue(OverlapHits[i].GetInstanceID(), out var Ground) && (Player || !Ground)) return true;
                if (Count < OverlapHits.Length) return false;
                if (OverlapHits.Length == NativeCollision.MaximumQueryHits)
                    throw new InvalidOperationException($"A landing clearance query reached its {NativeCollision.MaximumQueryHits}-hit limit.");
                Array.Resize(ref OverlapHits, OverlapHits.Length * 2);
            }
        }
    }

    readonly struct PlaneFootprint
    {
        internal PlaneFootprint(Transform Plane, Vector3 PlayerAnchor, NativeMeshAssets? MeshAssets)
        {
            var Renderers = Plane.GetComponentsInChildren<MeshRenderer>(true);
            if (Renderers.Length == 0) throw new InvalidOperationException("The crashed plane has no mesh renderers.");
            var Bounds = Renderers[0].bounds;
            for (var i = 1; i < Renderers.Length; i++) Bounds.Encapsulate(Renderers[i].bounds);
            MinimumWorldOffset = Bounds.min - PlayerAnchor;
            MaximumWorldOffset = Bounds.max - PlayerAnchor;
            MinimumWorldOffset.y = MaximumWorldOffset.y = 0f;
            PivotWorldOffset = Plane.position - PlayerAnchor;
            CenterWorldOffset = Bounds.center - Plane.position;
            Size = Bounds.size;
            Rotation = Plane.rotation;
            if (MeshAssets is null) { Geometry = Array.Empty<NativeMeshGeometry>(); return; }
            var Count = 0;
            foreach (var Renderer in Renderers)
                if (Renderer.enabled && Renderer.gameObject.activeInHierarchy) Count++;
            Geometry = new NativeMeshGeometry[Count];
            var Index = 0;
            foreach (var Renderer in Renderers)
                if (Renderer.enabled && Renderer.gameObject.activeInHierarchy)
                    Geometry[Index++] = MeshAssets.Resolve(Renderer.GetComponent<MeshFilter>());
        }
        internal readonly Vector3 MinimumWorldOffset, MaximumWorldOffset, PivotWorldOffset, CenterWorldOffset, Size;
        internal readonly Quaternion Rotation;
        internal readonly NativeMeshGeometry[] Geometry;
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
