using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllEffectMode : byte
{
    Timed = 1,
    EmitHook = 2,
    Zone = 3
}

public enum PllEffectParticleColorMode : byte
{
    Original = 0,
    Override = 1
}

public enum PllNativeEffectParticles : byte
{
    None = 0,
    Fire = 1,
    SmokeOff = 2,
    SmokeLit = 3
}

public enum PllEffectMaterialColorChannel : byte
{
    None = 0,
    Color = 1,
    Tint = 2,
    BaseColor = 3
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Effects/Effect")]
public sealed class PllEffectAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] ParticleSystem[] particles = Array.Empty<ParticleSystem>();
    [SerializeField, Min(0f)] float duration = 1f;
    [SerializeField] PllEffectMode mode = PllEffectMode.Timed;
    [SerializeField] AudioClip? ambience = null;
    [SerializeField, InspectorName("Biome Ambient Color")] Color color = Color.white;
    [SerializeField] AudioClip? sound = null;
    [SerializeField] string emitHook = "";
    [SerializeField] GameObject? presentationRoot = null;
    [SerializeField] PllEffectParticleColorMode ParticleColorModeValue = PllEffectParticleColorMode.Original;
    [SerializeField] Color ParticleColorValue = Color.white;
    [SerializeField] PllNativeEffectParticles NativeParticlesValue = PllNativeEffectParticles.None;
    [SerializeField] PllEffectMaterialColorChannel MaterialColorChannelValue = PllEffectMaterialColorChannel.None;
    [SerializeField] Renderer? ColorRendererValue = null;
    [SerializeField, Min(0)] int ColorMaterialSlotValue = 0;
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
            throw new InvalidOperationException("Effect compiled Cover Delay changed.");
    }
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public GameObject Root => root;
    public IReadOnlyList<ParticleSystem> Particles => particles ?? Array.Empty<ParticleSystem>();
    public float Duration => duration;
    public PllEffectMode Mode => mode;
    public AudioClip? Ambience => ambience;
    public Color Color => color;
    public AudioClip? Sound => sound;
    public string EmitHook => emitHook ?? "";
    public GameObject? PresentationRoot => presentationRoot;
    public PllEffectParticleColorMode ParticleColorMode => ParticleColorModeValue;
    public Color ParticleColor => ParticleColorValue;
    public PllNativeEffectParticles NativeParticles => NativeParticlesValue;
    public PllEffectMaterialColorChannel MaterialColorChannel => MaterialColorChannelValue;
    public Renderer? ColorRenderer => ColorRendererValue;
    public int ColorMaterialSlot => ColorMaterialSlotValue;
    public Color MaterialColor => MaterialColorValue;
}
