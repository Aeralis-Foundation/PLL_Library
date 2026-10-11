using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using PLL.Library;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllFixtureAuthoringValidator
    {
        internal FixtureAuthoring Validate(SourceIdentity Identity, GenerationAuthoring Generation)
        {
            var Fixtures = new FixtureAuthoring(
                DiscoverGraphComponents<PllLuggageAuthoring>(Identity.Graph),
                DiscoverGraphComponents<PllCampfireAuthoring>(Identity.Graph),
                DiscoverComponents<PllStatueAuthoring>(Identity.Graph.ByPath[Identity.Graph.RootPath]),
                DiscoverGraphComponents<PllItemSpawnerAuthoring>(Identity.Graph),
                DiscoverGraphComponents<PllRestPointAuthoring>(Identity.Graph),
                DiscoverGraphComponents<PllHangingRopeAuthoring>(Identity.Graph));
            RequireGraphComponents(Identity.Graph, Fixtures.Luggage, Identity.Label, "luggageDefinitions");
            RequireGraphComponents(Identity.Graph, Fixtures.ItemSpawners, Identity.Label, "item spawners");

            ValidateCampfires(Identity, Generation, Fixtures);
            ValidateStatue(Identity, Generation, Fixtures);
            ValidateLuggage(Identity, Generation, Fixtures);
            PllClimbingFixtureCompiler.Validate(Identity, Fixtures);
            var RootNode = Identity.Graph.ByPath[Identity.Graph.RootPath];
            ValidateItemSpawners(Identity.Root, Identity.Floor, Identity.Campfire,
                ComponentsInNode(RootNode, Fixtures.ItemSpawners), Identity.Label);
            ValidateCandidateAuthoring(Identity, Fixtures);
            return Fixtures;
        }

        static void ValidateCandidateAuthoring(SourceIdentity Identity, FixtureAuthoring Fixtures)
        {
            foreach (var Node in Identity.Graph.Nodes)
            {
                if (Node.IsRoot) continue;
                var Root = Node.SourceRoot;
                var Context = "Biome definition '" + Identity.Label + "' candidate '" + Node.SourcePath + "'";
                if (Root.GetComponentsInChildren<PllStatueAuthoring>(true).Length != 0)
                    throw new InvalidOperationException(Context + " cannot contain a statue.");
                ValidateCandidateItemSpawners(
                    Root, ComponentsInNode(Node, Fixtures.ItemSpawners), Context);
            }
        }

        static void ValidateCandidateItemSpawners(
            GameObject CandidateRoot, IReadOnlyList<PllItemSpawnerAuthoring> Spawners, string Context)
        {
            var Roots = new List<Transform>();
            foreach (var Value in Spawners)
            {
                var ItemContext = Context + " item spawner '"
                    + PllHierarchy.NamePath(CandidateRoot.transform, Value.transform) + "'";
                if (Value.Root != Value.gameObject || Roots.Any(Existing => TransformOwnership.Overlaps(Existing, Value.transform)))
                    throw new InvalidOperationException(ItemContext + " has an invalid or overlapping Root.");
                ValidateTransformPath(CandidateRoot.transform, Value.transform, ItemContext);
                Roots.Add(Value.transform);
                ValidateItemSpawnerFields(CandidateRoot, Value, ItemContext);
            }
        }

        static void ValidateItemSpawners(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PllItemSpawnerAuthoring> Values, string Definition)
        {
            var ItemRoots = new List<Transform>();
            foreach (var Value in Values)
            {
                var Context = "Biome definition '" + Definition + "' item spawner '"
                    + PllHierarchy.NamePath(Root.transform, Value.transform) + "'";
                RequireFeatureOwned(Value.transform, Floor, Campfire, Context);
                RequireStableOwned(Root.transform, Value.transform, Context);
                if (Value.Root != Value.gameObject)
                    throw new InvalidOperationException(Context + " Root must be the component GameObject.");
                if (ItemRoots.Any(Existing => TransformOwnership.Overlaps(Existing, Value.transform)))
                    throw new InvalidOperationException(Context + " overlaps another item-spawner root.");
                ItemRoots.Add(Value.transform);
                ValidateItemSpawnerFields(Root, Value, Context);
            }
        }

        static void ValidateItemSpawnerFields(
            GameObject Owner, PllItemSpawnerAuthoring Value, string Context)
        {
            foreach (var Component in Value.GetComponents<Component>())
            {
                if (!Component || ReferenceEquals(Component, Value) || Component is Transform) continue;
                if (Component is Spawner || Component is Peak.SpawnedItemTracker
                    || Component is ISpawner || Component is PhotonView)
                    throw new InvalidOperationException(Context + " source Root contains native spawning mechanics.");
            }
            RequirePrefabAsset(Value.Item, Context + " Item", false);
            var Items = Value.Item.GetComponentsInChildren<Item>(true);
            if (Items.Length != 1)
                throw new InvalidOperationException(Context + " Item must contain exactly one PEAK Item component.");
            if (Value.SpawnPoints.Count == 0)
                throw new InvalidOperationException(Context + " requires at least one SpawnPoint.");
            var Points = new HashSet<Transform>();
            foreach (var Point in Value.SpawnPoints)
            {
                if (!Point || !TransformOwnership.Contains(Value.transform, Point) || !TransformOwnership.Contains(Owner.transform, Point) || !Points.Add(Point))
                    throw new InvalidOperationException(Context + " SpawnPoints must be distinct owned Transforms.");
                ValidateTransformPath(Owner.transform, Point, Context + " SpawnPoint");
            }
            if (!ManifestNumber(Value.Chance) || Value.Chance < 0f || Value.Chance > 1f
                || Value.PlayersInRoomRequirement < 0 || Value.BelowAscentRequirement < -1)
                throw new InvalidOperationException(Context + " contains an invalid chance or room/ascent requirement.");
        }

        static void RequireUnconditionalStable(
            Transform Root, Transform Value, GenerationAuthoring Generation, string Context)
        {
            RequireStableOwned(Root, Value, Context);
            RejectConditionalOwnership(Value, Generation, Context);
        }

        static void RejectConditionalOwnership(
            Transform Value, GenerationAuthoring Generation, string Context)
        {
            foreach (var Entry in VariationEntryRoots(Generation.Variations))
                if (TransformOwnership.Contains(Entry, Value))
                    throw new InvalidOperationException(Context + " cannot belong to a variation branch.");
            foreach (var Target in Generation.FinalGeneration)
                if (Target && TransformOwnership.Contains(Target.transform, Value))
                    throw new InvalidOperationException(Context + " cannot belong to a final-generation target.");
        }

        static void ValidateCampfires(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures)
        {
            var Template = Identity.Root;
            var CampfireRoot = Identity.Campfire;
            var Biome = Identity.Label;
            var MarkersByPath = Fixtures.Campfires.GroupBy(Value => OwnerNode(Identity.Graph, Value,
                    "Biome definition '" + Biome + "' campfire authoring").SourcePath)
                .ToDictionary(Group => Group.Key, Group => Group.ToArray(), StringComparer.Ordinal);
            foreach (var Marker in Fixtures.Campfires)
                ValidateCampfireMarker(Identity, Generation, Marker);

            PllCampfireAuthoring[] Stable;
            MarkersByPath.TryGetValue(Identity.Graph.RootPath, out Stable);
            Stable = Stable ?? Array.Empty<PllCampfireAuthoring>();
            var CandidatePaths = new HashSet<string>(MarkersByPath.Keys.Where(Path =>
                !string.Equals(Path, Identity.Graph.RootPath, StringComparison.Ordinal)), StringComparer.Ordinal);
            if (Stable.Length == 1 && CandidatePaths.Count == 0) return;
            if (Stable.Length != 0)
                throw new InvalidOperationException("Biome definition '" + Biome
                    + "' must choose exactly one stable or generated model-first campfire mode.");
            if (CandidatePaths.Count == 0)
                throw new InvalidOperationException("Biome definition '" + Biome
                    + "' must contain one stable model-first campfire or one generated campfire factory.");

            var ReachesCampfire = new Dictionary<string, bool>(StringComparer.Ordinal);
            Func<string, bool> Reaches = null;
            Reaches = Path =>
            {
                bool Result;
                if (ReachesCampfire.TryGetValue(Path, out Result)) return Result;
                var Node = Identity.Graph.ByPath[Path];
                Result = CandidatePaths.Contains(Path) || Node.Edges.Any(Edge => Reaches(Edge.TargetPath));
                ReachesCampfire.Add(Path, Result);
                return Result;
            };
            foreach (var Node in Identity.Graph.Nodes) Reaches(Node.SourcePath);

            PrefabNode FactoryNode = null;
            PropSpawner Factory = null;
            foreach (var Node in Identity.Graph.Nodes)
                foreach (var Edge in Node.Edges)
                {
                    if (!Reaches(Edge.TargetPath)) continue;
                    if (!Edge.Spawner)
                        throw new InvalidOperationException("Biome definition '" + Biome
                            + "' generated campfire candidates must be selected directly by one PropSpawner.");
                    if (Factory && !ReferenceEquals(Factory, Edge.Spawner))
                        throw new InvalidOperationException("Biome definition '" + Biome
                            + "' generated campfire candidates are reachable from more than one PropSpawner.");
                    FactoryNode = Node;
                    Factory = Edge.Spawner;
                }
            if (!Factory || FactoryNode == null || !FactoryNode.IsRoot)
                throw new InvalidOperationException("Biome definition '" + Biome
                    + "' generated campfire mode requires one root-prefab PropSpawner factory.");

            var FactoryEdges = FactoryNode.Edges.Where(Value => ReferenceEquals(Value.Spawner, Factory)).ToArray();
            if (FactoryEdges.Length == 0 || Factory.props == null || FactoryEdges.Length != Factory.props.Length
                || FactoryEdges.Any(Edge => !CandidatePaths.Contains(Edge.TargetPath)
                    || MarkersByPath[Edge.TargetPath].Length != 1))
                throw new InvalidOperationException("Biome definition '" + Biome
                    + "' generated campfire factory slots must select candidates containing exactly one campfire marker each.");
            ValidateCampfireFactory(Identity, Generation, Factory);
            foreach (var Path in CandidatePaths)
            {
                var Candidate = Identity.Graph.ByPath[Path].SourceRoot;
                if (Candidate.GetComponentsInChildren<PllLuggageAuthoring>(true).Length != 0
                    || Candidate.GetComponentsInChildren<PllStatueAuthoring>(true).Length != 0
                    || Candidate.GetComponentsInChildren<RespawnChest>(true).Length != 0)
                    throw new InvalidOperationException("Biome definition '" + Biome + "' campfire candidate '"
                        + Path + "' cannot also author luggage or a statue.");
                if (Candidate.GetComponent<PllChildPropAuthoring>())
                    throw new InvalidOperationException("Biome definition '" + Biome + "' campfire candidate '"
                        + Path + "' cannot add a competing child-prop factory modifier.");
            }
        }

        static void ValidateCampfireMarker(
            SourceIdentity Identity, GenerationAuthoring Generation, PllCampfireAuthoring Source)
        {
            var Template = Identity.Root;
            var CampfireRoot = Identity.Campfire;
            var Biome = Identity.Label;
            var Node = OwnerNode(Identity.Graph, Source, "Biome definition '" + Biome + "' campfire authoring");
            var Owner = Node.SourceRoot.transform;
            var Context = "Biome definition '" + Biome + "' campfire authoring in '" + Node.SourcePath + "'";
            if (!Source.enabled || Source.Root != Source.gameObject)
                throw new InvalidOperationException(Context + " Root must be its enabled component GameObject.");
            if (Node.IsRoot)
            {
                if (Source.transform == CampfireRoot || !TransformOwnership.Contains(CampfireRoot, Source.transform)
                    || !TransformOwnership.DiscoverableWhenActivated(CampfireRoot, Source.transform))
                    throw new InvalidOperationException(Context + " must be a discoverable strict campfire-root child.");
                RequireUnconditionalStable(Template.transform, Source.transform, Generation, Context);
            }
            else
            {
                if (!Node.SourceRoot.activeSelf || !TransformOwnership.DiscoverableWhenActivated(Owner, Source.transform)
                    || TransformOwnership.HasPropSpawnerInPath(Owner, Source.transform))
                    throw new InvalidOperationException(Context
                        + " must be active-relative candidate presentation outside nested PropSpawner hierarchies.");
                ValidateTransformPath(Owner, Source.transform, Context);
            }
            ValidateModelPresentation(Source.transform, Source.ModelRoot, Context);
            var Particles = new[] { Source.FireParticles, Source.SmokeParticlesOff, Source.SmokeParticlesLit };
            if (Particles.Any(Value => !Value) || Particles.Distinct().Count() != Particles.Length)
                throw new InvalidOperationException(Context + " requires three distinct particle systems.");
            for (var i = 0; i < Particles.Length; i++)
            {
                if (!TransformOwnership.Contains(Source.transform, Particles[i].transform))
                    throw new InvalidOperationException(Context + " particle systems must be owned by Root.");
                ValidateTransformPath(Source.transform, Particles[i].transform, Context + " particle system");
            }
            if (!Source.LogRoot || !TransformOwnership.Contains(Source.transform, Source.LogRoot))
                throw new InvalidOperationException(Context + " LogRoot must be owned by Root.");
            ValidateTransformPath(Source.transform, Source.LogRoot, Context + " LogRoot");
            ValidateOptionalOwned(Source.transform, Source.EnableWhenLit, Context + " EnableWhenLit");
            ValidateOptionalOwned(Source.transform, Source.DisableWhenLit, Context + " DisableWhenLit");
            if (Source.Loop)
            {
                if (!TransformOwnership.Contains(Source.transform, Source.Loop.transform))
                    throw new InvalidOperationException(Context + " Loop must be owned by Root.");
                ValidateTransformPath(Source.transform, Source.Loop.transform, Context + " Loop");
            }
            RejectSourceMechanics(Source.gameObject, Context, true);
        }

        static void ValidateCampfireFactory(
            SourceIdentity Identity, GenerationAuthoring Generation, PropSpawner Factory)
        {
            var Template = Identity.Root;
            var CampfireRoot = Identity.Campfire;
            var Biome = Identity.Label;
            var Context = "Biome definition '" + Biome + "' generated campfire factory";
            if (!Factory.enabled || !TransformOwnership.Contains(CampfireRoot, Factory.transform)
                || !TransformOwnership.DiscoverableWhenActivated(CampfireRoot, Factory.transform))
                throw new InvalidOperationException(Context + " must be an enabled discoverable campfire-root PropSpawner.");
            ValidateTransformPath(Template.transform, Factory.transform, Context);
            for (var Current = Factory.transform.parent; Current && Current != CampfireRoot; Current = Current.parent)
                if (Current.GetComponent<PropSpawner>())
                    throw new InvalidOperationException(Context + " cannot belong to another PropSpawner hierarchy.");
            RejectConditionalOwnership(Factory.transform, Generation, Context);
            if (Factory.area != Vector2.zero || Factory.raycastPosition || Factory.nrOfSpawns != 1
                || Factory.randomSpawns || Factory.minSpawnCount != 0 || !Factory.chanceToUseSpawner.Equals(1f)
                || Factory.currentSpawns != 0 || !Factory.syncTransforms || Factory.overrideProps
                || Factory.overridePropsList || !ValidCampfireModifiers(Factory.modifiers)
                || Factory.constraints == null || Factory.constraints.Count != 0
                || Factory.postConstraints == null || Factory.postConstraints.Count != 0
                || Factory.postSpawnBehaviors == null || Factory.postSpawnBehaviors.Count != 0)
                throw new InvalidOperationException(Context + " does not match the exact deterministic PEAK fixture.");
            foreach (var Zone in Generation.PropZones)
                if (Zone.Spawners.Contains(Factory))
                    throw new InvalidOperationException(Context + " cannot receive a prop-zone constraint.");
        }

        static bool ValidCampfireModifiers(IReadOnlyList<PropSpawnerMod> Modifiers)
        {
            if (Modifiers == null || Modifiers.Any(Value => Value == null || Value.mute)) return false;
            var Segment = 0;
            foreach (var Modifier in Modifiers)
            {
                var Type = Modifier.GetType();
                if (Type == typeof(Campfire_Set_Segment))
                {
                    Segment++;
                    continue;
                }
                if (Type != typeof(PSM_SetForwardRotationToNormal)
                    && Type != typeof(PSM_SetUpRotationToNormal)
                    && Type != typeof(PSM_SnapPosition)
                    && Type != typeof(PSM_SpecificRotation)) return false;
            }
            return Segment == 1;
        }

        static void ValidateStatue(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures)
        {
            var Template = Identity.Root;
            var Floor = Identity.Floor;
            var Campfire = Identity.Campfire;
            var Anchor = Identity.StatueRoot;
            var Biome = Identity.Label;
            if (Fixtures.Statues.Length == 0)
            {
                if (Anchor)
                    throw new InvalidOperationException("Biome definition '" + Biome
                        + "' anchors.statueRoot is set without PllStatueAuthoring.");
                return;
            }
            if (Fixtures.Statues.Length != 1)
                throw new InvalidOperationException("Biome definition '" + Biome
                    + "' must contain at most one PllStatueAuthoring.");
            var Source = Fixtures.Statues[0];
            var Wrapper = Source.gameObject;
            var Context = "Biome definition '" + Biome + "' statue authoring";
            if (!Source.enabled || Anchor != Wrapper || Wrapper == Template
                || !TransformOwnership.DiscoverableWhenActivated(Template.transform, Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(Template.transform, Source.transform)
                || !TransformOwnership.Contains(Floor, Source.transform) && !TransformOwnership.Contains(Campfire, Source.transform))
                throw new InvalidOperationException(Context
                    + " must be the enabled stable wrapper identified by anchors.statueRoot.");
            RequireUnconditionalStable(Template.transform, Source.transform, Generation, Context);
            ValidateModelPresentation(Source.transform, Source.ModelRoot, Context);
            var Skeleton = Source.SkeletonRoot ? Source.SkeletonRoot : Source.ModelRoot;
            if (!Skeleton || Skeleton == Wrapper || !Skeleton.transform.IsChildOf(Source.transform)
                || !TransformOwnership.DiscoverableWhenActivated(Source.transform, Skeleton.transform))
                throw new InvalidOperationException(Context + " SkeletonRoot must be an active strict owned child.");
            if (!Source.SpawnPoint || Source.SpawnPoint.gameObject == Source.ModelRoot
                || Source.SpawnPoint == Source.transform || !Source.SpawnPoint.IsChildOf(Source.transform)
                || !TransformOwnership.DiscoverableWhenActivated(Source.transform, Source.SpawnPoint))
                throw new InvalidOperationException(Context + " SpawnPoint must be a distinct active strict owned child.");
            ValidateTransformPath(Source.transform, Skeleton.transform, Context + " SkeletonRoot");
            ValidateTransformPath(Source.transform, Source.SpawnPoint, Context + " SpawnPoint");
            if (string.IsNullOrEmpty(Source.DisplayName) || !ManifestNumber(Source.OpenTime) || Source.OpenTime <= 0f)
                throw new InvalidOperationException(Context + " requires a display name and positive finite OpenTime.");
            ValidateSourceAnimator(Wrapper, Source.AnimatorController, Context);
            ValidateItemCatalog(Source.Items, Context);
            RejectSourceMechanics(Wrapper, Context, false);
        }

        static void ValidateLuggage(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures)
        {
            var Template = Identity.Root;
            var CampfireRoot = Identity.Campfire;
            var Biome = Identity.Label;
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            var Roots = new Transform[Fixtures.Luggage.Length];
            var Fixed = new HashSet<PllLuggageAuthoring>();
            for (var i = 0; i < Fixtures.Luggage.Length; i++)
            {
                var Source = Fixtures.Luggage[i];
                var Node = OwnerNode(Identity.Graph, Source,
                    "Biome definition '" + Biome + "' luggageDefinitions[" + i + "]");
                var Context = "Biome definition '" + Biome + "' luggageDefinitions[" + i
                    + "] in '" + Node.SourcePath + "'";
                ValidateLuggageComponent(Source, Identity.Definition.Behavior, Context);
                if (!Ids.Add(Source.Id))
                    throw new InvalidOperationException("Biome definition '" + Biome
                        + "' contains duplicate luggage ID '" + Source.Id + "'.");
                if (Node.IsRoot)
                {
                    if (Source.transform == CampfireRoot
                        || !TransformOwnership.Contains(CampfireRoot, Source.transform)
                        || !TransformOwnership.ParentDiscoverable(CampfireRoot, Source.transform)
                        || Source.gameObject.activeSelf
                        || TransformOwnership.HasConfigurationBranch(CampfireRoot, Source.transform))
                        throw new InvalidOperationException(Context
                            + " fixed luggage must be one inactive, discoverable CampfireRoot child outside configuration branches.");
                    RequireUnconditionalStable(
                        Template.transform, Source.transform, Generation, Context);
                    Fixed.Add(Source);
                }
                else if (Source.gameObject != Node.SourceRoot
                    || !Source.gameObject.activeSelf
                    || !Source.SpawnChance.Equals(1f)
                    || Source.ReplacesAuthored
                    || Source.ReplacesNativeId.Length != 0)
                    throw new InvalidOperationException(Context
                        + " generated luggage must occupy its active candidate root with SpawnChance 1 and no replacement.");
                Roots[i] = Source.transform;
            }

            RequireDisjointRoots(Roots, "Biome definition '" + Biome + "' luggage roots cannot overlap.");
            var AuthoredTargets = new HashSet<PllLuggageAuthoring>();
            var NativeTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Source in Fixtures.Luggage)
            {
                var Target = Source.ReplacesAuthored;
                if (Target && (!Fixed.Contains(Source) || ReferenceEquals(Source, Target)
                    || !Fixed.Contains(Target) || Target.ReplacesAuthored
                    || Target.ReplacesNativeId.Length != 0))
                    throw new InvalidOperationException("Biome definition '" + Biome
                        + "' luggage '" + Source.Id
                        + "' has an invalid authored replacement target.");
                if (Target && !AuthoredTargets.Add(Target))
                    throw new InvalidOperationException("Biome definition '" + Biome
                        + "' has multiple luggage definitions replacing '" + Target.Id + "'.");
                if (Source.ReplacesNativeId.Length != 0
                    && !NativeTargets.Add(Source.ReplacesNativeId))
                    throw new InvalidOperationException("Biome definition '" + Biome
                        + "' has multiple luggage definitions replacing native target '"
                        + Source.ReplacesNativeId + "'.");
            }
        }

        internal static void ValidateLuggageComponent(
            PllLuggageAuthoring Source, PllBiomeBehavior? Behavior, string Context)
        {
            if (!Source.enabled || Source.GetComponents<PllLuggageAuthoring>().Length != 1)
                throw new InvalidOperationException(Context
                    + " must own one enabled PllLuggageAuthoring component.");
            if (!PllAuthoringValueContract.IsLocalId(Source.Id)
                || !PllAuthoringValueContract.IsDisplayText(Source.DisplayName))
                throw new InvalidOperationException(Context
                    + " ID or DisplayName violates the synchronized text contract.");
            if (!PllAuthoringValueContract.IsDefined(Source.ContentsMode)
                || !PllAuthoringValueContract.IsDefined(Source.RepeatMode)
                || !PllAuthoringValueContract.ValidLuggageContents(
                    Source.ContentsMode, Source.CustomItems.Count,
                    Source.NativeContentsId))
                throw new InvalidOperationException(Context
                    + " has an invalid contents or repeat mode.");
            if (!PllAuthoringValueContract.CanEncodeExactly(Source.OpenTime)
                || Source.OpenTime <= 0f
                || !PllAuthoringValueContract.CanEncodeExactly(Source.SpawnChance)
                || Source.SpawnChance < 0f || Source.SpawnChance > 1f)
                throw new InvalidOperationException(Context
                    + " has an invalid OpenTime or SpawnChance.");
            ValidateOptionalToken(
                Source.NativeContentsId, Context + " NativeContentsId");
            ValidateOptionalToken(
                Source.ReplacesNativeId, Context + " ReplacesNativeId");
            if (Source.ReplacesAuthored && Source.ReplacesNativeId.Length != 0)
                throw new InvalidOperationException(Context
                    + " cannot replace authored and native luggage together.");
            if (Behavior == PllBiomeBehavior.Authored
                && Source.ContentsMode == PllLuggageContentsMode.NativeProgression)
                throw new InvalidOperationException(Context
                    + " cannot use NativeProgression with Authored biome behavior.");
            if (Source.AnimatorController)
                ValidateLuggageAnimatorController(
                    Source.AnimatorController, Context + " AnimatorController");

            var Activate = Source.ActivateOnOpen
                ? Source.ActivateOnOpen.transform : null;
            ValidateLuggagePresentation(Source.transform, Activate, Context);
            ValidateLuggageActivation(Source, Activate, Context);
            ValidateLuggageSpawnGroups(Source, Activate, Context);
            ValidateLuggageItems(Source, Context);
            RejectLuggageSourceMechanics(Source.gameObject, Context);
        }

        static void ValidateLuggagePresentation(
            Transform Root, Transform ActivateOnOpen, string Context)
        {
            var RendererFound = Root.GetComponentsInChildren<Renderer>(true).Any(Value =>
                Value && Value.enabled
                && (ActivateOnOpen == null
                    || !TransformOwnership.Contains(ActivateOnOpen, Value.transform))
                && TransformOwnership.DiscoverableWhenActivated(Root, Value.transform));
            var ColliderFound = Root.GetComponentsInChildren<Collider>(true).Any(Value =>
                Value && Value.enabled && !Value.isTrigger
                && (ActivateOnOpen == null
                    || !TransformOwnership.Contains(ActivateOnOpen, Value.transform))
                && TransformOwnership.DiscoverableWhenActivated(Root, Value.transform));
            if (!RendererFound || !ColliderFound)
                throw new InvalidOperationException(Context
                    + " needs active-relative enabled presentation Renderer and non-trigger Collider outside ActivateOnOpen.");
        }

        static void ValidateLuggageActivation(
            PllLuggageAuthoring Source, Transform Activate, string Context)
        {
            if (!Activate) return;
            var Root = Source.transform;
            if (Activate == Root || !TransformOwnership.Contains(Root, Activate)
                || Activate.gameObject.activeSelf || !Activate.parent
                || !TransformOwnership.DiscoverableWhenActivated(Root, Activate.parent))
                throw new InvalidOperationException(Context
                    + " ActivateOnOpen must be one inactive strict child subtree with active ancestors.");
            ValidateTransformPath(Root, Activate, Context + " ActivateOnOpen");
            foreach (var Component in Activate.GetComponentsInChildren<Component>(true))
            {
                if (!Component)
                    throw new InvalidOperationException(Context
                        + " ActivateOnOpen contains a missing component.");
                if (Component is Transform || Component is MeshFilter
                    || Component is Renderer || Component is Light
                    || Component is LODGroup) continue;
                if (Component is ParticleSystem Particle)
                {
                    if (!Particle.main.loop) continue;
                }
                else if (Component is AudioSource Audio)
                {
                    if (Audio.clip && !Audio.loop) continue;
                }
                throw new InvalidOperationException(Context
                    + " ActivateOnOpen contains gameplay, physics, spawning, networking, or looping presentation.");
            }
        }

        static void ValidateLuggageSpawnGroups(
            PllLuggageAuthoring Source, Transform Activate, string Context)
        {
            if (Source.SpawnGroups.Count == 0)
                throw new InvalidOperationException(Context
                    + " requires at least one SpawnGroup.");
            long WeightSum = 0;
            for (var GroupIndex = 0;
                GroupIndex < Source.SpawnGroups.Count; GroupIndex++)
            {
                var Group = Source.SpawnGroups[GroupIndex];
                if (Group == null || Group.Weight < 1 || Group.Points.Count == 0)
                    throw new InvalidOperationException(Context + " SpawnGroups["
                        + GroupIndex + "] is null, empty, or has nonpositive weight.");
                WeightSum += Group.Weight;
                if (WeightSum > int.MaxValue)
                    throw new InvalidOperationException(Context
                        + " SpawnGroup weights exceed Int32 capacity.");
                var Points = new HashSet<Transform>();
                for (var PointIndex = 0;
                    PointIndex < Group.Points.Count; PointIndex++)
                {
                    var Point = Group.Points[PointIndex];
                    if (!Point || Point == Source.transform
                        || !TransformOwnership.Contains(Source.transform, Point)
                        || !TransformOwnership.DiscoverableWhenActivated(
                            Source.transform, Point)
                        || TransformOwnership.HasPropSpawnerInPath(
                            Source.transform, Point)
                        || Activate
                            && TransformOwnership.Overlaps(Activate, Point)
                        || !Points.Add(Point))
                        throw new InvalidOperationException(Context + " SpawnGroups["
                            + GroupIndex + "].Points[" + PointIndex
                            + "] is missing, duplicate, inactive, unowned, conditionally generated, or overlaps ActivateOnOpen.");
                    ValidateTransformPath(
                        Source.transform, Point, Context + " SpawnGroups["
                            + GroupIndex + "].Points[" + PointIndex + "]");
                }
            }
        }

        static void ValidateLuggageItems(
            PllLuggageAuthoring Source, string Context)
        {
            var Items = new HashSet<GameObject>();
            long WeightSum = 0;
            for (var Index = 0; Index < Source.CustomItems.Count; Index++)
            {
                var Entry = Source.CustomItems[Index];
                if (Entry == null || !Entry.Item || Entry.Weight < 1
                    || !Items.Add(Entry.Item))
                    throw new InvalidOperationException(Context + " CustomItems["
                        + Index + "] is missing, duplicate, or has nonpositive weight.");
                WeightSum += Entry.Weight;
                if (WeightSum > int.MaxValue)
                    throw new InvalidOperationException(Context
                        + " CustomItem weights exceed Int32 capacity.");
                CandidatePath(Entry.Item, Context + " CustomItems[" + Index + "].Item");
                var Mechanics = Entry.Item.GetComponentsInChildren<Item>(true);
                if (Mechanics.Length != 1 || !((Component)(object)Mechanics[0]))
                    throw new InvalidOperationException(Context + " CustomItems["
                        + Index + "] must contain exactly one PEAK Item component.");
            }
        }

        static void RejectLuggageSourceMechanics(
            GameObject Root, string Context)
        {
            if (Root.GetComponentInChildren<Spawner>(true)
                || Root.GetComponentInChildren<Peak.SpawnedItemTracker>(true)
                || Root.GetComponentInChildren<PhotonView>(true)
                || Root.GetComponentInChildren<Animator>(true)
                || Root.GetComponentInChildren<Item>(true)
                || Root.GetComponentInChildren<Campfire>(true))
                throw new InvalidOperationException(Context
                    + " source contains PEAK interaction, spawning, networking, item, or Animator mechanics owned by build staging.");
        }

        static void ValidateModelPresentation(Transform Wrapper, GameObject Model, string Context)
        {
            if (!Model || Model.transform == Wrapper || !Model.transform.IsChildOf(Wrapper)
                || !TransformOwnership.DiscoverableWhenActivated(Wrapper, Model.transform))
                throw new InvalidOperationException(Context + " ModelRoot must be an active strict owned child.");
            ValidateTransformPath(Wrapper, Model.transform, Context + " ModelRoot");
            if (!Model.GetComponentsInChildren<MeshRenderer>(true).Any(Value => Value && Value.enabled
                    && TransformOwnership.DiscoverableWhenActivated(Model.transform, Value.transform))
                || !Model.GetComponentsInChildren<Collider>(true).Any(Value => Value && Value.enabled
                    && TransformOwnership.DiscoverableWhenActivated(Model.transform, Value.transform)))
                throw new InvalidOperationException(Context
                    + " ModelRoot needs an active-relative enabled MeshRenderer and Collider.");
        }

        static void ValidateOptionalOwned(Transform Root, GameObject Value, string Context)
        {
            if (!Value) return;
            if (!TransformOwnership.Contains(Root, Value.transform)) throw new InvalidOperationException(Context + " must be owned by Root.");
            ValidateTransformPath(Root, Value.transform, Context);
        }

        static void ValidateSourceAnimator(
            GameObject Wrapper, RuntimeAnimatorController AuthoredController, string Context)
        {
            var Animators = Wrapper.GetComponents<Animator>();
            if (Animators.Length > 1 || Animators.Length == 1 && (!Animators[0] || !Animators[0].enabled))
                throw new InvalidOperationException(Context + " may contain at most one enabled root Animator.");
            var Controller = AuthoredController
                ? AuthoredController : Animators.Length == 1 ? Animators[0].runtimeAnimatorController : null;
            if (Controller) ValidateLuggageAnimatorController(Controller, Context + " AnimatorController");
        }

        internal static void ValidateLuggageAnimatorController(RuntimeAnimatorController Value, string Context)
        {
            if (!Value || !EditorUtility.IsPersistent(Value))
                throw new InvalidOperationException(Context + " must be a persistent controller asset.");
            var Visited = new HashSet<RuntimeAnimatorController>();
            while (Value is AnimatorOverrideController)
            {
                if (!Visited.Add(Value))
                    throw new InvalidOperationException(Context + " contains an override-controller cycle.");
                Value = ((AnimatorOverrideController)Value).runtimeAnimatorController;
                if (!Value) throw new InvalidOperationException(Context + " has no base controller.");
            }
            var Controller = Value as AnimatorController;
            if (!Controller || Controller.layers.Length == 0 || Controller.layers[0].stateMachine == null)
                throw new InvalidOperationException(Context + " must resolve to an AnimatorController with layer 0.");
            int UnclaspCount = 0, OpenCount = 0;
            CountLuggageStates(Controller.layers[0].stateMachine, ref UnclaspCount, ref OpenCount);
            if (UnclaspCount != 1 || OpenCount != 1)
                throw new InvalidOperationException(Context
                    + " layer 0 must contain exact Luggage_Unclasp and Luggage_Open states.");
            int ReclaspCount = 0, ReclaspTriggerCount = 0;
            foreach (var Parameter in Controller.parameters)
            {
                if (!string.Equals(Parameter.name, "Reclasp", StringComparison.Ordinal)) continue;
                ReclaspCount++;
                if (Parameter.type == AnimatorControllerParameterType.Trigger) ReclaspTriggerCount++;
            }
            if (ReclaspCount != 1 || ReclaspTriggerCount != 1)
                throw new InvalidOperationException(Context + " must contain one Trigger parameter named Reclasp.");
        }

        static void CountLuggageStates(AnimatorStateMachine Machine, ref int UnclaspCount, ref int OpenCount)
        {
            foreach (var Child in Machine.states)
                if (string.Equals(Child.state.name, "Luggage_Unclasp", StringComparison.Ordinal)) UnclaspCount++;
                else if (string.Equals(Child.state.name, "Luggage_Open", StringComparison.Ordinal)) OpenCount++;
            foreach (var Child in Machine.stateMachines)
                CountLuggageStates(Child.stateMachine, ref UnclaspCount, ref OpenCount);
        }

        internal static void ValidateItemCatalog(IReadOnlyList<GameObject> Items, string Context)
        {
            var Seen = new HashSet<GameObject>();
            for (var i = 0; i < Items.Count; i++)
            {
                var Item = Items[i];
                if (!Item || !Seen.Add(Item))
                    throw new InvalidOperationException(Context + " contains a missing or duplicate item prefab.");
                CandidatePath(Item, Context + " Items[" + i + "]");
                var Mechanics = Item.GetComponentsInChildren<Item>(true);
                if (Mechanics.Length != 1 || !((Component)(object)Mechanics[0]))
                    throw new InvalidOperationException(Context
                        + " contains an item prefab without exactly one PEAK Item component.");
            }
        }

        static void RejectSourceMechanics(GameObject Wrapper, string Context, bool Campfire)
        {
            if (Wrapper.GetComponentsInChildren<Campfire>(true).Length != 0
                || Wrapper.GetComponentsInChildren<Luggage>(true).Length != 0
                || Wrapper.GetComponentsInChildren<Peak.SpawnedItemTracker>(true).Length != 0
                || Wrapper.GetComponentsInChildren<PhotonView>(true).Length != 0
                || !Campfire && Wrapper.GetComponentsInChildren<Spawner>(true).Length != 0)
                throw new InvalidOperationException(Context
                    + " contains creator-authored PEAK spawning or networking mechanics; the builder owns them.");
        }

        static void ValidateOptionalToken(string Value, string Context)
        {
            if (Value.Length != 0) ValidateToken(Value, Context);
        }

    }

    internal sealed class FixtureAuthoring
    {
        internal FixtureAuthoring(
            PllLuggageAuthoring[] Luggage, PllCampfireAuthoring[] Campfires,
            PllStatueAuthoring[] Statues, PllItemSpawnerAuthoring[] ItemSpawners,
            PllRestPointAuthoring[] RestPoints, PllHangingRopeAuthoring[] HangingRopes)
        {
            this.Luggage = Luggage;
            this.Campfires = Campfires;
            this.Statues = Statues;
            this.ItemSpawners = ItemSpawners;
            this.RestPoints = RestPoints; this.HangingRopes = HangingRopes;
        }

        internal PllLuggageAuthoring[] Luggage { get; }
        internal PllCampfireAuthoring[] Campfires { get; }
        internal PllStatueAuthoring[] Statues { get; }
        internal PllItemSpawnerAuthoring[] ItemSpawners { get; }
        internal PllRestPointAuthoring[] RestPoints { get; }
        internal PllHangingRopeAuthoring[] HangingRopes { get; }
    }
}
