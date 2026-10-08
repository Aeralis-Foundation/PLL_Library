using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class SpawnerConstraintAuthoringContract
{
    readonly PropZoneContract[] PropZones;
    readonly NoSpawnContract[] NoSpawnMarkers;
    readonly SpawnerConstraintContract[] SpawnerConstraints;

    SpawnerConstraintAuthoringContract(
        PropZoneContract[] PropZones,
        NoSpawnContract[] NoSpawnMarkers,
        SpawnerConstraintContract[] SpawnerConstraints)
    {
        this.PropZones = PropZones;
        this.NoSpawnMarkers = NoSpawnMarkers;
        this.SpawnerConstraints = SpawnerConstraints;
    }

    bool RequiresNoSpawn => NoSpawnMarkers.Length != 0;

    internal static SpawnerConstraintAuthoringContract Create(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        var PropZones = PropZoneContract.CreateAll(Template, Floor, Campfire, Id, Graph, out var ZonedSpawners);
        var NoSpawnMarkers = NoSpawnContract.CreateAll(Template, Id, Graph);
        var SpawnerConstraints = SpawnerConstraintContract.CreateAll(
            Template, Id, Graph, NoSpawnMarkers.Length != 0, ZonedSpawners);
        return new SpawnerConstraintAuthoringContract(PropZones, NoSpawnMarkers, SpawnerConstraints);
    }

    internal void Validate(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        foreach (var PropZone in PropZones) PropZone.Validate(Template, Floor, Campfire, Id, Graph);
        foreach (var NoSpawnMarker in NoSpawnMarkers) NoSpawnMarker.Validate(Template, Id, Graph);
        foreach (var SpawnerConstraint in SpawnerConstraints)
            SpawnerConstraint.Validate(Template, Id, Graph, RequiresNoSpawn);
    }

    internal void ValidateStableRuntime(Transform Root, BiomeId Id)
    {
        foreach (var PropZone in PropZones)
            if (!PropZone.Generated) PropZone.ValidateStableRuntime(Root, Id);
        foreach (var NoSpawnMarker in NoSpawnMarkers)
            if (!NoSpawnMarker.Generated) NoSpawnMarker.ValidateStableRuntime(Root, Id);
        foreach (var SpawnerConstraint in SpawnerConstraints)
            if (!SpawnerConstraint.Generated)
                SpawnerConstraint.ValidateStableRuntime(Root, Id, RequiresNoSpawn);
    }

    internal void ValidateGeneratedRuntime(Transform FeatureRoot, BiomeId Id)
    {
        foreach (var Source in FeatureRoot.GetComponentsInChildren<PllPropZoneAuthoring>(true))
        {
            if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(FeatureRoot, Source.transform)) continue;
            RequireOne(PropZones, Contract => Contract.TryValidateGenerated(Source, Id), Id, "prop-zone authoring", Source);
        }
        foreach (var Source in FeatureRoot.GetComponentsInChildren<PLLNoPropSpawn>(true))
        {
            if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(FeatureRoot, Source.transform)) continue;
            RequireOne(NoSpawnMarkers, Contract => Contract.TryValidateGenerated(Source, Id), Id, "no-prop-spawn marker", Source);
        }
        foreach (var Source in FeatureRoot.GetComponentsInChildren<PropSpawner>(true))
        {
            if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(FeatureRoot, Source.transform)) continue;
            var Matched = false;
            foreach (var Contract in SpawnerConstraints)
            {
                if (!Contract.TryValidateGenerated(Source, Id, RequiresNoSpawn)) continue;
                if (Matched)
                    throw new InvalidOperationException(
                        $"Biome '{Id}' generated PropSpawner '{Source.name}' matches more than one sealed source contract.");
                Matched = true;
            }
            if (Matched) continue;
            var Constraints = Source.constraints;
            if (Constraints is null) continue;
            foreach (var Constraint in Constraints)
                if (Constraint is PllNoPropSpawnConstraint or PllPropZoneConstraint)
                    throw new InvalidOperationException(
                        $"Biome '{Id}' generated an unsealed translated PropSpawner '{Source.name}'.");
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(PropZones.Length);
        foreach (var PropZone in PropZones) PropZone.WriteManifest(Writer);
        Writer.Write(NoSpawnMarkers.Length);
        foreach (var NoSpawnMarker in NoSpawnMarkers) NoSpawnMarker.WriteManifest(Writer);
        Writer.Write(SpawnerConstraints.Length);
        foreach (var SpawnerConstraint in SpawnerConstraints) SpawnerConstraint.WriteManifest(Writer);
    }



    static void RequireOne<T>(
        IReadOnlyList<T> Contracts,
        Func<T, bool> Match,
        BiomeId Id,
        string Name,
        Component Source)
    {
        var Matched = false;
        foreach (var Contract in Contracts)
        {
            if (!Match(Contract)) continue;
            if (Matched)
                throw new InvalidOperationException($"Biome '{Id}' generated {Name} '{Source.name}' matches more than one sealed source contract.");
            Matched = true;
        }
        if (!Matched)
            throw new InvalidOperationException($"Biome '{Id}' generated an unsealed {Name} '{Source.name}'.");
    }
}
