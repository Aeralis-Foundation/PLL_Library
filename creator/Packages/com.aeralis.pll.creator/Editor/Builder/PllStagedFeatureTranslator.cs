using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Photon.Pun;
using PLL.Library;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllBuildArtifacts;
using static PLL.Creator.Editor.PllFixtureAuthoringValidator;
using static PLL.Creator.Editor.PllGenerationAuthoringValidator;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    [InitializeOnLoad]
    internal sealed class PllStagedFeatureTranslator
    {
        const float CampfireMoraleBoostRadius = 12f;
        const float CampfireMoraleBoostBaseline = 0.25f;
        const float CampfireMoraleBoostPerAdditionalScout = 0f;
        readonly ProjectStage Stage;
        readonly string Prefix;
        readonly DefinitionCatalog Catalog;
        readonly IReadOnlyDictionary<string, string> StagedPaths;
        readonly IReadOnlyDictionary<PllFluidDefinition, PllFluidDefinition> FluidDefinitions;
        readonly PllSurfaceCurvatureBaker CurvatureBaker;
        readonly PllPropInteractionCompiler PropCompiler;
        readonly IReadOnlyList<MountedToolInsertionRecipe> MountedToolInsertions;

        internal PllStagedFeatureTranslator(
            ProjectStage Stage, string Prefix, DefinitionCatalog Catalog,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyDictionary<PllFluidDefinition, PllFluidDefinition> FluidDefinitions,
            PllSurfaceCurvatureBaker CurvatureBaker, PllPropInteractionCompiler PropCompiler,
            IReadOnlyList<MountedToolInsertionRecipe> MountedToolInsertions)
        {
            this.Stage = Stage;
            this.Prefix = Prefix;
            this.Catalog = Catalog;
            this.StagedPaths = StagedPaths;
            this.FluidDefinitions = FluidDefinitions;
            this.CurvatureBaker = CurvatureBaker; this.PropCompiler = PropCompiler;
            this.MountedToolInsertions = MountedToolInsertions;
        }

        internal void Translate(PrefabNode Node, GameObject StagedRoot)
        {
            RewriteCandidateReferences(Node, StagedRoot, StagedPaths);
            var SourceRoot = Node.SourceRoot;
            foreach (var Source in ComponentsInNode(Node, Catalog.NativeResolvers))
            {
                if (!Source.FluidDefinition) continue;
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "Fluid Definition");
                using var Serialized = new SerializedObject(Staged);
                PllBundleBuilder.Property(Serialized, "FluidDefinitionValue").objectReferenceValue =
                    FluidDefinitions[Source.FluidDefinition];
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var Source in ComponentsInNode(Node, Catalog.SurfaceTextures))
                CurvatureBaker.Prepare(MapComponent(SourceRoot, StagedRoot, Source, "Surface Textures"));
            PropCompiler.Prepare(StagedRoot);
            var ClimbSurfaces = ComponentsInNode(Node, Catalog.ClimbSurfaces);
            var Campfires = ComponentsInNode(Node, Catalog.Campfires);
            var Luggage = ComponentsInNode(Node, Catalog.Luggage);
            var Statues = Node.IsRoot ? Catalog.Statues : Array.Empty<PllStatueAuthoring>();
            var Effects = ComponentsInNode(Node, Catalog.Effects);
            var CustomVisuals = Node.IsRoot
                ? Catalog.CustomVisuals : Array.Empty<CustomVisualParticleGraph>();
            var Stingers = Node.IsRoot ? Catalog.Stingers : Array.Empty<PllBiomeStingerAuthoring>();
            var ChildProps = ComponentsInNode(Node, Catalog.ChildProps);
            var PropZones = ComponentsInNode(Node, Catalog.PropZones);
            var ItemSpawners = ComponentsInNode(Node, Catalog.ItemSpawners);
            var NodeIndex = Array.IndexOf(Catalog.Graph.Nodes, Node);
            if (Node.IsRoot) Catalog.HookedGeneration.Translate(StagedRoot);
            if (Node.IsRoot) TranslateFog(SourceRoot, StagedRoot);
            TranslateCollisionGeometry(Node, StagedRoot);
            TranslateClimbSurfaces(
                SourceRoot, StagedRoot, ClimbSurfaces,
                Catalog.ClimbSurfaceCatalog.MaterialTargets);
            TranslateCampfires(SourceRoot, StagedRoot, Campfires);
            foreach (var Source in ComponentsInNode(Node, Catalog.Hazards))
            {
                var Hazard = MapComponent(SourceRoot, StagedRoot, Source, "Hazard");
                if (Hazard.Behavior is not PllContactInjuryBehavior Contact) continue;
                var Target = Contact.Contact.gameObject;
                Target.layer = PllClimbSurfaceAuthoringValidator.MapLayer;
                var Gate = Target.AddComponent<PllHazardContact>();
                using (var Serialized = new SerializedObject(Gate))
                {
                    PllBundleBuilder.Property(Serialized, "DefinitionValue").objectReferenceValue = Hazard;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var Modifier = Target.AddComponent<CollisionModifier>();
                Modifier.applyEffects = false; Modifier.applyPetrify = false;
                Modifier.statusType = CharacterAfflictions.STATUSTYPE.Injury;
                Modifier.damage = Contact.InjuryAmount; Modifier.cooldown = Contact.CooldownSeconds;
                Modifier.knockback = Contact.Knockback; Modifier.ragdolledKnockback = Contact.RagdolledKnockback;
                Modifier.knockbackTowardsFwdVector = Contact.ForwardBias;
                Modifier.knockbackOnlyWhenStanding = Contact.KnockbackOnlyWhenStanding;
                Modifier.preventFallDamageIfKnockback = Contact.PreventFallDamage;
                Modifier.staminaBackOnBounce = 0f;
            }
            TranslateLuggage(Stage, Prefix, NodeIndex, SourceRoot, StagedRoot, Luggage);
            TranslateStatues(Stage, Prefix, NodeIndex, SourceRoot, StagedRoot, Statues);
            PllClimbingFixtureCompiler.TranslateRestPoints(SourceRoot, StagedRoot,
                ComponentsInNode(Node, Catalog.RestPoints));
            PllClimbingFixtureCompiler.TranslateHangingRopes(SourceRoot, StagedRoot,
                ComponentsInNode(Node, Catalog.HangingRopes));
            if (Node.IsRoot) TranslateVariations(SourceRoot, StagedRoot, Catalog.Variations);
            if (Node.IsRoot)
            {
                foreach (var Zone in Catalog.EntityZones)
                    PllActorZoneAuthoringValidator.Translate(
                        MapComponent(SourceRoot, StagedRoot, Zone, "Entity Zone"));
                foreach (var Zone in Catalog.HazardZones)
                    PllActorZoneAuthoringValidator.Translate(
                        MapComponent(SourceRoot, StagedRoot, Zone, "Hazard Zone"));
            }
            if (Node.IsRoot)
                PllAmuletSpawnerCompiler.Translate(SourceRoot, StagedRoot,
                    Catalog.AmuletSpawners, Catalog.Definition.Behavior);
            RewriteZoneActionTargets(Node, StagedRoot);
            TranslateEffects(SourceRoot, StagedRoot, Effects);
            TranslateCustomVisuals(SourceRoot, StagedRoot, CustomVisuals);
            TranslateStingers(SourceRoot, StagedRoot, Stingers);
            TranslateChildProps(SourceRoot, StagedRoot, ChildProps, Node.IsRoot);
            ConfigureCandidateChildEdges(Node, Catalog.Graph, StagedRoot);
            foreach (var Insertion in MountedToolInsertions)
            {
                if (Insertion.Node != Node) continue;
                var Spawner = MapComponent(SourceRoot, StagedRoot, Insertion.Source, "mounted tool producer");
                Spawner.modifiers ??= new List<PropSpawnerMod>();
                Spawner.modifiers.Insert(Insertion.Index, new PllMountedToolInsertionModifier());
            }
            InjectNoPropConstraints(StagedRoot, Catalog.NoPropSpawn.Length != 0);
            TranslatePropZones(SourceRoot, StagedRoot, PropZones);
            TranslateNativeResolvers(Stage, Prefix, SourceRoot, StagedRoot,
                ComponentsInNode(Node, Catalog.NativeResolvers));
            TranslateItemSpawners(SourceRoot, StagedRoot, ItemSpawners);
            if (Node.IsRoot) TranslateFinalGeneration(SourceRoot, StagedRoot, Catalog.FinalGeneration);
            NormalizePropSpawners(StagedRoot);
            PllBundleSerialization.SealPrefab(StagedRoot);
        }

        void TranslateCollisionGeometry(PrefabNode Node, GameObject StagedRoot)
        {
            foreach (var Geometry in Catalog.CollisionGeometry)
            {
                if (Geometry.Node != Node || !Geometry.GeneratedMesh) continue;
                var Target = MapTransform(Node.SourceRoot, StagedRoot,
                    Geometry.Target, "automatic collision geometry").gameObject;
                PllClimbSurfaceAuthoringValidator.ConfigureStaticMeshCollider(
                    Target.AddComponent<MeshCollider>(), Geometry.GeneratedMesh);
                if (!PllClimbSurfaceAuthoringValidator.IsClimbLayer(Target.layer))
                    Target.layer = PllClimbSurfaceAuthoringValidator.MapLayer;
            }
        }

        static void TranslateClimbSurfaces(
            GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PllClimbSurfaceAuthoring> Sources,
            IReadOnlyDictionary<GameObject, int> MaterialTargets)
        {
            var Targets = new List<GameObject>();
            var SeenTargets = new HashSet<GameObject>();
            foreach (var Source in Sources)
                if (SeenTargets.Add(Source.gameObject))
                    Targets.Add(Source.gameObject);
            var SourcePath = AssetDatabase.GetAssetPath(SourceRoot);
            foreach (var Target in MaterialTargets.Keys
                .Where(Value => string.Equals(
                    AssetDatabase.GetAssetPath(Value), SourcePath,
                    StringComparison.Ordinal))
                .OrderBy(Value => PllHierarchy.NamePath(
                    SourceRoot.transform, Value.transform),
                    StringComparer.Ordinal))
                if (SeenTargets.Add(Target)) Targets.Add(Target);

            foreach (var Target in Targets)
            {
                var StagedTarget = MapTransform(
                    SourceRoot, StagedRoot, Target.transform,
                    "climb-surface target").gameObject;
                if (StagedTarget.GetComponents<ClimbModifierSurface>().Length != 0)
                    throw new InvalidOperationException(
                        "Staged climb target already contains native mechanics.");
                var Surface = StagedTarget.AddComponent<ClimbModifierSurface>();
                Surface.onlySlideDown = false;
                Surface.speedMultiplier = 1f;
                Surface.slipModifier = 0f;
                Surface.staminaUsageMultiplier = 1f;
                Surface.applyStatus = false;
                Surface.statusType =
                    default(CharacterAfflictions.STATUSTYPE);
                Surface.applyPetrify = false;
                Surface.statusAmount = 0.5f;
                Surface.statusCooldown = 0.5f;
                Surface.staticClimbCost = false;
                PllClimbSurfaceAuthoringValidator.ValidateNeutralNative(
                    Surface, "Staged climb surface");
            }
            foreach (var Source in Sources)
            {
                var Staged = MapComponent(
                    SourceRoot, StagedRoot, Source,
                    "climb-surface authoring");
                if (!Staged
                    || Staged.GetComponents<PllClimbSurfaceAuthoring>().Length != 1
                    || Staged.GetComponents<ClimbModifierSurface>().Length != 1)
                    throw new InvalidOperationException(
                        "Staged climb-surface translation changed its object carrier or native target.");
            }
        }
        static void TranslateFinalGeneration(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PLLFinalGeneration> Sources)
        {
            foreach (var Source in Sources)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "final-generation root");
                if (!Source.gameObject.activeSelf || !Staged.gameObject.activeSelf)
                    throw new InvalidOperationException(
                        "Final-generation source and pre-translation staged roots must be active.");
                var Descendants = Source.GetComponentsInChildren<Transform>(true)
                    .Where(Value => Value != Source.transform).ToArray();
                var States = Descendants.Select(Value => Value.gameObject.activeSelf).ToArray();
                Staged.gameObject.SetActive(false);
                if (Staged.gameObject.activeSelf)
                    throw new InvalidOperationException("Staged final-generation root remained active.");
                for (var i = 0; i < Descendants.Length; i++)
                {
                    var Mapped = MapTransform(SourceRoot, StagedRoot, Descendants[i],
                        "final-generation descendant");
                    if (Mapped.gameObject.activeSelf != States[i])
                        throw new InvalidOperationException(
                            "Staging a final-generation root changed a descendant activeSelf state.");
                }
            }
        }

        static void TranslateFog(GameObject SourceRoot, GameObject StagedRoot)
        {
            var SourceRuntime = SourceRoot.GetComponent<CustomSegmentRuntime>();
            var StagedRuntime = MapComponent(SourceRoot, StagedRoot, SourceRuntime, "segment runtime");
            var SourceOrigin = SourceRuntime.FogOrigin;
            var StagedOrigin = MapComponent(SourceRoot, StagedRoot, SourceOrigin, "fog origin");
            var SourceDisableFog = SourceOrigin.disableFog;
            var SourceAuthorings = SourceRoot.GetComponentsInChildren<PllFogAuthoring>(true);
            if (SourceAuthorings.Length != 1 || !SourceAuthorings[0] || !SourceAuthorings[0].enabled
                || SourceAuthorings[0].gameObject != SourceOrigin.gameObject
                || !Enum.IsDefined(typeof(PllFogMode), SourceAuthorings[0].Mode))
                throw new InvalidOperationException(
                    "The source prefab requires exactly one enabled PllFogAuthoring on its fog origin.");
            var SourceAuthoring = SourceAuthorings[0];
            var Mode = SourceAuthoring.Mode;
            var StagedAuthoring = MapComponent(SourceRoot, StagedRoot, SourceAuthoring, "fog authoring");
            StagedOrigin.disableFog = Mode != PllFogMode.Normal;
            var StagedAuthorings = StagedRoot.GetComponentsInChildren<PllFogAuthoring>(true);
            if (ReferenceEquals(SourceOrigin, StagedOrigin) || SourceOrigin.disableFog != SourceDisableFog
                || StagedRuntime.FogOrigin != StagedOrigin || StagedOrigin.disableFog != (Mode != PllFogMode.Normal)
                || StagedAuthorings.Length != 1 || !ReferenceEquals(StagedAuthorings[0], StagedAuthoring)
                || !StagedAuthoring || !StagedAuthoring.enabled || StagedAuthoring.gameObject != StagedOrigin.gameObject
                || StagedAuthoring.Mode != Mode)
                throw new InvalidOperationException("Staged fog translation changed its source or mapped the wrong fog origin.");
        }

        static void TranslateStingers(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllBiomeStingerAuthoring> Sources)
        {
            foreach (var Source in Sources)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "stingerDefinitions");
                var SourceAudio = Source.GetComponent<AudioSource>();
                var StagedAudio = MapComponent(SourceRoot, StagedRoot, SourceAudio, "stinger AudioSource");
                var SourceMixer = SourceAudio.outputAudioMixerGroup;
                var SourcePlayOnAwake = SourceAudio.playOnAwake;
                var SourcePlaying = SourceAudio.isPlaying;
                var SourceSample = SourceAudio.timeSamples;
                StagedAudio.outputAudioMixerGroup = null;
                StagedAudio.playOnAwake = false;
                StagedAudio.Stop();
                StagedAudio.timeSamples = 0;
                if (Staged.gameObject != StagedAudio.gameObject
                    || !ReferenceEquals(StagedAudio.outputAudioMixerGroup, null)
                    || StagedAudio.playOnAwake || StagedAudio.isPlaying || StagedAudio.timeSamples != 0
                    || SourceAudio.outputAudioMixerGroup != SourceMixer
                    || SourceAudio.playOnAwake != SourcePlayOnAwake
                    || SourceAudio.isPlaying != SourcePlaying || SourceAudio.timeSamples != SourceSample)
                    throw new InvalidOperationException("Staged stinger translation changed its source or retained playback state.");
            }
        }

        static void TranslateCampfires(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllCampfireAuthoring> Sources)
        {
            foreach (var Source in Sources)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "campfire authoring");
                var View = AddConfiguredPhotonView(Staged.gameObject);
                var Campfire = Staged.gameObject.AddComponent<Campfire>();
                Campfire.burnsFor = 180f;
                Campfire.cookTime = 5f;
                Campfire.endSize = new Vector2(0.1f, 0.2f);
                Campfire.endRot = 3f;
                Campfire.injuryReduction = 0.2f;
                Campfire.moraleBoostRadius = CampfireMoraleBoostRadius;
                Campfire.moraleBoostBaseline = CampfireMoraleBoostBaseline;
                Campfire.moraleBoostPerAdditionalScout = CampfireMoraleBoostPerAdditionalScout;
                Campfire.fireParticles = MapComponent(
                    SourceRoot, StagedRoot, Source.FireParticles, "campfire FireParticles");
                Campfire.smokeParticlesOff = MapComponent(
                    SourceRoot, StagedRoot, Source.SmokeParticlesOff, "campfire SmokeParticlesOff");
                Campfire.smokeParticlesLit = MapComponent(
                    SourceRoot, StagedRoot, Source.SmokeParticlesLit, "campfire SmokeParticlesLit");
                Campfire.logRoot = MapTransform(SourceRoot, StagedRoot, Source.LogRoot, "campfire LogRoot");
                Campfire.enableWhenLit = Source.EnableWhenLit
                    ? MapGameObject(SourceRoot, StagedRoot, Source.EnableWhenLit, "campfire EnableWhenLit") : null;
                Campfire.disableWhenLit = Source.DisableWhenLit
                    ? MapGameObject(SourceRoot, StagedRoot, Source.DisableWhenLit, "campfire DisableWhenLit") : null;
                Campfire.loop = Source.Loop
                    ? MapComponent(SourceRoot, StagedRoot, Source.Loop, "campfire Loop") : null;
                Campfire.fireStart = Array.Empty<SFX_Instance>();
                Campfire.extinguish = Array.Empty<SFX_Instance>();
                Campfire.moraleBoost = Array.Empty<SFX_Instance>();
                if (!Campfire.enabled || Staged.gameObject.GetComponents<Campfire>().Length != 1
                    || Campfire.fireParticles != Staged.FireParticles
                    || Campfire.smokeParticlesOff != Staged.SmokeParticlesOff
                    || Campfire.smokeParticlesLit != Staged.SmokeParticlesLit
                    || Campfire.logRoot != Staged.LogRoot || Campfire.enableWhenLit != Staged.EnableWhenLit
                    || Campfire.disableWhenLit != Staged.DisableWhenLit || Campfire.loop != Staged.Loop
                    || !Campfire.moraleBoostRadius.Equals(CampfireMoraleBoostRadius)
                    || !Campfire.moraleBoostBaseline.Equals(CampfireMoraleBoostBaseline)
                    || !Campfire.moraleBoostPerAdditionalScout.Equals(CampfireMoraleBoostPerAdditionalScout)
                    || Campfire.fireStart == null || Campfire.extinguish == null || Campfire.moraleBoost == null)
                    throw new InvalidOperationException("Staged campfire does not match its model-first authoring contract.");
                ValidateConfiguredPhotonView(Staged.gameObject, View, "Staged campfire");
            }
        }

        static void TranslateLuggage(
            ProjectStage Stage, string Prefix, int NodeIndex,
            GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PllLuggageAuthoring> Sources)
        {
            var StagedBySource = new Dictionary<
                PllLuggageAuthoring, PllLuggageAuthoring>(Sources.Count);
            for (var Index = 0; Index < Sources.Count; Index++)
            {
                var Source = Sources[Index];
                var Staged = MapComponent(
                    SourceRoot, StagedRoot, Source, "luggage authoring");
                var Root = Staged.gameObject;
                AddConfiguredPhotonView(Root);
                ConfigureLuggageAnimator(
                    Stage, Prefix, NodeIndex, Index, Root,
                    Source.AnimatorController, "luggage");
                var Native = Root.AddComponent<Luggage>();
                var ActivateOnOpen = Source.ActivateOnOpen
                    ? MapGameObject(
                        SourceRoot, StagedRoot, Source.ActivateOnOpen,
                        "luggage ActivateOnOpen")
                    : null;
                PllStagedLuggageShapeContract.Configure(
                    Native, Source, ActivateOnOpen,
                    Point => MapTransform(
                        SourceRoot, StagedRoot, Point,
                        "luggage spawn point"));
                SetLuggageClosed(Native);
                StagedBySource.Add(Source, Staged);
            }

            foreach (var Source in Sources)
            {
                var Staged = StagedBySource[Source];
                using var Serialized = new SerializedObject(Staged);
                Serialized.UpdateIfRequiredOrScript();
                Property(Serialized, "ActivateOnOpenValue")
                    .objectReferenceValue = Source.ActivateOnOpen
                        ? MapGameObject(
                            SourceRoot, StagedRoot, Source.ActivateOnOpen,
                            "luggage ActivateOnOpen")
                        : null;
                var Replacement = Property(
                    Serialized, "ReplacesAuthoredValue");
                if (Source.ReplacesAuthored)
                {
                    if (!StagedBySource.TryGetValue(
                            Source.ReplacesAuthored, out var StagedTarget))
                        throw new InvalidOperationException(
                            "Staged luggage authored replacement is outside its fixed source prefab.");
                    Replacement.objectReferenceValue = StagedTarget;
                }
                else Replacement.objectReferenceValue = null;

                var Groups = Property(Serialized, "SpawnGroupsValue");
                if (Groups.arraySize != Source.SpawnGroups.Count)
                    throw new InvalidOperationException(
                        "Staged luggage changed its SpawnGroups array.");
                for (var GroupIndex = 0;
                    GroupIndex < Source.SpawnGroups.Count; GroupIndex++)
                {
                    var Points = Groups.GetArrayElementAtIndex(GroupIndex)
                        .FindPropertyRelative("PointsValue");
                    var SourcePoints = Source.SpawnGroups[GroupIndex].Points;
                    if (Points == null || Points.arraySize != SourcePoints.Count)
                        throw new InvalidOperationException(
                            "Staged luggage changed a SpawnGroup point array.");
                    for (var PointIndex = 0;
                        PointIndex < SourcePoints.Count; PointIndex++)
                        Points.GetArrayElementAtIndex(PointIndex)
                            .objectReferenceValue = MapTransform(
                                SourceRoot, StagedRoot,
                                SourcePoints[PointIndex],
                                "luggage SpawnGroups point");
                }
                Serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(Staged);
            }

            foreach (var Source in Sources)
            {
                var Staged = StagedBySource[Source];
                var Root = Staged.gameObject;
                var Native = Root.GetComponents<Luggage>();
                var Views = Root.GetComponents<PhotonView>();
                var Animators = Root.GetComponents<Animator>();
                if (!Staged.enabled
                    || Staged.GetComponents<PllLuggageAuthoring>().Length != 1
                    || Root.GetComponentsInChildren<PllLuggageAuthoring>(true).Length != 1
                    || Native.Length != 1 || !Native[0].enabled
                    || Native[0].GetType() != typeof(Luggage)
                    || Views.Length != 1 || Animators.Length != 1
                    || Root.GetComponentsInChildren<Luggage>(true).Length != 1
                    || Root.GetComponentsInChildren<PhotonView>(true).Length != 1
                    || Root.GetComponentsInChildren<Animator>(true).Length != 1
                    || Root.GetComponentsInChildren<Item>(true).Length != 0
                    || Root.GetComponentsInChildren<Campfire>(true).Length != 0
                    || Root.GetComponentsInChildren<RespawnChest>(true).Length != 0
                    || Root.activeSelf != Source.gameObject.activeSelf
                    || !MatchesLuggageShape(Native[0], Staged))
                    throw new InvalidOperationException(
                        "Staged luggage does not match its logical authoring contract.");
                ValidateConfiguredPhotonView(
                    Root, Views[0], "Staged luggage");
                ValidateConfiguredAnimator(
                    Root, Animators[0], Source.AnimatorController,
                    "Staged luggage");
            }
        }
        static void TranslateStatues(
            ProjectStage Stage, string Prefix, int NodeIndex,
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllStatueAuthoring> Sources)
        {
            for (var i = 0; i < Sources.Count; i++)
            {
                var Source = Sources[i];
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "statue authoring");
                var Root = Staged.gameObject;
                var View = AddConfiguredPhotonView(Root);
                var Animator = ConfigureLuggageAnimator(
                    Stage, Prefix, NodeIndex, i, Root, Source.AnimatorController, "statue");
                var Chest = Root.AddComponent<RespawnChest>();
                Chest.skeleton = MapGameObject(SourceRoot, StagedRoot,
                    Source.SkeletonRoot ? Source.SkeletonRoot : Source.ModelRoot, "statue SkeletonRoot");
                Chest.displayName = Source.DisplayName;
                Chest.timeToOpen = Source.OpenTime;
                PllStagedLuggageShapeContract.ConfigureNeutral(Chest, false);
                Chest.spawnPointMode = Spawner.SpawnPointMode.SingleList;
                Chest.spawnSpots = new List<Transform>
                {
                    MapTransform(SourceRoot, StagedRoot,
                        Source.SpawnPoint, "statue SpawnPoint")
                };
                Chest.weightedSpawnSpots =
                    new List<Spawner.WeightedSpawnPointEntry>();
                SetLuggageClosed(Chest);
                if (!Chest.enabled || Chest.GetType() != typeof(RespawnChest) || Chest.IsOpen
                    || Chest.skeleton != (Staged.SkeletonRoot ? Staged.SkeletonRoot : Staged.ModelRoot)
                    || Chest.displayName != Staged.DisplayName || !Chest.timeToOpen.Equals(Staged.OpenTime)
                    || !PllStagedLuggageShapeContract.MatchesNeutral(Chest, false)
                    || Chest.spawnPointMode != Spawner.SpawnPointMode.SingleList
                    || Chest.spawnSpots is not { Count: 1 }
                    || Chest.spawnSpots[0] != Staged.SpawnPoint
                    || Chest.weightedSpawnSpots is not { Count: 0 })
                    throw new InvalidOperationException("Staged statue does not match its model-first authoring contract.");
                ValidateConfiguredPhotonView(Root, View, "Staged statue");
                ValidateConfiguredAnimator(Root, Animator, Source.AnimatorController, "Staged statue");
            }
        }

        static Animator ConfigureLuggageAnimator(
            ProjectStage Stage, string Prefix, int NodeIndex, int ComponentIndex,
            GameObject Root, RuntimeAnimatorController Authored, string Kind)
        {
            var Animators = Root.GetComponents<Animator>();
            if (Animators.Length > 1)
                throw new InvalidOperationException("Staged " + Kind + " acquired multiple root Animators.");
            var Animator = Animators.Length == 1 ? Animators[0] : Root.AddComponent<Animator>();
            var Controller = Authored ? Authored : Animator.runtimeAnimatorController;
            if (!Controller)
            {
                var Path = Stage.RootAssetPath + "/" + Prefix + "-" + Kind + "-"
                    + NodeIndex.ToString("D4", CultureInfo.InvariantCulture) + "-"
                    + ComponentIndex.ToString("D4", CultureInfo.InvariantCulture) + ".controller";
                Controller = CreateLuggageAnimatorController(Path);
            }
            ValidateLuggageAnimatorController(Controller, "Staged " + Kind + " AnimatorController");
            Animator.runtimeAnimatorController = Controller;
            Animator.enabled = true;
            return Animator;
        }

        static RuntimeAnimatorController CreateLuggageAnimatorController(string Path)
        {
            var Controller = AnimatorController.CreateAnimatorControllerAtPath(Path);
            if (!Controller) throw new InvalidOperationException("Unity could not create staged AnimatorController '" + Path + "'.");
            var Machine = Controller.layers[0].stateMachine;
            var Idle = Machine.AddState("Idle");
            var Unclasp = Machine.AddState("Luggage_Unclasp");
            Machine.AddState("Luggage_Open");
            Machine.defaultState = Idle;
            Controller.AddParameter("Reclasp", AnimatorControllerParameterType.Trigger);
            var Transition = Unclasp.AddTransition(Idle);
            Transition.hasExitTime = false;
            Transition.hasFixedDuration = true;
            Transition.duration = 0f;
            Transition.AddCondition(AnimatorConditionMode.If, 0f, "Reclasp");
            EditorUtility.SetDirty(Controller);
            AssetDatabase.SaveAssetIfDirty(Controller);
            return Controller;
        }

        static void SetLuggageClosed(Luggage Value)
        {
            using var Serialized = new SerializedObject(Value);
            Serialized.UpdateIfRequiredOrScript();
            var State = Serialized.FindProperty("state");
            if (State == null || State.propertyType != SerializedPropertyType.Enum)
                throw new InvalidOperationException("PEAK's serialized Luggage state is unavailable.");
            State.intValue = (int)Luggage.LuggageState.Closed;
            Serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static bool MatchesLuggageShape(
            global::Luggage Value, PllLuggageAuthoring Source)
        {
            using var Serialized = new SerializedObject(Value);
            Serialized.UpdateIfRequiredOrScript();
            var Groups = Serialized.FindProperty("weightedSpawnSpots");
            if (Groups == null || !Groups.isArray
                || Value.weightedSpawnSpots == null
                || Groups.arraySize != Value.weightedSpawnSpots.Count)
                return false;
            var Odds = new float[Groups.arraySize];
            for (var Index = 0; Index < Odds.Length; Index++)
            {
                var Property = Groups.GetArrayElementAtIndex(Index)
                    .FindPropertyRelative("percentageOdds");
                if (Property == null
                    || Property.propertyType
                        != SerializedPropertyType.Float) return false;
                Odds[Index] = Property.floatValue;
            }
            var ReadIndex = 0;
            var Matches = PllStagedLuggageShapeContract.Matches(
                Value, Source, Group =>
                {
                    if (ReadIndex >= Odds.Length
                        || !ReferenceEquals(
                            Value.weightedSpawnSpots[ReadIndex], Group))
                        return float.NaN;
                    return Odds[ReadIndex++];
                });
            return Matches && ReadIndex == Odds.Length;
        }
        internal static bool MatchesConfiguredAnimator(
            GameObject Root, Animator Value) => Value && Value.enabled
            && Value.gameObject == Root
            && Root.GetComponents<Animator>().Length == 1
            && Root.GetComponentsInChildren<Animator>(true).Length == 1
            && Value.runtimeAnimatorController;
        internal static void ValidateConfiguredAnimator(
            GameObject Root, Animator Value, RuntimeAnimatorController Authored, string Context)
        {
            if (!MatchesConfiguredAnimator(Root, Value)
                || Authored && Value.runtimeAnimatorController != Authored)
                throw new InvalidOperationException(Context + " does not own its exact enabled Animator.");
            ValidateLuggageAnimatorController(Value.runtimeAnimatorController, Context + " AnimatorController");
        }

        static PhotonView AddConfiguredPhotonView(GameObject Root)
        {
            var View = Root.AddComponent<PhotonView>();
            View.enabled = true;
            View.Group = 0;
            View.prefixField = -1;
            View.Synchronization = ViewSynchronization.UnreliableOnChange;
            View.OwnershipTransfer = OwnershipOption.Fixed;
            View.observableSearch = PhotonView.ObservableSearch.AutoFindAll;
            View.sceneViewId = 0;
            View.ObservedComponents = new List<Component>();
            return View;
        }

        internal static void ValidateConfiguredPhotonView(GameObject Root, PhotonView View, string Context)
        {
            if (!View || !View.enabled || View.gameObject != Root
                || Root.GetComponentsInChildren<PhotonView>(true).Length != 1 || View.ViewID != 0
                || View.Group != 0 || View.prefixField != -1
                || View.Synchronization != ViewSynchronization.UnreliableOnChange
                || View.OwnershipTransfer != OwnershipOption.Fixed
                || View.observableSearch != PhotonView.ObservableSearch.AutoFindAll
                || View.sceneViewId != 0 || View.ObservedComponents == null
                || View.ObservedComponents.Count != 0)
                throw new InvalidOperationException(Context + " does not own the exact unassigned PhotonView fixture.");
        }

        void RewriteZoneActionTargets(PrefabNode Node, GameObject StagedRoot)
        {
            foreach (var Source in ComponentsInNode(Node, Catalog.EffectZones))
            {
                var Staged = MapComponent(Node.SourceRoot, StagedRoot, Source, "Effect Zone actions");
                using var Serialized = new SerializedObject(Staged);
                var Triggers = PllBundleBuilder.Property(Serialized, "FogTriggersValue");
                for (var i = 0; i < Source.FogTriggers.Count; i++)
                {
                    var Target = Source.FogTriggers[i].Target;
                    var TargetNode = OwnerNode(Catalog.Graph, Target, "FogTriggers.Target");
                    var TargetRoot = TargetNode == Node ? StagedRoot
                        : AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[TargetNode.SourcePath]);
                    Triggers.GetArrayElementAtIndex(i).FindPropertyRelative("TargetValue").objectReferenceValue =
                        MapComponent(TargetNode.SourceRoot, TargetRoot, Target, "FogTriggers.Target");
                }
                var SourceTriggers = PllBundleBuilder.Property(Serialized, "NativeSourceTriggersValue");
                for (var i = 0; i < Source.NativeSourceTriggers.Count; i++)
                {
                    var Target = Source.NativeSourceTriggers[i].Target;
                    var TargetNode = OwnerNode(Catalog.Graph, Target, "NativeSourceTriggers.Target");
                    var TargetRoot = TargetNode == Node ? StagedRoot
                        : AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[TargetNode.SourcePath]);
                    SourceTriggers.GetArrayElementAtIndex(i).FindPropertyRelative("TargetValue").objectReferenceValue =
                        MapComponent(TargetNode.SourceRoot, TargetRoot, Target, "NativeSourceTriggers.Target");
                }
                var ActorTriggers = PllBundleBuilder.Property(Serialized, "ActorSourceTriggersValue");
                for (var i = 0; i < Source.ActorSourceTriggers.Count; i++)
                {
                    var Target = Source.ActorSourceTriggers[i].Target;
                    var TargetNode = OwnerNode(Catalog.Graph, Target, "ActorSourceTriggers.Target");
                    var TargetRoot = TargetNode == Node ? StagedRoot
                        : AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[TargetNode.SourcePath]);
                    ActorTriggers.GetArrayElementAtIndex(i).FindPropertyRelative("TargetValue").objectReferenceValue =
                        MapComponent(TargetNode.SourceRoot, TargetRoot, Target, "ActorSourceTriggers.Target");
                }
                var Interactions = PllBundleBuilder.Property(Serialized, "InteractionTriggersValue");
                for (var i = 0; i < Source.InteractionTriggers.Count; i++)
                {
                    var Target = Source.InteractionTriggers[i].Target;
                    var TargetNode = OwnerNode(Catalog.Graph, Target, "InteractionTriggers.Target");
                    var TargetRoot = TargetNode == Node ? StagedRoot
                        : AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[TargetNode.SourcePath]);
                    Interactions.GetArrayElementAtIndex(i).FindPropertyRelative("TargetValue").objectReferenceValue =
                        MapComponent(TargetNode.SourceRoot, TargetRoot, Target, "InteractionTriggers.Target");
                }
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void RewriteCandidateReferences(
            PrefabNode Node, GameObject StagedRoot, IReadOnlyDictionary<string, string> StagedPaths)
        {
            var SpawnerEdges = Node.Edges.Where(Value => Value.Spawner).ToLookup(Value => Value.Spawner);
            foreach (var Source in Node.SourceRoot.GetComponentsInChildren<PropSpawner>(true))
            {
                var Spawner = MapComponent(Node.SourceRoot, StagedRoot, Source, "PropSpawner candidates");
                var Edges = SpawnerEdges[Source];
                var Props = new GameObject[Edges.Count()];
                foreach (var Edge in Edges)
                    Props[Edge.Index] = AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[Edge.TargetPath]);
                Spawner.props = Props;
                Spawner.overrideProps = false; Spawner.overridePropsList = null;
            }
            foreach (var EdgeGroup in Node.Edges.Where(Value => Value.ChildProps)
                .GroupBy(Value => Value.ChildProps))
            {
                var Child = MapComponent(Node.SourceRoot, StagedRoot, EdgeGroup.Key, "PllChildPropAuthoring.props");
                var Serialized = new SerializedObject(Child);
                var Property = PllBundleBuilder.Property(Serialized, "props");
                foreach (var Edge in EdgeGroup)
                    Property.GetArrayElementAtIndex(Edge.Index).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[Edge.TargetPath]);
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var Edge in Node.Edges)
            {
                if (!Edge.ActorZone) continue;
                var Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[Edge.TargetPath]);
                if (Edge.ActorZone is PllEntityZoneAuthoring Entity)
                    ((PllAuthoredEntityZoneSource)MapComponent(Node.SourceRoot, StagedRoot, Entity, "Authored Entity Zone").Source).Prefab = Prefab;
                else if (Edge.ActorZone is PllHazardZoneAuthoring Hazard)
                    ((PllAuthoredHazardZoneSource)MapComponent(Node.SourceRoot, StagedRoot, Hazard, "Authored Hazard Zone").Source).Prefab = Prefab;
            }
        }

        static void ConfigureCandidateChildEdges(PrefabNode Node, PrefabGraph Graph, GameObject StagedRoot)
        {
            foreach (var EdgeGroup in Node.Edges.Where(Value => Value.Spawner).GroupBy(Value => Value.Spawner))
            {
                PllChildPropTiming? Timing = null;
                foreach (var Edge in EdgeGroup)
                {
                    var Child = Graph.ByPath[Edge.TargetPath].SourceRoot.GetComponent<PllChildPropAuthoring>();
                    if (Child) Timing = Child.Timing;
                }
                if (!Timing.HasValue) continue;
                var Spawner = MapComponent(Node.SourceRoot, StagedRoot, EdgeGroup.Key, "child-prop parent spawner");
                ConfigureChildEdgeTiming(Spawner, Timing.Value);
            }
        }

        static void ConfigureChildEdgeTiming(PropSpawner Spawner, PllChildPropTiming Timing)
        {
            Spawner.modifiers = Spawner.modifiers ?? new List<PropSpawnerMod>();
            Spawner.postSpawnBehaviors = Spawner.postSpawnBehaviors ?? new List<PostSpawnBehavior>();
            if (Timing == PllChildPropTiming.Immediate
                && !Spawner.modifiers.OfType<PSM_ChildSpawners>().Any())
                Spawner.modifiers.Add(new PSM_ChildSpawners());
            if (Timing == PllChildPropTiming.Late
                && !Spawner.postSpawnBehaviors.OfType<PSB_ChildSpawners>().Any())
                Spawner.postSpawnBehaviors.Add(new PSB_ChildSpawners());
        }

        static PllChildPropTiming? NestedChildTiming(IReadOnlyList<GameObject> Candidates)
        {
            PllChildPropTiming? Timing = null;
            foreach (var Candidate in Candidates)
            {
                var Child = Candidate.GetComponent<PllChildPropAuthoring>();
                if (!Child) continue;
                if (Timing.HasValue && Timing.Value != Child.Timing)
                    throw new InvalidOperationException(
                        "Child-prop authoring selects nested child-prop candidates with conflicting timings.");
                Timing = Child.Timing;
            }
            return Timing;
        }

        static void TranslateVariations(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllVariationAuthoring> Variations)
        {
            var TranslatedOwners = new HashSet<GameObject>();
            foreach (var Source in Variations)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "variationDefinitions");
                var SourceGroups = Source.gameObject.GetComponents<PllVariationAuthoring>();
                var Group = Array.FindIndex(SourceGroups, Value => ReferenceEquals(Value, Source));
                if (Group < 0 || Group > 3)
                    throw new InvalidOperationException("Variation component order changed during staging.");
                for (var i = 0; i < Source.Entries.Count; i++)
                {
                    var EntryRoot = MapGameObject(SourceRoot, StagedRoot, Source.Entries[i].Root,
                        "variationDefinitions entry root");
                    var Native = EntryRoot.AddComponent<VariantObject>();
                    Native.group = (VariantObject.Group)Group;
                    Native.spawnChance = Source.Entries[i].Weight;
                    EntryRoot.SetActive(false);
                }
                if (TranslatedOwners.Add(Staged.gameObject)) Staged.gameObject.AddComponent<VariantObjectSelector>();
            }
        }

        static void TranslateEffects(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllEffectAuthoring> Effects)
        {
            foreach (var Source in Effects)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "effectDefinitions");
                if (Source.PresentationRoot)
                    MapGameObject(SourceRoot, StagedRoot, Source.PresentationRoot,
                        "effect PresentationRoot").SetActive(false);
                foreach (var SourceParticle in Source.Root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var Particle = MapComponent(SourceRoot, StagedRoot, SourceParticle, "effect particle");
                    var Main = Particle.main;
                    Main.playOnAwake = false;
                    Particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        static void TranslateCustomVisuals(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<CustomVisualParticleGraph> Visuals)
        {
            foreach (var Graph in Visuals)
            {
                var Source = Graph.Authoring;
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "custom visual");
                var Transforms = new Transform[Graph.Transforms.Length];
                var ActiveStates = new bool[Transforms.Length];
                var Positions = new Vector3[Transforms.Length];
                var Rotations = new Quaternion[Transforms.Length];
                var Scales = new Vector3[Transforms.Length];
                for (var i = 0; i < Transforms.Length; i++)
                {
                    var Transform = MapTransform(
                        SourceRoot, StagedRoot, Graph.Transforms[i], "custom visual transform");
                    Transforms[i] = Transform;
                    ActiveStates[i] = Transform.gameObject.activeSelf;
                    Positions[i] = Transform.localPosition;
                    Rotations[i] = Transform.localRotation;
                    Scales[i] = Transform.localScale;
                }
                var Renderers = new Renderer[Graph.Renderers.Length];
                var RendererStates = new bool[Renderers.Length];
                for (var i = 0; i < Renderers.Length; i++)
                {
                    var Renderer = MapComponent(
                        SourceRoot, StagedRoot, Graph.Renderers[i], "custom visual Renderer");
                    Renderers[i] = Renderer;
                    RendererStates[i] = Renderer.enabled;
                }
                var Particles = new ParticleSystem[Graph.Particles.Length];
                for (var i = 0; i < Particles.Length; i++)
                {
                    var Particle = MapComponent(
                        SourceRoot, StagedRoot, Graph.Particles[i], "custom visual particle");
                    Particles[i] = Particle;
                    var Main = Particle.main;
                    Main.playOnAwake = false;
                    Particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                Staged.gameObject.SetActive(false);
                if (Staged.gameObject.activeSelf)
                    throw new InvalidOperationException("Staged custom visual root remained active.");
                for (var i = 0; i < Transforms.Length; i++)
                {
                    if ((Transforms[i] != Staged.transform
                            && Transforms[i].gameObject.activeSelf != ActiveStates[i])
                        || !Transforms[i].localPosition.Equals(Positions[i])
                        || !Transforms[i].localRotation.Equals(Rotations[i])
                        || !Transforms[i].localScale.Equals(Scales[i]))
                        throw new InvalidOperationException(
                            "Staging a custom visual changed a descendant active state or local transform.");
                }
                for (var i = 0; i < Renderers.Length; i++)
                    if (Renderers[i].enabled != RendererStates[i])
                        throw new InvalidOperationException(
                            "Staging a custom visual changed a Renderer enabled state.");
                foreach (var Particle in Particles)
                    if (Particle.main.playOnAwake || !Particle.isStopped || Particle.particleCount != 0)
                        throw new InvalidOperationException(
                            "Staged custom visual particles must be playOnAwake=false, stopped, and cleared.");
            }
        }

        static void TranslateChildProps(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllChildPropAuthoring> Values,
            bool Permanent)
        {
            foreach (var Source in Values)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "child props");
                var NestedTiming = NestedChildTiming(Staged.Props);
                for (var i = 0; i < Source.Points.Count; i++)
                {
                    var Point = MapTransform(SourceRoot, StagedRoot, Source.Points[i], "child prop Point");
                    if (Permanent && Source.Timing == PllChildPropTiming.Late)
                    {
                        var Grouper = Point.gameObject.AddComponent<PropGrouper>();
                        Grouper.timing = PropGrouper.PropGrouperTiming.Late;
                    }
                    var Child = new GameObject("__PLL_ChildSpawner");
                    Child.transform.SetParent(Point, false);
                    var Spawner = Child.AddComponent<PropSpawner>();
                    Spawner.area = Vector2.zero;
                    Spawner.rayLength = 5000f;
                    Spawner.raycastPosition = false;
                    Spawner.nrOfSpawns = 1;
                    Spawner.randomSpawns = false;
                    Spawner.minSpawnCount = 0;
                    Spawner.chanceToUseSpawner = Source.ChancePerPoint;
                    Spawner.props = Staged.Props.ToArray();
                    Spawner.overrideProps = false;
                    Spawner.overridePropsList = null;
                    Spawner.syncTransforms = true;
                    Spawner.layerType = HelperFunctions.LayerType.TerrainMap;
                    Spawner.modifiers = new List<PropSpawnerMod> { new PllSocketTransformModifier() };
                    Spawner.constraints = new List<PropSpawnerConstraint>();
                    Spawner.postConstraints = new List<PropSpawnerConstraintPost>();
                    Spawner.postSpawnBehaviors = new List<PostSpawnBehavior>();
                    if (NestedTiming.HasValue) ConfigureChildEdgeTiming(Spawner, NestedTiming.Value);
                }
            }
        }

        static void InjectNoPropConstraints(GameObject StagedRoot, bool Required)
        {
            foreach (var Spawner in StagedRoot.GetComponentsInChildren<PropSpawner>(true))
            {
                var Constraints = Spawner.constraints ?? (Spawner.constraints = new List<PropSpawnerConstraint>());
                if (Constraints.Any(Value => Value is PllNoPropSpawnConstraint || Value is PllPropZoneConstraint))
                    throw new InvalidOperationException("A source PropSpawner contains a generated PLL constraint.");
                if (Required) Constraints.Add(new PllNoPropSpawnConstraint());
            }
        }

        static void NormalizePropSpawners(GameObject StagedRoot)
        {
            foreach (var Spawner in StagedRoot.GetComponentsInChildren<PropSpawner>(true))
            {
                Spawner.modifiers ??= new List<PropSpawnerMod>();
                Spawner.constraints ??= new List<PropSpawnerConstraint>();
                Spawner.postConstraints ??= new List<PropSpawnerConstraintPost>();
                Spawner.postSpawnBehaviors ??= new List<PostSpawnBehavior>();
                using var Serialized = new SerializedObject(Spawner);
                Serialized.UpdateIfRequiredOrScript();
                var ValidationConstraints = Serialized.FindProperty("validationConstraints");
                var PropSpawnData = Serialized.FindProperty("_propSpawnData");
                if (ValidationConstraints == null || !ValidationConstraints.isArray
                    || PropSpawnData == null
                    || PropSpawnData.propertyType != SerializedPropertyType.ManagedReference)
                    throw new InvalidOperationException("PEAK's serialized PropSpawner state is unavailable.");
                if (ValidationConstraints.arraySize == 0)
                {
                    ValidationConstraints.arraySize = 1;
                    ValidationConstraints.arraySize = 0;
                }
                if (PropSpawnData.managedReferenceValue == null)
                    PropSpawnData.managedReferenceValue = new PropSpawnData();
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void TranslatePropZones(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllPropZoneAuthoring> Zones)
        {
            foreach (var Source in Zones)
            {
                var Staged = MapComponent(SourceRoot, StagedRoot, Source, "prop zone");
                foreach (var SourceSpawner in Source.Spawners)
                {
                    var Spawner = MapComponent(SourceRoot, StagedRoot, SourceSpawner, "prop zone spawner");
                    var Constraints = Spawner.constraints ?? (Spawner.constraints = new List<PropSpawnerConstraint>());
                    Constraints.Add(new PllPropZoneConstraint(Staged.transform, Source.Volume, Source.Inverted));
                }
            }
        }

        static void TranslateNativeResolvers(
            ProjectStage Stage, string Prefix, GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PLLNativeResolver> Resolvers)
        {
            for (var i = 0; i < Resolvers.Count; i++)
            {
                var Source = Resolvers[i];
                var Resolver = MapComponent(SourceRoot, StagedRoot, Source, "native resolver");
                if (Source.Form == PllNativeResolverForm.Source
                    || Source.Kind is PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid) continue;
                switch (Source.Kind)
                {
                    case PLLNativeResolverKind.Water:
                        TranslateWater(Stage, Prefix, i, Resolver);
                        break;
                    case PLLNativeResolverKind.Rain:
                    case PLLNativeResolverKind.Wind:
                    case PLLNativeResolverKind.Snow:
                        TranslateWeather(Resolver);
                        break;
                    case PLLNativeResolverKind.SporeCloud:
                        TranslateSporeCloud(Resolver);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported native resolver kind reached staging.");
                }
            }
        }

        static void TranslateWater(ProjectStage Stage, string Prefix, int Index, PLLNativeResolver Resolver)
        {
            var Bounds = TransformedBounds(Resolver.transform, Resolver.Volume);
            var Surface = Resolver.SurfaceRenderer;
            if (!Surface)
            {
                var Mesh = CreateWaterQuad(Resolver.Volume);
                var MeshPath = Stage.RootAssetPath + "/" + Prefix + "-water-"
                    + Index.ToString("D4", CultureInfo.InvariantCulture) + ".asset";
                AssetDatabase.CreateAsset(Mesh, MeshPath);
                var SurfaceObject = new GameObject("__PLL_WaterSurface");
                SurfaceObject.transform.SetParent(Resolver.transform, false);
                SurfaceObject.transform.localPosition = Resolver.Volume.Center
                    + Vector3.up * (Resolver.Volume.Size.y * 0.5f);
                var Filter = SurfaceObject.AddComponent<MeshFilter>();
                Filter.sharedMesh = Mesh;
                Surface = SurfaceObject.AddComponent<MeshRenderer>();
                Surface.sharedMaterial = Resolver.GeneratedSurfaceMaterial;
            }
            var ZoneObject = new GameObject("__PLL_WaterZone");
            ZoneObject.transform.SetParent(Resolver.transform, false);
            ZoneObject.transform.position = Bounds.center;
            ZoneObject.transform.rotation = Quaternion.identity;
            var Zone = ZoneObject.AddComponent<WaterZone>();
            Zone.waterPlane = Surface;
            Zone.waterDepth = Bounds.size.y;
            Zone.zoneBounds = Bounds;
            Zone.movementModifierShallow = Resolver.MovementModifierShallow;
            Zone.movementModifierDeep = Resolver.MovementModifierDeep;
            Zone.movementModifierJumping = 1f;
            Zone.jumpMultiplier = 1f;
            Zone.animModShallow = Resolver.AnimModifierShallow;
            Zone.animModDeep = Resolver.AnimModifierDeep;
        }

        static Mesh CreateWaterQuad(PllZoneVolume Volume)
        {
            var HalfX = Volume.Size.x * 0.5f;
            var HalfZ = Volume.Size.z * 0.5f;
            var Mesh = new Mesh { name = "__PLL_WaterSurface" };
            Mesh.vertices = new[]
            {
                new Vector3(-HalfX, 0f, -HalfZ), new Vector3(-HalfX, 0f, HalfZ),
                new Vector3(HalfX, 0f, HalfZ), new Vector3(HalfX, 0f, -HalfZ)
            };
            Mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            Mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            Mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            Mesh.RecalculateBounds();
            return Mesh;
        }

        static void TranslateWeather(PLLNativeResolver Resolver)
        {
            var Bounds = TransformedBounds(Resolver.transform, Resolver.Volume);
            var Weather = new GameObject("__PLL_NativeWeather");
            Weather.transform.SetParent(Resolver.transform, false);
            Weather.transform.position = Bounds.center;
            Weather.transform.rotation = Quaternion.identity;
            Weather.AddComponent<PhotonView>();
            var Zone = Weather.AddComponent<WindChillZone>();
            Zone.windZoneBounds = Bounds;
            Zone.grabStaminaMultiplierDuringWind = 1f;
            Zone.forceRadius = 2f;
            Zone.delayBeforeForce = 2f;
            Zone.ragdolledWindForceMult = 0.5f;
            Zone.statusApplicationPerSecond = 0.01f;
            Zone.windForce = 15f;
            Zone.windItemFactor = 1f;
            Weather.SetActive(false);
        }

        static void TranslateSporeCloud(PLLNativeResolver Resolver)
        {
            var Cloud = new GameObject("__PLL_NativeSporeCloud");
            Cloud.transform.SetParent(Resolver.transform, false);
            Cloud.transform.localPosition = Resolver.Volume.Center;
            var Emitter = Cloud.AddComponent<WindAffectedStatusEmitter>();
            Emitter.radius = 1f;
            Emitter.minAmount = 0.2f;
            Emitter.extraWarningTime = 1f;
            Emitter.extraWindDisableTime = 2f;
            Cloud.AddComponent<DisableBasedOnRunSettings>();
            Cloud.SetActive(false);
        }

        static void TranslateItemSpawners(
            GameObject SourceRoot, GameObject StagedRoot, IReadOnlyList<PllItemSpawnerAuthoring> Values)
        {
            foreach (var Source in Values)
            {
                var Root = MapComponent(SourceRoot, StagedRoot, Source, "item spawner").gameObject;
                var Spawner = Root.AddComponent<Spawner>();
                Spawner.spawnMode = Spawner.SpawnMode.SingleItem;
                Spawner.spawnPointMode = Spawner.SpawnPointMode.SingleList;
                Spawner.spawnedObjectPrefab = null;
                Spawner.spawns = null;
                Spawner.spawnPool = default(SpawnPool);
                Spawner.spawnSpots = Source.SpawnPoints.Select(Value =>
                    MapTransform(SourceRoot, StagedRoot, Value, "item spawner SpawnPoint")).ToList();
                Spawner.weightedSpawnSpots = new List<Spawner.WeightedSpawnPointEntry>();
                Spawner.spawnUpTowardsTarget = null;
                Spawner.spawnTransformIsSpawnerTransform = false;
                Spawner.spawnAwayFromUpTarget = false;
                Spawner.centerItemsVisually = Source.CenterItemsVisually;
                Spawner.spawnOnStart = true;
                Spawner.isKinematic = Source.IsKinematic;
                Spawner.baseSpawnChance = Source.Chance;
                Spawner.playersInRoomRequirement = Source.PlayersInRoomRequirement;
                Spawner.belowAscentRequirement = Source.BelowAscentRequirement;
                Spawner.normalRunsOnly = false;
                Spawner.canRepeatSpawns = false;
                Spawner.heightBasedSpawnPools = new List<Spawner.HeightBasedSpawnListEntry>();
                Spawner.fallbackSpawn = null;
                Root.AddComponent<Peak.SpawnedItemTracker>();
            }
        }
    }
}
