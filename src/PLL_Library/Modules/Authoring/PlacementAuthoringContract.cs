using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class PlacementAuthoringContract
{
    readonly VariationAuthoringContract Variations;
    readonly SpawnerConstraintAuthoringContract SpawnerConstraints;
    readonly ChildPropAuthoringContract ChildProps;

    PlacementAuthoringContract(
        VariationAuthoringContract Variations,
        SpawnerConstraintAuthoringContract SpawnerConstraints,
        ChildPropAuthoringContract ChildProps)
    {
        this.Variations = Variations;
        this.SpawnerConstraints = SpawnerConstraints;
        this.ChildProps = ChildProps;
    }

    internal static PlacementAuthoringContract Create(
        PllBiomeDefinition Source,
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        var Floor = Source.Anchors.FloorRoot.transform;
        var Campfire = Source.Anchors.CampfireRoot.transform;
        return new PlacementAuthoringContract(
            VariationAuthoringContract.Create(Source, Template, Id, Graph),
            SpawnerConstraintAuthoringContract.Create(Template, Floor, Campfire, Id, Graph),
            ChildPropAuthoringContract.Create(Template, Floor, Campfire, Id, Graph));
    }

    internal void Validate(
        PllBiomeDefinition Source,
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        Variations.Validate(Source, Template, Id);
        var Floor = Source.Anchors.FloorRoot.transform;
        var Campfire = Source.Anchors.CampfireRoot.transform;
        SpawnerConstraints.Validate(Template, Floor, Campfire, Id, Graph);
        ChildProps.Validate(Template, Floor, Campfire, Id, Graph);
    }

    internal void ValidateStableRuntime(Transform Root, BiomeId Id)
    {
        SpawnerConstraints.ValidateStableRuntime(Root, Id);
        ChildProps.ValidateStableRuntime(Root, Id);
    }

    internal IReadOnlyList<VariationContract> VariationGroups => Variations.Groups;
    internal VariationOccurrence PrepareVariations(
        Transform Root, BiomeId Id, Func<string, string, float> EffectiveWeight) =>
        Variations.Prepare(Root, Id, EffectiveWeight);

    internal void ValidateGeneratedRuntime(Transform FeatureRoot, BiomeId Id)
    {
        if (!FeatureRoot) throw new InvalidOperationException($"Biome '{Id}' lost a generated feature root.");
        SpawnerConstraints.ValidateGeneratedRuntime(FeatureRoot, Id);
        ChildProps.ValidateGeneratedRuntime(FeatureRoot, Id);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Variations.WriteManifest(Writer);
        SpawnerConstraints.WriteManifest(Writer);
        ChildProps.WriteManifest(Writer);
    }
}
