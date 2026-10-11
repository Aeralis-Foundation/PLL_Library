using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEngine;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllSourceCatalogValidator
    {
        internal DefinitionCatalog Validate(PllBiomeDefinition definition)
        {
            var identity = new PllSourceIdentityValidator().Validate(definition);
            var ClimbSurfaces = new PllClimbSurfaceAuthoringValidator()
                .Validate(identity);
            var generation = new PllGenerationAuthoringValidator().Validate(identity);
            var fixtures = new PllFixtureAuthoringValidator().Validate(identity, generation);
            var presentation = new PllPresentationAuthoringValidator()
                .Validate(identity, generation, fixtures);
            var CollisionGeometry = PllCollisionAuthoringValidator.Capture(identity, presentation);
            PllGenerationAuthoringValidator.ValidateNoPropCoverage(generation, CollisionGeometry);
            var Catalog = new DefinitionCatalog(identity, ClimbSurfaces, CollisionGeometry, generation, fixtures, presentation);
            PllNativeSpawnerCompiler.Validate(identity, Catalog.NativeSpawners);
            var NativeSlots = new HashSet<(Renderer, int)>();
            foreach (var Material in Catalog.NativeMaterials)
            {
                var Selection = Material.Capture();
                var Node = OwnerNode(identity.Graph, Material, "Native Material");
                RequireStableOwned(Node.SourceRoot.transform, Material.transform, "Native Material");
                if (Node.IsRoot) RequireFeatureOwned(Material.transform, identity.Floor, identity.Campfire, "Native Material");
                if (!NativeSlots.Add((Selection.Renderer, Selection.Slot)))
                    throw new InvalidOperationException("Native Material assigns a renderer slot more than once.");
                foreach (var Surface in Catalog.SurfaceTextures)
                    for (var Index = 0; Index < Surface.Renderers.Count; Index++)
                        if (Surface.Renderers[Index] == Selection.Renderer && Surface.Slots[Index] == Selection.Slot)
                            throw new InvalidOperationException("Native Material and Surface Textures cannot own the same renderer slot.");
                foreach (var Visual in Catalog.Visuals)
                    if (Visual.PropertyName.Length != 0 && Visual.Root
                        && TransformOwnership.Contains(Visual.Root.transform, Selection.Renderer.transform))
                        throw new InvalidOperationException("Native Material cannot replace a Shader Visual property target.");
                foreach (var Flow in Catalog.NativeResolvers)
                {
                    if (Flow.SurfaceRenderer == Selection.Renderer && Selection.Slot == 0)
                        throw new InvalidOperationException("Native Material cannot replace an owned fluid surface.");
                    for (var Index = 0; Index < Flow.SurfaceRenderers.Count; Index++)
                        if (Flow.SurfaceRenderers[Index] == Selection.Renderer && Flow.SurfaceSlots[Index] == Selection.Slot)
                            throw new InvalidOperationException("Native Material cannot replace an owned fluid surface.");
                }
            }
            return Catalog;
        }

        internal DefinitionCatalog ValidateSelected(PllBiomeDefinition Definition)
        {
            var OwnerCatalog = Validate(Definition);
            var Group = OwnerCatalog.Group;
            if (Group == null) return OwnerCatalog;
            var Definitions = new PllBiomeDefinition[
                Group.Prefixes.Count + 1];
            var Catalogs = new DefinitionCatalog[Definitions.Length];
            for (var Index = 0; Index < Group.Prefixes.Count; Index++)
            {
                Definitions[Index] = Group.Prefixes[Index];
                Catalogs[Index] = Validate(Definitions[Index]);
            }
            Definitions[Definitions.Length - 1] = Definition;
            Catalogs[Catalogs.Length - 1] = OwnerCatalog;
            ValidateScope(Definitions, Catalogs);
            return OwnerCatalog;
        }

        internal static void ValidateScope(
            IReadOnlyList<PllBiomeDefinition> Definitions,
            IReadOnlyList<DefinitionCatalog> Catalogs)
        {
            var RootIds = new HashSet<int>();
            var Roots = new List<GameObject>();
            var Groups = new List<PllBiomeGroupContract>();
            foreach (var Catalog in Catalogs)
            {
                foreach (var Node in Catalog.Graph.Nodes)
                    if (RootIds.Add(Node.SourceRoot.GetInstanceID()))
                        Roots.Add(Node.SourceRoot);
                if (Catalog.Group != null) Groups.Add(Catalog.Group);
            }
            var OrderedGroups = PllBiomeGroupContract.ValidateScope(
                Definitions, Roots, Groups);
            var Ports = new Dictionary<int, BiomeConnectorPorts>(
                Catalogs.Count);
            foreach (var Catalog in Catalogs)
                Ports.Add(
                    Catalog.Definition.GetInstanceID(),
                    new BiomeConnectorPorts(
                        Catalog.Definition, Catalog.Entry, Catalog.Exit));
            ValidateConnectors(OrderedGroups, Ports);
        }

        internal static void ValidateScope(
            IReadOnlyList<PllBiomeDefinition> Definitions,
            IReadOnlyList<GameObject> Roots)
        {
            var Groups = new List<PllBiomeGroupContract>();
            foreach (var Definition in Definitions)
            {
                var Group = PllBiomeGroupContract.Capture(Definition);
                if (Group != null) Groups.Add(Group);
            }
            var OrderedGroups = PllBiomeGroupContract.ValidateScope(
                Definitions, Roots, Groups);
            var Ports = new Dictionary<int, BiomeConnectorPorts>(
                Definitions.Count);
            foreach (var Definition in Definitions)
                Ports.Add(
                    Definition.GetInstanceID(),
                    BiomeConnectorPorts.Capture(Definition));
            ValidateConnectors(OrderedGroups, Ports);
        }

        internal static void ValidateConnectors(
            IReadOnlyList<PllBiomeGroupContract> Groups,
            IReadOnlyDictionary<int, BiomeConnectorPorts> Ports)
        {
            foreach (var Group in Groups)
            {
                var Previous = Ports[
                    Group.Prefixes[0].GetInstanceID()];
                for (var Index = 1;
                    Index <= Group.Prefixes.Count; Index++)
                {
                    var NextDefinition = Index == Group.Prefixes.Count
                        ? Group.Owner : Group.Prefixes[Index];
                    var Next = Ports[NextDefinition.GetInstanceID()];
                    PllConnectorContract.Require(
                        Group.Owner.BiomeId.Value,
                        Previous.Definition.BiomeId.Value,
                        Previous.Exit.Connector,
                        Previous.Exit.transform.rotation,
                        Next.Definition.BiomeId.Value,
                        Next.Entry.Connector,
                        Next.Entry.transform.rotation);
                    Previous = Next;
                }
            }
        }
    }

    readonly struct BiomeConnectorPorts
    {
        internal BiomeConnectorPorts(
            PllBiomeDefinition Definition,
            SegmentPort Entry, SegmentPort Exit)
        {
            this.Definition = Definition;
            this.Entry = Entry;
            this.Exit = Exit;
        }

        internal PllBiomeDefinition Definition { get; }
        internal SegmentPort Entry { get; }
        internal SegmentPort Exit { get; }

        internal static BiomeConnectorPorts Capture(
            PllBiomeDefinition Definition) => Capture(Definition, Definition.RootPrefab);

        internal static BiomeConnectorPorts Capture(
            PllBiomeDefinition Definition, GameObject Root)
        {
            SegmentPort Entry = null;
            SegmentPort Exit = null;
            foreach (var Port in Root.GetComponentsInChildren<SegmentPort>(true))
                if (Port.Kind == SegmentPortKind.Entry)
                {
                    if (Entry)
                        throw new InvalidOperationException(
                            "Biome '" + Definition.BiomeId
                            + "' has more than one Entry SegmentPort.");
                    Entry = Port;
                }
                else if (Port.Kind == SegmentPortKind.Exit)
                {
                    if (Exit)
                        throw new InvalidOperationException(
                            "Biome '" + Definition.BiomeId
                            + "' has more than one Exit SegmentPort.");
                    Exit = Port;
                }
            if (!Entry || !Exit)
                throw new InvalidOperationException(
                    "Biome '" + Definition.BiomeId
                    + "' requires one Entry and one Exit SegmentPort.");
            return new BiomeConnectorPorts(
                Definition, Entry, Exit);
        }
    }

    sealed class DefinitionCatalog
    {
        internal DefinitionCatalog(
            SourceIdentity identity, ClimbSurfaceAuthoringCatalog ClimbSurfaceCatalog,
            CollisionGeometry[] CollisionGeometry,
            GenerationAuthoring generation, FixtureAuthoring fixtures,
            PresentationAuthoring presentation)
        {
            Definition = identity.Definition;
            Graph = identity.Graph;
            HookedGeneration = new PllHookedPropGenerationCompiler(Definition, Graph, generation.EffectZones);
            Entry = identity.Entry;
            Exit = identity.Exit;
            Group = identity.Group;
            this.ClimbSurfaceCatalog = ClimbSurfaceCatalog;
            this.CollisionGeometry = CollisionGeometry;
            ClimbSurfaces = ClimbSurfaceCatalog.Surfaces;
            Visuals = presentation.Visuals;
            CustomVisuals = presentation.CustomVisuals;
            Atmosphere = presentation.Atmosphere;
            Effects = presentation.Effects;
            FogZones = presentation.FogZones;
            EffectZones = generation.EffectZones;
            Luggage = fixtures.Luggage;
            Campfires = fixtures.Campfires;
            Statues = fixtures.Statues;
            RestPoints = fixtures.RestPoints; HangingRopes = fixtures.HangingRopes;
            Variations = generation.Variations;
            SoundEmitters = presentation.SoundEmitters;
            Stingers = presentation.Stingers;
            NativeResolvers = presentation.NativeResolvers;
            NativeVisualResolvers = presentation.NativeVisualResolvers;
            PropZones = generation.PropZones;
            NoPropSpawn = generation.NoPropSpawn;
            ChildProps = generation.ChildProps;
            ItemSpawners = fixtures.ItemSpawners;
            FinalGeneration = generation.FinalGeneration;
            CriticalGeneration = generation.CriticalGeneration;
            EntityZones = generation.EntityZones;
            HazardZones = generation.HazardZones;
            Hazards = generation.Hazards; Entities = generation.Entities;
            AmuletSpawners = generation.AmuletSpawners;
            HookedProps = presentation.HookedProps;
            SurfaceTextures = DiscoverGraphComponents<PllSurfaceTextureAuthoring>(Graph);
            NativeMaterials = DiscoverGraphComponents<PllNativeMaterialAuthoring>(Graph);
            NativeSpawners = DiscoverGraphComponents<PllNativeSpawnerAuthoring>(Graph);
            BreakableProps = DiscoverGraphComponents<PllBreakablePropAuthoring>(Graph);
            SwingingProps = DiscoverGraphComponents<PllSwingingPropAuthoring>(Graph);
            Vines = DiscoverGraphComponents<PllVineAuthoring>(Graph);
        }

        internal PllBiomeDefinition Definition { get; }
        internal PrefabGraph Graph { get; }
        internal PllHookedPropGenerationCompiler HookedGeneration { get; }
        internal SegmentPort Entry { get; }
        internal SegmentPort Exit { get; }
        internal PllBiomeGroupContract Group { get; }
        internal ClimbSurfaceAuthoringCatalog ClimbSurfaceCatalog { get; }
        internal CollisionGeometry[] CollisionGeometry { get; }
        internal PllClimbSurfaceAuthoring[] ClimbSurfaces { get; }
        internal PllVisualAuthoring[] Visuals { get; }
        internal CustomVisualParticleGraph[] CustomVisuals { get; }
        internal PllAtmosphereAuthoring Atmosphere { get; }
        internal PllEffectAuthoring[] Effects { get; }
        internal PllFogZoneAuthoring[] FogZones { get; }
        internal PllEffectZoneAuthoring[] EffectZones { get; }
        internal PllLuggageAuthoring[] Luggage { get; }
        internal PllCampfireAuthoring[] Campfires { get; }
        internal PllStatueAuthoring[] Statues { get; }
        internal PllRestPointAuthoring[] RestPoints { get; }
        internal PllHangingRopeAuthoring[] HangingRopes { get; }
        internal PllVariationAuthoring[] Variations { get; }
        internal PllSoundEmitterAuthoring[] SoundEmitters { get; }
        internal PllBiomeStingerAuthoring[] Stingers { get; }
        internal PLLNativeResolver[] NativeResolvers { get; }
        internal PLLNativeVisualResolver[] NativeVisualResolvers { get; }
        internal PllPropZoneAuthoring[] PropZones { get; }
        internal PLLNoPropSpawn[] NoPropSpawn { get; }
        internal PllChildPropAuthoring[] ChildProps { get; }
        internal PllItemSpawnerAuthoring[] ItemSpawners { get; }
        internal PLLFinalGeneration[] FinalGeneration { get; }
        internal PLLCriticalGeneration[] CriticalGeneration { get; }
        internal PllEntityZoneAuthoring[] EntityZones { get; }
        internal PllHazardZoneAuthoring[] HazardZones { get; }
        internal PllHazardAuthoring[] Hazards { get; }
        internal PllEntityAuthoring[] Entities { get; }
        internal PllAmuletSpawnerAuthoring[] AmuletSpawners { get; }
        internal PLLHookedProp[] HookedProps { get; }
        internal PllSurfaceTextureAuthoring[] SurfaceTextures { get; }
        internal PllNativeMaterialAuthoring[] NativeMaterials { get; }
        internal PllNativeSpawnerAuthoring[] NativeSpawners { get; }
        internal PllBreakablePropAuthoring[] BreakableProps { get; }
        internal PllSwingingPropAuthoring[] SwingingProps { get; }
        internal PllVineAuthoring[] Vines { get; }
    }

    sealed class PrefabGraph
    {
        internal PrefabGraph(
            string rootPath, PrefabNode[] nodes, Dictionary<string, PrefabNode> byPath)
        {
            RootPath = rootPath;
            Nodes = nodes;
            ByPath = byPath;
        }

        internal string RootPath { get; }
        internal PrefabNode[] Nodes { get; }
        internal Dictionary<string, PrefabNode> ByPath { get; }
    }

    sealed class PrefabNode
    {
        internal PrefabNode(GameObject SourceRoot, string SourcePath, bool IsRoot)
        {
            this.SourceRoot = SourceRoot;
            this.SourcePath = SourcePath;
            this.IsRoot = IsRoot;
            Components = Array.AsReadOnly(DiscoverComponents<Component>(SourceRoot));
            Edges = new List<PrefabEdge>();
        }

        internal GameObject SourceRoot { get; }
        internal string SourcePath { get; }
        internal bool IsRoot { get; }
        internal IReadOnlyList<Component> Components { get; }
        internal List<PrefabEdge> Edges { get; }
    }

    sealed class PrefabEdge
    {
        internal PrefabEdge(
            PropSpawner spawner, PllChildPropAuthoring childProps, int index, string targetPath, Component ActorZone = null)
        {
            Spawner = spawner;
            ChildProps = childProps;
            Index = index;
            TargetPath = targetPath;
            this.ActorZone = ActorZone;
        }

        internal PropSpawner Spawner { get; }
        internal PllChildPropAuthoring ChildProps { get; }
        internal Component ActorZone { get; }
        internal int Index { get; }
        internal string TargetPath { get; }
    }
}
