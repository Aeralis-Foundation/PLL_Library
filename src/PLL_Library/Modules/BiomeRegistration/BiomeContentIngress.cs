using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal static class BiomeContentIngress
{
    internal sealed class PreparedBiomeContent
    {
        internal PreparedBiomeContent(
            PllBiomeDefinition[] Definitions,
            Dictionary<BiomeId, byte[]> DependencySeals,
            CustomBiomeDefinition[] Biomes,
            CustomSegmentDefinition[] Segments,
            PllBiomeGroupContract[] Groups,
            CustomBiomeConfigDefaults[] ConfigurationDefaults)
        {
            this.Definitions = Definitions;
            this.DependencySeals = DependencySeals;
            this.Biomes = Biomes;
            this.Segments = Segments;
            this.Groups = Groups;
            this.ConfigurationDefaults = ConfigurationDefaults;
        }

        internal PllBiomeDefinition[] Definitions { get; }
        internal Dictionary<BiomeId, byte[]> DependencySeals { get; }
        internal CustomBiomeDefinition[] Biomes { get; }
        internal CustomSegmentDefinition[] Segments { get; }
        internal PllBiomeGroupContract[] Groups { get; }
        internal CustomBiomeConfigDefaults[] ConfigurationDefaults { get; }
    }

    internal static PreparedBiomeContent Prepare(AssetBundle Bundle)
    {
        var Registries = Bundle.LoadAllAssets<PllBiomeRegistry>();
        if (Registries.Length != 1)
            throw new ArgumentException(
                $"The AssetBundle must contain exactly one PllBiomeRegistry; found {Registries.Length}.", nameof(Bundle));
        var Registry = Registries[0];
        if (!Registry)
            throw new ArgumentException("The AssetBundle contains a missing PllBiomeRegistry.", nameof(Bundle));
        Registry.Validate();
        var RegistryDefinitions = Registry.Definitions;
        if (RegistryDefinitions.Count == 0)
            throw new ArgumentException("The AssetBundle PllBiomeRegistry contains no biome definitions.", nameof(Bundle));

        var DefinitionArray = new PllBiomeDefinition[RegistryDefinitions.Count];
        var DefinitionObjects = new HashSet<int>();
        var DefinitionRoots = new Dictionary<int, PllBiomeDefinition>();
        for (var Index = 0; Index < DefinitionArray.Length; Index++)
        {
            var Definition = RegistryDefinitions[Index];
            DefinitionArray[Index] = Definition;
            DefinitionObjects.Add(Definition.GetInstanceID());
            var Root = Definition.RootPrefab;
            if (!Root)
                throw new ArgumentException($"Biome '{Definition.BiomeId}' has no root prefab.", nameof(Bundle));
            if (!DefinitionRoots.TryAdd(Root.GetInstanceID(), Definition))
                throw new ArgumentException(
                    $"Biome '{Definition.BiomeId}' shares its root prefab with another registry definition.", nameof(Bundle));
        }

        var BundleDefinitions = Bundle.LoadAllAssets<PllBiomeDefinition>();
        if (BundleDefinitions.Length != DefinitionArray.Length)
            throw new ArgumentException(
                $"The PllBiomeRegistry contains {DefinitionArray.Length} definitions, but the AssetBundle contains "
                    + $"{BundleDefinitions.Length} definition assets.",
                nameof(Bundle));
        foreach (var Definition in BundleDefinitions)
            if (!Definition || !DefinitionObjects.Remove(Definition.GetInstanceID()))
                throw new ArgumentException(
                    $"Biome definition '{(Definition ? Definition.name : "missing")}' is not owned by the "
                        + "AssetBundle PllBiomeRegistry.",
                    nameof(Bundle));

        var RootAssets = Bundle.LoadAllAssets<GameObject>();
        var SeenRoots = new HashSet<int>();
        foreach (var Root in RootAssets)
        {
            if (!Root)
                throw new ArgumentException("The AssetBundle contains a missing root GameObject asset.", nameof(Bundle));
            var RootId = Root.GetInstanceID();
            var Links = Root.GetComponentsInChildren<PllBiomeRoot>(true);
            if (!DefinitionRoots.TryGetValue(RootId, out var Definition))
            {
                if (Links.Length != 0)
                    throw new ArgumentException(
                        $"AssetBundle root prefab '{Root.name}' has a PllBiomeRoot but is not owned by the registry.",
                        nameof(Bundle));
                continue;
            }
            if (!SeenRoots.Add(RootId))
                throw new ArgumentException(
                    $"Biome '{Definition.BiomeId}' root prefab occurs more than once in the AssetBundle.", nameof(Bundle));
            if (Links.Length > 1 || Links.Length == 1
                && (Links[0].gameObject != Root || Links[0].Definition != Definition))
                throw new ArgumentException(
                    $"Biome '{Definition.BiomeId}' root link must be unique, direct, and reference its registry definition.",
                    nameof(Bundle));
        }
        foreach (var Definition in DefinitionArray)
            if (!SeenRoots.Contains(Definition.RootPrefab.GetInstanceID()))
                throw new ArgumentException(
                    $"Biome '{Definition.BiomeId}' root prefab is not a root GameObject asset in the AssetBundle.",
                    nameof(Bundle));

        foreach (var Definition in DefinitionArray) ValidateDefinition(Definition);
        var DependencySeals = ReadDependencySeals(Bundle, DefinitionArray);
        var Biomes = new CustomBiomeDefinition[DefinitionArray.Length];
        var Segments = new CustomSegmentDefinition[DefinitionArray.Length];
        var ConfigurationDefaults = new CustomBiomeConfigDefaults[DefinitionArray.Length];
        for (var i = 0; i < DefinitionArray.Length; i++)
        {
            var Definition = DefinitionArray[i];
            var Id = Definition.BiomeId;
            var Defaults = CustomBiomeConfigDefaults.Create(Definition);
            ConfigurationDefaults[i] = Defaults;
            Biomes[i] = new CustomBiomeDefinition(
                Id.Value, Definition.ContentRevision, Definition.DayNightProfile, Definition.Behavior);
            Segments[i] = new CustomSegmentDefinition(
                $"{Id.Value}/segment",
                Definition.ContentRevision,
                $"{Id.Value}/segment-type",
                Id.Value,
                Definition.RootPrefab,
                Definition.RootPrefab.GetComponent<CustomSegmentRuntime>(),
                Definition.Anchors);
        }
        var CapturedGroups = new List<PllBiomeGroupContract>();
        foreach (var Definition in DefinitionArray)
            if (PllBiomeGroupContract.Capture(Definition) is { } Group)
                CapturedGroups.Add(Group);
        var Groups = PllBiomeGroupContract.ValidateScope(
            DefinitionArray, RootAssets, CapturedGroups);
        return new PreparedBiomeContent(
            DefinitionArray, DependencySeals, Biomes, Segments, Groups, ConfigurationDefaults);
    }

    static Dictionary<BiomeId, byte[]> ReadDependencySeals(
        AssetBundle Bundle, IReadOnlyList<PllBiomeDefinition> Definitions)
    {
        var DefinitionsById = new Dictionary<BiomeId, PllBiomeDefinition>();
        foreach (var Definition in Definitions) DefinitionsById.Add(Definition.BiomeId, Definition);
        var Result = new Dictionary<BiomeId, byte[]>();
        foreach (var Address in Bundle.GetAllAssetNames())
        {
            if (!PllDependencySeal.HasAddressPrefix(Address)) continue;
            if (!PllDependencySeal.TryParseAddress(Address, out var Id, out var Seal))
                throw new ArgumentException(
                    "The AssetBundle contains a malformed presentation dependency seal address.", nameof(Bundle));
            var Definition = Bundle.LoadAsset<PllBiomeDefinition>(Address);
            if (!Definition || !DefinitionsById.TryGetValue(Id, out var Expected) || Definition != Expected)
                throw new ArgumentException(
                    $"Presentation dependency seal for biome '{Id}' does not identify its exact definition.",
                    nameof(Bundle));
            if (!Result.TryAdd(Id, Seal))
                throw new ArgumentException(
                    $"Biome '{Id}' has more than one presentation dependency seal address.", nameof(Bundle));
        }
        return Result;
    }

    static void ValidateDefinition(PllBiomeDefinition Definition)
    {
        var Id = Definition.BiomeId;
        PllBiomeBehaviorCompatibility.RequireDefined(Definition.Behavior, $"Biome '{Id}'");
        SegmentRegistry.ValidateToken(Definition.ContentRevision, nameof(PllBiomeDefinition.ContentRevision));
        if (!Definition.DayNightProfile)
            throw new ArgumentException($"Biome '{Id}' has no DayNightProfile.", nameof(Definition));
        var Root = Definition.RootPrefab;
        var Anchors = Definition.Anchors;
        if (!Anchors)
            throw new ArgumentException($"Biome '{Id}' has no PllBiomePrefabAnchors.", nameof(Definition));

        var Runtimes = Root.GetComponentsInChildren<CustomSegmentRuntime>(true);
        if (Runtimes.Length != 1 || Runtimes[0].gameObject != Root)
            throw new ArgumentException($"Biome '{Id}' root must directly own exactly one CustomSegmentRuntime.", nameof(Definition));
        var AnchorComponents = Root.GetComponentsInChildren<PllBiomePrefabAnchors>(true);
        if (AnchorComponents.Length != 1 || !ReferenceEquals(AnchorComponents[0], Anchors))
            throw new ArgumentException($"Biome '{Id}' root must own exactly the supplied PllBiomePrefabAnchors.", nameof(Definition));
        var Runtime = Runtimes[0];
        if (!Anchors.FloorRoot || !Anchors.CampfireRoot || !Runtime.SegmentParent || !Runtime.SegmentCampfire
            || Anchors.FloorRoot != Runtime.SegmentParent || Anchors.CampfireRoot != Runtime.SegmentCampfire)
            throw new ArgumentException($"Biome '{Id}' floor and campfire roots must match CustomSegmentRuntime.", nameof(Definition));

        var Ports = Root.GetComponentsInChildren<SegmentPort>(true);
        SegmentPort? Start = null;
        foreach (var Port in Ports)
            if (Port.Kind == SegmentPortKind.Start)
            {
                if (Start)
                    throw new ArgumentException($"Biome '{Id}' contains more than one Start port.", nameof(Definition));
                Start = Port;
            }
        if (!Start || Anchors.StartAnchor != Start.transform)
            throw new ArgumentException(
                $"Biome '{Id}' Start anchor must be its exact Start SegmentPort transform.", nameof(Definition));

        RequireOwned(Root.transform, Anchors.FloorRoot.transform, Id, "floor root");
        RequireOwned(Root.transform, Anchors.CampfireRoot.transform, Id, "campfire root");
        RequireOwned(
            Root.transform,
            Anchors.PlayerFormationRoot ? Anchors.PlayerFormationRoot.transform : null,
            Id,
            "player formation root");
        RequireOwned(Root.transform, Anchors.PlaneAnchor, Id, "plane anchor");
        RequireOwned(Root.transform, Anchors.FogBeginAnchor, Id, "fog begin anchor");
        RequireOwned(Root.transform, Anchors.FogCutoffAnchor, Id, "fog cutoff anchor");
        if (!Anchors.StatueRoot) return;
        RequireOwned(Root.transform, Anchors.StatueRoot!.transform, Id, "statue root");
        if (!TransformOwnership.Contains(Runtime.SegmentParent.transform, Anchors.StatueRoot.transform)
            && !TransformOwnership.Contains(Runtime.SegmentCampfire.transform, Anchors.StatueRoot.transform))
            throw new ArgumentException($"Biome '{Id}' statue root must belong to a feature root.", nameof(Definition));
    }

    static void RequireOwned(Transform Root, Transform? Value, BiomeId Id, string Name)
    {
        if (!Value || !TransformOwnership.Contains(Root, Value))
            throw new ArgumentException($"Biome '{Id}' {Name} is missing or outside its root prefab.", "Definition");
        for (var Current = Value; Current != Root; Current = Current.parent)
            if (Current.GetComponent<PropSpawner>())
                throw new ArgumentException($"Biome '{Id}' {Name} cannot be owned by a PropSpawner.", "Definition");
    }
}
