using UnityEngine;

namespace PLL.Library;

public enum PllCustomVisualSpace : byte
{
    Segment = 0,
    ObservedPlayer = 1
}

#pragma warning disable CS0649
[DisallowMultipleComponent]
[AddComponentMenu("PLL/Visuals/Custom Visual")]
public sealed class PllCustomVisualAuthoring : MonoBehaviour
{
    [SerializeField] private PllCustomVisualSpace space = PllCustomVisualSpace.ObservedPlayer;
    [SerializeField] private Vector3 positionOffset;
    [SerializeField, Min(0f)] private float repositionDistance = 10f;

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
            throw new System.InvalidOperationException("Custom Visual compiled Cover Delay changed.");
    }
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public PllCustomVisualSpace Space => space;
    public Vector3 PositionOffset => positionOffset;
    public float RepositionDistance => repositionDistance;
}
#pragma warning restore CS0649
