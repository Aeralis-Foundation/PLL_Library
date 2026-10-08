using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[CreateAssetMenu(fileName = "PllBiomeRegistry", menuName = "PLL/Biome Registry")]
public sealed class PllBiomeRegistry : ScriptableObject
{
    [SerializeField] PllBiomeDefinition[] definitions = Array.Empty<PllBiomeDefinition>();

    public IReadOnlyList<PllBiomeDefinition> Definitions => definitions ?? Array.Empty<PllBiomeDefinition>();

    public void Validate()
    {
        if (definitions is null) throw new InvalidOperationException("The biome registry definition list is missing.");
        var ids = new HashSet<BiomeId>();
        foreach (var definition in definitions)
        {
            if (!definition) throw new InvalidOperationException("The biome registry contains a missing definition.");
            if (!definition.BiomeId.IsValid)
                throw new InvalidOperationException($"Biome definition '{definition.name}' has an invalid stable ID.");
            if (!ids.Add(definition.BiomeId))
                throw new InvalidOperationException($"Biome ID '{definition.BiomeId}' occurs more than once in this registry.");
        }
    }

    public bool TryGet(BiomeId id, out PllBiomeDefinition? definition)
    {
        definition = null;
        if (!id.IsValid) return false;
        Validate();
        foreach (var candidate in definitions)
        {
            if (!candidate || candidate.BiomeId != id) continue;
            definition = candidate;
        }
        return definition;
    }
}
