using UnityEngine;

namespace PLL.Library;

public enum PllBiomeStingerMode : byte
{
    OnEnter = 0,
    EmitHook = 1,
    OnEnterAndEmitHook = 2
}

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("PLL/Audio/Biome Stinger")]
public sealed class PllBiomeStingerAuthoring : MonoBehaviour
{
    [SerializeField] private PllBiomeStingerMode mode = PllBiomeStingerMode.OnEnter;
    [SerializeField] private string emitHook = "";

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
            throw new System.InvalidOperationException("Biome Stinger compiled Cover Delay changed.");
    }
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public PllBiomeStingerMode Mode => mode;
    public string EmitHook => emitHook ?? "";
}
