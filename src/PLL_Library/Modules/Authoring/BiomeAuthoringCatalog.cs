using System;
using System.Collections.Generic;
using System.IO;

namespace PLL.Library;

internal static class BiomeAuthoringCatalog
{
    static readonly Dictionary<string, BiomeAuthoringContract> Contracts = new(StringComparer.Ordinal);
    static bool registrySealed;

    internal static BiomeAuthoringContract[] Prepare(
        IReadOnlyList<PllBiomeDefinition> definitions,
        IReadOnlyDictionary<BiomeId, byte[]> dependencySeals)
    {
        if (registrySealed) throw new InvalidOperationException("The biome authoring catalog is sealed.");
        if (dependencySeals is null) throw new ArgumentNullException(nameof(dependencySeals));
        ClimbSurfaceAuthoringContract.SealProvider();
        var contracts = new BiomeAuthoringContract[definitions.Count];
        var ids = new HashSet<BiomeId>();
        var FluidDefinitions = new Dictionary<PllFluidDefinition, FluidDefinitionContract>();
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (!definition) throw new ArgumentException("A biome definition was destroyed.", nameof(definitions));
            dependencySeals.TryGetValue(definition.BiomeId, out var dependencySeal);
            contracts[i] = BiomeAuthoringContract.Create(definition, dependencySeal, FluidDefinitions);
            if (!ids.Add(contracts[i].Id))
                throw new InvalidOperationException($"Biome '{contracts[i].Id}' occurs more than once in the authoring batch.");
        }
        foreach (var contract in contracts)
            if (Contracts.ContainsKey(contract.SegmentId))
                throw new InvalidOperationException($"Segment '{contract.SegmentId}' already has registered authoring metadata.");
        return contracts;
    }

    internal static void Register(IReadOnlyList<BiomeAuthoringContract> contracts)
    {
        if (registrySealed) throw new InvalidOperationException("The biome authoring catalog is sealed.");
        foreach (var contract in contracts)
            if (Contracts.ContainsKey(contract.SegmentId))
                throw new InvalidOperationException($"Segment '{contract.SegmentId}' already has registered authoring metadata.");
        foreach (var contract in contracts) Contracts.Add(contract.SegmentId, contract);
    }

    internal static void Unregister(IReadOnlyList<BiomeAuthoringContract> contracts)
    {
        if (registrySealed) throw new InvalidOperationException("The biome authoring catalog is sealed.");
        foreach (var contract in contracts)
            if (!Contracts.TryGetValue(contract.SegmentId, out var current) || !ReferenceEquals(current, contract))
                throw new InvalidOperationException(
                    $"Segment '{contract.SegmentId}' authoring metadata is not owned by the registration batch.");
        foreach (var contract in contracts)
        {
            contract.ClearExternal();
            Contracts.Remove(contract.SegmentId);
        }
    }

    internal static bool TryGet(string segmentId, out BiomeAuthoringContract contract)
    {
        contract = null!;
        if (segmentId is null) return false;
        return Contracts.TryGetValue(segmentId, out contract!);
    }

    internal static void ClearExternal()
    {
        foreach (var contract in Contracts.Values) contract.ClearExternal();
    }

    internal static void SealRegistry() => registrySealed = true;

    internal static int RequiredNativeVisualMask
    {
        get
        {
            var result = 0;
            foreach (var contract in Contracts.Values) result |= contract.RequiredNativeVisualMask;
            return result;
        }
    }

    internal static void WriteManifest(BinaryWriter writer)
    {
        var contracts = new BiomeAuthoringContract[Contracts.Count];
        Contracts.Values.CopyTo(contracts, 0);
        Array.Sort(contracts, (left, right) => StringComparer.Ordinal.Compare(left.SegmentId, right.SegmentId));
        writer.Write(contracts.Length);
        foreach (var contract in contracts) contract.WriteManifest(writer);
    }

    internal static void WriteSourceManifest(BinaryWriter writer)
    {
        var contracts = new BiomeAuthoringContract[Contracts.Count];
        Contracts.Values.CopyTo(contracts, 0);
        Array.Sort(contracts, (left, right) => StringComparer.Ordinal.Compare(left.SegmentId, right.SegmentId));
        writer.Write(contracts.Length);
        foreach (var contract in contracts) contract.WriteSourceManifest(writer);
    }
}
