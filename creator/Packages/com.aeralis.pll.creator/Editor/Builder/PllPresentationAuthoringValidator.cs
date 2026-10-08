using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllGenerationAuthoringValidator;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllPresentationAuthoringValidator
    {
        internal PresentationAuthoring Validate(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures)
        {
            var Graph = Identity.Graph;
            var Atmosphere = PllAtmosphereAuthoringValidator.ValidateOptional(Identity);
            var CustomVisualAuthoring = DiscoverGraphComponents<PllCustomVisualAuthoring>(Graph);
            var Presentation = new PresentationAuthoring(
                DiscoverGraphComponents<PllVisualAuthoring>(Graph),
                Array.Empty<CustomVisualParticleGraph>(),
                DiscoverGraphComponents<PllEffectAuthoring>(Graph),
                DiscoverGraphComponents<PllFogZoneAuthoring>(Graph),
                DiscoverGraphComponents<PllSoundEmitterAuthoring>(Graph),
                DiscoverComponents<PllBiomeStingerAuthoring>(Identity.Root),
                DiscoverGraphComponents<PLLNativeResolver>(Graph),
                DiscoverGraphComponents<PLLNativeVisualResolver>(Graph),
                Atmosphere,
                DiscoverGraphComponents<PLLHookedProp>(Graph));
            RequireGraphComponents(Graph, Presentation.Visuals, Identity.Label, "visuals");
            RequireGraphComponents(Graph, CustomVisualAuthoring, Identity.Label, "custom visuals");
            RequireGraphComponents(Graph, Presentation.Effects, Identity.Label, "effectDefinitions");
            RequireGraphComponents(Graph, Presentation.SoundEmitters, Identity.Label, "soundEmitters");
            RequireComponents(Identity.Root, Presentation.Stingers, Identity.Label, "stingerDefinitions");
            RequireGraphComponents(Graph, Presentation.NativeResolvers, Identity.Label, "native resolvers");
            RequireGraphComponents(
                Graph, Presentation.NativeVisualResolvers, Identity.Label, "native visual resolvers");
            RequireGraphComponents(Graph, Presentation.HookedProps, Identity.Label, "hooked props");
            RequireObjects(Identity.Definition.AmbienceSounds, Identity.Label, "ambienceSounds");
            RequireObjects(Identity.Definition.EffectSounds, Identity.Label, "effectSounds");
            RequireObjects(Identity.Definition.EnterSounds, Identity.Label, "enterSounds");

            ValidateFogZonesAndActions(Identity, Generation, Presentation);
            ValidateCandidateAuthoring(Identity);
            ValidateEffects(Identity, Generation, Fixtures, Presentation);
            ValidateVisuals(Identity, Generation, Fixtures, Presentation);
            ValidateSoundEmitters(Identity, Generation, Fixtures, Presentation);
            ValidateStingers(Identity, Generation, Fixtures, Presentation);
            ValidateNativeVisualResolvers(Identity, Presentation.NativeVisualResolvers);
            var ZonePresentations = new HashSet<PllEffectAuthoring>();
            ValidateEffectZonePresentations(
                Identity, Generation, Fixtures, Presentation, ZonePresentations);
            ValidateNativeResolvers(Identity.Root, Identity.Floor, Identity.Campfire,
                ComponentsInNode(Graph.ByPath[Graph.RootPath], Presentation.NativeResolvers), Identity.Label, ZonePresentations);
            foreach (var Node in Graph.Nodes)
                if (!Node.IsRoot)
                    foreach (var Resolver in ComponentsInNode(Node, Presentation.NativeResolvers))
                    {
                        ValidateFlowSource(Resolver, Identity.Label);
                        ValidateTransformPath(Node.SourceRoot.transform, Resolver.transform, "Generated native flow source");
                    }
            var CustomVisuals = ValidateCustomVisuals(
                Identity, Generation, Fixtures, Presentation, CustomVisualAuthoring);
            Presentation = new PresentationAuthoring(
                Presentation.Visuals, CustomVisuals, Presentation.Effects, Presentation.FogZones,
                Presentation.SoundEmitters, Presentation.Stingers, Presentation.NativeResolvers,
                Presentation.NativeVisualResolvers, Presentation.Atmosphere, Presentation.HookedProps);
            foreach (var Effect in Presentation.Effects)
            {
                if (Effect.Mode != PllEffectMode.Zone || ZonePresentations.Contains(Effect)) continue;
                var EffectNode = OwnerNode(Graph, Effect, "Zone effect");
                throw new InvalidOperationException("Biome definition '" + Identity.Label + "' Zone effect '"
                    + EffectNode.SourcePath + ":" + PllHierarchy.NamePath(EffectNode.SourceRoot.transform,
                        Effect.transform) + "' has no exclusive zone or resolver owner.");
            }
            ValidateManagedFixtureRoots(Identity, Fixtures, Presentation);
            ValidateHookedProps(Identity, Generation, Fixtures, Presentation);
            return Presentation;
        }

        static void ValidateFogZonesAndActions(
            SourceIdentity Identity, GenerationAuthoring Generation,
            PresentationAuthoring Presentation)
        {
            var FogZones = Presentation.FogZones;
            RequireGraphComponents(Identity.Graph, FogZones, Identity.Label, "fog zones");
            if (FogZones.Length > PllBundleSerialization.MaximumHookedActions)
                throw new InvalidOperationException("Fog Zone declarations exceed the authoritative world target capacity.");
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            var Targets = new HashSet<PllFogZoneAuthoring>(FogZones);
            foreach (var Fog in FogZones)
            {
                var Node = OwnerNode(Identity.Graph, Fog, "Fog Zone");
                var Context = "Biome definition '" + Identity.Label + "' Fog Zone '"
                    + Node.SourcePath + ":" + PllHierarchy.NamePath(Node.SourceRoot.transform, Fog.transform) + "'";
                if (!Fog.enabled || Fog.GetComponents<PllFogZoneAuthoring>().Length != 1)
                    throw new InvalidOperationException(Context + " must be one enabled source component.");
                Fog.ValidateSettings();
                if (!Ids.Add(Fog.StableId))
                    throw new InvalidOperationException(Context + " duplicates another Fog Zone source identity.");
                if (Node.IsRoot)
                    RequireFeatureOwned(Fog.transform, Identity.Floor, Identity.Campfire, Context);
                else if (!TransformOwnership.DiscoverableWhenActivated(Node.SourceRoot.transform, Fog.transform))
                    throw new InvalidOperationException(Context + " must be active relative to its candidate root.");
                ValidateTransformPath(Node.SourceRoot.transform, Fog.transform, Context);
            }
            var NativeSources = new HashSet<PLLNativeResolver>(Presentation.NativeResolvers.Where(Resolver =>
                Resolver.Form == PllNativeResolverForm.Source
                    && Resolver.Kind is (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
                || Resolver.Form == PllNativeResolverForm.Zone && Resolver.Kind is (PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
                    && OwnerNode(Identity.Graph, Resolver, "Native Source action target").IsRoot));
            long ActionCount = 0;
            foreach (var Zone in Generation.EffectZones)
            {
                ActionCount += (long)Zone.FogTriggers.Count + Zone.PropTriggers.Count + Zone.NativeSourceTriggers.Count;
                if (ActionCount > PllBundleSerialization.MaximumHookedActions)
                    throw new InvalidOperationException("Biome generation, fog, and native source actions exceed the authoritative world action capacity.");
                var Pairs = new HashSet<(PllEffectZoneTransition, PllFogZoneAuthoring)>();
                foreach (var Trigger in Zone.FogTriggers)
                    if (Trigger == null || !Trigger.Target || !Targets.Contains(Trigger.Target)
                        || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                        || Trigger.Operation is not (PllFogZoneOperation.Activate or PllFogZoneOperation.Deactivate)
                        || !Pairs.Add((Trigger.Transition, Trigger.Target)))
                        throw new InvalidOperationException("Effect Zone fog action has a missing, foreign, duplicate, or conflicting target.");
                var SourcePairs = new HashSet<(PllEffectZoneTransition, PLLNativeResolver)>();
                foreach (var Trigger in Zone.NativeSourceTriggers)
                    if (Trigger == null || !Trigger.Target || !NativeSources.Contains(Trigger.Target)
                        || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                        || Trigger.Operation is not (PllNativeSourceOperation.Activate or PllNativeSourceOperation.Deactivate)
                        || !SourcePairs.Add((Trigger.Transition, Trigger.Target)))
                        throw new InvalidOperationException("Effect Zone native source action has a missing, foreign, duplicate, conflicting, or unsupported target.");
                foreach (var Trigger in Zone.NativeSourceTriggers)
                    if (Trigger.Target.SourceSettings.SpawnLights)
                        throw new InvalidOperationException("Action-controlled fluid sources cannot use baked flow lights. Disable Spawn Lights.");
            }
        }

        static void ValidateCandidateAuthoring(SourceIdentity Identity)
        {
            foreach (var Node in Identity.Graph.Nodes)
            {
                if (Node.IsRoot) continue;
                if (Node.SourceRoot.GetComponentsInChildren<PLLNativeResolver>(true)
                        .Any(Resolver => Resolver.Form != PllNativeResolverForm.Source
                            || Resolver.Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid))
                    || Node.SourceRoot.GetComponentsInChildren<PLLNativeVisualResolver>(true).Length != 0
                    || Node.SourceRoot.GetComponentsInChildren<PllCustomVisualAuthoring>(true).Length != 0
                    || Node.SourceRoot.GetComponentsInChildren<PllAtmosphereAuthoring>(true).Length != 0
                    || Node.SourceRoot.GetComponentsInChildren<PllBiomeStingerAuthoring>(true).Length != 0)
                    throw new InvalidOperationException("Biome definition '" + Identity.Label
                        + "' candidate '" + Node.SourcePath
                        + "' cannot contain a gameplay resolver, native visual resolver, custom visual, atmosphere, or biome stinger.");
            }
        }

        static void ValidateEffectZonePresentations(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation, HashSet<PllEffectAuthoring> Presentations)
        {
            var Effects = new HashSet<PllEffectAuthoring>(Presentation.Effects);
            foreach (var Zone in Generation.EffectZones)
            {
                var Node = OwnerNode(Identity.Graph, Zone, "Effect Zone");
                var Context = "Biome definition '" + Identity.Label + "' effect zone '"
                    + Node.SourcePath + ":" + PllHierarchy.NamePath(Node.SourceRoot.transform, Zone.transform) + "'";

                var Local = new HashSet<PllEffectAuthoring>();
                for (var i = 0; i < Zone.Effects.Count; i++)
                {
                    var Effect = Zone.Effects[i];
                    if (!Effect || !Effects.Contains(Effect) || !Local.Add(Effect)
                        || Effect.Mode != PllEffectMode.Zone || !Effect.Root
                        || Effect.Root.transform == Zone.transform
                        || !Effect.Root.transform.IsChildOf(Zone.transform))
                        throw new InvalidOperationException(Context + " Effects[" + i
                            + "] must be one distinct owned strict-descendant Zone effect.");
                    if (!Presentations.Add(Effect))
                        throw new InvalidOperationException(Context + " Effects[" + i
                            + "] is owned by another effect zone or native resolver.");
                }
                foreach (var Resolver in Presentation.NativeResolvers)
                    if (Resolver && TransformOwnership.Overlaps(Zone.transform, Resolver.transform))
                        throw new InvalidOperationException(Context + " overlaps a native-resolver root.");
                foreach (var Emitter in Presentation.SoundEmitters)
                    if (Emitter && Emitter.Root && TransformOwnership.Overlaps(Zone.transform, Emitter.Root.transform))
                        throw new InvalidOperationException(Context + " overlaps a sound-emitter root.");
                foreach (var Stinger in Presentation.Stingers)
                    if (Stinger && TransformOwnership.Overlaps(Zone.transform, Stinger.transform))
                        throw new InvalidOperationException(Context + " overlaps a stinger root.");
                foreach (var Luggage in Fixtures.Luggage)
                    if (Luggage && TransformOwnership.Overlaps(Zone.transform, Luggage.transform))
                        throw new InvalidOperationException(Context + " overlaps a luggage root.");
            }
        }

        static void ValidateManagedFixtureRoots(
            SourceIdentity Identity, FixtureAuthoring Fixtures, PresentationAuthoring Presentation)
        {
            foreach (var Statue in Fixtures.Statues)
                ValidateManagedRootDisjoint(Statue.transform, Fixtures, Presentation, Statue,
                    "Biome definition '" + Identity.Label + "' statue authoring");
            for (var i = 0; i < Fixtures.Luggage.Length; i++)
            {
                var Luggage = Fixtures.Luggage[i];
                var Node = OwnerNode(Identity.Graph, Luggage,
                    "Biome definition '" + Identity.Label + "' luggageDefinitions[" + i + "]");
                ValidateManagedRootDisjoint(Luggage.transform, Fixtures, Presentation, Luggage,
                    "Biome definition '" + Identity.Label + "' luggageDefinitions[" + i
                        + "] in '" + Node.SourcePath + "'");
            }
        }

        static void ValidateManagedRootDisjoint(
            Transform Value, FixtureAuthoring Fixtures, PresentationAuthoring Presentation,
            Component Owner, string Context)
        {
            foreach (var Effect in Presentation.Effects)
                if (Effect && Effect.Root && TransformOwnership.Overlaps(Value, Effect.Root.transform))
                    throw new InvalidOperationException(Context + " overlaps an effect root.");
            foreach (var Visual in Presentation.Visuals)
                if (Visual && Visual.Root && TransformOwnership.Overlaps(Value, Visual.Root.transform))
                    throw new InvalidOperationException(Context + " overlaps a visual root.");
            foreach (var Visual in Presentation.CustomVisuals)
                if (Visual.Authoring && TransformOwnership.Overlaps(Value, Visual.Authoring.transform))
                    throw new InvalidOperationException(Context + " overlaps a custom visual root.");
            foreach (var Sound in Presentation.SoundEmitters)
                if (Sound && Sound.Root && TransformOwnership.Overlaps(Value, Sound.Root.transform))
                    throw new InvalidOperationException(Context + " overlaps a sound-emitter root.");
            foreach (var Stinger in Presentation.Stingers)
                if (Stinger && TransformOwnership.Overlaps(Value, Stinger.transform))
                    throw new InvalidOperationException(Context + " overlaps a stinger root.");
            foreach (var Campfire in Fixtures.Campfires)
                if (Campfire && !ReferenceEquals(Campfire, Owner) && Campfire.Root
                    && TransformOwnership.Overlaps(Value, Campfire.Root.transform))
                    throw new InvalidOperationException(Context + " overlaps a campfire root.");
            foreach (var Statue in Fixtures.Statues)
                if (Statue && !ReferenceEquals(Statue, Owner) && TransformOwnership.Overlaps(Value, Statue.transform))
                    throw new InvalidOperationException(Context + " overlaps a statue root.");
            foreach (var Luggage in Fixtures.Luggage)
                if (Luggage && !ReferenceEquals(Luggage, Owner)
                    && TransformOwnership.Overlaps(Value, Luggage.transform))
                    throw new InvalidOperationException(Context + " overlaps a luggage root.");
        }

        static void ValidateHookedProps(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation)
        {
            var Root = Identity.Root;
            var Floor = Identity.Floor;
            var Campfire = Identity.Campfire;
            var Segment = Identity.Segment;
            var Anchors = Identity.Anchors;
            var Definition = Identity.Label;
            var EntryRoots = VariationEntryRoots(Generation.Variations);
            var MarkerRoots = new[]
            {
                Anchors.StartAnchor, Anchors.PlayerFormationRoot.transform, Anchors.PlaneAnchor,
                Anchors.FogBeginAnchor, Anchors.FogCutoffAnchor, Segment.FogOrigin.transform,
                Segment.FogCutout.transform, Segment.ProgressPoint.transform
            };
            foreach (var Hooked in Presentation.HookedProps)
            {
                var Node = OwnerNode(Identity.Graph, Hooked,
                    "Biome definition '" + Definition + "' hooked prop");
                var Owner = Node.SourceRoot.transform;
                var Context = "Biome definition '" + Definition + "' hooked prop '" + Node.SourcePath
                    + ":" + PllHierarchy.NamePath(Owner, Hooked.transform) + "'";
                if (!Hooked.enabled || Hooked.GetComponents<PLLHookedProp>().Length != 1)
                    throw new InvalidOperationException(Context + " must be one enabled component on its GameObject.");
                if (Node.IsRoot)
                {
                    if (Hooked.transform == Root.transform
                        || !TransformOwnership.Contains(Floor, Hooked.transform) && !TransformOwnership.Contains(Campfire, Hooked.transform)
                        || TransformOwnership.HasPropSpawnerInPath(Root.transform, Hooked.transform)
                        || !DiscoverableWhenConfigured(Root.transform, Hooked.transform, EntryRoots))
                        throw new InvalidOperationException(Context
                            + " must be a discoverable ordinary prop under one feature root and outside PropSpawner hierarchies.");
                }
                else if (!Node.SourceRoot.activeSelf || !TransformOwnership.DiscoverableWhenActivated(Owner, Hooked.transform))
                    throw new InvalidOperationException(Context
                        + " must be active relative to its reachable candidate prefab root.");
                ValidateTransformPath(Owner, Hooked.transform, Context);
                ValidateCreatorComponent(Hooked, Context);

                if (ManagedHookOwner(Node, Hooked.transform, Generation, Fixtures, Presentation)
                    || Node.IsRoot && (Hooked.transform == Floor || Hooked.transform == Campfire
                        || MarkerRoots.Any(Value => Value && TransformOwnership.Contains(Value, Hooked.transform)))
                    || Hooked.GetComponent<PllPropZoneAuthoring>()
                    || Hooked.GetComponent<PLLFinalGeneration>()
                    || Hooked.GetComponent<PLLForceSpawner>()
                    || Hooked.GetComponent<PLLNoPropSpawn>())
                    throw new InvalidOperationException(Context + " is owned by a managed fixture, marker, or presentation role.");
            }
        }

        static bool ManagedHookOwner(
            PrefabNode Node, Transform Value, GenerationAuthoring Generation,
            FixtureAuthoring Fixtures, PresentationAuthoring Presentation)
        {
            for (var Current = Value; Current; Current = Current.parent)
            {
                if (Current.GetComponent<SegmentPort>()) return true;
                if (Current == Node.SourceRoot.transform) break;
            }
            return Presentation.Effects.Any(Effect => BelongsToNode(Node, Effect)
                    && Effect.Root && TransformOwnership.Contains(Effect.Root.transform, Value))
                || Generation.EffectZones.Any(Zone => BelongsToNode(Node, Zone) && TransformOwnership.Contains(Zone.transform, Value))
                || Presentation.Visuals.Any(Visual => BelongsToNode(Node, Visual)
                    && Visual.Root && TransformOwnership.Contains(Visual.Root.transform, Value))
                || Presentation.CustomVisuals.Any(Visual => BelongsToNode(Node, Visual.Authoring)
                    && TransformOwnership.Contains(Visual.Authoring.transform, Value))
                || Presentation.SoundEmitters.Any(Sound => BelongsToNode(Node, Sound)
                    && Sound.Root && TransformOwnership.Contains(Sound.Root.transform, Value))
                || Presentation.Stingers.Any(Stinger => BelongsToNode(Node, Stinger)
                    && TransformOwnership.Contains(Stinger.transform, Value))
                || Presentation.NativeResolvers.Any(Resolver => BelongsToNode(Node, Resolver)
                    && Resolver.Root && TransformOwnership.Contains(Resolver.Root.transform, Value))
                || Fixtures.ItemSpawners.Any(Item => BelongsToNode(Node, Item)
                    && Item.Root && TransformOwnership.Contains(Item.Root.transform, Value))
                || Fixtures.Statues.Any(Statue => BelongsToNode(Node, Statue) && TransformOwnership.Contains(Statue.transform, Value))
                || Fixtures.Luggage.Any(Luggage => BelongsToNode(Node, Luggage)
                    && TransformOwnership.Contains(Luggage.transform, Value))
                || Fixtures.Campfires.Any(Campfire => BelongsToNode(Node, Campfire)
                    && Campfire.Root && TransformOwnership.Contains(Campfire.Root.transform, Value));
        }

        internal static void ValidateNativeResolvers(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PLLNativeResolver> Resolvers, string Definition,
            HashSet<PllEffectAuthoring> Presentations)
        {
            var WeatherCount = 0;
            foreach (var Resolver in Resolvers)
            {
                var Context = "Biome definition '" + Definition + "' native resolver '"
                    + PllHierarchy.NamePath(Root.transform, Resolver.transform) + "'";
                RequireFeatureOwned(Resolver.transform, Floor, Campfire, Context);
                RequireStableOwned(Root.transform, Resolver.transform, Context);
                if (Resolver.Kind == PLLNativeResolverKind.CustomFluid
                    || Resolver.SourceProfile == PllNativeFlowProfile.AuthoredSurface)
                {
                    PllFluidAuthoringValidator.Validate(Resolver, Context);
                    continue;
                }
                if (Resolver.Form == PllNativeResolverForm.Source)
                {
                    ValidateFlowSource(Resolver, Context);
                    continue;
                }
                if (Resolver.Form != PllNativeResolverForm.Zone)
                    throw new InvalidOperationException("Native Resolver Form is unsupported.");
                ValidateTransition(Resolver.Transition);
                if (Resolver.Transition.CoverDelaySeconds > 0f)
                    throw new InvalidOperationException("Native Zone Cover Delay has no supported presentation channel.");
                if (Resolver.Root != Resolver.gameObject)
                    throw new InvalidOperationException(Context + " Root must be the component GameObject.");
                ValidateZoneVolume(Resolver.Volume, Context + " Volume");
                if (Resolver.Kind == PLLNativeResolverKind.None
                    || !Enum.IsDefined(typeof(PLLNativeResolverKind), Resolver.Kind))
                    throw new InvalidOperationException(Context + " has an unsupported kind.");
                if (Resolver.transform.Find("__PLL_WaterZone") || Resolver.transform.Find("__PLL_NativeWeather")
                    || Resolver.transform.Find("__PLL_NativeSporeCloud"))
                    throw new InvalidOperationException(Context + " contains a reserved staged child name.");
                switch (Resolver.Kind)
                {
                    case PLLNativeResolverKind.Water:
                        if (Resolver.Volume.Shape != PllZoneShape.Box)
                            throw new InvalidOperationException(Context + " Water requires a Box Volume.");
                        ValidateNonnegative(Resolver.MovementModifierShallow, Context + " MovementModifierShallow");
                        ValidateNonnegative(Resolver.MovementModifierDeep, Context + " MovementModifierDeep");
                        ValidateNonnegative(Resolver.AnimModifierShallow, Context + " AnimModifierShallow");
                        ValidateNonnegative(Resolver.AnimModifierDeep, Context + " AnimModifierDeep");
                        var Right = Resolver.transform.TransformVector(Vector3.right);
                        var Up = Resolver.transform.TransformVector(Vector3.up);
                        var Forward = Resolver.transform.TransformVector(Vector3.forward);
                        ValidateFinite(Right, Context + " transformed local X axis");
                        ValidateFinite(Up, Context + " transformed local Y axis");
                        ValidateFinite(Forward, Context + " transformed local Z axis");
                        if (Right.sqrMagnitude == 0f || Up.sqrMagnitude == 0f || Forward.sqrMagnitude == 0f)
                            throw new InvalidOperationException(Context + " Water Root cannot have a collapsed transformed axis.");
                        if (Vector3.Angle(Up, Vector3.up) > 0.01f)
                            throw new InvalidOperationException(Context + " Water Root may use yaw but not pitch or roll.");
                        var Bounds = TransformedBounds(Resolver.transform, Resolver.Volume);
                        ValidateFinite(Bounds.center, Context + " transformed Volume center");
                        ValidateFinite(Bounds.size, Context + " transformed Volume size");
                        if (Bounds.size.x <= 0f || Bounds.size.y <= 0f || Bounds.size.z <= 0f)
                            throw new InvalidOperationException(Context + " transformed Water Volume must have positive bounds.");
                        var HasRenderer = (bool)Resolver.SurfaceRenderer;
                        var HasMaterial = (bool)Resolver.GeneratedSurfaceMaterial;
                        if (HasRenderer == HasMaterial)
                            throw new InvalidOperationException(Context
                                + " Water requires exactly one authored renderer or generated-surface material.");
                        if (HasRenderer)
                        {
                            if (Resolver.SurfaceRenderer.transform == Resolver.transform
                                || !Resolver.SurfaceRenderer.transform.IsChildOf(Resolver.transform))
                                throw new InvalidOperationException(Context + " SurfaceRenderer must be a strict owned descendant.");
                            RequireStableOwned(Resolver.transform, Resolver.SurfaceRenderer.transform,
                                Context + " SurfaceRenderer");
                            var Filter = Resolver.SurfaceRenderer.GetComponent<MeshFilter>();
                            var Materials = Resolver.SurfaceRenderer.sharedMaterials;
                            if (!Filter || !Filter.sharedMesh || Materials.Length == 0 || Materials.Any(Value => !Value))
                                throw new InvalidOperationException(Context
                                    + " SurfaceRenderer requires a MeshFilter, mesh, and complete material slots.");
                        }
                        if (Resolver.Presentation)
                            throw new InvalidOperationException(Context + " Water does not accept a Presentation.");
                        if (Resolver.GetComponentsInChildren<WaterZone>(true).Length != 0)
                            throw new InvalidOperationException(Context + " source already contains WaterZone.");
                        break;
                    case PLLNativeResolverKind.Lava:
                        if (Resolver.Volume.Shape != PllZoneShape.Box || Resolver.Presentation
                            || !Resolver.SurfaceRenderer)
                            throw new InvalidOperationException("Lava Zone requires a Box Volume and an owned surface renderer, with no Zone effect.");
                        if (Resolver.SurfaceRenderer.transform == Resolver.transform
                            || !Resolver.SurfaceRenderer.transform.IsChildOf(Resolver.transform)
                            || !Resolver.SurfaceRenderer.TryGetComponent<MeshFilter>(out var LavaFilter)
                            || !LavaFilter.sharedMesh || Resolver.SurfaceRenderer.sharedMaterials.Length != 1
                            || !Resolver.SurfaceRenderer.sharedMaterial || !Resolver.SurfaceRenderer.enabled
                            || Resolver.SurfaceRenderer.forceRenderingOff)
                            throw new InvalidOperationException("Lava Zone requires one visible owned surface mesh and material.");
                        for (var Node = Resolver.SurfaceRenderer.transform; Node != Resolver.transform; Node = Node.parent)
                            if (!Node.gameObject.activeSelf)
                                throw new InvalidOperationException("Lava Zone surface must be active under its source.");
                        if (Resolver.GeneratedSurfaceMaterial)
                        {
                            if (!EditorUtility.IsPersistent(Resolver.GeneratedSurfaceMaterial))
                                throw new InvalidOperationException("Material Overrides must be a persistent asset.");
                            PllNativeFlowSettings.ValidateMaterialOverridesPayload(Resolver.GeneratedSurfaceMaterial);
                        }
                        ValidateNonnegative(Resolver.LavaHeatRate, Context + " Heat Rate");
                        ValidateNonnegative(Resolver.LavaHeat, Context + " Heat");
                        break;
                    case PLLNativeResolverKind.Rain:
                    case PLLNativeResolverKind.Wind:
                    case PLLNativeResolverKind.Snow:
                        if (++WeatherCount > 1)
                            throw new InvalidOperationException("Biome definition '" + Definition
                                + "' may contain at most one Rain, Wind, or Snow resolver.");
                        ValidateResolverPresentation(Resolver, Presentations, Context, false);
                        if (Resolver.Volume.Shape != PllZoneShape.Box)
                            throw new InvalidOperationException(Context + " weather requires a Box Volume.");
                        if (Resolver.GetComponentsInChildren<WindChillZone>(true).Length != 0)
                            throw new InvalidOperationException(Context + " source already contains WindChillZone.");
                        break;
                    case PLLNativeResolverKind.SporeCloud:
                        ValidateResolverPresentation(Resolver, Presentations, Context, true);
                        if (Resolver.Volume.Shape != PllZoneShape.Sphere)
                            throw new InvalidOperationException(Context + " SporeCloud requires a Sphere Volume.");
                        ValidateSporeTransform(Resolver, Context);
                        if (Resolver.GetComponentsInChildren<WindAffectedStatusEmitter>(true).Length != 0
                            || Resolver.GetComponentsInChildren<DisableBasedOnRunSettings>(true).Length != 0)
                            throw new InvalidOperationException(Context + " source already contains native spore components.");
                        break;
                }
            }
        }

        internal static void ValidateFlowSource(PLLNativeResolver Resolver, string Context)
        {
            if (Resolver.Kind == PLLNativeResolverKind.CustomFluid || Resolver.SourceProfile == PllNativeFlowProfile.AuthoredSurface)
            {
                if (Resolver.Form != PllNativeResolverForm.Source)
                    throw new InvalidOperationException("Generated fluid resolvers must use Source form.");
                PllFluidAuthoringValidator.Validate(Resolver, Context); return;
            }
            if (Resolver.Form != PllNativeResolverForm.Source || Resolver.Root != Resolver.gameObject
                || Resolver.Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava)
                || !Resolver.enabled || Resolver.GetComponents<PLLNativeResolver>().Length != 1
                || Resolver.Presentation || Resolver.GeneratedSurfaceMaterial)
                throw new InvalidOperationException(Context + " requires one enabled Water or Lava Source with no Zone effect or generated surface.");
            ValidateTransition(Resolver.Transition);
            var Settings = Resolver.SourceSettings;
            Settings.Validate(Resolver.Kind, Resolver.SourceProfile);
            if (Resolver.Transition.CoverDelaySeconds > 0f && Settings.SpawnLights)
                throw new InvalidOperationException("Baked flow lighting cannot fade. Set Cover Delay to zero.");
            if (!Resolver.InitiallyEnabled && Settings.SpawnLights)
                throw new InvalidOperationException("Initially disabled fluid sources cannot use baked flow lights. Disable Spawn Lights.");
            Settings.ValidateOutputSelections(null);
            if (Settings.Material) PllNativeFlowSettings.ValidateMaterialOverridesPayload(Settings.Material);
            PllNativeFlowSettings.ValidateTransform(Resolver.transform);
            using var Serialized = new SerializedObject(Resolver);
            var Input = PllBundleBuilder.Property(Serialized, "SourceSettingsValue");
            var End = Input.GetEndProperty();
            while (Input.Next(true) && !SerializedProperty.EqualContents(Input, End))
                if (Input.propertyType == SerializedPropertyType.ObjectReference
                    && !Input.objectReferenceValue && Input.objectReferenceInstanceIDValue != 0)
                    throw new InvalidOperationException("Flow input '" + Input.displayName + "' is missing.");
            foreach (var Anchor in new[] { Settings.RayStart, Settings.RayEnd, Settings.Endpoint })
                if (Anchor && (Anchor == Resolver.transform || !Anchor.IsChildOf(Resolver.transform)))
                    throw new InvalidOperationException("Flow anchors must be owned strict descendants of the source.");
            if (Resolver.SourceProfile == PllNativeFlowProfile.RayWaterfall)
            {
                var Renderer = Resolver.SurfaceRenderer;
                if (!Renderer || Renderer.transform == Resolver.transform
                    || !Renderer.transform.IsChildOf(Resolver.transform)
                    || !Renderer.enabled || Renderer.forceRenderingOff)
                    throw new InvalidOperationException("Ray waterfall requires an owned surface renderer child.");
                var Filter = Renderer.GetComponent<MeshFilter>();
                if (!Filter || !Filter.sharedMesh || Renderer.sharedMaterials.Length != 1 || !Renderer.sharedMaterial)
                    throw new InvalidOperationException("Ray waterfall requires one surface mesh and material.");
                for (var Node = Renderer.transform; Node != Resolver.transform; Node = Node.parent)
                    if (!Node.gameObject.activeSelf)
                        throw new InvalidOperationException("Ray waterfall surface must be active under its source.");
            }
            else if (Resolver.SurfaceRenderer)
                throw new InvalidOperationException("Simulated flow uses its native provider surface, not a Zone surface renderer.");
            foreach (var Prefab in new[] { Settings.EndRock, Settings.Splash, Settings.SpawnAlongSpline })
                if (Prefab) RequirePrefabAsset(Prefab, "Flow output input");
            if (Settings.DetailsAlongSpline != null)
                foreach (var Prefab in Settings.DetailsAlongSpline) RequirePrefabAsset(Prefab, "Flow detail input");
            if (Settings.Material && !EditorUtility.IsPersistent(Settings.Material)
                || Settings.PusherSfx && !EditorUtility.IsPersistent(Settings.PusherSfx))
                throw new InvalidOperationException("Flow material and SFX inputs must be persistent assets.");
        }

        static void ValidateResolverPresentation(
            PLLNativeResolver Resolver, HashSet<PllEffectAuthoring> Presentations,
            string Context, bool RequireZeroDuration)
        {
            if (Resolver.SurfaceRenderer || Resolver.GeneratedSurfaceMaterial)
                throw new InvalidOperationException(Context + " does not accept Water surface fields.");
            var Presentation = Resolver.Presentation;
            if (!Presentation || !Presentation.Root || !Presentations.Add(Presentation)
                || Presentation.Mode != PllEffectMode.Zone)
                throw new InvalidOperationException(Context + " requires one exclusively owned Zone Presentation.");
            if (!TransformOwnership.Contains(Resolver.transform, Presentation.transform)
                || Presentation.Root.transform == Resolver.transform
                || !Presentation.Root.transform.IsChildOf(Resolver.transform))
                throw new InvalidOperationException(Context
                    + " Presentation component and strict-descendant Root must be owned by the resolver.");
            if (RequireZeroDuration && Presentation.Duration != 0f)
                throw new InvalidOperationException(Context + " SporeCloud Presentation duration must be zero.");
        }

        static void ValidateSporeTransform(PLLNativeResolver Resolver, string Context)
        {
            var Right = Resolver.transform.TransformVector(Vector3.right);
            var Up = Resolver.transform.TransformVector(Vector3.up);
            var Forward = Resolver.transform.TransformVector(Vector3.forward);
            var Lengths = new[] { Right.magnitude, Up.magnitude, Forward.magnitude };
            if (Lengths.Any(Value => !ManifestNumber(Value) || Value <= 0f))
                throw new InvalidOperationException(Context + " has an invalid transformed axis.");
            var Maximum = Lengths.Max();
            var Minimum = Lengths.Min();
            if ((Maximum - Minimum) / Maximum > 0.0001f
                || Math.Abs(Vector3.Dot(Right.normalized, Up.normalized)) > 0.0001f
                || Math.Abs(Vector3.Dot(Right.normalized, Forward.normalized)) > 0.0001f
                || Math.Abs(Vector3.Dot(Up.normalized, Forward.normalized)) > 0.0001f)
                throw new InvalidOperationException(Context + " must remain a uniformly scaled world sphere without shear.");
        }

        static void ValidateNativeVisualResolvers(
            SourceIdentity Identity, IReadOnlyList<PLLNativeVisualResolver> Resolvers)
        {
            if (Resolvers.Count > 1)
                throw new InvalidOperationException("Biome definition '" + Identity.Label
                    + "' may contain at most one PLLNativeVisualResolver.");
            if (Resolvers.Count == 0) return;
            var Resolver = Resolvers[0];
            var Context = "Biome definition '" + Identity.Label + "' native visual resolver";
            if (Resolver.gameObject != Identity.Root)
                throw new InvalidOperationException(Context + " must be the only component of its type directly on the biome root.");
            if (Resolver.Preset == PllNativeVisualPreset.None
                || !Enum.IsDefined(typeof(PllNativeVisualPreset), Resolver.Preset))
                throw new InvalidOperationException(Context + " must select one supported non-None Preset.");
            ValidateTransition(Resolver.Transition);
            PllEffectAppearanceValueContract.ValidateNativeVisual(Resolver);
        }

        static CustomVisualParticleGraph[] ValidateCustomVisuals(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation, IReadOnlyList<PllCustomVisualAuthoring> Visuals)
        {
            if (Visuals.Count == 0) return Array.Empty<CustomVisualParticleGraph>();
            var PresentationRoot = RequireCustomVisualPresentationRoot(Identity.Root, Identity.Label);
            var Roots = new Transform[Visuals.Count];
            var Graphs = new CustomVisualParticleGraph[Visuals.Count];
            for (var i = 0; i < Visuals.Count; i++)
            {
                var Visual = Visuals[i];
                var Node = OwnerNode(Identity.Graph, Visual,
                    "Biome definition '" + Identity.Label + "' custom visual[" + i + "]");
                var Context = "Biome definition '" + Identity.Label + "' custom visual[" + i
                    + "] in '" + Node.SourcePath + ":"
                    + PllHierarchy.NamePath(Node.SourceRoot.transform, Visual.transform) + "'";
                if (!Node.IsRoot || Visual.gameObject == PresentationRoot.gameObject
                    || !Visual.transform.IsChildOf(PresentationRoot))
                    throw new InvalidOperationException(Context
                        + " must be a strict descendant of the biome root's direct Presentation subtree.");
                RequireStableOwned(Identity.Root.transform, Visual.transform, Context);
                if (!Enum.IsDefined(typeof(PllCustomVisualSpace), Visual.Space))
                    throw new InvalidOperationException(Context + " has an unsupported Space.");
                ValidateTransition(Visual.Transition);
                ValidateFinite(Visual.PositionOffset, Context + " PositionOffset");
                if (!ManifestNumber(Visual.RepositionDistance) || Visual.RepositionDistance < 0f)
                    throw new InvalidOperationException(Context + " RepositionDistance must be finite and nonnegative.");
                Graphs[i] = ValidateCustomVisualParticleGraph(Visual.gameObject, Visual, Context);
                if (Visual.Transition.CoverDelaySeconds > 0f)
                {
                    foreach (var Renderer in Graphs[i].Renderers)
                        if (Renderer is not ParticleSystemRenderer)
                            throw new InvalidOperationException("Cover Delay requires particle-only custom visual presentation.");
                    foreach (var Particle in Graphs[i].Particles) ValidateParticleTransition(Particle);
                }
                ValidateCustomVisualOwnership(
                    Identity, Generation, Fixtures, Presentation, Visual.transform, Context);
                Roots[i] = Visual.transform;
            }
            RequireDisjointRoots(Roots, "Biome definition '" + Identity.Label
                + "' custom visual roots must be distinct and non-overlapping.");
            return Graphs;
        }

        static Transform RequireCustomVisualPresentationRoot(GameObject Root, string Definition)
        {
            Transform Match = null;
            for (var i = 0; i < Root.transform.childCount; i++)
            {
                var Child = Root.transform.GetChild(i);
                if (!string.Equals(Child.name, "Presentation", StringComparison.Ordinal)) continue;
                if (Match)
                    throw new InvalidOperationException("Biome definition '" + Definition
                        + "' custom visuals require exactly one direct child named 'Presentation'.");
                Match = Child;
            }
            if (!Match)
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' custom visuals require exactly one direct child named 'Presentation'.");
            RequireStableOwned(Root.transform, Match, "Biome definition '" + Definition + "' Presentation subtree");
            return Match;
        }

        static void ValidateCustomVisualOwnership(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation, Transform Root, string Context)
        {
            RejectOverlap(Root, Identity.Floor, Context, "FloorRoot");
            RejectOverlap(Root, Identity.Campfire, Context, "CampfireRoot");
            foreach (var Variation in Generation.Variations)
                foreach (var Entry in Variation.Entries)
                    if (Entry != null && Entry.Root)
                        RejectOverlap(Root, Entry.Root.transform, Context, "variation entry root");
            foreach (var Zone in Generation.EffectZones) RejectOverlap(Root, Zone.transform, Context, "effect-zone root");
            foreach (var Zone in Generation.PropZones) RejectOverlap(Root, Zone.transform, Context, "prop-zone root");
            foreach (var Marker in Generation.NoPropSpawn) RejectOverlap(Root, Marker.transform, Context, "no-prop root");
            foreach (var Child in Generation.ChildProps) RejectOverlap(Root, Child.transform, Context, "child-prop root");
            foreach (var Final in Generation.FinalGeneration) RejectOverlap(Root, Final.transform, Context, "final target");
            foreach (var Campfire in Fixtures.Campfires) RejectOverlap(Root, Campfire.Root.transform, Context, "campfire root");
            foreach (var Luggage in Fixtures.Luggage) RejectOverlap(Root, Luggage.transform, Context, "luggage root");
            foreach (var Statue in Fixtures.Statues) RejectOverlap(Root, Statue.transform, Context, "statue root");
            foreach (var Item in Fixtures.ItemSpawners) RejectOverlap(Root, Item.Root.transform, Context, "item-spawner root");
            foreach (var Effect in Presentation.Effects) RejectOverlap(Root, Effect.Root.transform, Context, "effect root");
            foreach (var Visual in Presentation.Visuals) RejectOverlap(Root, Visual.Root.transform, Context, "renderer-gate root");
            foreach (var Emitter in Presentation.SoundEmitters) RejectOverlap(Root, Emitter.Root.transform, Context, "sound-emitter root");
            foreach (var Stinger in Presentation.Stingers) RejectOverlap(Root, Stinger.transform, Context, "stinger root");
            foreach (var Resolver in Presentation.NativeResolvers) RejectOverlap(Root, Resolver.Root.transform, Context, "gameplay-resolver root");
            foreach (var Hooked in Presentation.HookedProps) RejectOverlap(Root, Hooked.transform, Context, "hooked-prop root");
            foreach (var Force in DiscoverComponents<PLLForceSpawner>(Identity.Root))
                RejectOverlap(Root, Force.transform, Context, "force-spawner root");
        }

        static void RejectOverlap(Transform Root, Transform Other, string Context, string Role)
        {
            if (Other && TransformOwnership.Overlaps(Root, Other))
                throw new InvalidOperationException(Context + " overlaps a " + Role + ".");
        }

        internal static void ValidateCustomVisualSelection(GameObject BiomeRoot, GameObject VisualRoot, string Context)
        {
            if (!BiomeRoot || !VisualRoot || VisualRoot == BiomeRoot)
                throw new InvalidOperationException(Context + " requires a strict biome-root descendant.");
            var Presentation = RequireCustomVisualPresentationRoot(BiomeRoot, BiomeRoot.name);
            if (VisualRoot == Presentation.gameObject || !VisualRoot.transform.IsChildOf(Presentation))
                throw new InvalidOperationException(Context
                    + " requires a strict descendant of the biome root's direct Presentation subtree.");
            if (TransformOwnership.HasPropSpawnerInPath(BiomeRoot.transform, VisualRoot.transform))
                throw new InvalidOperationException(Context + " cannot use a PropSpawner-owned hierarchy.");
            for (var Current = VisualRoot.transform.parent; Current && Current != BiomeRoot.transform; Current = Current.parent)
                foreach (var Component in Current.GetComponents<Component>())
                    if (Component && ManagedCustomVisualOwner(Component))
                        throw new InvalidOperationException(Context + " overlaps managed owner '"
                            + Component.GetType().FullName + "'.");
            ValidateCustomVisualParticleGraph(VisualRoot, null, Context);
        }

        static bool ManagedCustomVisualOwner(Component Component) =>
            Component is PllCustomVisualAuthoring || Component is PllEffectAuthoring
            || Component is PllEffectZoneAuthoring || Component is PllVisualAuthoring
            || Component is PllSoundEmitterAuthoring || Component is PllBiomeStingerAuthoring
            || Component is PLLNativeResolver
            || Component is PLLNativeVisualResolver
            || Component is PllVariationAuthoring || Component is PllPropZoneAuthoring
            || Component is PLLNoPropSpawn || Component is PllChildPropAuthoring
            || Component is PllItemSpawnerAuthoring || Component is PLLFinalGeneration || Component is PLLCriticalGeneration
            || Component is PLLForceSpawner || Component is PLLHookedProp
            || Component is PllCampfireAuthoring || Component is PllLuggageAuthoring
            || Component is PllStatueAuthoring || Component is PropSpawner
            || Component is LevelGenStep;

        internal static CustomVisualParticleGraph ValidateCustomVisualParticleGraph(
            GameObject Root, PllCustomVisualAuthoring Owner, string Context)
        {
            if (!Root) throw new InvalidOperationException(Context + " root is missing.");
            var Owners = Root.GetComponentsInChildren<PllCustomVisualAuthoring>(true);
            var ValidOwner = Owner
                ? Owner.gameObject == Root && Owners.Length == 1 && Owners[0] == Owner
                : Owners.Length == 0;
            if (!ValidOwner)
                throw new InvalidOperationException(Context
                    + " must contain only its one root PllCustomVisualAuthoring component.");
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
            {
                if (!Component)
                    throw new InvalidOperationException(Context + " contains a missing component.");
                if (Component is Transform || Component is ParticleSystem || Component is Renderer
                    || Component is MeshFilter || Component is ParticleSystemForceField || Component is Light
                    || Owner && Component == Owner) continue;
                throw new InvalidOperationException(Context + " contains unsupported component '"
                    + Component.GetType().FullName + "' at '"
                    + PllHierarchy.NamePath(Root.transform, Component.transform)
                    + "'. Custom visuals support only built-in particle presentation.");
            }

            var Transforms = DiscoverComponents<Transform>(Root);
            var Particles = DiscoverComponents<ParticleSystem>(Root);
            var Renderers = DiscoverComponents<Renderer>(Root);
            var MeshFilters = DiscoverComponents<MeshFilter>(Root);
            if (Particles.Length == 0)
                throw new InvalidOperationException(Context + " must contain at least one ParticleSystem.");
            var Owned = new HashSet<ParticleSystem>(Particles);
            var Incoming = new HashSet<ParticleSystem>();
            var Edges = new Dictionary<ParticleSystem, ParticleSystem[]>();
            var ReferencedLights = new HashSet<Light>();
            foreach (var Particle in Particles)
            {
                ValidateParticleCallbacks(Root.transform, Particle, Context);
                var Children = new ParticleSystem[Particle.subEmitters.subEmittersCount];
                for (var i = 0; i < Children.Length; i++)
                {
                    var Child = Particle.subEmitters.GetSubEmitterSystem(i);
                    if (!Child || !Owned.Contains(Child))
                        throw new InvalidOperationException(Context + " particle system '"
                            + PllHierarchy.NamePath(Root.transform, Particle.transform)
                            + "' has a missing or externally owned sub-emitter at index " + i + ".");
                    Children[i] = Child;
                    Incoming.Add(Child);
                }
                Edges.Add(Particle, Children);
                var Lights = Particle.lights;
                if (Lights.enabled && !Lights.light)
                    throw new InvalidOperationException(Context + " particle system '"
                        + PllHierarchy.NamePath(Root.transform, Particle.transform)
                        + "' has an enabled Lights module without a Light prefab.");
                if (Lights.light)
                {
                    if (!TransformOwnership.Contains(Root.transform, Lights.light.transform) && !EditorUtility.IsPersistent(Lights.light))
                        throw new InvalidOperationException(Context + " particle system '"
                            + PllHierarchy.NamePath(Root.transform, Particle.transform)
                            + "' references a scene-only Light prefab.");
                    ReferencedLights.Add(Lights.light);
                }
            }

            var States = new Dictionary<ParticleSystem, byte>();
            foreach (var Particle in Particles) Visit(Particle);
            var Roots = Particles.Where(Particle => !Incoming.Contains(Particle)).ToArray();
            foreach (var RootParticle in Roots)
            {
                if (!RootParticle.main.loop)
                    throw new InvalidOperationException(Context + " root particle system '"
                        + PllHierarchy.NamePath(Root.transform, RootParticle.transform)
                        + "' must enable Loop; PLL does not change looping on the staged copy.");
            }

            foreach (var Renderer in Renderers)
                ValidateCustomVisualRenderer(Root.transform, Renderer, Context);
            foreach (var Filter in MeshFilters)
                if (!Filter.sharedMesh || !Filter.GetComponent<Renderer>())
                    throw new InvalidOperationException(Context + " MeshFilter '"
                        + PllHierarchy.NamePath(Root.transform, Filter.transform)
                        + "' must have a mesh and a Renderer on the same presentation object.");
            foreach (var Light in Root.GetComponentsInChildren<Light>(true))
                if (!ReferencedLights.Contains(Light))
                    throw new InvalidOperationException(Context + " Light '"
                        + PllHierarchy.NamePath(Root.transform, Light.transform)
                        + "' is not referenced by a discovered ParticleSystem Lights module.");
            return new CustomVisualParticleGraph(
                Owner, Transforms, Particles, Roots, Renderers, MeshFilters);

            void Visit(ParticleSystem Particle)
            {
                byte State;
                if (States.TryGetValue(Particle, out State))
                {
                    if (State == 1)
                        throw new InvalidOperationException(Context + " contains a sub-emitter cycle at '"
                            + PllHierarchy.NamePath(Root.transform, Particle.transform) + "'.");
                    return;
                }
                States.Add(Particle, 1);
                foreach (var Child in Edges[Particle]) Visit(Child);
                States[Particle] = 2;
            }
        }

        static void ValidateParticleCallbacks(Transform Root, ParticleSystem Particle, string Context)
        {
            var Path = PllHierarchy.NamePath(Root, Particle.transform);
            if (Particle.main.stopAction != ParticleSystemStopAction.None)
                throw new InvalidOperationException(Context + " particle system '" + Path
                    + "' must use Stop Action None for repeatable biome re-entry.");
            var Collision = Particle.collision;
            if (Collision.enabled && Collision.sendCollisionMessages)
                throw new InvalidOperationException(Context + " particle system '" + Path
                    + "' sends unsupported collision callbacks.");
            var Trigger = Particle.trigger;
            if (Trigger.enabled && (Trigger.inside == ParticleSystemOverlapAction.Callback
                || Trigger.outside == ParticleSystemOverlapAction.Callback
                || Trigger.enter == ParticleSystemOverlapAction.Callback
                || Trigger.exit == ParticleSystemOverlapAction.Callback))
                throw new InvalidOperationException(Context + " particle system '" + Path
                    + "' uses an unsupported Trigger callback action.");
        }

        static void ValidateCustomVisualRenderer(Transform Root, Renderer Renderer, string Context)
        {
            var Path = PllHierarchy.NamePath(Root, Renderer.transform);
            var Materials = Renderer.sharedMaterials;
            if (Materials.Length == 0 || Materials.Any(Material => !Material || !Material.shader))
                throw new InvalidOperationException(Context + " Renderer '" + Path
                    + "' has a missing material or shader dependency.");
            var ParticleRenderer = Renderer as ParticleSystemRenderer;
            if (!ParticleRenderer) return;
            if (ParticleRenderer.renderMode == ParticleSystemRenderMode.Mesh)
            {
                var Meshes = new Mesh[ParticleRenderer.meshCount];
                if (Meshes.Length == 0 || ParticleRenderer.GetMeshes(Meshes) != Meshes.Length
                    || Meshes.Any(Mesh => !Mesh))
                    throw new InvalidOperationException(Context + " ParticleSystemRenderer '"
                        + Path + "' has a missing mesh dependency.");
            }
            var Particle = ParticleRenderer.GetComponent<ParticleSystem>();
            if (Particle && Particle.trails.enabled
                && (!ParticleRenderer.trailMaterial || !ParticleRenderer.trailMaterial.shader))
                throw new InvalidOperationException(Context + " ParticleSystemRenderer '"
                    + Path + "' has a missing trail material or shader dependency.");
        }

        static void ValidateEffects(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation)
        {
            var Definition = Identity.Label;
            var Effects = Presentation.Effects;
            var EntryRoots = VariationEntryRoots(Generation.Variations);
            var NoEntries = new HashSet<Transform>();
            var Roots = new Transform[Effects.Length];
            for (var i = 0; i < Effects.Length; i++)
            {
                var Effect = Effects[i];
                var Node = OwnerNode(Identity.Graph, Effect,
                    "Biome definition '" + Definition + "' effectDefinitions[" + i + "]");
                var Template = Node.SourceRoot;
                var ConfiguredEntries = Node.IsRoot ? EntryRoots : NoEntries;
                var Context = "Biome definition '" + Definition + "' effectDefinitions[" + i
                    + "] in '" + Node.SourcePath + "'";
                var EffectRoot = Effect.Root;
                if (!EffectRoot || EffectRoot == Template
                    || !DiscoverableWhenConfigured(Template.transform, EffectRoot.transform, ConfiguredEntries))
                    throw new InvalidOperationException(Context + " must reference a discoverable non-template child root.");
                RequireStableOwned(Template.transform, EffectRoot.transform, Context + " root");
                using var SerializedEffect = new SerializedObject(Effect);
                var PresentationRoot = Effect.PresentationRoot;
                if (!PresentationRoot && PllAuthoringCommands.Property(SerializedEffect, "presentationRoot").objectReferenceInstanceIDValue != 0)
                    throw new InvalidOperationException(Context + " references a destroyed PresentationRoot.");
                if (PresentationRoot)
                {
                    if (PresentationRoot == EffectRoot
                        || !PresentationRoot.transform.IsChildOf(EffectRoot.transform))
                        throw new InvalidOperationException(Context
                            + " PresentationRoot must be a strict owned child of Root.");
                    RequireStableOwned(EffectRoot.transform, PresentationRoot.transform,
                        Context + " PresentationRoot");
                    ValidateEffectPresentation(PresentationRoot, Context);
                }
                if (!ManifestNumber(Effect.Duration) || Effect.Duration < 0f)
                    throw new InvalidOperationException(Context + " duration must be finite and nonnegative.");
                switch (Effect.Mode)
                {
                    case PllEffectMode.Timed:
                        if (Effect.Duration <= 0f)
                            throw new InvalidOperationException(Context + " Timed mode requires a positive duration.");
                        if (Effect.EmitHook.Length != 0)
                            throw new InvalidOperationException(Context + " declares an emit hook outside EmitHook mode.");
                        break;
                    case PllEffectMode.EmitHook:
                        ValidateCreatorComponent(Effect, Context + " emit hook");
                        break;
                    case PllEffectMode.Zone:
                        if (Effect.EmitHook.Length != 0)
                            throw new InvalidOperationException(Context + " declares an emit hook outside EmitHook mode.");
                        break;
                    default:
                        throw new InvalidOperationException(Context + " has an unsupported effect mode.");
                }
                if (!Effect.Ambience && PllAuthoringCommands.Property(SerializedEffect, "ambience").objectReferenceInstanceIDValue != 0
                    || !Effect.Sound && PllAuthoringCommands.Property(SerializedEffect, "sound").objectReferenceInstanceIDValue != 0)
                    throw new InvalidOperationException(Context + " references a destroyed sound.");
                ValidateTransition(Effect.Transition);
                if (Effect.Transition.CoverDelaySeconds > 0f)
                {
                    if (PresentationRoot)
                        foreach (var Component in PresentationRoot.GetComponentsInChildren<Component>(true))
                        {
                            if (Component is Animator || Component is Renderer and not ParticleSystemRenderer)
                                throw new InvalidOperationException("Cover Delay requires particle-only effect presentation without Animator control.");
                            if (Component is Light Light)
                                ValidateNonnegative(Light.intensity, "Cover Delay light intensity");
                        }
                }
                ValidateColor(Effect.Color, Context + " color");
                PllEffectAppearanceValueContract.Validate(Effect, Template.transform);
                ValidateEffectParticles(Template.transform, Effect, ConfiguredEntries, Context);
                Roots[i] = EffectRoot.transform;
            }
            RequireDisjointRoots(Roots, "Biome definition '" + Definition + "' effect roots cannot overlap.");
            foreach (var Effect in Effects)
                foreach (var Value in Fixtures.Luggage)
                    if (TransformOwnership.Overlaps(Effect.Root.transform, Value.transform))
                        throw new InvalidOperationException("Biome definition '" + Definition + "' luggage '"
                            + Value.Id + "' cannot overlap an effect root.");
        }

        static void ValidateEffectPresentation(GameObject PresentationRoot, string Context)
        {
            foreach (var Component in PresentationRoot.GetComponentsInChildren<Component>(true))
            {
                if (!Component)
                    throw new InvalidOperationException(Context + " PresentationRoot contains a missing component.");
                if (Component is Transform || Component is ParticleSystem || Component is Renderer
                    || Component is MeshFilter || Component is Light || Component is Animator
                    || Component is ParticleSystemForceField || Component is PllVisualAuthoring) continue;
                throw new InvalidOperationException(Context + " PresentationRoot contains unsupported component '"
                    + Component.GetType().FullName + "'.");
            }
            foreach (var Renderer in PresentationRoot.GetComponentsInChildren<Renderer>(true))
            {
                var Materials = Renderer.sharedMaterials;
                if (Materials.Length == 0 || Materials.Any(Value => !Value))
                    throw new InvalidOperationException(Context
                        + " PresentationRoot contains a Renderer with missing material dependencies.");
            }
            foreach (var Filter in PresentationRoot.GetComponentsInChildren<MeshFilter>(true))
                if (!Filter.sharedMesh)
                    throw new InvalidOperationException(Context
                        + " PresentationRoot contains a MeshFilter without a mesh.");
            foreach (var Light in PresentationRoot.GetComponentsInChildren<Light>(true))
            {
                if (Light.lightmapBakeType != LightmapBakeType.Realtime || !ManifestNumber(Light.intensity)
                    || !ManifestNumber(Light.range) || !ManifestNumber(Light.spotAngle)
                    || !ManifestNumber(Light.innerSpotAngle))
                    throw new InvalidOperationException(Context
                        + " PresentationRoot Lights must be finite realtime lights.");
                ValidateColor(Light.color, Context + " PresentationRoot Light color");
            }
            foreach (var Animator in PresentationRoot.GetComponentsInChildren<Animator>(true))
                if (!Animator.runtimeAnimatorController)
                    throw new InvalidOperationException(Context
                        + " PresentationRoot Animator has no runtime controller.");
        }

        static void ValidateEffectParticles(
            Transform Template, PllEffectAuthoring Effect, HashSet<Transform> EntryRoots, string Context)
        {
            var Root = Effect.Root;
            var Particles = Root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var Particle in Particles)
            {
                if (!DiscoverableWhenConfigured(Template, Particle.transform, EntryRoots)
                    || TransformOwnership.HasPropSpawnerInPath(Template, Particle.transform))
                    throw new InvalidOperationException(Context
                        + " contains an inactive or PropSpawner-owned particle system.");
                ValidateTransformPath(Template, Particle.transform, Context + " particle system");
                if (Effect.Transition.CoverDelaySeconds > 0f) ValidateParticleTransition(Particle);
            }
            var Roots = ValidateEffectParticleGraph(Root, Context);
            if (Effect.Particles.Count != Roots.Length)
                throw new InvalidOperationException(Context
                    + " Particles must contain exactly the canonical sub-emitter graph roots.");
            for (var i = 0; i < Roots.Length; i++)
                if (Effect.Particles[i] != Roots[i])
                    throw new InvalidOperationException(Context + " Particles[" + i
                        + "] is not the canonical sub-emitter graph root at that position.");
        }

        internal static ParticleSystem[] ValidateEffectParticleGraph(GameObject Root, string Context)
        {
            if (!Root) throw new InvalidOperationException(Context + " root is missing.");
            var Particles = DiscoverComponents<ParticleSystem>(Root);
            var Owned = new HashSet<ParticleSystem>(Particles);
            var Incoming = new HashSet<ParticleSystem>();
            var Edges = new Dictionary<ParticleSystem, ParticleSystem[]>();
            foreach (var Particle in Particles)
            {
                var SubEmitters = Particle.subEmitters;
                var Children = new ParticleSystem[SubEmitters.subEmittersCount];
                for (var i = 0; i < Children.Length; i++)
                {
                    var Child = SubEmitters.GetSubEmitterSystem(i);
                    if (!Child || !Owned.Contains(Child))
                        throw new InvalidOperationException(Context + " particle system '"
                            + PllHierarchy.NamePath(Root.transform, Particle.transform)
                            + "' has a missing or externally owned sub-emitter at index " + i + ".");
                    if (!Incoming.Add(Child))
                        throw new InvalidOperationException(Context + " particle system '"
                            + PllHierarchy.NamePath(Root.transform, Child.transform)
                            + "' has multiple sub-emitter parents.");
                    Children[i] = Child;
                }
                Edges.Add(Particle, Children);
            }

            var States = new Dictionary<ParticleSystem, byte>();
            foreach (var Particle in Particles) Visit(Particle);
            return Particles.Where(Particle => !Incoming.Contains(Particle)).ToArray();

            void Visit(ParticleSystem Particle)
            {
                byte State;
                if (States.TryGetValue(Particle, out State))
                {
                    if (State == 1)
                        throw new InvalidOperationException(Context + " contains a sub-emitter cycle at '"
                            + PllHierarchy.NamePath(Root.transform, Particle.transform) + "'.");
                    return;
                }
                States.Add(Particle, 1);
                foreach (var Child in Edges[Particle]) Visit(Child);
                States[Particle] = 2;
            }
        }

        static void ValidateVisuals(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation)
        {
            var Definition = Identity.Label;
            var Visuals = Presentation.Visuals;
            var EntryRoots = VariationEntryRoots(Generation.Variations);
            var NoEntries = new HashSet<Transform>();
            var Roots = new Transform[Visuals.Length];
            for (var i = 0; i < Visuals.Length; i++)
            {
                var Visual = Visuals[i];
                var Node = OwnerNode(Identity.Graph, Visual,
                    "Biome definition '" + Definition + "' visuals[" + i + "]");
                var Template = Node.SourceRoot;
                var ConfiguredEntries = Node.IsRoot ? EntryRoots : NoEntries;
                var Context = "Biome definition '" + Definition + "' visuals[" + i
                    + "] in '" + Node.SourcePath + "'";
                var VisualRoot = Visual.Root;
                if (!VisualRoot || VisualRoot == Template
                    || !DiscoverableWhenConfigured(Template.transform, VisualRoot.transform, ConfiguredEntries))
                    throw new InvalidOperationException(Context + " must reference a discoverable non-template child root.");
                RequireStableOwned(Template.transform, VisualRoot.transform, Context + " root");
                ValidateVisualComponents(VisualRoot, Context);
                var Renderers = VisualRoot.GetComponentsInChildren<Renderer>(true);
                if (Renderers.Length == 0)
                    throw new InvalidOperationException(Context + " root contains no Renderer.");
                foreach (var Renderer in Renderers)
                {
                    if (!Renderer || !DiscoverableWhenConfigured(
                            Template.transform, Renderer.transform, ConfiguredEntries)
                        || TransformOwnership.HasPropSpawnerInPath(Template.transform, Renderer.transform))
                        throw new InvalidOperationException(Context
                            + " contains an inactive, missing, or PropSpawner-owned Renderer.");
                    ValidateTransformPath(Template.transform, Renderer.transform, Context + " Renderer");
                }
                ValidateFinite(Visual.TriggerValue, Context + " trigger value");
                var Shader = Visual.Shader;
                var Material = Visual.Material;
                if (!ReferenceEquals(Shader, null) && !Shader || !ReferenceEquals(Material, null) && !Material)
                    throw new InvalidOperationException(Context + " references a destroyed shader or material.");
                if (Shader && Material && Material.shader != Shader)
                    throw new InvalidOperationException(Context + " material does not use its declared shader.");
                var PropertyName = Visual.PropertyName;
                if ((Shader || Material) && PropertyName.Length == 0)
                    throw new InvalidOperationException(Context + " needs a shader property name.");
                if (!Shader && !Material && PropertyName.Length != 0)
                    throw new InvalidOperationException(Context
                        + " declares a shader property without a shader or material.");
                Renderer TargetRenderer = null;
                Material TargetMaterial = null;
                if (PropertyName.Length != 0)
                    foreach (var Renderer in Renderers)
                        foreach (var Candidate in Renderer.sharedMaterials)
                        {
                            if (!Candidate || Material && Candidate != Material
                                || !Material && Shader && Candidate.shader != Shader) continue;
                            if (TargetRenderer)
                                throw new InvalidOperationException(Context
                                    + " shader-property target is ambiguous across Renderer material slots.");
                            TargetRenderer = Renderer;
                            TargetMaterial = Candidate;
                        }
                if (PropertyName.Length != 0 && (!TargetRenderer || !TargetMaterial))
                    throw new InvalidOperationException(Context
                        + " cannot resolve its shader-property target under the visual root.");
                if (TargetMaterial && !TargetMaterial.HasProperty(PropertyName))
                    throw new InvalidOperationException(Context + " material has no '" + PropertyName + "' property.");
                var TargetShader = TargetMaterial ? TargetMaterial.shader : null;
                if (TargetShader)
                {
                    var PropertyIndex = TargetShader.FindPropertyIndex(PropertyName);
                    if (PropertyIndex < 0)
                        throw new InvalidOperationException(Context + " shader has no '" + PropertyName + "' property.");
                    var PropertyType = TargetShader.GetPropertyType(PropertyIndex);
                    if (PropertyType != UnityEngine.Rendering.ShaderPropertyType.Float
                        && PropertyType != UnityEngine.Rendering.ShaderPropertyType.Range)
                        throw new InvalidOperationException(Context + " property '" + PropertyName
                            + "' is not a float or range.");
                }
                ValidateTransition(Visual.Transition);
                if (Visual.Transition.CoverDelaySeconds > 0f && !TargetMaterial)
                    throw new InvalidOperationException("Cover Delay requires a resolved Float or Range material property.");
                if (PropertyName.Length != 0) ValidateToken(PropertyName, Context + " property name");
                Roots[i] = VisualRoot.transform;
            }
            RequireDisjointRoots(Roots, "Biome definition '" + Definition
                + "' visual roots must be distinct and non-overlapping.");
            foreach (var Visual in Visuals)
                foreach (var Value in Fixtures.Luggage)
                    if (TransformOwnership.Overlaps(Visual.Root.transform, Value.transform))
                        throw new InvalidOperationException("Biome definition '" + Definition + "' luggage '"
                            + Value.Id + "' cannot overlap a managed visual root.");
        }

        static void ValidateVisualComponents(GameObject Root, string Context)
        {
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                if (!Component || !(Component is Transform) && !(Component is Renderer)
                    && !(Component is MeshFilter) && !(Component is ParticleSystem)
                    && !(Component is PllVisualAuthoring) && !(Component is PllEffectAuthoring))
                    throw new InvalidOperationException(Context + " root contains unsupported component '"
                        + (Component ? Component.GetType().FullName : "missing script")
                        + "'. Visual roots may contain rendering components and PLL authoring metadata only.");
        }

        static void ValidateSoundEmitters(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation)
        {
            var Definition = Identity.Label;
            var Emitters = Presentation.SoundEmitters;
            var EntryRoots = VariationEntryRoots(Generation.Variations);
            var NoEntries = new HashSet<Transform>();
            var Roots = new Transform[Emitters.Length];
            for (var i = 0; i < Emitters.Length; i++)
            {
                var Emitter = Emitters[i];
                var Node = OwnerNode(Identity.Graph, Emitter,
                    "Biome definition '" + Definition + "' soundEmitters[" + i + "]");
                var Template = Node.SourceRoot;
                var ConfiguredEntries = Node.IsRoot ? EntryRoots : NoEntries;
                var Context = "Biome definition '" + Definition + "' soundEmitters[" + i
                    + "] in '" + Node.SourcePath + "'";
                var EmitterRoot = Emitter.Root;
                var Audio = Emitter.AudioSource;
                if (!EmitterRoot || EmitterRoot == Template
                    || !DiscoverableWhenConfigured(Template.transform, EmitterRoot.transform, ConfiguredEntries))
                    throw new InvalidOperationException(Context + " must reference a discoverable non-template child root.");
                RequireStableOwned(Template.transform, EmitterRoot.transform, Context + " root");
                if (!Audio || Audio.gameObject != EmitterRoot)
                    throw new InvalidOperationException(Context + " must own an AudioSource on its target root.");
                ValidateTransition(Emitter.Transition);
                if (Emitter.Transition.CoverDelaySeconds > 0f && !Audio.loop)
                    throw new InvalidOperationException("Cover Delay requires looping audio.");
                ValidateAudioSource(Audio, Context);
                Roots[i] = EmitterRoot.transform;
            }
            RequireDisjointRoots(Roots, "Biome definition '" + Definition + "' sound emitter roots cannot overlap.");
            for (var i = 0; i < Emitters.Length; i++)
            {
                var Emitter = Emitters[i];
                foreach (var Effect in Presentation.Effects)
                    if (TransformOwnership.Overlaps(Emitter.Root.transform, Effect.Root.transform))
                        throw new InvalidOperationException("Biome definition '" + Definition + "' sound emitter '"
                            + Emitter.name + "' cannot overlap an effect root.");
                foreach (var Value in Fixtures.Luggage)
                    if (TransformOwnership.Overlaps(Emitter.Root.transform, Value.transform))
                        throw new InvalidOperationException("Biome definition '" + Definition + "' sound emitter '"
                            + Emitter.name + "' cannot overlap luggage '" + Value.Id + "'.");
            }
        }

        static void ValidateStingers(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation)
        {
            if (Presentation.Stingers.Length == 0) return;
            var ForceSpawners = Identity.Root.GetComponentsInChildren<PLLForceSpawner>(true);
            var GenerationSteps = Identity.Root.GetComponentsInChildren<LevelGenStep>(true);
            for (var i = 0; i < Presentation.Stingers.Length; i++)
            {
                var Stinger = Presentation.Stingers[i];
                var Context = "Biome definition '" + Identity.Label + "' stingerDefinitions[" + i + "]";
                if (!Stinger)
                    throw new InvalidOperationException(Context
                        + " must be an enabled active strict-descendant leaf of the root prefab.");
                var PlacementError = PllBiomeStingerValidation.BasicPlacementError(Identity.Root, Stinger.gameObject);
                PllBiomeStingerValidation.Validate(Stinger, PlacementError,
                    (Message, Owner) => { throw new InvalidOperationException(Context + " " + Message); });
                RequireStableOwned(Identity.Root.transform, Stinger.transform, Context);
                var Audio = Stinger.GetComponent<AudioSource>();
                ValidateAudioCurve(Audio.GetCustomCurve(AudioSourceCurveType.CustomRolloff), Context, "custom rolloff");
                ValidateAudioCurve(Audio.GetCustomCurve(AudioSourceCurveType.SpatialBlend), Context, "spatial blend");
                ValidateAudioCurve(Audio.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix), Context, "reverb zone mix");
                ValidateAudioCurve(Audio.GetCustomCurve(AudioSourceCurveType.Spread), Context, "spread");
                ValidateStingerOwnership(Identity, Generation, Fixtures, Presentation,
                    ForceSpawners, GenerationSteps, Stinger, Context);
            }
        }

        static void ValidateStingerOwnership(
            SourceIdentity Identity, GenerationAuthoring Generation, FixtureAuthoring Fixtures,
            PresentationAuthoring Presentation, PLLForceSpawner[] ForceSpawners,
            LevelGenStep[] GenerationSteps, PllBiomeStingerAuthoring Stinger, string Context)
        {
            var Root = Stinger.transform;
            if (TransformOwnership.HasPropSpawnerInPath(Identity.Root.transform, Root))
                throw new InvalidOperationException(Context + " cannot use a PropSpawner-owned hierarchy.");
            RejectOverlap(Root, Identity.Floor, Context, "FloorRoot");
            RejectOverlap(Root, Identity.Campfire, Context, "CampfireRoot");
            if (Identity.Anchors.StatueRoot)
                RejectOverlap(Root, Identity.Anchors.StatueRoot.transform, Context, "StatueRoot");
            RejectOverlap(Root, Identity.Anchors.StartAnchor, Context, "StartAnchor");
            RejectOverlap(Root, Identity.Anchors.PlayerFormationRoot.transform, Context, "PlayerFormationRoot");
            RejectOverlap(Root, Identity.Anchors.PlaneAnchor, Context, "PlaneAnchor");
            RejectOverlap(Root, Identity.Anchors.FogBeginAnchor, Context, "FogBeginAnchor");
            RejectOverlap(Root, Identity.Anchors.FogCutoffAnchor, Context, "FogCutoffAnchor");
            RejectOverlap(Root, Identity.Segment.FogOrigin.transform, Context, "FogOrigin");
            RejectOverlap(Root, Identity.Segment.FogCutout.transform, Context, "FogCutout");
            RejectOverlap(Root, Identity.Segment.ProgressPoint.transform, Context, "ProgressPoint");
            RejectOverlap(Root, Identity.Entry.transform, Context, "segment-port root");
            RejectOverlap(Root, Identity.Exit.transform, Context, "segment-port root");
            foreach (var Variation in Generation.Variations)
                foreach (var Entry in Variation.Entries)
                    if (Entry != null && Entry.Root) RejectOverlap(Root, Entry.Root.transform, Context, "variation entry root");
            foreach (var Value in Generation.EffectZones) RejectOverlap(Root, Value.Root.transform, Context, "effect-zone root");
            foreach (var Value in Generation.PropZones) RejectOverlap(Root, Value.Root.transform, Context, "prop-zone root");
            foreach (var Value in Generation.NoPropSpawn) RejectOverlap(Root, Value.transform, Context, "no-prop root");
            foreach (var Value in Generation.ChildProps) RejectOverlap(Root, Value.Root.transform, Context, "child-prop root");
            foreach (var Value in Generation.FinalGeneration) RejectOverlap(Root, Value.transform, Context, "final-generation root");
            foreach (var Value in Fixtures.Campfires) RejectOverlap(Root, Value.Root.transform, Context, "campfire root");
            foreach (var Value in Fixtures.Luggage) RejectOverlap(Root, Value.transform, Context, "luggage root");
            foreach (var Value in Fixtures.Statues) RejectOverlap(Root, Value.transform, Context, "statue root");
            foreach (var Value in Fixtures.ItemSpawners) RejectOverlap(Root, Value.Root.transform, Context, "item-spawner root");
            foreach (var Value in Presentation.Effects)
            {
                RejectOverlap(Root, Value.Root.transform, Context, "effect root");
                if (Value.PresentationRoot) RejectOverlap(Root, Value.PresentationRoot.transform, Context, "effect presentation root");
            }
            foreach (var Value in Presentation.Visuals) RejectOverlap(Root, Value.Root.transform, Context, "visual root");
            foreach (var Value in Presentation.CustomVisuals) RejectOverlap(Root, Value.Authoring.transform, Context, "custom-visual root");
            foreach (var Value in Presentation.SoundEmitters) RejectOverlap(Root, Value.Root.transform, Context, "sound-emitter root");
            foreach (var Value in Presentation.NativeResolvers) RejectOverlap(Root, Value.Root.transform, Context, "native-resolver root");
            foreach (var Value in Presentation.HookedProps) RejectOverlap(Root, Value.transform, Context, "hooked-prop root");
            foreach (var Value in Presentation.Stingers)
                if (Value != Stinger) RejectOverlap(Root, Value.transform, Context, "stinger root");
            foreach (var Value in ForceSpawners) RejectOverlap(Root, Value.transform, Context, "force-spawner root");
            foreach (var Value in GenerationSteps)
                if (Value.transform != Identity.Root.transform) RejectOverlap(Root, Value.transform, Context, "generation-step root");
        }
        static void ValidateAudioSource(AudioSource Source, string Context)
        {
            if (!Source.clip || Source.resource != Source.clip)
                throw new InvalidOperationException(Context + " must use a direct AudioClip resource.");
            if (!Source.enabled || Source.playOnAwake || Source.isPlaying || Source.timeSamples != 0)
                throw new InvalidOperationException(Context
                    + " must be enabled, stopped at its beginning, and have playOnAwake disabled.");
            if (!ManifestNumber(Source.volume) || !ManifestNumber(Source.pitch) || !ManifestNumber(Source.panStereo)
                || !ManifestNumber(Source.spatialBlend) || !ManifestNumber(Source.reverbZoneMix)
                || !ManifestNumber(Source.dopplerLevel) || !ManifestNumber(Source.spread)
                || !ManifestNumber(Source.minDistance) || !ManifestNumber(Source.maxDistance))
                throw new InvalidOperationException(Context + " exceeds the synchronized numeric range.");
            if (Source.minDistance < 0f || Source.maxDistance <= 0f || Source.maxDistance < Source.minDistance)
                throw new InvalidOperationException(Context + " needs a finite positive AudioSource maxDistance.");
            if (!Source.spatialBlend.Equals(1f))
                throw new InvalidOperationException(Context + " needs a finite fully-3D AudioSource spatialBlend.");
            var Group = Source.outputAudioMixerGroup;
            if (!ReferenceEquals(Group, null))
            {
                if (!Group || !Group.audioMixer)
                    throw new InvalidOperationException(Context + " has an invalid output AudioMixerGroup.");
                if (Array.IndexOf(Group.audioMixer.FindMatchingGroups(string.Empty), Group) < 0)
                    throw new InvalidOperationException(Context
                        + " output AudioMixerGroup is not discoverable in its AudioMixer.");
            }
            ValidateAudioCurve(Source.GetCustomCurve(AudioSourceCurveType.CustomRolloff), Context, "custom rolloff");
            ValidateAudioCurve(Source.GetCustomCurve(AudioSourceCurveType.SpatialBlend), Context, "spatial blend");
            ValidateAudioCurve(Source.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix), Context, "reverb zone mix");
            ValidateAudioCurve(Source.GetCustomCurve(AudioSourceCurveType.Spread), Context, "spread");
        }

        static void ValidateAudioCurve(AnimationCurve Curve, string Context, string Name)
        {
            if (Curve == null) return;
            foreach (var Key in Curve.keys)
                if (!ManifestNumber(Key.time) || !ManifestNumber(Key.value)
                    || !ValidTangent(Key.inTangent) || !ValidTangent(Key.outTangent)
                    || !ManifestNumber(Key.inWeight) || !ManifestNumber(Key.outWeight))
                    throw new InvalidOperationException(Context + " " + Name
                        + " curve exceeds the synchronized numeric range.");
        }

        internal static void ValidateTransition(PllPresentationTransition Transition)
        {
            if (PllAuthoringValueContract.TransitionFailure(Transition.CoverDelaySeconds) is { } Failure)
                throw new InvalidOperationException(Failure);
        }

        static void ValidateParticleTransition(ParticleSystem Particle)
        {
            if (PllAuthoringValueContract.ParticleTransitionFailure(Particle) is { } Failure)
                throw new InvalidOperationException(Failure);
        }

        static bool ValidTangent(float Value) =>
            !float.IsNaN(Value) && (float.IsInfinity(Value) || ManifestNumber(Value));
    }

    internal sealed class PresentationAuthoring
    {
        internal PresentationAuthoring(
            PllVisualAuthoring[] Visuals, CustomVisualParticleGraph[] CustomVisuals,
            PllEffectAuthoring[] Effects, PllFogZoneAuthoring[] FogZones,
            PllSoundEmitterAuthoring[] SoundEmitters,
            PllBiomeStingerAuthoring[] Stingers, PLLNativeResolver[] NativeResolvers,
            PLLNativeVisualResolver[] NativeVisualResolvers,
            PllAtmosphereAuthoring Atmosphere, PLLHookedProp[] HookedProps)
        {
            this.Visuals = Visuals;
            this.CustomVisuals = CustomVisuals;
            this.Effects = Effects;
            this.FogZones = FogZones;
            this.SoundEmitters = SoundEmitters;
            this.Stingers = Stingers;
            this.NativeResolvers = NativeResolvers;
            this.NativeVisualResolvers = NativeVisualResolvers;
            this.Atmosphere = Atmosphere;
            this.HookedProps = HookedProps;
        }

        internal PllVisualAuthoring[] Visuals { get; }
        internal CustomVisualParticleGraph[] CustomVisuals { get; }
        internal PllEffectAuthoring[] Effects { get; }
        internal PllFogZoneAuthoring[] FogZones { get; }
        internal PllSoundEmitterAuthoring[] SoundEmitters { get; }
        internal PllBiomeStingerAuthoring[] Stingers { get; }
        internal PLLNativeResolver[] NativeResolvers { get; }
        internal PLLNativeVisualResolver[] NativeVisualResolvers { get; }
        internal PllAtmosphereAuthoring Atmosphere { get; }
        internal PLLHookedProp[] HookedProps { get; }
    }

    internal sealed class CustomVisualParticleGraph
    {
        internal CustomVisualParticleGraph(
            PllCustomVisualAuthoring Authoring, Transform[] Transforms, ParticleSystem[] Particles,
            ParticleSystem[] RootParticles, Renderer[] Renderers, MeshFilter[] MeshFilters)
        {
            this.Authoring = Authoring;
            this.Transforms = Transforms;
            this.Particles = Particles;
            this.RootParticles = RootParticles;
            this.Renderers = Renderers;
            this.MeshFilters = MeshFilters;
        }

        internal PllCustomVisualAuthoring Authoring { get; }
        internal Transform[] Transforms { get; }
        internal ParticleSystem[] Particles { get; }
        internal ParticleSystem[] RootParticles { get; }
        internal Renderer[] Renderers { get; }
        internal MeshFilter[] MeshFilters { get; }
    }
}
