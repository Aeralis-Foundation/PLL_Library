using UnityEngine;

namespace PLL.Library;

[AddComponentMenu("PLL/Visuals/Shader Visual")]
public sealed class PllVisualAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] Shader? shader = null;
    [SerializeField] Material? material = null;
    [SerializeField] string propertyName = "";
    [SerializeField] float triggerValue = 1f;
    [SerializeField] bool enabledByDefault = false;

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
            throw new System.InvalidOperationException("Shader Visual compiled Cover Delay changed.");
    }
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public GameObject Root => root;
    public Shader? Shader => shader;
    public Material? Material => material;
    public string PropertyName => propertyName ?? "";
    public float TriggerValue => triggerValue;
    public bool EnabledByDefault => enabledByDefault;
}
