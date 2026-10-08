using UnityEngine;

namespace PLL.Library;

#pragma warning disable CS0649
[DisallowMultipleComponent]
[AddComponentMenu("PLL/Visuals/Atmosphere")]
public sealed class PllAtmosphereAuthoring : MonoBehaviour
{
    [SerializeField] Material? SkyboxMaterialValue;
    [SerializeField] Texture2D? FogTextureValue;

    public Material? SkyboxMaterial => SkyboxMaterialValue;
    public Texture2D? FogTexture => FogTextureValue;
}
#pragma warning restore CS0649
