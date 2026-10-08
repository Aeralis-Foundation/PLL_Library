using UnityEngine;

namespace PLL.Library;

[AddComponentMenu("PLL/Audio/Sound Emitter")]
public sealed class PllSoundEmitterAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] AudioSource audioSource = null!;

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
            throw new System.InvalidOperationException("Sound Emitter compiled Cover Delay changed.");
    }
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public GameObject Root => root;
    public AudioSource AudioSource => audioSource;
}
