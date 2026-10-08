using System;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Effects/Fog Zone")]
public sealed class PllFogZoneAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] string StableIdValue = "";
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume volume = new(PllZoneShape.Box, Vector3.zero, new Vector3(20f, 10f, 20f), 10f);
#endif
    [SerializeField, HideInInspector] PllZoneShape BundleShape = default;
    [SerializeField, HideInInspector] Vector3 BundleCenter = default;
    [SerializeField, HideInInspector] Vector3 BundleSize = default;
    [SerializeField, HideInInspector] float BundleRadius = 0f;
    [SerializeField] bool InitiallyEnabledValue = true;
    [SerializeField, Min(0f)] float VisibilityDistanceValue = 10f;
    [SerializeField] Color ColorValue = UnityEngine.Color.white;
    [SerializeField, Min(0f)] float BoundaryBlendDistanceValue = 0f;
    [SerializeField] int PriorityValue = 0;
#if PLL_CREATOR_SDK
    [SerializeField] PllPresentationTransition TransitionValue = default;
#endif
    [SerializeField, HideInInspector] float BundleCoverDelaySeconds = 0f;

    public string StableId => StableIdValue ?? "";
#if PLL_CREATOR_SDK
    public PllZoneVolume Volume => volume;
    internal void SealBundleSerialization()
    {
        BundleShape = volume.Shape; BundleCenter = volume.Center;
        BundleSize = volume.Size; BundleRadius = volume.Radius;
        BundleCoverDelaySeconds = Transition.CoverDelaySeconds;
    }
    public void ValidateBundleSerialization()
    {
        if (!volume.Equals(new PllZoneVolume(BundleShape, BundleCenter, BundleSize, BundleRadius)))
            throw new InvalidOperationException("Fog Zone compiled volume changed.");
        if (!BundleCoverDelaySeconds.Equals(Transition.CoverDelaySeconds))
            throw new InvalidOperationException("Fog Zone compiled Cover Delay changed.");
    }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
#endif
    public bool InitiallyEnabled => InitiallyEnabledValue;
    public float VisibilityDistance => VisibilityDistanceValue;
    public Color Color => new(ColorValue.r, ColorValue.g, ColorValue.b, 1f);
    public float BoundaryBlendDistance => BoundaryBlendDistanceValue;
    public int Priority => PriorityValue;
#if PLL_CREATOR_SDK
    public PllPresentationTransition Transition => TransitionValue;
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif

    public void ValidateSettings()
    {
        if (!Guid.TryParseExact(StableId, "N", out var Id) || Id == Guid.Empty
            || !string.Equals(Id.ToString("N"), StableId, StringComparison.Ordinal))
            throw new ArgumentException("Fog Zone identity must be a nonempty canonical source ID.");
        if (PllAuthoringValueContract.ZoneVolumeFailure(Volume) is { } Failure)
            throw new ArgumentException(Failure.Message);
        if (!PllAuthoringValueContract.CanEncode(VisibilityDistance) || VisibilityDistance <= 0f)
            throw new ArgumentException("Fog Zone visibility distance must be finite positive meters.");
        if (!PllAuthoringValueContract.CanEncode(Color.r) || !PllAuthoringValueContract.CanEncode(Color.g)
            || !PllAuthoringValueContract.CanEncode(Color.b))
            throw new ArgumentException("Fog Zone color must have finite RGB values.");
        if (!PllAuthoringValueContract.CanEncode(BoundaryBlendDistance) || BoundaryBlendDistance < 0f)
            throw new ArgumentException("Fog Zone boundary blend distance must be finite nonnegative meters.");
        if (!PllAuthoringValueContract.CanEncode(Transition.CoverDelaySeconds) || Transition.CoverDelaySeconds < 0f)
            throw new ArgumentException("Cover Delay must be finite nonnegative seconds.");
        ValidateTransform(transform, Volume.Shape);
    }

    public static void ValidateTransform(Transform Root, PllZoneShape Shape)
    {
        var Matrix = Root.localToWorldMatrix;
        Vector3 X = Matrix.GetColumn(0), Y = Matrix.GetColumn(1), Z = Matrix.GetColumn(2);
        Vector3 Position = Matrix.GetColumn(3);
        var Determinant = Matrix.determinant;
        var XLength = X.magnitude; var YLength = Y.magnitude; var ZLength = Z.magnitude;
        if (!float.IsFinite(Determinant) || Determinant == 0f
            || !float.IsFinite(XLength) || !float.IsFinite(YLength) || !float.IsFinite(ZLength)
            || !float.IsFinite(Position.x) || !float.IsFinite(Position.y) || !float.IsFinite(Position.z)
            || XLength <= 0f || YLength <= 0f || ZLength <= 0f)
            throw new ArgumentException("Fog Zone transform must be finite and nonsingular.");
        if (Shape != PllZoneShape.Sphere) return;
        var Scale = Mathf.Max(XLength, Mathf.Max(YLength, ZLength));
        if (Mathf.Abs(XLength - YLength) > Scale * 0.0001f || Mathf.Abs(XLength - ZLength) > Scale * 0.0001f
            || Mathf.Abs(Vector3.Dot(X / XLength, Y / YLength)) > 0.0001f
            || Mathf.Abs(Vector3.Dot(X / XLength, Z / ZLength)) > 0.0001f
            || Mathf.Abs(Vector3.Dot(Y / YLength, Z / ZLength)) > 0.0001f)
            throw new ArgumentException("Sphere Fog Zone requires uniform world scale without shear.");
    }
}
