using UnityEngine;

namespace PLL.Library;

public enum PllNativeVisualPreset : byte
{
    None = 0,
    Beach = 1,
    Tropics = 2,
    Roots = 3,
    Alpine = 4,
    Mesa = 5,
    Caldera = 6,
    Gloom = 7,
    Volcano = 8,
    Citadel = 9,
    Peak = 10,
    Nadir = 11
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Visuals/Native Visual Resolver")]
public sealed class PLLNativeVisualResolver : MonoBehaviour
{
    [SerializeField] private PllNativeVisualPreset preset = PllNativeVisualPreset.None;
    [SerializeField, InspectorName("Suppress Native Particles")] private bool SuppressNativeParticlesValue = false;
    [SerializeField] PllEffectParticleColorMode ParticleColorModeValue = PllEffectParticleColorMode.Original;
    [SerializeField] Color ParticleColorValue = Color.white;
    [SerializeField] PllEffectMaterialColorChannel MaterialColorChannelValue = PllEffectMaterialColorChannel.None;
    [SerializeField] Color MaterialColorValue = Color.white;

#if PLL_CREATOR_SDK
    [SerializeField] PllPresentationTransition TransitionValue = default;
#endif
    [SerializeField, HideInInspector] float BundleCoverDelaySeconds = 0f;

#if PLL_CREATOR_SDK
    public PllPresentationTransition Transition => TransitionValue;
    internal void SealBundleSerialization() => BundleCoverDelaySeconds = Transition.CoverDelaySeconds;
    public void ValidateBundleSerialization()
    {
        if (!BundleCoverDelaySeconds.Equals(Transition.CoverDelaySeconds))
            throw new System.InvalidOperationException("Native Visual Resolver compiled Cover Delay changed.");
    }
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public PllNativeVisualPreset Preset => preset;
    public bool SuppressNativeParticles => SuppressNativeParticlesValue;
    public PllEffectParticleColorMode ParticleColorMode => ParticleColorModeValue;
    public Color ParticleColor => ParticleColorValue;
    public PllEffectMaterialColorChannel MaterialColorChannel => MaterialColorChannelValue;
    public Color MaterialColor => MaterialColorValue;
}
