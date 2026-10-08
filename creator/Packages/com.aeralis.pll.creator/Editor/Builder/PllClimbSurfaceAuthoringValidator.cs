using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllClimbSurfaceAuthoringValidator
    {
        internal const int TerrainLayer = 20;
        internal const int MapLayer = 21;
        internal const float StaticMeshContactOffset = 0.01f;
        internal const MeshColliderCookingOptions StaticMeshCookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation
            | MeshColliderCookingOptions.EnableMeshCleaning
            | MeshColliderCookingOptions.WeldColocatedVertices
            | MeshColliderCookingOptions.UseFastMidphase;

        internal ClimbSurfaceAuthoringCatalog Validate(SourceIdentity Identity)
        {
            foreach (var Node in Identity.Graph.Nodes)
                if (Node.SourceRoot.GetComponentInChildren<ClimbModifierSurface>(true))
                    throw new InvalidOperationException("Biome definition '"
                        + Identity.Label
                        + "' source graph contains creator-authored ClimbModifierSurface mechanics.");
            var Markers = DiscoverGraphComponents<PllClimbSurfaceAuthoring>(
                Identity.Graph);
            RequireGraphComponents(
                Identity.Graph, Markers, Identity.Label, "climb surfaces");
            var ObjectsByTarget = new Dictionary<GameObject, PllClimbSurfaceAuthoring>();
            foreach (var Marker in Markers)
            {
                var Node = OwnerNode(
                    Identity.Graph, Marker,
                    "Biome definition '" + Identity.Label
                        + "' climb surface");
                var Context = "Biome definition '" + Identity.Label
                    + "' climb surface '"
                    + PllHierarchy.NamePath(
                        Node.SourceRoot.transform, Marker.transform) + "'";
                if (!ObjectsByTarget.TryAdd(Marker.gameObject, Marker))
                    throw new InvalidOperationException(
                        Context + " requires exactly one enabled marker.");
                if (Node.IsRoot)
                {
                    RequireFeatureOwned(
                        Marker.transform, Identity.Floor,
                        Identity.Campfire, Context);
                    RequireStableOwned(
                        Identity.Root.transform, Marker.transform, Context);
                }
                else
                {
                    if (TransformOwnership.HasPropSpawnerInPath(
                            Node.SourceRoot.transform, Marker.transform))
                        throw new InvalidOperationException(
                            Context
                                + " cannot belong to a nested PropSpawner hierarchy.");
                    ValidateTransformPath(
                        Node.SourceRoot.transform, Marker.transform, Context);
                }
                if (ReservedSubtree(
                        Node.SourceRoot.transform, Marker.transform))
                    throw new InvalidOperationException(
                        Context
                            + " cannot belong to RuntimeMarkers or Presentation.");
                ValidateFields(Marker, Context);
            }

            var MaterialCarriers =
                DiscoverGraphComponents<PllClimbSurfaceMaterialAuthoring>(
                    Identity.Graph);
            if (MaterialCarriers.Length > 1
                || MaterialCarriers.Length == 1
                && (MaterialCarriers[0].gameObject != Identity.Root
                    || MaterialCarriers[0].transform != Identity.Root.transform))
                throw new InvalidOperationException(
                    "Biome definition '" + Identity.Label
                        + "' climb material rules must use one direct root carrier.");
            var MaterialCarrier = MaterialCarriers.Length == 0
                ? null : MaterialCarriers[0];
            var MaterialTargets = new Dictionary<GameObject, int>();
            if (!MaterialCarrier)
                return new ClimbSurfaceAuthoringCatalog(
                    Markers, null, MaterialTargets);
            var MaterialIndices = ValidateMaterialCarrier(
                MaterialCarrier, Identity.Label);

            var ColliderGroups = new List<List<Collider>>();
            var GroupByObject = new Dictionary<GameObject, int>();
            foreach (var Collider in DiscoverGraphComponents<Collider>(
                Identity.Graph))
            {
                var Node = OwnerNode(
                    Identity.Graph, Collider,
                    "Biome definition '" + Identity.Label
                        + "' climb material target");
                if (!Eligible(Collider)
                    || !ValidOwnership(
                        Node, Collider.transform, Identity)) continue;
                if (!GroupByObject.TryGetValue(
                        Collider.gameObject, out var GroupIndex))
                {
                    GroupIndex = ColliderGroups.Count;
                    GroupByObject.Add(Collider.gameObject, GroupIndex);
                    ColliderGroups.Add(new List<Collider>());
                }
                ColliderGroups[GroupIndex].Add(Collider);
            }
            var UsedRules = new bool[MaterialCarrier.Rules.Count];
            foreach (var Pair in GroupByObject)
            {
                var Target = Pair.Key;
                if (ObjectsByTarget.TryGetValue(Target, out var ObjectRule)
                    && ObjectRule.Mode == PllClimbSurfaceMode.Always)
                    continue;
                var Colliders = ColliderGroups[Pair.Value];
                var ResolvedRule = ResolveMaterialRule(
                    Target, Colliders, MaterialIndices,
                    Identity.Label);
                if (ResolvedRule < 0) continue;
                MaterialTargets.Add(Target, ResolvedRule);
                UsedRules[ResolvedRule] = true;
            }
            for (var Index = 0; Index < UsedRules.Length; Index++)
                if (!UsedRules[Index])
                    throw new InvalidOperationException(
                        "Biome definition '" + Identity.Label
                            + "' climb material rule " + Index
                            + " has no eligible fallback target.");
            return new ClimbSurfaceAuthoringCatalog(
                Markers, MaterialCarrier, MaterialTargets);
        }

        internal static void ValidateLayerChanges(
            EditablePrefabOwner Owner,
            IReadOnlyList<GameObject> Targets,
            IReadOnlyDictionary<GameObject, List<Collider>>
                CollidersByTarget)
        {
            var MaterialTargets = new List<GameObject>();
            var TargetMaterials = new HashSet<PhysicsMaterial>();
            foreach (var Target in Targets)
            {
                var TargetColliders = CollidersByTarget[Target];
                var Context = "Biome definition '" + Owner.Label
                    + "' climb target '"
                    + PllHierarchy.NamePath(
                        Owner.StageRoot.transform,
                        Target.transform) + "'";
                if (Target.GetComponent<ClimbModifierSurface>())
                    throw new InvalidOperationException(
                        Context
                            + " contains creator-authored ClimbModifierSurface mechanics.");
                if (Target != Owner.MaterialRoot
                    && Target.GetComponent<
                        PllClimbSurfaceMaterialAuthoring>())
                    throw new InvalidOperationException(
                        Context
                            + " cannot own climb material rules outside the biome root.");
                var Markers = Target.GetComponents<
                    PllClimbSurfaceAuthoring>();
                if (Markers.Length > 1)
                    throw new InvalidOperationException(
                        Context + " requires at most one climb surface marker.");
                var Marker = Markers.Length == 0 ? null : Markers[0];
                if (Marker)
                    ValidateFields(Marker, Context, MapLayer);
                if (Marker && Marker.Mode == PllClimbSurfaceMode.Always)
                    continue;
                MaterialTargets.Add(Target);
                foreach (var Collider in TargetColliders)
                    if (Collider.sharedMaterial)
                        TargetMaterials.Add(Collider.sharedMaterial);
            }
            if (MaterialTargets.Count == 0
                || TargetMaterials.Count == 0) return;

            var MaterialCarriers = Owner.MaterialRoot.GetComponentsInChildren<
                PllClimbSurfaceMaterialAuthoring>(true);
            if (!MaterialCarriers.Any(Carrier => Carrier
                    && Carrier.Rules.Any(Rule => Rule != null
                        && Rule.PhysicsMaterial
                        && TargetMaterials.Contains(
                            Rule.PhysicsMaterial)))) return;
            if (MaterialCarriers.Length != 1
                || MaterialCarriers[0].gameObject != Owner.MaterialRoot)
                throw new InvalidOperationException(
                    "Biome definition '" + Owner.Label
                        + "' climb material rules must use one direct root carrier.");
            var MaterialIndices = ValidateMaterialCarrier(
                MaterialCarriers[0], Owner.Label);
            foreach (var Target in MaterialTargets)
                ResolveMaterialRule(
                    Target, CollidersByTarget[Target],
                    MaterialIndices, Owner.Label);
        }

        internal static (GameObject Target, Mesh SharedMesh)[]
            ValidateMeshColliderFallback(
                GameObject Selected, EditablePrefabOwner Owner,
                string Action)
        {
            var Skinned = DiscoverComponents<SkinnedMeshRenderer>(Selected);
            if (Skinned.Length != 0)
            {
                var Context = ClimbTargetContext(
                    Action, Owner, Skinned[0].transform);
                RequireEditableTarget(
                    Owner, Skinned[0].transform, Context);
                throw new InvalidOperationException(
                    Context
                        + " uses SkinnedMeshRenderer geometry. Add authored collision before running the command.");
            }

            var Renderers = DiscoverComponents<MeshRenderer>(Selected);
            if (Renderers.Length == 0)
                throw new InvalidOperationException(
                    Action
                        + " found no MeshRenderer geometry or Colliders in the selected hierarchy.");
            var Targets = new List<(GameObject Target, Mesh SharedMesh)>(
                Renderers.Length);
            var ValidatedMeshes = new HashSet<Mesh>();
            foreach (var Renderer in Renderers)
            {
                var Context = ClimbTargetContext(
                    Action, Owner, Renderer.transform);
                RequireEditableTarget(Owner, Renderer.transform, Context);
                RequireStaticTarget(Renderer.transform, Owner, Context);
                if (!Renderer.enabled)
                    throw new InvalidOperationException(
                        Context + " contains a disabled MeshRenderer.");
                if (!TransformOwnership.DiscoverableWhenActivated(
                        Selected.transform, Renderer.transform))
                    throw new InvalidOperationException(
                        Context
                            + " is inactive relative to the selected geometry root.");
                if (Renderer.GetComponents<MeshRenderer>().Length != 1)
                    throw new InvalidOperationException(
                        Context
                            + " requires exactly one MeshRenderer on the GameObject.");
                var Filters = Renderer.GetComponents<MeshFilter>();
                if (Filters.Length != 1)
                    throw new InvalidOperationException(
                        Context
                            + " requires exactly one MeshFilter on the same GameObject.");
                var Mesh = Filters[0].sharedMesh;
                if (!Mesh || ValidatedMeshes.Add(Mesh)) ValidateColliderMesh(Mesh, Context);
                Targets.Add((Renderer.gameObject, Mesh));
            }
            return Targets.ToArray();
        }

        internal static void RequireStaticTarget(
            Transform Value, EditablePrefabOwner Owner,
            string Context)
        {
            for (var Current = Value; ; Current = Current.parent)
            {
                if (Current.GetComponent<Rigidbody>()
                    || Current.GetComponent<ArticulationBody>())
                    throw new InvalidOperationException(
                        Context
                            + " cannot be governed by Rigidbody or ArticulationBody.");
                if (Current == Owner.StageRoot.transform) return;
            }
        }

        internal static void ValidateNeutralNative(
            ClimbModifierSurface Surface, string Context)
        {
            if (!Surface.enabled
                || Surface.GetComponents<ClimbModifierSurface>().Length != 1
                || !IsClimbLayer(Surface.gameObject.layer)
                || !Surface.GetComponents<Collider>().Any(Eligible)
                || Surface.onlySlideDown
                || !Surface.speedMultiplier.Equals(1f)
                || !Surface.slipModifier.Equals(0f)
                || !Surface.staminaUsageMultiplier.Equals(1f)
                || Surface.applyStatus
                || Surface.statusType
                    != default(CharacterAfflictions.STATUSTYPE)
                || Surface.applyPetrify
                || !Surface.statusAmount.Equals(0.5f)
                || !Surface.statusCooldown.Equals(0.5f)
                || Surface.staticClimbCost)
                throw new InvalidOperationException(
                    Context
                        + " has invalid native fields, layer, or Collider coverage.");
        }

        internal static void ValidateFields(
            PllClimbSurfaceAuthoring Marker, string Context)
        {
            ValidateFields(
                Marker, Context, Marker.gameObject.layer);
            if (!Marker.GetComponents<Collider>()
                    .Any(IsEnabledSolidCollider))
                throw new InvalidOperationException(
                    Context
                        + " requires an enabled non-trigger Collider on the same GameObject.");
        }

        static void ValidateFields(
            PllClimbSurfaceAuthoring Marker, string Context,
            int Layer)
        {
            if (!Marker.enabled
                || Marker.GetComponents<PllClimbSurfaceAuthoring>().Length != 1)
                throw new InvalidOperationException(
                    Context + " requires exactly one enabled marker.");
            ValidateValues(
                Marker.Mode, Marker.EmitHook,
                Marker.StaminaUsageMultiplier,
                Marker.SpeedMultiplier, Context);
            if (!IsClimbLayer(Layer))
                throw new InvalidOperationException(
                    Context + " must use Terrain layer 20 or Map layer 21.");
        }

        internal static bool ValidMaterial(
            PhysicsMaterial Value) => Value
            && EditorUtility.IsPersistent(Value)
            && !string.IsNullOrEmpty(
                AssetDatabase.GetAssetPath(Value))
            && float.IsFinite(Value.dynamicFriction)
            && float.IsFinite(Value.staticFriction)
            && float.IsFinite(Value.bounciness);

        internal static bool ValidValues(
            PllClimbSurfaceMode Mode, string Hook,
            float StaminaUsageMultiplier,
            float SpeedMultiplier) =>
            ValidMultipliers(
                StaminaUsageMultiplier, SpeedMultiplier)
            && (Mode == PllClimbSurfaceMode.Always
                    && Hook.Length == 0
                || Mode == PllClimbSurfaceMode.EmitHook
                    && PllAuthoringValueContract.IsHook(Hook));

        static void ValidateValues(
            PllClimbSurfaceMode Mode, string Hook,
            float StaminaUsageMultiplier,
            float SpeedMultiplier, string Context)
        {
            if (!ValidValues(
                    Mode, Hook, StaminaUsageMultiplier,
                    SpeedMultiplier))
                throw new InvalidOperationException(
                    Context
                        + " requires a valid mode/hook pair and finite, nonnegative multipliers exactly representable to four decimal places.");
        }

        internal static bool IsClimbLayer(int Layer) =>
            Layer is TerrainLayer or MapLayer;

        internal static void ConfigureStaticMeshCollider(MeshCollider Collider, Mesh SharedMesh)
        {
            Collider.enabled = true;
            Collider.isTrigger = false;
            Collider.convex = false;
            Collider.contactOffset = StaticMeshContactOffset;
            Collider.cookingOptions = StaticMeshCookingOptions;
            Collider.sharedMesh = SharedMesh;
        }

        internal static bool IsSupportedColliderType(Collider Value) =>
            Value && (Value.GetType() == typeof(BoxCollider)
                || Value.GetType() == typeof(SphereCollider)
                || Value.GetType() == typeof(CapsuleCollider)
                || Value.GetType() == typeof(MeshCollider)
                || Value.GetType() == typeof(TerrainCollider));

        internal static bool HasRequiredColliderData(
            Collider Value)
        {
            if (Value.GetType() == typeof(MeshCollider))
                return ((MeshCollider)Value).sharedMesh;
            if (Value.GetType() == typeof(TerrainCollider))
                return ((TerrainCollider)Value).terrainData;
            return true;
        }

        static bool IsEnabledSolidCollider(Collider Value) =>
            Value && Value.enabled && !Value.isTrigger;

        internal static bool Eligible(Collider Value) =>
            IsEnabledSolidCollider(Value)
            && IsClimbLayer(Value.gameObject.layer);

        internal static string ClimbTargetContext(
            string Action, EditablePrefabOwner Owner,
            Transform Target) => Action + " target '"
            + PllHierarchy.NamePath(
                Owner.StageRoot.transform, Target) + "'";

        internal static void ValidateColliderMesh(Mesh Mesh, string Context)
        {
            if (!Mesh)
                throw new InvalidOperationException(
                    Context + " has no MeshFilter shared mesh.");
            if (!Mesh.isReadable)
                throw new InvalidOperationException(
                    Context
                        + " requires a readable MeshFilter shared mesh.");
            var VertexCount = Mesh.vertexCount;
            var SubMeshCount = Mesh.subMeshCount;
            if (VertexCount < 3 || SubMeshCount == 0)
                throw new InvalidOperationException(
                    Context
                        + " has no valid triangle collision geometry.");
            var Bounds = Mesh.bounds;
            ValidateFinite(Bounds.center, Context + " mesh bounds center");
            ValidateFinite(Bounds.extents, Context + " mesh bounds extents");
            if (Bounds.extents.x < 0f || Bounds.extents.y < 0f
                || Bounds.extents.z < 0f)
                throw new InvalidOperationException(
                    Context + " has invalid MeshFilter bounds.");
            var Vertices = new List<Vector3>(VertexCount);
            Mesh.GetVertices(Vertices);
            var Indices = new List<int>();
            var HasSolidTriangle = false;
            for (var Index = 0; Index < SubMeshCount; Index++)
            {
                var IndexCount = Mesh.GetIndexCount(Index);
                if (IndexCount == 0) continue;
                if (Mesh.GetTopology(Index) != MeshTopology.Triangles
                    || IndexCount < 3 || IndexCount % 3 != 0)
                    throw new InvalidOperationException(
                        Context
                            + " has non-triangle MeshFilter geometry that cannot define a MeshCollider.");
                if (HasSolidTriangle) continue;
                Mesh.GetIndices(Indices, Index);
                for (var Face = 0; Face < Indices.Count; Face += 3)
                {
                    var A = Vertices[Indices[Face]];
                    var B = Vertices[Indices[Face + 1]];
                    var C = Vertices[Indices[Face + 2]];
                    var ABX = (double)B.x - A.x;
                    var ABY = (double)B.y - A.y;
                    var ABZ = (double)B.z - A.z;
                    var ACX = (double)C.x - A.x;
                    var ACY = (double)C.y - A.y;
                    var ACZ = (double)C.z - A.z;
                    var CrossX = ABY * ACZ - ABZ * ACY;
                    var CrossY = ABZ * ACX - ABX * ACZ;
                    var CrossZ = ABX * ACY - ABY * ACX;
                    if (!double.IsFinite(CrossX) || !double.IsFinite(CrossY)
                        || !double.IsFinite(CrossZ)
                        || CrossX == 0d && CrossY == 0d && CrossZ == 0d) continue;
                    HasSolidTriangle = true;
                    break;
                }
            }
            if (!HasSolidTriangle)
                throw new InvalidOperationException(
                    Context
                        + " has no nondegenerate triangle collision geometry.");
        }

        static Dictionary<PhysicsMaterial, int> ValidateMaterialCarrier(
            PllClimbSurfaceMaterialAuthoring MaterialCarrier,
            string Definition)
        {
            if (!MaterialCarrier.enabled
                || MaterialCarrier.GetComponents<
                    PllClimbSurfaceMaterialAuthoring>().Length != 1
                || MaterialCarrier.Rules.Count == 0)
                throw new InvalidOperationException(
                    "Biome definition '" + Definition
                        + "' climb material carrier must be enabled, unique, and nonempty.");
            var MaterialIndices = new Dictionary<PhysicsMaterial, int>();
            for (var Index = 0;
                Index < MaterialCarrier.Rules.Count; Index++)
            {
                var Rule = MaterialCarrier.Rules[Index];
                var Context = "Biome definition '" + Definition
                    + "' climb material rule " + Index;
                if (Rule == null || !Rule.PhysicsMaterial
                    || !MaterialIndices.TryAdd(
                        Rule.PhysicsMaterial, Index))
                    throw new InvalidOperationException(
                        Context
                            + " requires one unique nonnull PhysicsMaterial.");
                if (!ValidMaterial(Rule.PhysicsMaterial))
                    throw new InvalidOperationException(
                        Context
                            + " must reference a persistent PhysicsMaterial asset with finite friction and bounciness.");
                ValidateValues(
                    Rule.Mode, Rule.EmitHook,
                    Rule.StaminaUsageMultiplier,
                    Rule.SpeedMultiplier, Context);
            }
            return MaterialIndices;
        }

        static int ResolveMaterialRule(
            GameObject Target, IReadOnlyList<Collider> Colliders,
            IReadOnlyDictionary<PhysicsMaterial, int> MaterialIndices,
            string Definition)
        {
            var ResolvedRule = -1;
            var Matched = 0;
            foreach (var Collider in Colliders)
            {
                int RuleIndex;
                if (!Collider.sharedMaterial
                    || !MaterialIndices.TryGetValue(
                        Collider.sharedMaterial, out RuleIndex))
                    continue;
                if (ResolvedRule >= 0 && ResolvedRule != RuleIndex)
                    throw new InvalidOperationException(
                        "Biome definition '" + Definition
                            + "' climb fallback GameObject '"
                            + Target.name
                            + "' mixes PhysicsMaterial rules.");
                ResolvedRule = RuleIndex;
                Matched++;
            }
            if (Matched != 0 && Matched != Colliders.Count)
                throw new InvalidOperationException(
                    "Biome definition '" + Definition
                        + "' climb fallback GameObject '"
                        + Target.name
                        + "' mixes matched and unmatched Colliders.");
            return ResolvedRule;
        }

        static bool ValidOwnership(
            PrefabNode Node, Transform Value,
            SourceIdentity Identity)
        {
            if (Node.IsRoot)
                return (TransformOwnership.Contains(
                            Identity.Floor, Value)
                        || TransformOwnership.Contains(
                            Identity.Campfire, Value))
                    && !TransformOwnership.HasPropSpawnerInPath(
                        Node.SourceRoot.transform, Value);
            return !TransformOwnership.HasPropSpawnerInPath(
                    Node.SourceRoot.transform, Value)
                && !ReservedSubtree(
                    Node.SourceRoot.transform, Value);
        }

        static bool ValidMultipliers(
            float StaminaUsageMultiplier,
            float SpeedMultiplier) =>
            StaminaUsageMultiplier >= 0f
            && ExactManifestNumber(StaminaUsageMultiplier)
            && SpeedMultiplier >= 0f
            && ExactManifestNumber(SpeedMultiplier);

    }

    internal sealed class ClimbSurfaceAuthoringCatalog
    {
        internal ClimbSurfaceAuthoringCatalog(
            PllClimbSurfaceAuthoring[] Surfaces,
            PllClimbSurfaceMaterialAuthoring MaterialAuthoring,
            IReadOnlyDictionary<GameObject, int> MaterialTargets)
        {
            this.Surfaces = Surfaces;
            this.MaterialAuthoring = MaterialAuthoring;
            this.MaterialTargets = MaterialTargets;
        }

        internal PllClimbSurfaceAuthoring[] Surfaces { get; }
        internal PllClimbSurfaceMaterialAuthoring MaterialAuthoring { get; }
        internal IReadOnlyDictionary<GameObject, int> MaterialTargets { get; }
    }
}
