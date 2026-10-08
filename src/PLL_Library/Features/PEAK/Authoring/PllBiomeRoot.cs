using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Biomes/Biome Root")]
public sealed class PllBiomeRoot : MonoBehaviour
{
    [SerializeField] PllBiomeDefinition definition = null!;

    public PllBiomeDefinition Definition => definition;
}
