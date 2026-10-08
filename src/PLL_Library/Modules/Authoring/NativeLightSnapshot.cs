using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

internal sealed class NativeLightSnapshot
{
    NativeLightSnapshot(Light Source)
    {
        LocalPosition = Source.transform.localPosition;
        LocalRotation = Source.transform.localRotation;
        LocalScale = Source.transform.localScale;
        Active = Source.gameObject.activeSelf;
        Enabled = Source.enabled;
        Type = Source.type;
        Color = Source.color;
        ColorTemperature = Source.colorTemperature;
        UseColorTemperature = Source.useColorTemperature;
        Intensity = Source.intensity;
        BounceIntensity = Source.bounceIntensity;
        Range = Source.range;
        SpotAngle = Source.spotAngle;
        InnerSpotAngle = Source.innerSpotAngle;
        Shadows = Source.shadows;
        ShadowStrength = Source.shadowStrength;
        ShadowResolution = Source.shadowResolution;
        ShadowCustomResolution = Source.shadowCustomResolution;
        ShadowBias = Source.shadowBias;
        ShadowNormalBias = Source.shadowNormalBias;
        ShadowNearPlane = Source.shadowNearPlane;
        CullingMask = Source.cullingMask;
        RenderingLayerMask = Source.renderingLayerMask;
        RenderMode = Source.renderMode;
        Flare = Source.flare;
        Cookie = Source.cookie;
        CookieSize = Source.cookieSize2D;
        Validate();
    }

    Vector3 LocalPosition { get; }
    Quaternion LocalRotation { get; }
    Vector3 LocalScale { get; }
    bool Active { get; }
    bool Enabled { get; }
    LightType Type { get; }
    Color Color { get; }
    float ColorTemperature { get; }
    bool UseColorTemperature { get; }
    float Intensity { get; }
    float BounceIntensity { get; }
    float Range { get; }
    float SpotAngle { get; }
    float InnerSpotAngle { get; }
    LightShadows Shadows { get; }
    float ShadowStrength { get; }
    LightShadowResolution ShadowResolution { get; }
    int ShadowCustomResolution { get; }
    float ShadowBias { get; }
    float ShadowNormalBias { get; }
    float ShadowNearPlane { get; }
    int CullingMask { get; }
    int RenderingLayerMask { get; }
    LightRenderMode RenderMode { get; }
    UnityEngine.Flare? Flare { get; }
    Texture? Cookie { get; }
    Vector2 CookieSize { get; }

    internal static NativeLightSnapshot Capture(Light Source) =>
        new(Source ? Source : throw new ArgumentNullException(nameof(Source)));

    internal void CopyTo(Light Target)
    {
        if (!Target) throw new ArgumentNullException(nameof(Target));
        Target.gameObject.SetActive(false);
        Target.enabled = false;
        Target.transform.localPosition = LocalPosition;
        Target.transform.localRotation = LocalRotation;
        Target.transform.localScale = LocalScale;
        Target.type = Type;
        Target.color = Color;
        Target.colorTemperature = ColorTemperature;
        Target.useColorTemperature = UseColorTemperature;
        Target.intensity = Intensity;
        Target.bounceIntensity = BounceIntensity;
        Target.range = Range;
        Target.spotAngle = SpotAngle;
        Target.innerSpotAngle = InnerSpotAngle;
        Target.shadows = Shadows;
        Target.shadowStrength = ShadowStrength;
        Target.shadowResolution = ShadowResolution;
        Target.shadowCustomResolution = ShadowCustomResolution;
        Target.shadowBias = ShadowBias;
        Target.shadowNormalBias = ShadowNormalBias;
        Target.shadowNearPlane = ShadowNearPlane;
        Target.cullingMask = CullingMask;
        Target.renderingLayerMask = RenderingLayerMask;
        Target.renderMode = RenderMode;
        Target.flare = Flare;
        Target.cookie = Cookie;
        Target.cookieSize2D = CookieSize;
        Target.enabled = Enabled;
        Target.gameObject.SetActive(Active);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        ManifestEncoding.WriteVector(Writer, LocalPosition);
        ManifestEncoding.WriteScalar(Writer, LocalRotation.x); ManifestEncoding.WriteScalar(Writer, LocalRotation.y);
        ManifestEncoding.WriteScalar(Writer, LocalRotation.z); ManifestEncoding.WriteScalar(Writer, LocalRotation.w);
        ManifestEncoding.WriteVector(Writer, LocalScale);
        Writer.Write(Active); Writer.Write(Enabled); Writer.Write((int)Type);
        ManifestEncoding.WriteColor(Writer, Color);
        ManifestEncoding.WriteScalar(Writer, ColorTemperature);
        Writer.Write(UseColorTemperature);
        ManifestEncoding.WriteScalar(Writer, Intensity); ManifestEncoding.WriteScalar(Writer, BounceIntensity);
        ManifestEncoding.WriteScalar(Writer, Range); ManifestEncoding.WriteScalar(Writer, SpotAngle);
        ManifestEncoding.WriteScalar(Writer, InnerSpotAngle); Writer.Write((int)Shadows);
        ManifestEncoding.WriteScalar(Writer, ShadowStrength); Writer.Write((int)ShadowResolution);
        Writer.Write(ShadowCustomResolution); ManifestEncoding.WriteScalar(Writer, ShadowBias);
        ManifestEncoding.WriteScalar(Writer, ShadowNormalBias); ManifestEncoding.WriteScalar(Writer, ShadowNearPlane);
        Writer.Write(CullingMask); Writer.Write(RenderingLayerMask); Writer.Write((int)RenderMode);
        WriteResource(Writer, Flare); WriteResource(Writer, Cookie);
        ManifestEncoding.WriteScalar(Writer, CookieSize.x); ManifestEncoding.WriteScalar(Writer, CookieSize.y);
    }

    void Validate()
    {
        if (!UnityValueContract.Finite(LocalPosition)
            || !UnityValueContract.Finite(LocalRotation)
            || LocalRotation.x == 0f && LocalRotation.y == 0f
                && LocalRotation.z == 0f && LocalRotation.w == 0f
            || !UnityValueContract.Finite(LocalScale)
            || !UnityValueContract.Finite(Color)
            || !float.IsFinite(ColorTemperature) || ColorTemperature < 0f
            || !FiniteNonnegative(Intensity) || !FiniteNonnegative(BounceIntensity)
            || !FiniteNonnegative(Range) || !float.IsFinite(SpotAngle) || SpotAngle < 0f || SpotAngle > 179f
            || !float.IsFinite(InnerSpotAngle) || InnerSpotAngle < 0f || InnerSpotAngle > SpotAngle
            || !float.IsFinite(ShadowStrength) || ShadowStrength < 0f || ShadowStrength > 1f
            || ShadowCustomResolution < -1 || !FiniteNonnegative(ShadowBias)
            || !FiniteNonnegative(ShadowNormalBias) || !FiniteNonnegative(ShadowNearPlane)
            || !FiniteNonnegative(CookieSize.x) || !FiniteNonnegative(CookieSize.y))
            throw new InvalidOperationException("A native SpecialDay Light has invalid presentation values.");
    }

    static void WriteResource(BinaryWriter Writer, UnityEngine.Object? Value)
    {
        Writer.Write((bool)Value);
        if (!Value) return;
        var Fingerprint = VisualFingerprintWriter.HashSerializedObject(Value, "PLL/NativeSpecialDayLightResource/v1");
        Writer.Write(Fingerprint.Length);
        Writer.Write(Fingerprint);
    }

    static bool FiniteNonnegative(float Value) => Value >= 0f && float.IsFinite(Value);
}