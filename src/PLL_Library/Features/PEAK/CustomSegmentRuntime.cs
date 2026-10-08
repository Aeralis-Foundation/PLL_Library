using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
public sealed class CustomSegmentRuntime : MonoBehaviour
{
    [SerializeField] GameObject segmentParent = null!;
    [SerializeField] GameObject segmentCampfire = null!;
    [SerializeField] FogSphereOrigin fogOrigin = null!;
    [SerializeField] FogCutoutZone fogCutout = null!;
#if PLL_CREATOR_SDK
    [SerializeField] MountainProgressHandler.ProgressPoint progressPoint = null!;
#endif
    [SerializeReference, HideInInspector] MountainProgressHandler.ProgressPoint BundleProgressPoint = null!;
    bool registered;

    public GameObject SegmentParent => segmentParent;
    public GameObject SegmentCampfire => segmentCampfire;
    public FogSphereOrigin FogOrigin => fogOrigin;
    public FogCutoutZone FogCutout => fogCutout;
#if PLL_CREATOR_SDK
    public MountainProgressHandler.ProgressPoint ProgressPoint => progressPoint;

    internal void SealBundleSerialization() => BundleProgressPoint = progressPoint;
#else
    public MountainProgressHandler.ProgressPoint ProgressPoint => BundleProgressPoint;
#endif

    internal bool Registered => registered;
    internal void Freeze() => registered = true;
    internal void Unfreeze() => registered = false;
}
