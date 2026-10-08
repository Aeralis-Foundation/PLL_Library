using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PLL.Library;

internal sealed class BiomeAuthoringContract
{
    readonly PllBiomeDefinition Source;
    readonly PllBiomeBehavior BehaviorValue;
    readonly Transform Template;
    readonly byte[]? DependencySeal;
    readonly VisualAuthoringContract[] Visuals;
    readonly CustomVisualContract[] CustomVisuals;
    readonly NativeVisualContract? NativeVisual;
    readonly AtmosphereAuthoringContract? Atmosphere;
    readonly EffectAuthoringContract[] Effects;
    readonly EffectZoneAuthoringContract[] EffectZones;
    readonly FogZoneAuthoringContract[] FogZones;
    readonly IReadOnlyList<PllFogShaderAdapter> FogShaders;
    readonly NativeResolverContract[] NativeResolvers;
    readonly SurfaceTextureAuthoringContract[] SurfaceTextures;
    readonly PropInteractionContract[] PropInteractions;
    readonly GeneratedNativeFlowContract[] GeneratedFlows;
    readonly IReadOnlyList<NativeFlowContract> FlowSources;
    readonly Dictionary<NativeFlowContract, int> FlowSourceIndices = new();
    readonly Dictionary<GameObject, GeneratedNativeFlowContract> GeneratedFlowsByPrefab = new();
    readonly ItemSpawnerContract[] ItemSpawnerContracts;
    readonly HookedPropContract[] HookedPropContracts;
    readonly HookedPropGenerationContract[] HookedGenerationContracts;
    readonly GeneratedPropAssetCatalog? GeneratedPropAssets;
    readonly ClimbSurfaceAuthoringContract[] ClimbSurfaces;
    readonly PlacementAuthoringContract Placement;
    readonly CriticalGenerationContract[] CriticalGeneration;
    readonly EntityZoneContract[] EntityZones;
    readonly HazardZoneContract[] HazardZones;
    readonly HazardAuthoringContract[] HazardDefinitions;
    readonly EntityAuthoringContract[] EntityDefinitions;
    readonly SourceActivationTarget[] SourceTargets;
    readonly Dictionary<Component, int> ActorSourceIndices = new();
    readonly NativeClimbingFixtureContract[] ClimbingFixtureContracts;
    readonly AmuletSpawnerPlan? Amulets;
    readonly CampfireAuthoringContract? CampfireAuthoring;
    readonly StatueContract? Statue;
    readonly LuggageContract[] Luggage;
    readonly SoundEmitterContract[] SoundEmitters;
    readonly StingerContract[] Stingers;
    readonly AudioClip[] AmbienceClips;
    readonly AudioClip[] EffectSoundClips;
    readonly AudioClip[] EnterSoundClips;
    readonly BiomeId[] FavoriteBiomes;
    readonly BiomeId[] OverwrittenBiomes;
    readonly BiomeId[] BeforeBiomes;
    readonly BiomeId[] AfterBiomes;
    NativeVisualSource? BoundNativeVisualSource;

    BiomeAuthoringContract(
        PllBiomeDefinition Source,
        Transform Template,
        byte[]? DependencySeal,
        VisualAuthoringContract[] Visuals,
        CustomVisualContract[] CustomVisuals,
        NativeVisualContract? NativeVisual,
        AtmosphereAuthoringContract? Atmosphere,
        EffectAuthoringContract[] Effects,
        EffectZoneAuthoringContract[] EffectZones,
        FogZoneAuthoringContract[] FogZones,
        PllFogShaderAdapter[] FogShaderAdapters,
        NativeResolverContract[] NativeResolvers,
        SurfaceTextureAuthoringContract[] SurfaceTextures, PropInteractionContract[] PropInteractions,
        GeneratedNativeFlowContract[] GeneratedFlows,
        NativeFlowContract[] FlowSources,
        ItemSpawnerContract[] ItemSpawnerContracts,
        HookedPropContract[] HookedPropContracts,
        HookedPropGenerationContract[] HookedGenerationContracts, GeneratedPropAssetCatalog? GeneratedPropAssets,
        ClimbSurfaceAuthoringContract[] ClimbSurfaces,
        PlacementAuthoringContract Placement,
        CriticalGenerationContract[] CriticalGeneration,
        EntityZoneContract[] EntityZones,
        HazardZoneContract[] HazardZones,
        HazardAuthoringContract[] HazardDefinitions, EntityAuthoringContract[] EntityDefinitions,
        NativeClimbingFixtureContract[] ClimbingFixtures,
        AmuletSpawnerPlan? Amulets,
        CampfireAuthoringContract? CampfireAuthoring,
        StatueContract? Statue,
        LuggageContract[] Luggage,
        SoundEmitterContract[] SoundEmitters,
        StingerContract[] Stingers,
        AudioClip[] AmbienceClips,
        AudioClip[] EffectSoundClips,
        AudioClip[] EnterSoundClips,
        BiomeId[] FavoriteBiomes,
        BiomeId[] OverwrittenBiomes,
        BiomeId[] BeforeBiomes,
        BiomeId[] AfterBiomes)
    {
        this.Source = Source;
        BehaviorValue = Source.Behavior;
        this.Template = Template;
        this.DependencySeal = DependencySeal is null ? null : (byte[])DependencySeal.Clone();
        this.Visuals = Visuals;
        this.CustomVisuals = CustomVisuals;
        this.NativeVisual = NativeVisual;
        this.Atmosphere = Atmosphere;
        this.Effects = Effects;
        this.EffectZones = EffectZones;
        this.FogZones = FogZones;
        FogShaders = Array.AsReadOnly(FogShaderAdapters);
        this.NativeResolvers = NativeResolvers;
        this.SurfaceTextures = SurfaceTextures; this.PropInteractions = PropInteractions;
        this.GeneratedFlows = GeneratedFlows;
        this.FlowSources = Array.AsReadOnly(FlowSources);
        for (var i = 0; i < FlowSources.Length; i++) FlowSourceIndices.Add(FlowSources[i], FogZones.Length + i);
        foreach (var Flow in GeneratedFlows) GeneratedFlowsByPrefab.Add(Flow.CandidateTemplate, Flow);
        this.ItemSpawnerContracts = ItemSpawnerContracts;
        this.HookedPropContracts = HookedPropContracts;
        this.HookedGenerationContracts = HookedGenerationContracts;
        this.GeneratedPropAssets = GeneratedPropAssets;
        this.ClimbSurfaces = ClimbSurfaces;
        this.Placement = Placement;
        this.CriticalGeneration = CriticalGeneration;
        this.EntityZones = EntityZones;
        this.HazardZones = HazardZones;
        this.HazardDefinitions = HazardDefinitions; this.EntityDefinitions = EntityDefinitions;
        SourceTargets = new SourceActivationTarget[FlowSources.Length + HazardDefinitions.Length + EntityDefinitions.Length];
        for (var i = 0; i < FlowSources.Length; i++)
            SourceTargets[i] = new SourceActivationTarget(FogZones.Length + i, FlowSources[i].CoverDelaySeconds, FlowSources[i].InitiallyEnabled);
        for (var i = 0; i < HazardDefinitions.Length; i++)
        {
            var Index = FlowSources.Length + i;
            SourceTargets[Index] = new SourceActivationTarget(FogZones.Length + Index, 0f, HazardDefinitions[i].Source.InitiallyEnabled);
            ActorSourceIndices.Add(HazardDefinitions[i].Source, SourceTargets[Index].TargetIndex);
        }
        for (var i = 0; i < EntityDefinitions.Length; i++)
        {
            var Index = FlowSources.Length + HazardDefinitions.Length + i;
            SourceTargets[Index] = new SourceActivationTarget(FogZones.Length + Index, 0f, EntityDefinitions[i].Source.InitiallyEnabled);
            ActorSourceIndices.Add(EntityDefinitions[i].Source, SourceTargets[Index].TargetIndex);
        }
        ClimbingFixtureContracts = ClimbingFixtures;
        this.Amulets = Amulets;
        this.CampfireAuthoring = CampfireAuthoring;
        this.Statue = Statue;
        this.Luggage = Luggage;
        this.SoundEmitters = SoundEmitters;
        this.Stingers = Stingers;
        this.AmbienceClips = AmbienceClips;
        this.EffectSoundClips = EffectSoundClips;
        this.EnterSoundClips = EnterSoundClips;
        this.FavoriteBiomes = FavoriteBiomes;
        this.OverwrittenBiomes = OverwrittenBiomes;
        this.BeforeBiomes = BeforeBiomes;
        this.AfterBiomes = AfterBiomes;
        Id = Source.BiomeId;
        SegmentId = $"{Id.Value}/segment";
        DisplayTitle = Source.DisplayTitle;
        Color = Source.Color;
    }

    internal BiomeId Id { get; }
    internal string SegmentId { get; }
    internal string DisplayTitle { get; }
    internal Color Color { get; }
    internal IReadOnlyList<AudioClip> Ambience => AmbienceClips;
    internal IReadOnlyList<AudioClip> EffectSounds => EffectSoundClips;
    internal IReadOnlyList<AudioClip> EnterSounds => EnterSoundClips;
    internal IReadOnlyList<ItemSpawnerContract> ItemSpawners => ItemSpawnerContracts;
    internal IReadOnlyList<HookedPropContract> HookedProps => HookedPropContracts;
    internal IReadOnlyList<HookedPropGenerationContract> HookedGeneration => HookedGenerationContracts;
    internal GeneratedPropAssetCatalog? GeneratedPropCatalog => GeneratedPropAssets;
    internal int AmuletOpportunityCoverage => Amulets?.OpportunityCoverage ?? 0;
    internal IReadOnlyList<string> AmuletSourceBindings => Amulets?.SourceBindings ?? Array.Empty<string>();
    internal void ResolveAmuletSource(NativeAmuletSourceContract Provider) => Amulets?.BindProvider(Provider);
    internal void ConfigureAmulets(Transform Root, MapRun Run, MapSlot Owner) =>
        Amulets?.Configure(Root, Id, Run, Owner);
    internal bool RequiresNativeBeetles
    {
        get
        {
            foreach (var Zone in EntityZones) if (Zone.RequiresBeetles) return true;
            foreach (var Definition in EntityDefinitions) if (Definition.RequiresBeetles) return true;
            return false;
        }
    }
    internal EntityZoneBinding[] BindEntityZones(Transform Root)
    {
        if (EntityZones.Length == 0) return Array.Empty<EntityZoneBinding>();
        var Bindings = new EntityZoneBinding[EntityZones.Length];
        for (var Index = 0; Index < Bindings.Length; Index++) Bindings[Index] = EntityZones[Index].Bind(Root, Id);
        return Bindings;
    }
    internal bool RequiresNativeZombies => Array.Exists(EntityZones, static Zone => Zone.RequiresZombies)
        || Array.Exists(EntityDefinitions, static Definition => Definition.RequiresZombies);
    internal bool RequiresNativeCondors => Array.Exists(EntityZones, static Zone => Zone.RequiresCondors)
        || Array.Exists(EntityDefinitions, static Definition => Definition.RequiresCondors);
    internal IReadOnlyList<HazardAuthoringContract> Hazards => HazardDefinitions;
    internal IReadOnlyList<EntityAuthoringContract> Entities => EntityDefinitions;
    internal int RequiredMechanicalHazardMask
    {
        get { var Mask = 0; foreach (var Hazard in HazardDefinitions) Mask |= Hazard.RequiredMechanicalMask; return Mask; }
    }
    internal void ResolveMechanicalHazards(NativeMechanicalHazards? Providers)
    {
        foreach (var Hazard in HazardDefinitions) Hazard.ResolveExternal(Providers);
    }
    internal bool RequiresNativeTornados => Array.Exists(HazardZones, static Zone => Zone.RequiresTornados);
    internal bool RequiresNativeTumbleweeds => Array.Exists(HazardZones, static Zone => Zone.RequiresTumbleweeds);
    internal IReadOnlyList<NativeClimbingFixtureContract> ClimbingFixtures => ClimbingFixtureContracts;
    internal IReadOnlyList<SurfaceTextureAuthoringContract> SurfaceTextureContracts => SurfaceTextures;
    internal IReadOnlyList<PropInteractionContract> PropInteractionContracts => PropInteractions;
    internal int RequiredNativeVineMask
    {
        get { var Mask = 0; foreach (var Prop in PropInteractions) Mask |= Prop.RequiresVineMask; return Mask; }
    }
    internal void ResolveVineSources(NativeVineRecipes? Recipes)
    {
        foreach (var Prop in PropInteractions) if (Prop.RequiresVineMask != 0) Prop.ResolveNative(Recipes!);
    }
    internal bool RequiresNativeRopes => Array.Exists(ClimbingFixtureContracts, static Fixture => Fixture.RequiresRope);
    internal bool RequiresNativeMountedTools => Array.Exists(ClimbingFixtureContracts, static Fixture => Fixture.RequiresMountedTool);
    internal void ResolveRopeSource(NativeHangingRopeSource? Rope, NativeMountedToolSource? Tool = null)
    {
        foreach (var Fixture in ClimbingFixtureContracts) Fixture.ResolveExternal(Rope, Tool);
    }
    internal HazardZoneBinding[] BindHazardZones(Transform Root)
    {
        var Bindings = new HazardZoneBinding[HazardZones.Length];
        for (var Index = 0; Index < Bindings.Length; Index++) Bindings[Index] = HazardZones[Index].Bind(Root, Id);
        return Bindings;
    }
    internal void ResolveEntitySources(NativeBeetleSource? Beetles, NativeZombieSource? Zombies, NativeCondorSource? Condors)
    {
        foreach (var Definition in EntityDefinitions) Definition.ResolveExternal(Beetles, Zombies, Condors);
        foreach (var Zone in EntityZones) Zone.ResolveDefinitionSources(EntityDefinitions);
        foreach (var Zone in EntityZones) Zone.ResolveExternal(Beetles, Zombies, Condors);
    }
    internal void ResolveHazardSources(NativeTornadoSource? Tornados, NativeTumbleweedSource? Tumbleweeds)
    {
        foreach (var Zone in HazardZones) Zone.ResolveExternal(Tornados, Tumbleweeds);
    }
    internal IReadOnlyList<EffectAuthoringContract> EffectContracts => Effects;
    internal IReadOnlyList<VisualAuthoringContract> VisualContracts => Visuals;
    internal NativeVisualSource? NativeVisualSource => BoundNativeVisualSource;
    internal NativeVisualContract? NativeVisualAppearance => NativeVisual;
    internal bool SuppressNativeParticles => NativeVisual?.SuppressNativeParticles ?? false;
    internal bool RequiresNativeFog
    {
        get
        {
            foreach (var Zone in EffectZones)
                if (Zone.RequiresNativeFog) return true;
            return false;
        }
    }
    internal DayNightProfile EffectiveDayNightProfile => NativeVisual is null
        ? Source.DayNightProfile
        : BoundNativeVisualSource?.DayNightProfile
            ?? throw new InvalidOperationException($"Biome '{Id}' native visual preset is not live-bound.");
    internal IReadOnlyList<EffectZoneAuthoringContract> EffectZoneContracts => EffectZones;
    internal IReadOnlyList<FogZoneAuthoringContract> FogZoneContracts => FogZones;
    internal IReadOnlyList<PllFogShaderAdapter> FogShaderAdapters => FogShaders;
    internal IReadOnlyList<NativeFlowContract> NativeSourceContracts => FlowSources;
    internal int NativeSourceTargetIndex(NativeFlowContract Source) => FlowSourceIndices[Source];
    internal IReadOnlyList<SourceActivationTarget> SourceActivationTargets => SourceTargets;
    internal int ActorSourceTargetIndex(Component Source) => ActorSourceIndices[Source];

    internal readonly struct SourceActivationTarget
    {
        internal SourceActivationTarget(int TargetIndex, float CoverDelaySeconds, bool InitiallyEnabled)
        {
            this.TargetIndex = TargetIndex; this.CoverDelaySeconds = CoverDelaySeconds; this.InitiallyEnabled = InitiallyEnabled;
        }
        internal int TargetIndex { get; }
        internal float CoverDelaySeconds { get; }
        internal bool InitiallyEnabled { get; }
    }
    internal bool HasGeneratedFlows => GeneratedFlows.Length != 0;
    internal bool ContainsGeneratedFlows(GameObject Prefab) => GeneratedFlowsByPrefab.ContainsKey(Prefab);
    internal bool HasFixedFlows => Array.Exists(NativeResolvers, static Resolver => Resolver.IsFlow);
    internal NativeFlowRuntime[] BindGeneratedFlows(GameObject Prefab, GameObject Instance, GeneratedGeometryBudget Budget, Transform BiomeFrame) =>
        GeneratedFlowsByPrefab.TryGetValue(Prefab, out var Contract)
            ? Contract.Resolve(Instance.transform, Id, Budget, BiomeFrame) : Array.Empty<NativeFlowRuntime>();
    internal NativeFlowRuntime[] PrepareFixedFlows(Transform Root, GeneratedGeometryBudget Budget, FinalForceOccurrence Selection)
    {
        var Flows = new List<NativeFlowRuntime>();
        foreach (var Resolver in NativeResolvers)
            if (Resolver.ResolveFlow(Root, Id, Budget, Selection) is { } Flow) Flows.Add(Flow);
        return Flows.ToArray();
    }
    internal void InitializeFixedFlows(NativeFlowRuntime[] Flows, NativeFlowSurfaceQuery Trace)
    {
        foreach (var Flow in Flows)
            if (!Flow.Initialize(new PropSpawner.SpawnData(), Trace))
                throw new InvalidOperationException($"Native flow '{Flow.SourceTransform.name}' could not be placed.");
    }
    internal IReadOnlyList<SoundEmitterContract> SoundEmitterContracts => SoundEmitters;
    internal void ValidatePlacementRuntime(Transform Root) => Placement.ValidateStableRuntime(Root, Id);
    internal IReadOnlyList<VariationContract> VariationGroups => Placement.VariationGroups;
    internal VariationOccurrence PrepareVariations(
        Transform Root, Func<string, string, float> EffectiveWeight) =>
        Placement.PrepareVariations(Root, Id, EffectiveWeight);
    internal void ValidateGeneratedPlacement(Transform FeatureRoot) => Placement.ValidateGeneratedRuntime(FeatureRoot, Id);
    internal IReadOnlyDictionary<LevelGenStep, int>? ResolveCriticalGeneration(Transform Root)
    {
        if (CriticalGeneration.Length == 0) return null;
        var Ranks = new Dictionary<LevelGenStep, int>();
        for (var Rank = 0; Rank < CriticalGeneration.Length; Rank++)
            CriticalGeneration[Rank].Resolve(Root, Id, Ranks, Rank);
        return Ranks;
    }
    internal bool HasGeneratedLuggage
    {
        get
        {
            foreach (var Value in Luggage)
                if (Value.Generated) return true;
            return false;
        }
    }
    internal int RequiredNativeResolverMask
    {
        get
        {
            var Result = 0;
            foreach (var Resolver in NativeResolvers)
                Result |= Resolver.NativeSourceMask;
            foreach (var Flow in GeneratedFlows) Result |= Flow.NativeSourceMask;
            return Result;
        }
    }
    internal int RequiredNativeVisualMask => NativeVisual is null ? 0 : 1 << (int)NativeVisual.Preset;
    internal int RequiredNativeParticleMask => NativeVisual is null || NativeVisual.SuppressNativeParticles
        ? 0
        : 1 << (int)NativeVisual.Preset;
    internal int RequiredNativeEffectParticleMask
    {
        get
        {
            var Result = 0;
            foreach (var Effect in Effects) Result |= Effect.NativeSourceMask;
            return Result;
        }
    }
    internal bool NeedsNativeCatalog
    {
        get
        {
            foreach (var Value in Luggage)
                if (Value.RequiresNativeCatalog) return true;
            return false;
        }
    }

    internal static BiomeAuthoringContract Create(PllBiomeDefinition Source, byte[]? DependencySeal,
        Dictionary<PllFluidDefinition, FluidDefinitionContract> FluidDefinitions)
    {
        if (DependencySeal is not null && DependencySeal.Length != 32)
            throw new ArgumentException("A presentation dependency seal must contain 32 bytes.", nameof(DependencySeal));
        var FavoriteBiomes = CopyBiomeIds(Source.Favorites, Source.BiomeId, nameof(PllBiomeDefinition.Favorites));
        var OverwrittenBiomes = CopyBiomeIds(Source.Overwrites, Source.BiomeId, nameof(PllBiomeDefinition.Overwrites));
        var BeforeBiomes = CopyBiomeIds(Source.Before, Source.BiomeId, nameof(PllBiomeDefinition.Before));
        var AfterBiomes = CopyBiomeIds(Source.After, Source.BiomeId, nameof(PllBiomeDefinition.After));
        var Root = Source.RootPrefab ? Source.RootPrefab.transform : throw new ArgumentException(
            $"Biome '{Source.BiomeId}' is missing its root prefab.", nameof(Source));
        ValidateColor(Source.Color, Source.BiomeId, "ambient color");
        var AmbienceClips = CopyClips(Source.AmbienceSounds, Source.BiomeId, "ambient sound");
        var EffectSoundClips = CopyClips(Source.EffectSounds, Source.BiomeId, "effect sound");
        var EnterSoundClips = CopyClips(Source.EnterSounds, Source.BiomeId, "entry sound");
        var CandidateGraph = AuthoringCandidateGraph.Create(Root, Source.BiomeId);
        RequireCompleteGraphCatalog(Root, CandidateGraph, Source.EffectDefinitions, Source.BiomeId, "effect");
        RequireCompleteGraphCatalog(Root, CandidateGraph, Source.Visuals, Source.BiomeId, "visual");
        RequireCompleteGraphCatalog(Root, CandidateGraph, Source.LuggageDefinitions, Source.BiomeId, "luggage");
        RequireCompleteGraphCatalog(Root, CandidateGraph, Source.SoundEmitters, Source.BiomeId, "sound emitter");
        RequireRootCatalog(Root, CandidateGraph, Source.StingerDefinitions, Source.BiomeId, "stinger");

        var Effects = new EffectAuthoringContract[Source.EffectDefinitions.Count];
        var EffectComponents = new HashSet<PllEffectAuthoring>();
        for (var i = 0; i < Effects.Length; i++)
        {
            var Value = Source.EffectDefinitions[i];
            if (!Value || !EffectComponents.Add(Value))
                throw new ArgumentException($"Biome '{Source.BiomeId}' contains a missing or duplicate effect definition.", nameof(Source));
            Effects[i] = EffectAuthoringContract.Create(Root, CandidateGraph, Source.BiomeId, Value);
        }
        RequireStablePrefix(Effects, Value => Value.Generated, Source.BiomeId, "effect");

        var Visuals = new VisualAuthoringContract[Source.Visuals.Count];
        var VisualComponents = new HashSet<PllVisualAuthoring>();
        for (var i = 0; i < Visuals.Length; i++)
        {
            var Value = Source.Visuals[i];
            if (!Value || !VisualComponents.Add(Value))
                throw new ArgumentException($"Biome '{Source.BiomeId}' contains a missing or duplicate visual definition.", nameof(Source));
            var Effect = -1;
            GameObject? StagedActivationRoot = null;
            var VisualOwner = AuthoringBindingOwner.Create(Root, CandidateGraph, Value.transform, Source.BiomeId, "visual");
            for (var j = 0; j < Effects.Length; j++)
            {
                if (!ReferenceEquals(Effects[j].Owner.SourceRoot, VisualOwner.SourceRoot)) continue;
                if (!TransformOwnership.Contains(Effects[j].Root.Source.transform, Value.Root ? Value.Root.transform : null)) continue;
                if (Effect >= 0)
                    throw new ArgumentException($"Biome '{Source.BiomeId}' visual '{Value.name}' belongs to more than one effect root.", nameof(Source));
                Effect = j;
                if (Effects[j].PresentationRoot is { } Presentation
                    && Value.Root && TransformOwnership.Contains(Presentation.Source.transform, Value.Root.transform))
                    StagedActivationRoot = Presentation.Source;
            }
            Visuals[i] = VisualAuthoringContract.Create(Root, CandidateGraph, Source.BiomeId, Value, StagedActivationRoot, Effect);
        }
        RequireStablePrefix(Visuals, Value => Value.Generated, Source.BiomeId, "visual");
        for (var i = 0; i < Visuals.Length; i++)
            for (var j = i + 1; j < Visuals.Length; j++)
            {
                var Left = Visuals[i].Source;
                var Right = Visuals[j].Source;
                var SameOwner = ReferenceEquals(Visuals[i].Owner.SourceRoot, Visuals[j].Owner.SourceRoot);
                if (SameOwner && (TransformOwnership.Contains(Left.Root.transform, Right.Root.transform)
                    || TransformOwnership.Contains(Right.Root.transform, Left.Root.transform))
                    )
                    throw new ArgumentException($"Biome '{Source.BiomeId}' visual roots must be distinct and non-overlapping.", nameof(Source));
                if (SameOwner && Left.PropertyName.Length != 0 && Left.PropertyName == Right.PropertyName
                    && (Left.Material && Left.Material == Right.Material
                        || !Left.Material && !Right.Material && (Left.Shader || Right.Shader)))
                    throw new ArgumentException(
                        $"Biome '{Source.BiomeId}' visuals '{Left.name}' and '{Right.name}' write the same shader property target.", nameof(Source));
            }
        for (var i = 0; i < Effects.Length; i++)
            for (var j = i + 1; j < Effects.Length; j++)
                if (ReferenceEquals(Effects[i].Owner.SourceRoot, Effects[j].Owner.SourceRoot)
                    && (TransformOwnership.Contains(Effects[i].Root.Source.transform, Effects[j].Root.Source.transform)
                        || TransformOwnership.Contains(Effects[j].Root.Source.transform, Effects[i].Root.Source.transform)))
                    throw new ArgumentException($"Biome '{Source.BiomeId}' effect roots cannot overlap.", nameof(Source));
        var HookedGenerationContracts = HookedPropGenerationContract.CreateAll(Source, CandidateGraph, out var GeneratedPropAssets);
        var FogZones = FogZoneAuthoringContract.CreateAll(Root, Source.BiomeId, CandidateGraph);
        var SurfaceSources = AuthoringBindingOwner.GraphComponents<PllSurfaceTextureAuthoring>(Root,
            Source.BiomeId, CandidateGraph, "Surface Textures");
        var SurfaceTextures = new SurfaceTextureAuthoringContract[SurfaceSources.Count];
        var FluidSurfaces = new Dictionary<PLLNativeResolver, SurfaceTextureAuthoringContract>();
        var SurfaceSlots = new Dictionary<(Renderer, int), PllSurfaceTextureAuthoring>();
        for (var Index = 0; Index < SurfaceTextures.Length; Index++)
        {
            var Texture = SurfaceTextureAuthoringContract.Create(
                Root, CandidateGraph, Source.BiomeId, SurfaceSources[Index].Value, SurfaceSlots);
            SurfaceTextures[Index] = Texture;
            if (Texture.FluidSource is { } Fluid && !FluidSurfaces.TryAdd(Fluid, Texture))
                throw new ArgumentException("Surface Textures assigns one fluid source more than once.");
        }
        var PropInteractions = PropInteractionContract.CreateAll(Root, Source.BiomeId, CandidateGraph);
        var NativeResolvers = NativeResolverContract.CreateAll(Root, Source.BiomeId, CandidateGraph, Effects, FluidDefinitions, FluidSurfaces);
        var GeneratedFlows = NativeResolverContract.CreateGenerated(CandidateGraph, Source.BiomeId, FluidDefinitions, FluidSurfaces);
        var FlowSources = NativeResolvers.Where(Resolver => Resolver.IsFlow).Select(Resolver => Resolver.FlowSource!)
            .Concat(GeneratedFlows.SelectMany(Flow => Flow.FlowSources)).ToArray();
        foreach (var Texture in SurfaceTextures)
            for (var Slot = 0; Slot < Texture.Source.Renderers.Count; Slot++)
                foreach (var Flow in FlowSources)
                {
                    var FluidSource = Flow.Source;
                    var Renderer = Texture.Source.Renderers[Slot];
                    var MaterialSlot = Texture.Source.Slots[Slot];
                    var Overlap = FluidSource.SurfaceRenderer == Renderer && MaterialSlot == 0;
                    for (var Index = 0; !Overlap && Index < FluidSource.SurfaceRenderers.Count; Index++)
                        Overlap = FluidSource.SurfaceRenderers[Index] == Renderer && FluidSource.SurfaceSlots[Index] == MaterialSlot;
                    if (!Overlap) continue;
                    if (FluidSource.SourceProfile == PllNativeFlowProfile.RayWaterfall)
                        throw new ArgumentException("Surface Textures does not support the native ray waterfall height contract.");
                    if (Flow.Definition is not { } Definition
                        || (Flow.Form == PllNativeResolverForm.Zone ? Definition.ZoneMaterialMode : Definition.FlowMaterialMode) != PllFluidMaterialMode.Custom
                        || (Flow.Form == PllNativeResolverForm.Zone ? Definition.ZoneMaterial : Definition.FlowMaterial) != Texture.Material)
                        throw new ArgumentException("Fluid and Surface Textures must use the same Custom material asset.");
                }
        var HazardDefinitions = HazardAuthoringContract.CreateAll(Root, Source.Anchors.FloorRoot.transform,
            Source.Anchors.CampfireRoot.transform, Source.BiomeId, CandidateGraph);
        var EntityDefinitions = EntityAuthoringContract.CreateAll(Root, Source.Anchors.FloorRoot.transform,
            Source.Anchors.CampfireRoot.transform, Source.BiomeId, CandidateGraph);
        var EffectZones = EffectZoneAuthoringContract.CreateAll(Root, Source.BiomeId, CandidateGraph,
            Effects, HookedGenerationContracts, FogZones, FlowSources, HazardDefinitions, EntityDefinitions, PropInteractions);
        RequireStablePrefix(EffectZones, Value => Value.Generated, Source.BiomeId, "effect-zone");
        var ItemSpawnerContracts = ItemSpawnerContract.CreateAll(
            Root, Source.Anchors.FloorRoot.transform, Source.Anchors.CampfireRoot.transform,
            Source.BiomeId, CandidateGraph);
        var HookedPropContracts = HookedPropContract.CreateAll(Root, Source.BiomeId, CandidateGraph);
        var ClimbSurfaces = ClimbSurfaceAuthoringContract.CreateAll(
            Root, Source.Anchors.FloorRoot.transform, Source.Anchors.CampfireRoot.transform,
            Source.BiomeId, CandidateGraph);
        var Placement = PlacementAuthoringContract.Create(Source, Root, Source.BiomeId, CandidateGraph);
        var EntityZones = EntityZoneContract.CreateAll(Root,
            Source.Anchors.FloorRoot.transform, Source.Anchors.CampfireRoot.transform, Source.BiomeId, CandidateGraph);
        var HazardZones = HazardZoneContract.CreateAll(Root,
            Source.Anchors.FloorRoot.transform, Source.Anchors.CampfireRoot.transform, Source.BiomeId, CandidateGraph, EntityZones);
        var ClimbingFixtures = NativeClimbingFixtureContract.CreateAll(Root,
            Source.Anchors.FloorRoot.transform, Source.Anchors.CampfireRoot.transform, Source.BiomeId, CandidateGraph);
        var CriticalGeneration = CriticalGenerationContract.CreateAll(Source.Anchors, Source.BiomeId, CandidateGraph);
        var Amulets = AmuletSpawnerPlan.Create(Source, Root, CandidateGraph);
        var CampfireAuthoring = CampfireAuthoringContract.CreateOptional(
            Root, Source.Anchors.CampfireRoot.transform, Source.BiomeId, CandidateGraph);
        var Statue = StatueContract.CreateOptional(Root, Source.Anchors.StatueRoot, Source.BiomeId);

        var Luggage = new LuggageContract[Source.LuggageDefinitions.Count];
        var LuggageBySource = new Dictionary<PllLuggageAuthoring, LuggageContract>();
        var LuggageIds = new HashSet<string>(StringComparer.Ordinal);
        var LuggageRoots = new HashSet<GameObject>();
        for (var i = 0; i < Luggage.Length; i++)
        {
            var Value = Source.LuggageDefinitions[i];
            if (!Value || LuggageBySource.ContainsKey(Value))
                throw new ArgumentException(
                    $"Biome '{Source.BiomeId}' contains a missing or duplicate luggage definition.",
                    nameof(Source));
            var LuggageDefinition = LuggageContract.Create(
                Root, Source.Anchors.CampfireRoot.transform, Source.Behavior,
                Source.BiomeId, Value, CandidateGraph);
            Luggage[i] = LuggageDefinition;
            LuggageBySource.Add(Value, LuggageDefinition);
            if (!LuggageIds.Add(LuggageDefinition.Id))
                throw new ArgumentException(
                    $"Biome '{Source.BiomeId}' contains duplicate luggage ID '{LuggageDefinition.Id}'.",
                    nameof(Source));
            if (!LuggageRoots.Add(LuggageDefinition.Root))
                throw new ArgumentException(
                    $"Biome '{Source.BiomeId}' contains duplicate luggage root '{LuggageDefinition.Root.name}'.",
                    nameof(Source));
        }

        var AuthoredTargets = new HashSet<PllLuggageAuthoring>();
        var NativeTargets = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Luggage.Length; i++)
        {
            var Definition = Luggage[i];
            Definition.ValidateAuthoredReplacement(LuggageBySource, Source.BiomeId);
            if (Definition.ReplacesAuthored is { } Authored
                && !AuthoredTargets.Add(Authored))
                throw new ArgumentException(
                    $"Biome '{Source.BiomeId}' has multiple luggage definitions replacing '{Authored.Id}'.",
                    nameof(Source));
            if (Definition.ReplacesNativeId.Length != 0
                && !NativeTargets.Add(Definition.ReplacesNativeId))
                throw new ArgumentException(
                    $"Biome '{Source.BiomeId}' has multiple luggage definitions replacing native target '{Definition.ReplacesNativeId}'.",
                    nameof(Source));
            for (var j = i + 1; j < Luggage.Length; j++)
                if (TransformOwnership.Overlaps(
                    Definition.Root.transform, Luggage[j].Root.transform))
                    throw new ArgumentException(
                        $"Biome '{Source.BiomeId}' luggage roots cannot overlap.", nameof(Source));
        }
        foreach (var Definition in Luggage)
        {
            foreach (var Effect in Effects)
                if (TransformOwnership.Overlaps(
                    Effect.Root.Source.transform, Definition.Root.transform))
                    throw new ArgumentException(
                        $"Biome '{Source.BiomeId}' luggage '{Definition.Id}' cannot overlap an effect root.",
                        nameof(Source));
            foreach (var Visual in Visuals)
                if (TransformOwnership.Overlaps(
                    Visual.Root.Source.transform, Definition.Root.transform))
                    throw new ArgumentException(
                        $"Biome '{Source.BiomeId}' luggage '{Definition.Id}' cannot overlap a managed visual root.",
                        nameof(Source));
        }
        var SoundEmitters = new SoundEmitterContract[Source.SoundEmitters.Count];
        var SoundComponents = new HashSet<PllSoundEmitterAuthoring>();
        var SoundRoots = new HashSet<GameObject>();
        var SoundSources = new HashSet<AudioSource>();
        for (var i = 0; i < SoundEmitters.Length; i++)
        {
            var Value = Source.SoundEmitters[i];
            if (!Value || !SoundComponents.Add(Value))
                throw new ArgumentException($"Biome '{Source.BiomeId}' contains a missing or duplicate sound emitter definition.", nameof(Source));
            SoundEmitters[i] = SoundEmitterContract.Create(Root, CandidateGraph, Source.BiomeId, Value);
            var SoundRoot = SoundEmitters[i].Owner.BindingRoot;
            if (TransformOwnership.HasPropSpawnerInPath(SoundRoot, Value.transform)
                || TransformOwnership.HasPropSpawnerInPath(SoundRoot, SoundEmitters[i].Root.Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(SoundRoot, Value.AudioSource.transform))
                throw new ArgumentException($"Biome '{Source.BiomeId}' sound emitter '{Value.name}' must remain outside every PropSpawner hierarchy.", nameof(Source));
            if (!SoundRoots.Add(SoundEmitters[i].Root.Source))
                throw new ArgumentException($"Biome '{Source.BiomeId}' contains duplicate sound emitter root '{SoundEmitters[i].Root.Source.name}'.", nameof(Source));
            if (!SoundSources.Add(Value.AudioSource))
                throw new ArgumentException($"Biome '{Source.BiomeId}' contains duplicate sound emitter AudioSource '{Value.AudioSource.name}'.", nameof(Source));
        }
        RequireStablePrefix(SoundEmitters, Value => Value.Generated, Source.BiomeId, "sound-emitter");
        for (var i = 0; i < SoundEmitters.Length; i++)
            for (var j = i + 1; j < SoundEmitters.Length; j++)
                if (ReferenceEquals(SoundEmitters[i].Owner.SourceRoot, SoundEmitters[j].Owner.SourceRoot)
                    && (TransformOwnership.Contains(SoundEmitters[i].Root.Source.transform, SoundEmitters[j].Root.Source.transform)
                        || TransformOwnership.Contains(SoundEmitters[j].Root.Source.transform, SoundEmitters[i].Root.Source.transform)))
                    throw new ArgumentException($"Biome '{Source.BiomeId}' sound emitter roots cannot overlap.", nameof(Source));
        foreach (var Emitter in SoundEmitters)
        {
            foreach (var Effect in Effects)
                if (TransformOwnership.Contains(Effect.Root.Source.transform, Emitter.Root.Source.transform)
                    || TransformOwnership.Contains(Emitter.Root.Source.transform, Effect.Root.Source.transform))
                    throw new ArgumentException($"Biome '{Source.BiomeId}' sound emitter '{Emitter.Source.name}' cannot overlap an effect root.", nameof(Source));
            foreach (var Definition in Luggage)
                if (TransformOwnership.Overlaps(
                    Definition.Root.transform, Emitter.Root.Source.transform))
                    throw new ArgumentException($"Biome '{Source.BiomeId}' sound emitter '{Emitter.Source.name}' cannot overlap luggage '{Definition.Id}'.", nameof(Source));
        }

        var NativeVisual = NativeVisualContract.CreateOptional(Root, CandidateGraph, Source.BiomeId);
        var Atmosphere = AtmosphereAuthoringContract.CreateOptional(
            Root, CandidateGraph, Source.BiomeId);
        var CustomVisuals = CustomVisualContract.CreateAll(Source, Root, CandidateGraph, Source.BiomeId);
        var Stingers = new StingerContract[Source.StingerDefinitions.Count];
        var StingerComponents = new HashSet<PllBiomeStingerAuthoring>();
        for (var i = 0; i < Stingers.Length; i++)
        {
            var Value = Source.StingerDefinitions[i];
            if (!Value || !StingerComponents.Add(Value))
                throw new ArgumentException($"Biome '{Source.BiomeId}' contains a missing or duplicate stinger definition.", nameof(Source));
            Stingers[i] = StingerContract.Create(Root, Source.BiomeId, Value);
        }
        StingerContract.ValidateOwnership(Source, Root, Stingers, Source.BiomeId, true);
        var FogShaderAdapters = Source.CaptureFogShaderAdapters();
        if ((CustomVisuals.Length != 0 || Atmosphere is not null || FogShaderAdapters.Length != 0
                || Array.Exists(EntityZones, Zone => Zone.HasAppearance || Zone.RequiresPluginEntities)
                || Array.Exists(NativeResolvers, Resolver => Resolver.IsFlow) || GeneratedFlows.Length != 0 || SurfaceTextures.Length != 0)
            != (DependencySeal is not null))
            throw new ArgumentException(
                $"Biome '{Source.BiomeId}' presentation dependencies and dependency seal must occur together.",
                nameof(DependencySeal));

        var Contract = new BiomeAuthoringContract(
            Source, Root, DependencySeal, Visuals, CustomVisuals, NativeVisual, Atmosphere,
            Effects, EffectZones, FogZones, FogShaderAdapters, NativeResolvers, SurfaceTextures, PropInteractions, GeneratedFlows, FlowSources, ItemSpawnerContracts, HookedPropContracts,
            HookedGenerationContracts, GeneratedPropAssets, ClimbSurfaces, Placement,
            CriticalGeneration, EntityZones, HazardZones, HazardDefinitions, EntityDefinitions, ClimbingFixtures, Amulets,
            CampfireAuthoring, Statue, Luggage, SoundEmitters, Stingers,
            AmbienceClips, EffectSoundClips, EnterSoundClips,
            FavoriteBiomes, OverwrittenBiomes, BeforeBiomes, AfterBiomes);
        Contract.ValidateSource();
        return Contract;
    }

    static void RequireStablePrefix<T>(T[] Values, Func<T, bool> Generated, BiomeId Id, string Name)
    {
        var SawGenerated = false;
        foreach (var Value in Values)
        {
            if (Generated(Value)) SawGenerated = true;
            else if (SawGenerated)
                throw new ArgumentException($"Biome '{Id}' staged {Name} catalog does not place stable bindings before generated bindings.", "source");
        }
    }

    static void RequireCompleteGraphCatalog<T>(
        Transform Template,
        AuthoringCandidateGraph Graph,
        IReadOnlyList<T> Catalog,
        BiomeId Id,
        string Name) where T : Component
    {
        var Discovered = new HashSet<T>();
        Add(Template.gameObject);
        foreach (var Candidate in Graph.Ordered) Add(Candidate);
        if (Catalog.Count != Discovered.Count)
            throw new ArgumentException($"Biome '{Id}' staged {Name} catalog is incomplete.", "source");
        foreach (var Value in Catalog)
            if (!Value || !Discovered.Remove(Value))
                throw new ArgumentException($"Biome '{Id}' staged {Name} catalog contains a missing, duplicate, or foreign component.", "source");
        if (Discovered.Count != 0)
            throw new ArgumentException($"Biome '{Id}' staged {Name} catalog is incomplete.", "source");

        void Add(GameObject Root)
        {
            foreach (var Value in Root.GetComponentsInChildren<T>(true))
                if (!Value) throw new ArgumentException($"Biome '{Id}' {Name} graph contains a destroyed component.", "source");
                else Discovered.Add(Value);
        }
    }

    static void RequireRootCatalog<T>(
        Transform Template,
        AuthoringCandidateGraph Graph,
        IReadOnlyList<T> Catalog,
        BiomeId Id,
        string Name) where T : Component
    {
        var Discovered = new HashSet<T>();
        foreach (var Value in Template.GetComponentsInChildren<T>(true))
            if (!Value) throw new ArgumentException($"Biome '{Id}' {Name} root contains a destroyed component.", "source");
            else Discovered.Add(Value);
        foreach (var Candidate in Graph.Ordered)
            if (Candidate.GetComponentsInChildren<T>(true).Length != 0)
                throw new ArgumentException($"Biome '{Id}' {Name} authoring cannot occur in a candidate prefab.", "source");
        if (Catalog.Count != Discovered.Count)
            throw new ArgumentException($"Biome '{Id}' staged {Name} catalog is incomplete.", "source");
        foreach (var Value in Catalog)
            if (!Value || !Discovered.Remove(Value))
                throw new ArgumentException($"Biome '{Id}' staged {Name} catalog contains a missing, duplicate, or foreign component.", "source");
        if (Discovered.Count != 0)
            throw new ArgumentException($"Biome '{Id}' staged {Name} catalog is incomplete.", "source");
    }

    internal ResolvedBiomeAuthoring Resolve(Transform Root)
    {
        if (!Root) throw new InvalidOperationException($"Biome '{Id}' runtime root was destroyed.");
        var ResolvedEffects = new List<ResolvedEffect>();
        foreach (var Effect in Effects)
            if (!Effect.Generated) ResolvedEffects.Add(Effect.ResolveStable(Root, Id));
        var ResolvedVisuals = new List<ResolvedVisual>();
        foreach (var Visual in Visuals)
            if (!Visual.Generated) ResolvedVisuals.Add(Visual.ResolveStable(Root, Id));
        var ResolvedCustomVisuals = new ResolvedCustomVisual[CustomVisuals.Length];
        for (var i = 0; i < CustomVisuals.Length; i++)
            ResolvedCustomVisuals[i] = CustomVisuals[i].Resolve(Root, Id);
        var ResolvedAtmosphere = Atmosphere?.Resolve(Root, Id);
        var ResolvedEffectZones = new List<ResolvedEffectZone>();
        foreach (var Zone in EffectZones)
            if (!Zone.Generated) ResolvedEffectZones.Add(Zone.ResolveStable(Root, Id));
        var StableFogCount = 0;
        while (StableFogCount < FogZones.Length && !FogZones[StableFogCount].Generated) StableFogCount++;
        var ResolvedFogZones = new ResolvedFogZone[StableFogCount];
        for (var i = 0; i < ResolvedFogZones.Length; i++) ResolvedFogZones[i] = FogZones[i].ResolveStable(Root, Id);
        var ResolvedNativeResolvers = new List<ResolvedNativeResolver>();
        foreach (var Resolver in NativeResolvers)
            if (!Resolver.IsFlow) ResolvedNativeResolvers.Add(Resolver.Resolve(Root, Id));
        CampfireAuthoring?.Resolve(Root, Id);
        var ResolvedStatue = Statue?.Resolve(Root, Id);
        var ResolvedLuggage = new List<ResolvedLuggage>();
        foreach (var Definition in Luggage)
            if (!Definition.Generated) ResolvedLuggage.Add(Definition.Resolve(Root, Id));
        var ResolvedSoundEmitters = new List<ResolvedSoundEmitter>();
        foreach (var Emitter in SoundEmitters)
        {
            if (Emitter.Generated) continue;
            var Resolved = Emitter.ResolveStable(Root, Id);
            if (TransformOwnership.HasPropSpawnerInPath(Root, Resolved.Authoring.transform)
                || TransformOwnership.HasPropSpawnerInPath(Root, Resolved.Root.transform)
                || TransformOwnership.HasPropSpawnerInPath(Root, Resolved.AudioSource.transform))
                throw new InvalidOperationException($"Biome '{Id}' runtime sound emitter '{Resolved.Authoring.name}' entered a PropSpawner hierarchy.");
            ResolvedSoundEmitters.Add(Resolved);
        }
        var ResolvedStingers = new ResolvedStinger[Stingers.Length];
        for (var i = 0; i < Stingers.Length; i++) ResolvedStingers[i] = Stingers[i].Resolve(Root, Id);
        return new ResolvedBiomeAuthoring(
            this, ResolvedVisuals.ToArray(), ResolvedCustomVisuals, ResolvedAtmosphere,
            ResolvedEffects.ToArray(), ResolvedEffectZones.ToArray(), ResolvedFogZones,
            ResolvedNativeResolvers.ToArray(), ResolvedStatue, ResolvedLuggage.ToArray(), ResolvedSoundEmitters.ToArray(),
            ResolvedStingers);
    }

    internal ResolvedClimbSurfaceCatalog ResolveClimbSurfaces(MapSlot Slot)
    {
        if (Slot.CustomResolved is null)
            throw new InvalidOperationException(
                $"Biome '{Id}' has no resolved custom root for climb surfaces.");
        return ClimbSurfaceAuthoringContract.ResolveAll(
            ClimbSurfaces, Slot.CustomResolved.Root.Value, Slot, Id);
    }
    internal bool TryResolveGeneratedLuggage(PllLuggageAuthoring Source, out ResolvedLuggage Resolved)
    {
        foreach (var Definition in Luggage)
            if (Definition.Generated && string.Equals(Definition.Id, Source.Id, StringComparison.Ordinal))
            {
                Resolved = Definition.ResolveGenerated(Source, Id);
                return true;
            }
        Resolved = default;
        return false;
    }

    internal void ResolveExternal(
        ItemDatabaseCatalog Items,
        NativeLuggageCatalog? Native,
        NativeVisualSources NativeVisuals)
    {
        BoundNativeVisualSource = NativeVisual is null ? null : NativeVisuals.Require(NativeVisual.Preset);
        if (BoundNativeVisualSource is not null) NativeVisual!.ValidateAppearance(BoundNativeVisualSource);
        foreach (var Effect in Effects) Effect.ResolveExternal(NativeVisuals);
        var NativeReplacements = new HashSet<NativeLuggageFingerprint>();
        foreach (var Definition in Luggage)
        {
            Definition.ResolveExternal(Items, Native, Id);
            if (Definition.NativeReplacementFingerprint is { } Fingerprint
                && !NativeReplacements.Add(Fingerprint))
                throw new InvalidOperationException(
                    $"Biome '{Id}' has multiple luggage definitions resolving to one native replacement target.");
        }
        foreach (var Definition in ItemSpawnerContracts) Definition.ResolveExternal(Items, Id);
        Statue?.ResolveExternal(Items, Id);
    }

    internal void ClearExternal()
    {
        foreach (var Zone in EntityZones) Zone.ClearExternal();
        foreach (var Zone in HazardZones) Zone.ClearExternal();
        foreach (var Fixture in ClimbingFixtureContracts) Fixture.ClearExternal();
        foreach (var Hazard in HazardDefinitions) Hazard.ClearExternal();
        foreach (var Entity in EntityDefinitions) Entity.ClearExternal();
        Amulets?.ClearExternal();
        BoundNativeVisualSource = null;
        foreach (var Effect in Effects) Effect.ClearExternal();
        foreach (var Definition in ItemSpawnerContracts) Definition.ClearExternal();
        Statue?.ClearExternal();
        foreach (var Definition in Luggage) Definition.ClearExternal();
    }

    internal void ValidateSource()
    {
        if (!Source || !Template || Source.RootPrefab.transform != Template || Source.BiomeId != Id
            || Source.Behavior != BehaviorValue || Source.DisplayTitle != DisplayTitle
            || !Source.Color.Equals(Color) || !Source.DayNightProfile)
            throw new InvalidOperationException($"Biome '{Id}' changed its authoring metadata before sealing.");
        ValidateColor(Source.Color, Id, "ambient color");
        SameClips(Source.AmbienceSounds, AmbienceClips, Id, "ambient sounds");
        SameClips(Source.EffectSounds, EffectSoundClips, Id, "effect sounds");
        SameClips(Source.EnterSounds, EnterSoundClips, Id, "entry sounds");
        SameBiomeIds(Source.Favorites, FavoriteBiomes, Id, nameof(PllBiomeDefinition.Favorites));
        SameBiomeIds(Source.Overwrites, OverwrittenBiomes, Id, nameof(PllBiomeDefinition.Overwrites));
        SameBiomeIds(Source.Before, BeforeBiomes, Id, nameof(PllBiomeDefinition.Before));
        SameBiomeIds(Source.After, AfterBiomes, Id, nameof(PllBiomeDefinition.After));
        if (Source.Visuals.Count != Visuals.Length || Source.EffectDefinitions.Count != Effects.Length
            || Source.LuggageDefinitions.Count != Luggage.Length || Source.SoundEmitters.Count != SoundEmitters.Length
            || Source.StingerDefinitions.Count != Stingers.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its authoring definition counts before sealing.");
        var CandidateGraph = AuthoringCandidateGraph.Create(Template, Id);
        ValidateCompleteGraphCatalog(Template, CandidateGraph, Source.EffectDefinitions, "effect");
        ValidateCompleteGraphCatalog(Template, CandidateGraph, Source.Visuals, "visual");
        ValidateCompleteGraphCatalog(Template, CandidateGraph, Source.LuggageDefinitions, "luggage");
        ValidateCompleteGraphCatalog(Template, CandidateGraph, Source.SoundEmitters, "sound emitter");
        ValidateRootCatalog(Template, CandidateGraph, Source.StingerDefinitions, "stinger");
        for (var i = 0; i < Visuals.Length; i++)
        {
            if (!ReferenceEquals(Source.Visuals[i], Visuals[i].Source))
                throw new InvalidOperationException($"Biome '{Id}' reordered its visual definitions before sealing.");
            Visuals[i].Validate(Template, Id, CandidateGraph);
        }
        var DiscoveredCustomVisuals = Template.GetComponentsInChildren<PllCustomVisualAuthoring>(true);
        if (DiscoveredCustomVisuals.Length != CustomVisuals.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its custom visual catalog before sealing.");
        for (var i = 0; i < CustomVisuals.Length; i++) CustomVisuals[i].Validate(Source, Template, Id);
        NativeVisual?.Validate(Template, Id);
        if ((NativeVisual is null) != (Template.GetComponentsInChildren<PLLNativeVisualResolver>(true).Length == 0))
            throw new InvalidOperationException($"Biome '{Id}' changed its native visual resolver before sealing.");
        Atmosphere?.Validate(Template, Id);
        if ((Atmosphere is null)
            != (Template.GetComponentsInChildren<PllAtmosphereAuthoring>(true).Length == 0))
            throw new InvalidOperationException(
                $"Biome '{Id}' changed its atmosphere authoring before sealing.");
        for (var i = 0; i < Effects.Length; i++)
        {
            if (!ReferenceEquals(Source.EffectDefinitions[i], Effects[i].Source))
                throw new InvalidOperationException($"Biome '{Id}' reordered its effect definitions before sealing.");
            Effects[i].Validate(Template, Id, CandidateGraph);
        }
        foreach (var EffectZone in EffectZones) EffectZone.Validate(Template, Id, CandidateGraph);
        var CurrentFogZones = AuthoringBindingOwner.GraphComponents<PllFogZoneAuthoring>(Template, Id, CandidateGraph, "fog-zone authoring");
        if (CurrentFogZones.Count != FogZones.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its Fog Zone declaration count before sealing.");
        for (var i = 0; i < FogZones.Length; i++)
        {
            if (CurrentFogZones[i].Value != FogZones[i].Source)
                throw new InvalidOperationException($"Biome '{Id}' changed its Fog Zone declaration order before sealing.");
            FogZones[i].Validate(Template, Id, CandidateGraph);
        }
        foreach (var Resolver in NativeResolvers) Resolver.Validate(Template, Id, Effects);
        foreach (var Texture in SurfaceTextures) Texture.ValidateUnchanged(Id);
        foreach (var Prop in PropInteractions) Prop.Validate(Template, CandidateGraph, Id);
        foreach (var Flow in GeneratedFlows) Flow.Validate(Id, CandidateGraph);
        CampfireAuthoring?.Validate(Template, Source.Anchors.CampfireRoot.transform, Id, CandidateGraph);
        Statue?.Validate(Template, Id);
        foreach (var Definition in ItemSpawnerContracts)
            Definition.Validate(
                Template, Source.Anchors.FloorRoot.transform, Source.Anchors.CampfireRoot.transform,
                Id, CandidateGraph);
        foreach (var Definition in HookedPropContracts) Definition.Validate(Template, Id, CandidateGraph);
        foreach (var Definition in HookedGenerationContracts) Definition.Validate(Template, Id, CandidateGraph);
        foreach (var Zone in EntityZones) Zone.Validate(Template, Id);
        foreach (var Zone in HazardZones) Zone.Validate(Template, Id);
        foreach (var Hazard in HazardDefinitions) Hazard.Validate(Template, Id, CandidateGraph);
        foreach (var Entity in EntityDefinitions) Entity.Validate(Template, Id, CandidateGraph);
        foreach (var Fixture in ClimbingFixtureContracts) Fixture.Validate(Template, CandidateGraph, Id);
        Amulets?.Validate(Template, Id);
        ClimbSurfaceAuthoringContract.ValidateAll(
            ClimbSurfaces, Template, Source.Anchors.FloorRoot.transform,
            Source.Anchors.CampfireRoot.transform, Id, CandidateGraph);
        Placement.Validate(Source, Template, Id, CandidateGraph);
        if (Template.GetComponentsInChildren<PLLCriticalGeneration>(true).Length != CriticalGeneration.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its Critical Generation marker count before sealing.");
        foreach (var Critical in CriticalGeneration) Critical.Validate(Template, Id);
        for (var i = 0; i < Luggage.Length; i++)
        {
            if (!ReferenceEquals(Source.LuggageDefinitions[i], Luggage[i].Source))
                throw new InvalidOperationException($"Biome '{Id}' reordered its luggage definitions before sealing.");
            Luggage[i].Validate(Template, Id, CandidateGraph);
        }
        for (var i = 0; i < SoundEmitters.Length; i++)
        {
            if (!ReferenceEquals(Source.SoundEmitters[i], SoundEmitters[i].Source))
                throw new InvalidOperationException($"Biome '{Id}' reordered its sound emitter definitions before sealing.");
            SoundEmitters[i].Validate(Template, Id, CandidateGraph);
            var SoundRoot = SoundEmitters[i].Owner.BindingRoot;
            if (TransformOwnership.HasPropSpawnerInPath(SoundRoot, SoundEmitters[i].Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(SoundRoot, SoundEmitters[i].Root.Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(SoundRoot, SoundEmitters[i].Source.AudioSource.transform))
                throw new InvalidOperationException($"Biome '{Id}' sound emitter '{SoundEmitters[i].Source.name}' entered a PropSpawner hierarchy before sealing.");
        }
        for (var i = 0; i < Stingers.Length; i++)
        {
            if (!ReferenceEquals(Source.StingerDefinitions[i], Stingers[i].Source))
                throw new InvalidOperationException($"Biome '{Id}' reordered its stinger definitions before sealing.");
            Stingers[i].Validate(Template, Id);
        }
        StingerContract.ValidateOwnership(Source, Template, Stingers, Id, false);

        void ValidateCompleteGraphCatalog<T>(
            Transform Root,
            AuthoringCandidateGraph Graph,
            IReadOnlyList<T> Values,
            string Name) where T : Component
        {
            try { RequireCompleteGraphCatalog(Root, Graph, Values, Id, Name); }
            catch (ArgumentException Exception)
            {
                throw new InvalidOperationException($"Biome '{Id}' changed its staged {Name} catalog before sealing.", Exception);
            }
        }

        void ValidateRootCatalog<T>(
            Transform Root,
            AuthoringCandidateGraph Graph,
            IReadOnlyList<T> Values,
            string Name) where T : Component
        {
            try { RequireRootCatalog(Root, Graph, Values, Id, Name); }
            catch (ArgumentException Exception)
            {
                throw new InvalidOperationException($"Biome '{Id}' changed its staged {Name} catalog before sealing.", Exception);
            }
        }
    }

    internal void WriteManifest(BinaryWriter Writer) => WriteManifest(Writer, true);
    internal void WriteSourceManifest(BinaryWriter Writer) => WriteManifest(Writer, false);

    void WriteManifest(BinaryWriter Writer, bool External)
    {
        Writer.Write(SegmentId); Writer.Write(Id.Value); Writer.Write((byte)BehaviorValue);
        Writer.Write(DisplayTitle); WriteBiomeIds(Writer, FavoriteBiomes); WriteBiomeIds(Writer, OverwrittenBiomes);
        WriteBiomeIds(Writer, BeforeBiomes); WriteBiomeIds(Writer, AfterBiomes);
        ManifestEncoding.WriteColor(Writer, Color);
        Writer.Write(DependencySeal is not null);
        if (DependencySeal is not null) { Writer.Write(DependencySeal.Length); Writer.Write(DependencySeal); }
        Writer.Write(GeneratedPropAssets is not null);
        GeneratedPropAssets?.WriteManifest(Writer);
        WriteClips(Writer, AmbienceClips); WriteClips(Writer, EffectSoundClips); WriteClips(Writer, EnterSoundClips);
        Writer.Write(Visuals.Length);
        foreach (var Visual in Visuals) Visual.WriteManifest(Writer);
        Writer.Write(CustomVisuals.Length);
        foreach (var Visual in CustomVisuals) Visual.WriteManifest(Writer);
        Writer.Write(NativeVisual is not null);
        NativeVisual?.WriteManifest(Writer);
        Writer.Write(Atmosphere is not null);
        Atmosphere?.WriteManifest(Writer);
        Writer.Write(Effects.Length);
        foreach (var Effect in Effects) Effect.WriteManifest(Writer);
        Writer.Write(EffectZones.Length);
        foreach (var EffectZone in EffectZones) EffectZone.WriteManifest(Writer);
        Writer.Write(NativeResolvers.Length);
        foreach (var Resolver in NativeResolvers) Resolver.WriteManifest(Writer);
        Writer.Write(ItemSpawnerContracts.Length);
        foreach (var Definition in ItemSpawnerContracts) Definition.WriteManifest(Writer);
        Writer.Write(HookedGenerationContracts.Length);
        foreach (var Definition in HookedGenerationContracts) Definition.WriteManifest(Writer);
        Writer.Write(HookedPropContracts.Length);
        foreach (var Definition in HookedPropContracts) Definition.WriteManifest(Writer);
        Writer.Write(ClimbSurfaces.Length);
        foreach (var ClimbSurface in ClimbSurfaces) ClimbSurface.WriteManifest(Writer);
        Placement.WriteManifest(Writer);
        Writer.Write(CampfireAuthoring is not null);
        CampfireAuthoring?.WriteManifest(Writer);
        Writer.Write(Statue is not null);
        Statue?.WriteManifest(Writer);
        Writer.Write(Luggage.Length);
        foreach (var Definition in Luggage)
            if (External) Definition.WriteManifest(Writer);
            else Definition.WriteSourceManifest(Writer);
        Writer.Write(SoundEmitters.Length);
        foreach (var Emitter in SoundEmitters) Emitter.WriteManifest(Writer);
        Writer.Write(Stingers.Length);
        foreach (var Stinger in Stingers) Stinger.WriteManifest(Writer);
        Amulets?.WriteManifest(Writer, External);
        if (CriticalGeneration.Length != 0)
        {
            Writer.Write("PLL/CriticalGeneration/v1"); Writer.Write(CriticalGeneration.Length);
            foreach (var Critical in CriticalGeneration) Critical.WriteManifest(Writer);
        }
        if (EntityZones.Length != 0)
        {
            Writer.Write("PLL/EntityZones/v1"); Writer.Write(EntityZones.Length);
            foreach (var Zone in EntityZones) Zone.WriteManifest(Writer, External);
        }
        if (HazardZones.Length != 0)
        {
            Writer.Write("PLL/HazardZones/v1"); Writer.Write(HazardZones.Length);
            foreach (var Zone in HazardZones) Zone.WriteManifest(Writer);
        }
        if (ClimbingFixtureContracts.Length != 0)
        {
            Writer.Write("PLL/ClimbingFixtures/v1"); Writer.Write(ClimbingFixtureContracts.Length);
            foreach (var Fixture in ClimbingFixtureContracts) Fixture.WriteManifest(Writer, External);
        }
        if (SurfaceTextures.Length != 0)
        {
            Writer.Write("PLL/SurfaceTextures/v1"); Writer.Write(SurfaceTextures.Length);
            foreach (var Texture in SurfaceTextures) Texture.WriteManifest(Writer);
        }
        if (PropInteractions.Length != 0)
        {
            Writer.Write("PLL/PropInteractions/v1"); Writer.Write(PropInteractions.Length);
            foreach (var Prop in PropInteractions) Prop.WriteManifest(Writer);
        }
        if (FogZones.Length != 0)
        {
            Writer.Write("PLL/FogZones/v1"); Writer.Write(FogZones.Length);
            foreach (var Zone in FogZones) Zone.WriteManifest(Writer);
        }
        if (FogShaders.Count != 0)
        {
            Writer.Write("PLL/CustomFogShaders/v1"); Writer.Write(FogShaders.Count);
            foreach (var Adapter in FogShaders) Writer.Write(Adapter.PreparedContent);
        }
        if (GeneratedFlows.Length != 0)
        {
            Writer.Write("PLL/GeneratedNativeFlows/v1"); Writer.Write(GeneratedFlows.Length);
            foreach (var Flow in GeneratedFlows) Flow.WriteManifest(Writer);
        }
        if (HazardDefinitions.Length != 0)
        {
            Writer.Write("PLL/HazardDefinitions/v1"); Writer.Write(HazardDefinitions.Length);
            foreach (var Hazard in HazardDefinitions) Hazard.WriteManifest(Writer, External);
        }
        if (EntityDefinitions.Length != 0)
        {
            Writer.Write("PLL/EntityDefinitions/v1"); Writer.Write(EntityDefinitions.Length);
            foreach (var Entity in EntityDefinitions) Entity.WriteManifest(Writer, External);
        }
    }

    static AudioClip[] CopyClips(IReadOnlyList<AudioClip> Values, BiomeId Id, string Name)
    {
        var Result = new AudioClip[Values.Count];
        for (var i = 0; i < Result.Length; i++)
        {
            var Clip = Values[i];
            if (!Clip)
                throw new ArgumentException($"Biome '{Id}' contains a missing {Name}.", "source");
            Result[i] = Clip;
        }
        return Result;
    }

    static BiomeId[] CopyBiomeIds(IReadOnlyList<BiomeId> Values, BiomeId Id, string Name)
    {
        var Result = new BiomeId[Values.Count];
        for (var i = 0; i < Result.Length; i++)
        {
            var Value = Values[i];
            if (!Value.IsValid)
                throw new ArgumentException($"Biome '{Id}' contains an invalid biome ID in {Name}.", "source");
            if (Value == Id)
                throw new ArgumentException($"Biome '{Id}' cannot reference itself in {Name}.", "source");
            Result[i] = Value;
        }
        Array.Sort(Result, CompareBiomeIds);
        for (var i = 1; i < Result.Length; i++)
            if (Result[i - 1] == Result[i])
                throw new ArgumentException($"Biome '{Id}' contains duplicate biome ID '{Result[i]}' in {Name}.", "source");
        return Result;
    }

    static void SameClips(IReadOnlyList<AudioClip> Current, AudioClip[] Expected, BiomeId Id, string Name)
    {
        if (Current.Count != Expected.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its {Name} before sealing.");
        for (var i = 0; i < Expected.Length; i++)
            if (!Expected[i] || !ReferenceEquals(Current[i], Expected[i]))
                throw new InvalidOperationException($"Biome '{Id}' changed its {Name} before sealing.");
    }

    static void SameBiomeIds(IReadOnlyList<BiomeId> Current, BiomeId[] Expected, BiomeId Id, string Name)
    {
        if (Current.Count != Expected.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its {Name} before sealing.");
        var Values = new BiomeId[Current.Count];
        for (var i = 0; i < Values.Length; i++)
        {
            if (!Current[i].IsValid)
                throw new InvalidOperationException($"Biome '{Id}' changed its {Name} before sealing.");
            Values[i] = Current[i];
        }
        Array.Sort(Values, CompareBiomeIds);
        for (var i = 0; i < Values.Length; i++)
            if (Values[i] != Expected[i])
                throw new InvalidOperationException($"Biome '{Id}' changed its {Name} before sealing.");
    }

    static int CompareBiomeIds(BiomeId Left, BiomeId Right) =>
        StringComparer.Ordinal.Compare(Left.Value, Right.Value);

    static void WriteBiomeIds(BinaryWriter Writer, BiomeId[] Values)
    {
        Writer.Write(Values.Length);
        foreach (var Value in Values) Writer.Write(Value.Value);
    }

    internal static void ValidateColor(Color Value, BiomeId Id, string Name)
    {
        if (!ManifestEncoding.CanEncode(Value.r) || !ManifestEncoding.CanEncode(Value.g)
            || !ManifestEncoding.CanEncode(Value.b) || !ManifestEncoding.CanEncode(Value.a))
            throw new ArgumentException($"Biome '{Id}' {Name} exceeds the synchronized numeric range.", "source");
    }

    internal static void WriteClips(BinaryWriter Writer, IReadOnlyList<AudioClip> Clips)
    {
        Writer.Write(Clips.Count);
        foreach (var Clip in Clips) WriteClip(Writer, Clip);
    }

    internal static void WriteClip(BinaryWriter Writer, AudioClip? Clip)
    {
        if (PllAuthoringValueContract.DestroyedReference(Clip))
            throw new InvalidOperationException("An authoring audio clip was destroyed before manifest sealing.");
        Writer.Write(Clip ? true : false);
        if (!Clip) return;
        Writer.Write(Clip.name); Writer.Write(Clip.samples); Writer.Write(Clip.channels); Writer.Write(Clip.frequency);
        Writer.Write(Clip.length); Writer.Write((int)Clip.loadType); Writer.Write(Clip.preloadAudioData);
        Writer.Write(Clip.ambisonic); Writer.Write(Clip.loadInBackground);
    }

    internal static void RequireToken(string Value, BiomeId Id, string Name, bool Optional)
    {
        if (Optional && Value.Length == 0) return;
        try
        {
            SegmentRegistry.ValidateToken(Value, Name);
        }
        catch (ArgumentException Exception)
        {
            throw new ArgumentException($"Biome '{Id}' has an invalid {Name}.", "source", Exception);
        }
    }

}

internal sealed class ResolvedBiomeAuthoring
{
    internal ResolvedBiomeAuthoring(
        BiomeAuthoringContract BiomeContract,
        ResolvedVisual[] ResolvedVisuals,
        ResolvedCustomVisual[] ResolvedCustomVisuals,
        ResolvedAtmosphere? ResolvedAtmosphere,
        ResolvedEffect[] ResolvedEffects,
        ResolvedEffectZone[] ResolvedEffectZones,
        ResolvedFogZone[] ResolvedFogZones,
        ResolvedNativeResolver[] ResolvedNativeResolvers,
        ResolvedStatue? ResolvedStatue,
        ResolvedLuggage[] ResolvedLuggage,
        ResolvedSoundEmitter[] ResolvedSoundEmitters,
        ResolvedStinger[] ResolvedStingers)
    {
        Contract = BiomeContract;
        Visuals = ResolvedVisuals;
        CustomVisuals = ResolvedCustomVisuals;
        Atmosphere = ResolvedAtmosphere;
        Effects = ResolvedEffects;
        EffectZones = ResolvedEffectZones;
        FogZones = ResolvedFogZones;
        NativeResolvers = ResolvedNativeResolvers;
        Statue = ResolvedStatue;
        Luggage = ResolvedLuggage;
        SoundEmitters = ResolvedSoundEmitters;
        Stingers = ResolvedStingers;
    }

    internal BiomeAuthoringContract Contract { get; }
    internal ResolvedVisual[] Visuals { get; }
    internal ResolvedCustomVisual[] CustomVisuals { get; }
    internal ResolvedAtmosphere? Atmosphere { get; }
    internal ResolvedEffect[] Effects { get; }
    internal ResolvedEffectZone[] EffectZones { get; }
    internal ResolvedFogZone[] FogZones { get; }
    internal ResolvedNativeResolver[] NativeResolvers { get; }
    internal ResolvedStatue? Statue { get; }
    internal ResolvedLuggage[] Luggage { get; }
    internal ResolvedSoundEmitter[] SoundEmitters { get; }
    internal ResolvedStinger[] Stingers { get; }
}
