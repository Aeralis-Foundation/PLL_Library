using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeSpecialDaySnapshot
{
    readonly struct ShaderValue
    {
        internal ShaderValue(DayNightManager.ShaderParams Parameter, float Value)
        {
            this.Parameter = Parameter;
            this.Value = Value;
        }

        internal DayNightManager.ShaderParams Parameter { get; }
        internal float Value { get; }
    }

    readonly string StructuralPath;
    readonly ShaderValue[] ShaderValues;

    NativeSpecialDaySnapshot(
        string StructuralPath,
        SpecialDayZone Source,
        ShaderValue[] ShaderValues,
        NativeLightSnapshot? LightSnapshot)
    {
        this.StructuralPath = StructuralPath;
        OverrideSun = Source.overrideSun;
        UseCustomSun = Source.useCustomSun;
        var CapturesSun = OverrideSun || UseCustomSun;
        DayLightIntensity = CapturesSun ? Source.daylLightIntensity : 0f;
        NightLightIntensity = CapturesSun ? Source.nightLightIntensity : 0f;
        SpecialSunColor = CapturesSun ? Source.specialSunColor : default;
        UseCustomColorValues = Source.useCustomColorVals;
        SpecialTopColor = UseCustomColorValues ? Source.specialTopColor : default;
        SpecialMidColor = UseCustomColorValues ? Source.specialMidColor : default;
        SpecialBottomColor = UseCustomColorValues ? Source.specialBottomColor : default;
        this.ShaderValues = ShaderValues;
        OverrideFog = Source.overrideFog;
        FogDensity = OverrideFog ? Source.fogDensity : 0f;
        HideLensFlare = Source.hideLensFlare;
        Light = LightSnapshot;
    }

    internal bool OverrideSun { get; }
    internal float DayLightIntensity { get; }
    internal float NightLightIntensity { get; }
    internal Color SpecialSunColor { get; }
    internal bool UseCustomSun { get; }
    internal bool UseCustomColorValues { get; }
    internal Color SpecialTopColor { get; }
    internal Color SpecialMidColor { get; }
    internal Color SpecialBottomColor { get; }
    internal bool OverrideFog { get; }
    internal float FogDensity { get; }
    internal bool HideLensFlare { get; }
    internal NativeLightSnapshot? Light { get; }

    internal static NativeSpecialDaySnapshot Capture(SpecialDayZone Source, string StructuralPath)
    {
        if ((Source.overrideSun || Source.useCustomSun)
                && (!float.IsFinite(Source.daylLightIntensity) || !float.IsFinite(Source.nightLightIntensity)
                    || !UnityValueContract.Finite(Source.specialSunColor))
            || Source.useCustomColorVals
                && (!UnityValueContract.Finite(Source.specialTopColor)
                    || !UnityValueContract.Finite(Source.specialMidColor)
                    || !UnityValueContract.Finite(Source.specialBottomColor))
            || Source.overrideFog && !float.IsFinite(Source.fogDensity))
            throw new InvalidOperationException("A native SpecialDay presentation contains a non-finite value.");
        var Values = Source.globalShaderVals ?? Array.Empty<ShaderVal>();
        var ShaderValues = new ShaderValue[Values.Length];
        for (var Index = 0; Index < Values.Length; Index++)
        {
            if (Values[Index] is null || !Enum.IsDefined(typeof(DayNightManager.ShaderParams), Values[Index].parameter)
                || !float.IsFinite(Values[Index].value))
                throw new InvalidOperationException("A native SpecialDay presentation has an invalid global shader value.");
            ShaderValues[Index] = new ShaderValue(Values[Index].parameter, Values[Index].value);
        }
        return new NativeSpecialDaySnapshot(
            StructuralPath, Source, ShaderValues,
            Source.useCustomSun && Source.specialLight ? NativeLightSnapshot.Capture(Source.specialLight) : null);
    }

    internal Light? ApplyTo(SpecialDayZone Zone, Transform AdapterRoot)
    {
        if (!Zone) throw new ArgumentNullException(nameof(Zone));
        if (!AdapterRoot) throw new ArgumentNullException(nameof(AdapterRoot));
        Zone.overrideSun = OverrideSun;
        Zone.daylLightIntensity = DayLightIntensity;
        Zone.nightLightIntensity = NightLightIntensity;
        Zone.specialSunColor = SpecialSunColor;
        Zone.useCustomSun = UseCustomSun;
        Zone.useCustomColorVals = UseCustomColorValues;
        Zone.specialTopColor = SpecialTopColor;
        Zone.specialMidColor = SpecialMidColor;
        Zone.specialBottomColor = SpecialBottomColor;
        var Values = new ShaderVal[ShaderValues.Length];
        for (var Index = 0; Index < Values.Length; Index++)
            Values[Index] = new ShaderVal { parameter = ShaderValues[Index].Parameter, value = ShaderValues[Index].Value };
        Zone.globalShaderVals = Values;
        Zone.overrideFog = OverrideFog;
        Zone.fogDensity = FogDensity;
        Zone.hideLensFlare = HideLensFlare;
        var OwnedLight = FindOwnedLight(AdapterRoot);
        if (Light is null)
        {
            if (OwnedLight) { OwnedLight.enabled = false; OwnedLight.gameObject.SetActive(false); }
            Zone.specialLight = null;
            return null;
        }
        if (!OwnedLight)
        {
            var Owner = new GameObject("__PLL_NativeSpecialLight");
            Owner.transform.SetParent(AdapterRoot, false);
            OwnedLight = Owner.AddComponent<Light>();
        }
        Light.CopyTo(OwnedLight);
        Zone.specialLight = OwnedLight;
        return OwnedLight;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(StructuralPath);
        Writer.Write(OverrideSun);
        ManifestEncoding.WriteScalar(Writer, DayLightIntensity);
        ManifestEncoding.WriteScalar(Writer, NightLightIntensity);
        ManifestEncoding.WriteColor(Writer, SpecialSunColor);
        Writer.Write(UseCustomSun);
        Writer.Write(UseCustomColorValues);
        ManifestEncoding.WriteColor(Writer, SpecialTopColor); ManifestEncoding.WriteColor(Writer, SpecialMidColor);
        ManifestEncoding.WriteColor(Writer, SpecialBottomColor);
        Writer.Write(ShaderValues.Length);
        foreach (var Value in ShaderValues)
        {
            Writer.Write((int)Value.Parameter);
            ManifestEncoding.WriteScalar(Writer, Value.Value);
        }
        Writer.Write(OverrideFog);
        ManifestEncoding.WriteScalar(Writer, FogDensity);
        Writer.Write(HideLensFlare);
        Writer.Write(Light is not null);
        Light?.WriteManifest(Writer);
    }

    static Light? FindOwnedLight(Transform Root)
    {
        Light? Result = null;
        foreach (var Light in Root.GetComponentsInChildren<Light>(true))
        {
            if (!Light || Light.transform.parent != Root || Light.transform.childCount != 0
                || Light.GetComponents<Component>().Length != 2)
                throw new InvalidOperationException("The native SpecialDay adapter contains a foreign Light shape.");
            if (Result) throw new InvalidOperationException("The native SpecialDay adapter contains more than one Light.");
            Result = Light;
        }
        return Result;
    }
}
