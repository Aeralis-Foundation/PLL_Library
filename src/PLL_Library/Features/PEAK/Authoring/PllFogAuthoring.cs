using UnityEngine;

namespace PLL.Library;

public enum PllFogMode : byte
{
    Normal = 0,
    RisingFogless = 1,
    Fogless = 2
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Effects/Fog")]
public sealed class PllFogAuthoring : MonoBehaviour
{
    [SerializeField] private PllFogMode mode = PllFogMode.Normal;
    public PllFogMode Mode => mode;
}
