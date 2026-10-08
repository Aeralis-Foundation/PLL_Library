using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Biomes/Biome Group")]
public sealed class PllBiomeGroupAuthoring : MonoBehaviour
{
    [SerializeField] PllBiomeDefinition[] PrefixesValue = Array.Empty<PllBiomeDefinition>();

    public IReadOnlyList<PllBiomeDefinition> Prefixes =>
        PrefixesValue ?? Array.Empty<PllBiomeDefinition>();
}
