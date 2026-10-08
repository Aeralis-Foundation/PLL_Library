using UnityEngine;

namespace PLL.Library;

public enum PllVineProfile { Vine, HangingBridge }
public enum PllVineAppearance { Native, CustomSpan }
public enum PllVineEndpoints { AuthoredAnchors, NativePlatformConnection }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Climbing/Vine / Hanging Bridge")]
public sealed class PllVineAuthoring : MonoBehaviour
{
    public const int SpanNormalizationVersion = 1;
    [SerializeField, HideInInspector] string IdValue = string.Empty;
    [SerializeField] PllVineProfile ProfileValue = PllVineProfile.Vine;
    [SerializeField] PllVineEndpoints EndpointsValue = PllVineEndpoints.AuthoredAnchors;
    [SerializeField] Transform StartAnchorValue = null!;
    [SerializeField] Transform EndAnchorValue = null!;
    [SerializeField, Min(0f)] float MinimumDistanceValue = 1f;
    [SerializeField, Min(0f)] float MaximumDistanceValue = 50f;
    [SerializeField, Min(0f)] float MaximumHeightDifferenceValue = 100f;
    [SerializeField, Min(0f)] float HangAmountValue = 5f;
    [SerializeField] float ColliderOffsetValue = 0f;
    [SerializeField] Vector2 BoxShapeValue = Vector2.one;
    [SerializeField] PllVineAppearance AppearanceValue = PllVineAppearance.Native;
    [SerializeField] Mesh? SpanMeshValue = null;
    [SerializeField] Vector3 ModelStartValue = Vector3.zero;
    [SerializeField] Vector3 ModelEndValue = Vector3.forward;
    [SerializeField] Vector3 ModelUpValue = Vector3.up;
    [SerializeField, HideInInspector] int BundleSpanVersionValue = 0;

    public string Id => IdValue;
    public PllVineProfile Profile => ProfileValue;
    public PllVineEndpoints Endpoints => EndpointsValue;
    public Transform StartAnchor => StartAnchorValue;
    public Transform EndAnchor => EndAnchorValue;
    public float MinimumDistance => MinimumDistanceValue;
    public float MaximumDistance => MaximumDistanceValue;
    public float MaximumHeightDifference => MaximumHeightDifferenceValue;
    public float HangAmount => HangAmountValue;
    public float ColliderOffset => ColliderOffsetValue;
    public Vector2 BoxShape => BoxShapeValue;
    public PllVineAppearance Appearance => AppearanceValue;
    public Mesh? SpanMesh => SpanMeshValue;
    public Vector3 ModelStart => ModelStartValue;
    public Vector3 ModelEnd => ModelEndValue;
    public Vector3 ModelUp => ModelUpValue;
    public int BundleSpanVersion => BundleSpanVersionValue;
#if PLL_CREATOR_SDK
    public void SealSpanMesh(Mesh Mesh, float Length)
    {
        SpanMeshValue = Mesh; ModelStartValue = Vector3.zero;
        ModelEndValue = Vector3.forward * Length; ModelUpValue = Vector3.up;
        BundleSpanVersionValue = SpanNormalizationVersion;
    }
#else
    internal PropInteractionRuntime? Binding;
#endif
}
