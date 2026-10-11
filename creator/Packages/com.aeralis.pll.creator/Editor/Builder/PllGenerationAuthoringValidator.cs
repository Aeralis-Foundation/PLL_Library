using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllGenerationAuthoringValidator
    {
        internal GenerationAuthoring Validate(SourceIdentity Identity)
        {
            var Graph = Identity.Graph;
            var Root = Identity.Root;
            var RootNode = Graph.ByPath[Graph.RootPath];
            var Authoring = new GenerationAuthoring(
                DiscoverComponents<PllVariationAuthoring>(RootNode),
                DiscoverGraphComponents<PllEffectZoneAuthoring>(Graph),
                DiscoverGraphComponents<PllPropZoneAuthoring>(Graph),
                DiscoverGraphComponents<PLLNoPropSpawn>(Graph),
                DiscoverGraphComponents<PllChildPropAuthoring>(Graph),
                DiscoverComponents<PLLFinalGeneration>(RootNode),
                DiscoverComponents<PLLCriticalGeneration>(RootNode),
                DiscoverComponents<PllEntityZoneAuthoring>(RootNode),
                DiscoverComponents<PllHazardZoneAuthoring>(RootNode),
                DiscoverComponents<PllAmuletSpawnerAuthoring>(RootNode),
                DiscoverGraphComponents<PllHazardAuthoring>(Graph),
                DiscoverGraphComponents<PllEntityAuthoring>(Graph));
            RequireComponents(Root, Authoring.Variations, Identity.Label, "variationDefinitions");
            RequireGraphComponents(Graph, Authoring.EffectZones, Identity.Label, "effect zones");
            RequireGraphComponents(Graph, Authoring.PropZones, Identity.Label, "prop zones");
            RequireGraphComponents(Graph, Authoring.NoPropSpawn, Identity.Label, "no-prop markers");
            RequireGraphComponents(Graph, Authoring.ChildProps, Identity.Label, "child props");
            RequireGraphComponents(Graph, Authoring.Hazards, Identity.Label, "Hazards");
            RequireGraphComponents(Graph, Authoring.Entities, Identity.Label, "Entities");
            foreach (var Source in Authoring.Hazards)
            {
                if (PllHazardValueContract.Failure(Source) is string Failure) throw new InvalidOperationException(Failure);
                if (Source.GetComponentInChildren<CollisionModifier>(true))
                    throw new InvalidOperationException("Hazard source must not contain creator-authored native contact mechanics.");
                var Node = Graph.ByPath[AssetDatabase.GetAssetPath(Source)];
                RequireStableOwned(Node.SourceRoot.transform, Source.transform, "Hazard");
                if (Node.IsRoot) RequireFeatureOwned(Source.transform, Identity.Floor, Identity.Campfire, "Hazard");
            }
            foreach (var Source in Authoring.Entities)
            {
                if (PllEntityValueContract.Failure(Source) is string Failure) throw new InvalidOperationException(Failure);
                var Node = Graph.ByPath[AssetDatabase.GetAssetPath(Source)];
                RequireStableOwned(Node.SourceRoot.transform, Source.transform, "Entity");
                if (Node.IsRoot) RequireFeatureOwned(Source.transform, Identity.Floor, Identity.Campfire, "Entity");
            }

            ValidateVariations(Root, Authoring.Variations, Identity.Label);
            ValidateFinalGeneration(
                Root, Identity.Floor, Identity.Campfire, Authoring.FinalGeneration, Identity.Label);
            ValidateEffectZones(Identity, Authoring);
            PllActorZoneAuthoringValidator.Validate(Root, Identity.Floor, Identity.Campfire,
                Authoring.EntityZones, Authoring.HazardZones, Identity.Label);
            foreach (var Amulet in Authoring.AmuletSpawners)
            {
                ValidateAmulet(Amulet, Identity.Floor);
                if (Amulet.Category == PllAmuletOpportunityCategory.InheritBiome)
                    PllAmuletSpawnerAuthoring.ResolveCategory(Amulet.Category, Identity.Definition.Behavior);
            }
            if (PllAmuletSpawnerAuthoring.CapturePotential(Identity.Floor, Authoring.Variations,
                    Authoring.AmuletSpawners, Source => (Source.transform,
                        PllAmuletSpawnerAuthoring.ResolveCategory(Source.Category, Identity.Definition.Behavior))).Maximum > 1)
                throw new InvalidOperationException("An occurrence can contain only one selected amulet source.");
            ValidatePropZones(Root, Identity.Floor, Identity.Campfire,
                ComponentsInNode(RootNode, Authoring.PropZones), Identity.Label);
            ValidateNoPropSpawn(Root, Identity.Floor, Identity.Campfire,
                ComponentsInNode(RootNode, Authoring.NoPropSpawn), Identity.Label);
            ValidateChildProps(Root, Identity.Floor, Identity.Campfire,
                ComponentsInNode(RootNode, Authoring.ChildProps), Identity.Label);
            ValidateCandidateAuthoring(Identity, Authoring);
            foreach (var Marker in Authoring.CriticalGeneration)
            {
                var Context = "Critical Generation '" + PllHierarchy.NamePath(Root.transform, Marker.transform) + "'";
                PllCriticalGenerationContract.ValidateMarker(Marker, Context);
                PllCriticalGenerationContract.ValidateScope(Identity.Anchors, Marker.transform, Context, true);
            }
            return Authoring;
        }

        internal static void ValidateAmulet(PllAmuletSpawnerAuthoring Source, Transform Floor)
        {
            if (!Source.enabled || !TransformOwnership.Contains(Floor, Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(Floor, Source.transform))
                throw new InvalidOperationException("Amulet Spawner must be enabled beneath FloorRoot and outside a PropSpawner hierarchy.");
            ValidateZoneVolume(Source.Volume, "Amulet Spawner");
            if (Source.Category != PllAmuletOpportunityCategory.InheritBiome)
                PllAmuletSpawnerAuthoring.ResolveCategory(Source.Category, PllBiomeBehavior.Authored);
            if (Source.Presentation is PllCustomAmuletPedestal Custom) Custom.RequireValid();
            else if (Source.Presentation is not PllNativeAmuletPedestal)
                throw new InvalidOperationException("Amulet Spawner has an unsupported presentation.");
        }

        static void ValidateEffectZones(SourceIdentity Identity, GenerationAuthoring Authoring)
        {
            var Root = Identity.Root;
            var EntryRoots = VariationEntryRoots(Authoring.Variations);
            var Roots = new List<Transform>();
            foreach (var Zone in Authoring.EffectZones)
            {
                var Node = OwnerNode(Identity.Graph, Zone, "Effect Zone");
                var Owner = Node.SourceRoot.transform;
                var Context = "Biome definition '" + Identity.Label + "' effect zone '"
                    + Node.SourcePath + ":" + PllHierarchy.NamePath(Owner, Zone.transform) + "'";
                if (Node.IsRoot)
                    RequireFeatureOwned(Zone.transform, Identity.Floor, Identity.Campfire, Context);
                else if (Zone.PropTriggers.Count != 0 || Zone.FogTriggers.Count == 0 && Zone.NativeSourceTriggers.Count == 0 && Zone.ActorSourceTriggers.Count == 0)
                    throw new InvalidOperationException(Context + " generated actions require typed source or fog triggers and no generation triggers.");
                RequireStableOwned(Owner, Zone.transform, Context);
                if (Zone.Root != Zone.gameObject || Zone.transform == Identity.Floor
                    || Zone.transform == Identity.Campfire || !Zone.gameObject.activeSelf
                    || !(Node.IsRoot ? DiscoverableWhenConfigured(Owner, Zone.transform, EntryRoots)
                        : TransformOwnership.DiscoverableWhenActivated(Owner, Zone.transform)))
                    throw new InvalidOperationException(Context
                        + " Root must be its active stable component GameObject.");
                if (Roots.Any(Existing => TransformOwnership.Overlaps(Existing, Zone.transform)))
                    throw new InvalidOperationException(Context + " overlaps another effect-zone root.");
                Roots.Add(Zone.transform);
                ValidateZoneVolume(Zone.Volume, Context + " Volume");
                var ActorEdges = new HashSet<(PllEffectZoneTransition, MonoBehaviour)>();
                foreach (var Trigger in Zone.ActorSourceTriggers)
                {
                    if (Trigger is null || !Trigger.Target
                        || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                        || Trigger.Operation is not (PllNativeSourceOperation.Activate or PllNativeSourceOperation.Deactivate)
                        || Trigger.Target is PllHazardAuthoring Hazard && (Hazard.Behavior is PllPluginHazardBehavior || !Authoring.Hazards.Contains(Hazard))
                        || Trigger.Target is PllEntityAuthoring Entity && (Entity.Behavior is PllPluginEntityBehavior || !Authoring.Entities.Contains(Entity))
                        || Trigger.Target is not (PllHazardAuthoring or PllEntityAuthoring)
                        || !ActorEdges.Add((Trigger.Transition, Trigger.Target)))
                        throw new InvalidOperationException("Actor source action needs one admitted reversible native target per edge.");
                }
                if (PllAuthoringValueContract.TransitionFailure(Zone.Transition.CoverDelaySeconds) is { } Failure)
                    throw new InvalidOperationException(Context + " " + Failure);

                if (Zone.OccupancyHook.Length != 0
                    && !PllAuthoringValueContract.IsHook(Zone.OccupancyHook))
                    throw new InvalidOperationException(Context
                        + " Occupancy Hook must contain 1 to 128 UTF-16 code units and no whitespace, control character, '\\', '>', or malformed surrogate pair.");
            }
        }

        static void ValidateCandidateAuthoring(SourceIdentity Identity, GenerationAuthoring Authoring)
        {
            foreach (var Node in Identity.Graph.Nodes)
            {
                if (Node.IsRoot) continue;
                var Root = Node.SourceRoot;
                var Context = "Biome definition '" + Identity.Label + "' candidate '" + Node.SourcePath + "'";
                if (Root.GetComponentsInChildren<PllVariationAuthoring>(true).Length != 0
                    || Root.GetComponentsInChildren<PLLFinalGeneration>(true).Length != 0
                    || Root.GetComponentsInChildren<PLLCriticalGeneration>(true).Length != 0
                    || Root.GetComponentsInChildren<PLLForceSpawner>(true).Length != 0
                    || Root.GetComponentsInChildren<PllEntityZoneAuthoring>(true).Length != 0
                    || Root.GetComponentsInChildren<PllHazardZoneAuthoring>(true).Length != 0)
                    throw new InvalidOperationException(Context
                        + " cannot contain whole-biome variations, actor zones, a force spawner, or Critical/Final Generation roots.");
                ValidateCandidatePropZones(Root, ComponentsInNode(Node, Authoring.PropZones), Context);
                foreach (var Marker in ComponentsInNode(Node, Authoring.NoPropSpawn))
                {
                    if (!Marker.enabled || TransformOwnership.HasPropSpawnerInPath(Root.transform, Marker.transform))
                        throw new InvalidOperationException(Context
                            + " contains an invalid no-prop marker.");
                    ValidateTransformPath(Root.transform, Marker.transform, Context + " no-prop marker");
                }
                foreach (var Child in ComponentsInNode(Node, Authoring.ChildProps))
                    ValidateCandidateChildProps(Root, Child, Context);
            }
            foreach (var Node in Identity.Graph.Nodes)
                foreach (var SpawnerGroup in Node.Edges.Where(Value => Value.Spawner)
                    .GroupBy(Value => Value.Spawner))
                {
                    var Timings = new HashSet<PllChildPropTiming>();
                    foreach (var Edge in SpawnerGroup)
                    {
                        var Child = Identity.Graph.ByPath[Edge.TargetPath].SourceRoot
                            .GetComponent<PllChildPropAuthoring>();
                        if (Child) Timings.Add(Child.Timing);
                    }
                    if (Timings.Count > 1)
                        throw new InvalidOperationException("Biome definition '" + Identity.Label
                            + "' PropSpawner selects child-prop candidates with conflicting timings.");
                    if (Timings.Count == 0) continue;
                    var Spawner = SpawnerGroup.Key;
                    var Immediate = (Spawner.modifiers ?? new List<PropSpawnerMod>())
                        .OfType<PSM_ChildSpawners>().ToArray();
                    var Late = (Spawner.postSpawnBehaviors ?? new List<PostSpawnBehavior>())
                        .OfType<PSB_ChildSpawners>().ToArray();
                    var Timing = Timings.Single();
                    if (Immediate.Length > 1 || Late.Length > 1
                        || Immediate.Any(Value => Value.mute) || Late.Any(Value => Value.mute)
                        || Timing == PllChildPropTiming.Immediate && Late.Length != 0
                        || Timing == PllChildPropTiming.Late && Immediate.Length != 0)
                        throw new InvalidOperationException("Biome definition '" + Identity.Label
                            + "' PropSpawner has a conflicting native child-spawner mechanism.");
                }
        }

        static void ValidateVariations(
            GameObject Root, IReadOnlyList<PllVariationAuthoring> Variations, string Definition)
        {
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            var OwnerCounts = new Dictionary<Transform, int>();
            var EntryRoots = new HashSet<GameObject>();
            var Owners = new HashSet<Transform>();
            foreach (var Variation in Variations)
            {
                var Context = "Biome definition '" + Definition + "' variation '" + Variation.Id + "'";
                if (!Variation.enabled)
                    throw new InvalidOperationException(Context + " authoring component must be enabled.");
                if (!PllAuthoringValueContract.IsLocalId(Variation.Id))
                    throw new InvalidOperationException(Context
                        + " ID must contain 1 to 64 well-formed UTF-16 code units without path separators, '>', whitespace, or control characters.");
                if (!Ids.Add(Variation.Id))
                    throw new InvalidOperationException("Biome definition '" + Definition
                        + "' contains duplicate variation ID '" + Variation.Id + "'.");
                var Owner = Variation.transform;
                RequireStableOwned(Root.transform, Owner, Context);
                int Count;
                OwnerCounts.TryGetValue(Owner, out Count);
                if (++Count > 4)
                    throw new InvalidOperationException(Context + " exceeds the four native variation groups on one owner.");
                OwnerCounts[Owner] = Count;
                Owners.Add(Owner);
                if (Variation.Entries.Count < 2)
                    throw new InvalidOperationException(Context + " must contain at least two entries.");
                var EntryIds = new HashSet<string>(StringComparer.Ordinal);
                var Positive = false;
                for (var i = 0; i < Variation.Entries.Count; i++)
                {
                    var Entry = Variation.Entries[i];
                    if (Entry == null || !Entry.Root)
                        throw new InvalidOperationException(Context + " entry " + i + " has no root.");
                    if (Entry.Root.transform.parent != Owner)
                        throw new InvalidOperationException(Context + " entry " + i
                            + " root must be a direct child of the variation owner.");
                    if (!EntryRoots.Add(Entry.Root))
                        throw new InvalidOperationException(Context + " entry " + i + " root belongs to another group.");
                    if (!PllAuthoringValueContract.IsVariationEntryId(Entry.EntryId)
                        || !EntryIds.Add(Entry.EntryId))
                        throw new InvalidOperationException(Context + " entry " + i
                            + " has a missing, invalid, or duplicate identity. Select the Variation Group and use Prepare Entry IDs.");
                    if (!PllAuthoringValueContract.IsVariationWeight(Entry.Weight))
                        throw new InvalidOperationException(Context + " entry " + i
                            + " weight must be exactly encodable and between 0 and 100.");
                    Positive |= Entry.Weight > 0f;
                }
                if (!Positive)
                    throw new InvalidOperationException(Context + " has no enabled entry. Set at least one variation weight above zero.");
            }
            var OwnerArray = Owners.ToArray();
            for (var i = 0; i < OwnerArray.Length; i++)
            {
                var Owner = OwnerArray[i];
                foreach (var Nested in Owner.GetComponentsInChildren<PllVariationAuthoring>(true))
                    if (Nested.transform != Owner)
                        throw new InvalidOperationException("Biome definition '" + Definition
                            + "' contains nested variation selector owners.");
                if (Owner.GetComponentsInChildren<VariantObject>(true).Length != 0
                    || Owner.GetComponentsInChildren<VariationSwapper>(true).Length != 0
                    || Owner.GetComponentsInChildren<VariantObjectSelector>(true).Length != 0)
                    throw new InvalidOperationException("Biome definition '" + Definition
                        + "' variation owner '" + PllHierarchy.NamePath(Root.transform, Owner)
                        + "' contains a competing native variation component.");
                for (var j = i + 1; j < OwnerArray.Length; j++)
                    if (TransformOwnership.Contains(Owner, OwnerArray[j]) || TransformOwnership.Contains(OwnerArray[j], Owner))
                        throw new InvalidOperationException("Biome definition '" + Definition
                            + "' variation selector owners cannot occupy ancestor subtrees.");
            }
        }

        static void ValidateFinalGeneration(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PLLFinalGeneration> Markers, string Definition)
        {
            var Roots = new Transform[Markers.Count];
            for (var i = 0; i < Markers.Count; i++)
            {
                var Marker = Markers[i];
                var Context = "Biome definition '" + Definition + "' final-generation root '"
                    + PllHierarchy.NamePath(Root.transform, Marker.transform) + "'";
                RequireFeatureOwned(Marker.transform, Floor, Campfire, Context);
                RequireStableOwned(Root.transform, Marker.transform, Context);
                if (!Marker.enabled || !Marker.gameObject.activeSelf
                    || Marker.GetComponents<PLLFinalGeneration>().Length != 1
                    || Marker.GetComponentsInChildren<PLLFinalGeneration>(true).Length != 1)
                    throw new InvalidOperationException(Context
                        + " must be an active source root with exactly one non-overlapping marker.");
                Roots[i] = Marker.transform;
            }
            RequireDisjointRoots(Roots,
                "Biome definition '" + Definition + "' final-generation roots cannot overlap.");
        }

        static void ValidateCandidatePropZones(
            GameObject Root, IReadOnlyList<PllPropZoneAuthoring> Zones, string Context)
        {
            var Bound = new HashSet<PropSpawner>();
            foreach (var Zone in Zones)
            {
                var ZoneContext = Context + " prop zone '" + PllHierarchy.NamePath(Root.transform, Zone.transform) + "'";
                if (Zone.Root != Zone.gameObject || TransformOwnership.HasPropSpawnerInPath(Root.transform, Zone.transform))
                    throw new InvalidOperationException(ZoneContext + " must be an ordinary owned GameObject.");
                ValidateTransformPath(Root.transform, Zone.transform, ZoneContext);
                ValidateZoneVolume(Zone.Volume, ZoneContext + " Volume");
                if (Zone.Spawners.Count == 0)
                    throw new InvalidOperationException(ZoneContext + " must reference at least one spawner.");
                var Local = new HashSet<PropSpawner>();
                foreach (var Spawner in Zone.Spawners)
                    if (!Spawner || !TransformOwnership.Contains(Root.transform, Spawner.transform) || !Local.Add(Spawner)
                        || !Bound.Add(Spawner) || TransformOwnership.Contains(Spawner.transform, Zone.transform))
                        throw new InvalidOperationException(ZoneContext
                            + " contains a missing, foreign, duplicate, or multiply bound spawner.");
            }
        }

        static void ValidateCandidateChildProps(
            GameObject CandidateRoot, PllChildPropAuthoring Value, string Context)
        {
            var ChildContext = Context + " child props '" + PllHierarchy.NamePath(CandidateRoot.transform, Value.transform) + "'";
            if (Value.gameObject != CandidateRoot || Value.Root != CandidateRoot || Value.GetComponent<PropSpawner>())
                throw new InvalidOperationException(ChildContext + " must be attached to the candidate prefab root.");
            if (!Enum.IsDefined(typeof(PllChildPropTiming), Value.Timing)
                || !ManifestNumber(Value.ChancePerPoint) || Value.ChancePerPoint < 0f || Value.ChancePerPoint > 1f
                || Value.Points.Count == 0 || Value.Props.Count == 0)
                throw new InvalidOperationException(ChildContext + " has invalid timing, chance, points, or props.");
            var Points = new HashSet<Transform>();
            foreach (var Point in Value.Points)
            {
                if (!Point || Point == Value.transform || !Point.IsChildOf(Value.transform) || !Points.Add(Point)
                    || Point.childCount != 0 || Point.GetComponents<Component>().Length != 1
                    || !TransformOwnership.DiscoverableWhenActivated(Value.transform, Point))
                    throw new InvalidOperationException(ChildContext
                        + " Points must be distinct active-relative empty strict descendants.");
                ValidateTransformPath(Value.transform, Point, ChildContext + " Point");
            }
            foreach (var Prop in Value.Props) RequirePrefabAsset(Prop, ChildContext + " Prop");
        }

        static void ValidatePropZones(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PllPropZoneAuthoring> Zones, string Definition)
        {
            var Bound = new HashSet<PropSpawner>();
            foreach (var Zone in Zones)
            {
                var Context = "Biome definition '" + Definition + "' prop zone '"
                    + PllHierarchy.NamePath(Root.transform, Zone.transform) + "'";
                RequireFeatureOwned(Zone.transform, Floor, Campfire, Context);
                RequireStableOwned(Root.transform, Zone.transform, Context);
                if (Zone.Root != Zone.gameObject)
                    throw new InvalidOperationException(Context + " Root must be the component GameObject.");
                ValidateZoneVolume(Zone.Volume, Context + " Volume");
                if (Zone.Spawners.Count == 0)
                    throw new InvalidOperationException(Context + " must reference at least one spawner.");
                var Owner = TransformOwnership.Contains(Floor, Zone.transform) ? Floor : Campfire;
                var Local = new HashSet<PropSpawner>();
                for (var i = 0; i < Zone.Spawners.Count; i++)
                {
                    var Spawner = Zone.Spawners[i];
                    if (!Spawner || !Local.Add(Spawner) || !Bound.Add(Spawner))
                        throw new InvalidOperationException(Context + " contains a missing, duplicate, or multiply bound spawner.");
                    if (!TransformOwnership.Contains(Owner, Spawner.transform) || TransformOwnership.Contains(Spawner.transform, Zone.transform))
                        throw new InvalidOperationException(Context + " spawner " + i
                            + " must share its feature owner and cannot own the zone.");
                    if (Spawner.constraints != null && Spawner.constraints.Any(Value =>
                            Value is PllPropZoneConstraint || Value is PllNoPropSpawnConstraint))
                        throw new InvalidOperationException(Context + " spawner " + i
                            + " contains a creator-authored generated PLL constraint.");
                }
            }
        }

        static void ValidateNoPropSpawn(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PLLNoPropSpawn> Markers, string Definition)
        {
            foreach (var Marker in Markers)
            {
                var Context = "Biome definition '" + Definition + "' no-prop marker '"
                    + PllHierarchy.NamePath(Root.transform, Marker.transform) + "'";
                RequireFeatureOwned(Marker.transform, Floor, Campfire, Context);
                if (!Marker.enabled || TransformOwnership.HasPropSpawnerInPath(Root.transform, Marker.transform))
                    throw new InvalidOperationException(Context + " must be enabled permanent geometry outside a PropSpawner.");
            }
        }

        internal static void ValidateNoPropCoverage(GenerationAuthoring Authoring,
            IReadOnlyList<CollisionGeometry> Geometry)
        {
            foreach (var Marker in Authoring.NoPropSpawn)
            {
                if (Marker.GetComponentsInChildren<Collider>(true).Any(Value => Value && Value.enabled))
                    continue;
                var Covered = false;
                foreach (var Surface in Geometry)
                    if (Surface.GeneratedMesh && TransformOwnership.Contains(Marker.transform, Surface.Target))
                    {
                        Covered = true;
                        break;
                    }
                if (!Covered)
                    throw new InvalidOperationException("No Prop Spawn '" + Marker.name
                        + "' covers no source or staged Collider. Move it to solid geometry.");
            }
        }

        static void ValidateChildProps(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PllChildPropAuthoring> Values, string Definition)
        {
            foreach (var Value in Values)
            {
                var Context = "Biome definition '" + Definition + "' child props '"
                    + PllHierarchy.NamePath(Root.transform, Value.transform) + "'";
                RequireFeatureOwned(Value.transform, Floor, Campfire, Context);
                RequireStableOwned(Root.transform, Value.transform, Context);
                if (Value.Root != Value.gameObject || Value.GetComponent<PropSpawner>())
                    throw new InvalidOperationException(Context + " Root must be its ordinary non-spawner GameObject.");
                if (!Enum.IsDefined(typeof(PllChildPropTiming), Value.Timing))
                    throw new InvalidOperationException(Context + " has unknown Timing.");
                if (!ManifestNumber(Value.ChancePerPoint) || Value.ChancePerPoint < 0f || Value.ChancePerPoint > 1f)
                    throw new InvalidOperationException(Context + " ChancePerPoint must be within zero and one.");
                if (Value.Points.Count == 0 || Value.Props.Count == 0)
                    throw new InvalidOperationException(Context + " requires at least one Point and Prop.");
                var Points = new HashSet<Transform>();
                foreach (var Point in Value.Points)
                {
                    if (!Point || Point == Value.transform || !Point.IsChildOf(Value.transform) || !Points.Add(Point)
                        || Point.childCount != 0 || Point.GetComponents<Component>().Length != 1
                        || !TransformOwnership.DiscoverableWhenActivated(Value.transform, Point))
                        throw new InvalidOperationException(Context
                            + " Points must be distinct active-relative empty strict descendants.");
                    ValidateTransformPath(Value.transform, Point, Context + " Point");
                }
                foreach (var Prop in Value.Props)
                    RequirePrefabAsset(Prop, Context + " Prop");
            }
        }

        internal static void ValidateZoneVolume(PllZoneVolume Volume, string Context)
        {
            if (PllAuthoringValueContract.ZoneVolumeFailure(Volume) is { } Failure)
                throw new InvalidOperationException(Failure.Message + " " + Context + ".");
        }

        internal static Bounds TransformedBounds(Transform Root, PllZoneVolume Volume)
        {
            var Half = Volume.Shape == PllZoneShape.Box ? Volume.Size * 0.5f : Vector3.one * Volume.Radius;
            var Bounds = new Bounds(Root.TransformPoint(Volume.Center - Half), Vector3.zero);
            for (var X = -1; X <= 1; X += 2)
                for (var Y = -1; Y <= 1; Y += 2)
                    for (var Z = -1; Z <= 1; Z += 2)
                        Bounds.Encapsulate(Root.TransformPoint(Volume.Center
                            + Vector3.Scale(Half, new Vector3(X, Y, Z))));
            return Bounds;
        }

        internal static void ValidateNonnegative(float Value, string Context)
        {
            if (!ManifestNumber(Value) || Value < 0f)
                throw new InvalidOperationException(Context + " must be finite and nonnegative.");
        }
    }

    internal sealed class GenerationAuthoring
    {
        internal GenerationAuthoring(
            PllVariationAuthoring[] Variations, PllEffectZoneAuthoring[] EffectZones,
            PllPropZoneAuthoring[] PropZones, PLLNoPropSpawn[] NoPropSpawn,
            PllChildPropAuthoring[] ChildProps, PLLFinalGeneration[] FinalGeneration,
            PLLCriticalGeneration[] CriticalGeneration, PllEntityZoneAuthoring[] EntityZones,
            PllHazardZoneAuthoring[] HazardZones,
            PllAmuletSpawnerAuthoring[] AmuletSpawners,
            PllHazardAuthoring[] Hazards, PllEntityAuthoring[] Entities)
        {
            this.Variations = Variations;
            this.EffectZones = EffectZones;
            this.PropZones = PropZones;
            this.NoPropSpawn = NoPropSpawn;
            this.ChildProps = ChildProps;
            this.FinalGeneration = FinalGeneration;
            this.CriticalGeneration = CriticalGeneration;
            this.EntityZones = EntityZones;
            this.HazardZones = HazardZones;
            this.AmuletSpawners = AmuletSpawners;
            this.Hazards = Hazards; this.Entities = Entities;
        }

        internal PllVariationAuthoring[] Variations { get; }
        internal PllEffectZoneAuthoring[] EffectZones { get; }
        internal PllPropZoneAuthoring[] PropZones { get; }
        internal PLLNoPropSpawn[] NoPropSpawn { get; }
        internal PllChildPropAuthoring[] ChildProps { get; }
        internal PLLFinalGeneration[] FinalGeneration { get; }
        internal PLLCriticalGeneration[] CriticalGeneration { get; }
        internal PllEntityZoneAuthoring[] EntityZones { get; }
        internal PllHazardZoneAuthoring[] HazardZones { get; }
        internal PllAmuletSpawnerAuthoring[] AmuletSpawners { get; }
        internal PllHazardAuthoring[] Hazards { get; }
        internal PllEntityAuthoring[] Entities { get; }
    }
}
