using System;
using System.Collections.Generic;
using System.Globalization;
using PLL.Library;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class CollisionGeometryIdentity
    {
        readonly PrefabBinding Target;
        readonly PrefabBinding Owner;
        readonly PrefabBinding Marker;
        readonly PllCollisionMode Mode;
        readonly int TargetLayer;
        readonly int OwnerLayer;
        readonly AssetBinding GeneratedMesh, PreparedCurvatureRenderMesh;
        readonly CollisionShapeIdentity[] Colliders;
        readonly CollisionShapeIdentity[] TargetColliders;
        readonly Matrix4x4 TargetPose;
        readonly Matrix4x4 OwnerPose;

        CollisionGeometryIdentity(PrefabBinding Target, PrefabBinding Owner,
            PrefabBinding Marker, PllCollisionMode Mode, int TargetLayer,
            int OwnerLayer, AssetBinding GeneratedMesh, AssetBinding PreparedCurvatureRenderMesh, CollisionShapeIdentity[] Colliders,
            CollisionShapeIdentity[] TargetColliders, Matrix4x4 TargetPose, Matrix4x4 OwnerPose)
        {
            this.Target = Target;
            this.Owner = Owner;
            this.Marker = Marker;
            this.Mode = Mode;
            this.TargetLayer = TargetLayer;
            this.OwnerLayer = OwnerLayer;
            this.GeneratedMesh = GeneratedMesh; this.PreparedCurvatureRenderMesh = PreparedCurvatureRenderMesh;
            this.Colliders = Colliders;
            this.TargetColliders = TargetColliders;
            this.TargetPose = TargetPose;
            this.OwnerPose = OwnerPose;
        }

        internal static CollisionGeometryIdentity Capture(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, CollisionGeometry Geometry,
            AssetIdentityCatalog Assets, DefinitionBindingResolver CaptureResolver, PreparedMeshRecipeCatalog MeshRecipes)
        {
            var Shapes = new CollisionShapeIdentity[Geometry.Colliders.Length];
            for (var Index = 0; Index < Shapes.Length; Index++)
                Shapes[Index] = CollisionShapeIdentity.Capture(
                    Graph, StagedPaths, Geometry.Colliders[Index], Assets);
            var TargetShapes = Geometry.Target == Geometry.CollisionOwner
                ? Array.Empty<CollisionShapeIdentity>()
                : new CollisionShapeIdentity[Geometry.TargetColliders.Length];
            for (var Index = 0; Index < TargetShapes.Length; Index++)
                TargetShapes[Index] = CollisionShapeIdentity.Capture(
                    Graph, StagedPaths, Geometry.TargetColliders[Index], Assets);
            var Layer = Geometry.Target.gameObject.layer;
            if (Geometry.GeneratedMesh && !PllClimbSurfaceAuthoringValidator.IsClimbLayer(Layer))
                Layer = PllClimbSurfaceAuthoringValidator.MapLayer;
            var TargetBinding = PrefabBinding.Capture(Graph, StagedPaths, Geometry.Target, "collision.target");
            var PreparedCurvature = Geometry.GeneratedMesh && MeshRecipes.NeedsCurvature(Geometry.Target.GetComponent<MeshRenderer>())
                ? TargetBinding.Resolve<Transform>(CaptureResolver, "Staged prefab", "collision.target").GetComponent<MeshFilter>().sharedMesh : null;
            return new CollisionGeometryIdentity(
                TargetBinding,
                PrefabBinding.Capture(Graph, StagedPaths, Geometry.CollisionOwner, "collision.owner"),
                Geometry.Marker ? PrefabBinding.Capture(
                    Graph, StagedPaths, Geometry.Marker, "collision.marker") : null,
                Geometry.Mode, Layer, Geometry.GeneratedMesh
                    ? Layer : Geometry.CollisionOwner.gameObject.layer,
                Geometry.GeneratedMesh ? AssetBinding.Capture(
                    Geometry.GeneratedMesh, Assets, "collision.mesh") : null,
                PreparedCurvature ? AssetBinding.Capture(PreparedCurvature, Assets, "collision.curvatureRenderMesh") : null, Shapes, TargetShapes,
                Geometry.Target.localToWorldMatrix, Geometry.CollisionOwner.localToWorldMatrix);
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var ActualTarget = Target.Resolve<Transform>(Resolver, Context, "collision.target");
            var ActualOwner = Owner.Resolve<Transform>(Resolver, Context, "collision.owner");
            if (ActualTarget.gameObject.layer != TargetLayer || ActualOwner.gameObject.layer != OwnerLayer)
                throw DefinitionSemanticIdentity.Changed(Context, "collision.layer");
            if (!ActualTarget.localToWorldMatrix.Equals(TargetPose)
                || !ActualOwner.localToWorldMatrix.Equals(OwnerPose))
                throw DefinitionSemanticIdentity.Changed(Context, "collision.pose");
            var ActualMarker = Marker?.Resolve<PllCollisionAuthoring>(Resolver, Context, "collision.marker");
            if (ActualMarker && (!ActualMarker.enabled || ActualMarker.Mode != Mode))
                throw DefinitionSemanticIdentity.Changed(Context, "collision.mode");
            if (Marker == null && ActualTarget.GetComponent<PllCollisionAuthoring>())
                throw DefinitionSemanticIdentity.Changed(Context, "collision.marker");
            var ActualColliders = ActualOwner.GetComponents<Collider>();
            if (ActualColliders.Length != Colliders.Length + (GeneratedMesh == null ? 0 : 1))
                throw DefinitionSemanticIdentity.Changed(Context, "collision.colliders");
            for (var Index = 0; Index < Colliders.Length; Index++)
                Colliders[Index].Require(ActualColliders[Index], Resolver, Context);
            if (ActualTarget != ActualOwner)
            {
                var ActualTargetColliders = ActualTarget.GetComponents<Collider>();
                if (ActualTargetColliders.Length != TargetColliders.Length)
                    throw DefinitionSemanticIdentity.Changed(Context, "collision.targetColliders");
                for (var Index = 0; Index < TargetColliders.Length; Index++)
                    TargetColliders[Index].Require(ActualTargetColliders[Index], Resolver, Context);
            }
            if (GeneratedMesh == null) return;
            if (ActualColliders[Colliders.Length] is not MeshCollider MeshCollider
                || !MeshCollider.enabled || MeshCollider.isTrigger || MeshCollider.convex
                || MeshCollider.sharedMaterial
                || !MeshCollider.contactOffset.Equals(PllClimbSurfaceAuthoringValidator.StaticMeshContactOffset)
                || MeshCollider.cookingOptions != PllClimbSurfaceAuthoringValidator.StaticMeshCookingOptions)
                throw DefinitionSemanticIdentity.Changed(Context, "collision.generatedShape");
            GeneratedMesh.Require(MeshCollider.sharedMesh, Resolver, Context, "collision.mesh");
            var Filter = ActualTarget.GetComponent<MeshFilter>();
            if (!Filter) throw DefinitionSemanticIdentity.Changed(Context, "collision.meshOwner");
            if (PreparedCurvatureRenderMesh != null)
                PreparedCurvatureRenderMesh.Require(Filter.sharedMesh, Resolver, Context, "collision.curvatureRenderMesh");
            else if (Filter.sharedMesh != MeshCollider.sharedMesh)
                throw DefinitionSemanticIdentity.Changed(Context, "collision.meshOwner");
        }
    }

    internal sealed class CollisionShapeIdentity
    {
        readonly ClimbSurfaceColliderIdentity Binding;
        readonly string Shape;
        readonly AssetBinding Geometry;

        CollisionShapeIdentity(ClimbSurfaceColliderIdentity Binding,
            string Shape, AssetBinding Geometry)
        {
            this.Binding = Binding;
            this.Shape = Shape;
            this.Geometry = Geometry;
        }

        internal static CollisionShapeIdentity Capture(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, Collider Value,
            AssetIdentityCatalog Assets)
        {
            var Geometry = GeometryValue(Value);
            return new CollisionShapeIdentity(
                ClimbSurfaceColliderIdentity.Capture(Graph, StagedPaths, Value,
                    Assets, "collision.collider"), ShapeValue(Value),
                Geometry ? AssetBinding.Capture(Geometry,
                    Assets, "collision.collider.geometry") : null);
        }

        internal void Require(Collider Actual, DefinitionBindingResolver Resolver, string Context)
        {
            Binding.Require(Actual, Resolver, Context, "collision.collider");
            if (ShapeValue(Actual) != Shape)
                throw DefinitionSemanticIdentity.Changed(Context, "collision.collider.shape");
            if (Geometry != null)
                Geometry.Require(GeometryValue(Actual), Resolver, Context, "collision.collider.geometry");
            else if (GeometryValue(Actual))
                throw DefinitionSemanticIdentity.Changed(Context, "collision.collider.geometry");
        }

        static UnityEngine.Object GeometryValue(Collider Value) => Value switch
        {
            MeshCollider Mesh => Mesh.sharedMesh,
            TerrainCollider Terrain => Terrain.terrainData,
            _ => null
        };

        static string ShapeValue(Collider Value) => Value.enabled + "/" + Value.isTrigger
            + "/" + Number(Value.contactOffset) + "/" + (Value switch
            {
                BoxCollider Box => Vector(Box.center) + "/" + Vector(Box.size),
                SphereCollider Sphere => Vector(Sphere.center) + "/" + Number(Sphere.radius),
                CapsuleCollider Capsule => Vector(Capsule.center) + "/" + Number(Capsule.radius)
                    + "/" + Number(Capsule.height) + "/" + Capsule.direction,
                MeshCollider Mesh => Mesh.convex + "/" + (int)Mesh.cookingOptions,
                TerrainCollider => "terrain",
                _ => throw new InvalidOperationException("Collision uses an unsupported Collider type.")
            });

        static string Vector(Vector3 Value) => Number(Value.x) + "," + Number(Value.y) + "," + Number(Value.z);
        static string Number(float Value) => Value.ToString("R", CultureInfo.InvariantCulture);
    }
}
