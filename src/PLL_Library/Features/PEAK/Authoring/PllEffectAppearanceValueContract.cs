using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

public static class PllEffectAppearanceValueContract
{
    public static void ValidateNativeVisual(PLLNativeVisualResolver Source)
    {
        ValidateParticleColor(Source.ParticleColorMode, Source.ParticleColor, !Source.SuppressNativeParticles);
        ValidateColor(Source.MaterialColor, "Material color");
        if (Source.MaterialColorChannel is not PllEffectMaterialColorChannel.None and not PllEffectMaterialColorChannel.Color)
            throw new InvalidOperationException("Native ambient particles support only the Color material channel.");
        if ((Source.ParticleColorMode != PllEffectParticleColorMode.Original
                || Source.MaterialColorChannel != PllEffectMaterialColorChannel.None)
            && (Source.SuppressNativeParticles || Source.Preset is not PllNativeVisualPreset.Roots and not PllNativeVisualPreset.Caldera))
            throw new InvalidOperationException("Ambient particle appearance requires unsuppressed Roots or Caldera particles.");
    }

    public static Material? Validate(PllEffectAuthoring Source, Transform OwnerRoot)
    {
        if (!Source.Root) throw new InvalidOperationException("The effect root is missing.");
        ValidateNativeParticles(Source.NativeParticles, Source.Particles.Count != 0
            || Source.NativeParticles != PllNativeEffectParticles.None
                && Source.Root.GetComponentInChildren<ParticleSystem>(true), Source.PresentationRoot);
        ValidateParticleColor(Source.ParticleColorMode, Source.ParticleColor,
            Source.Particles.Count != 0 || Source.NativeParticles != PllNativeEffectParticles.None);
        return ValidateMaterialColor(Source.Root, OwnerRoot, Source.NativeParticles, Source.MaterialColorChannel,
            Source.ColorRenderer, Source.ColorMaterialSlot, Source.MaterialColor);
    }

    public static void ValidateNativeParticles(PllNativeEffectParticles Choice, bool HasAuthoredParticles,
        bool HasPresentation)
    {
        if (Choice is not PllNativeEffectParticles.None and not PllNativeEffectParticles.Fire
            and not PllNativeEffectParticles.SmokeOff and not PllNativeEffectParticles.SmokeLit)
            throw new InvalidOperationException("Native effect particles are unsupported.");
        if (Choice != PllNativeEffectParticles.None && (HasAuthoredParticles || HasPresentation))
            throw new InvalidOperationException("Native effect particles cannot share an authored particle or presentation graph.");
    }

    public static void ValidateParticleColor(PllEffectParticleColorMode Mode, Color Color, bool HasParticles)
    {
        if (Mode is not PllEffectParticleColorMode.Original
            and not PllEffectParticleColorMode.Override)
            throw new InvalidOperationException("Particle color mode is unsupported.");
        ValidateColor(Color, "Particle color");
        if (Mode == PllEffectParticleColorMode.Override && !HasParticles)
            throw new InvalidOperationException("Particle color override requires owned effect particles.");
    }

    public static Material? ValidateMaterialColor(GameObject Root, Transform OwnerRoot,
        PllNativeEffectParticles NativeParticles, PllEffectMaterialColorChannel Channel,
        Renderer? Renderer, int MaterialSlot, Color Color)
    {
        var Property = MaterialProperty(Channel);
        ValidateColor(Color, "Material color");
        if (Property.Length == 0)
        {
            if (Renderer || MaterialSlot != 0)
                throw new InvalidOperationException("Material color target requires a color channel.");
            return null;
        }
        if (NativeParticles != PllNativeEffectParticles.None)
        {
            if (Renderer || MaterialSlot != 0 || Channel != PllEffectMaterialColorChannel.Color)
                throw new InvalidOperationException("Native fire and smoke use their owned renderer's Color channel in slot zero.");
            return null;
        }
        return ValidateRendererOwner(Root, OwnerRoot, Renderer, MaterialSlot, Channel);
    }

    public static string MaterialProperty(PllEffectMaterialColorChannel Channel) => Channel switch
    {
        PllEffectMaterialColorChannel.None => "",
        PllEffectMaterialColorChannel.Color => "_Color",
        PllEffectMaterialColorChannel.Tint => "_Tint",
        PllEffectMaterialColorChannel.BaseColor => "_BaseColor",
        _ => throw new InvalidOperationException("Material color channel is unsupported.")
    };

    static Material ValidateRendererOwner(GameObject Root, Transform OwnerRoot, Renderer? Renderer,
        int MaterialSlot, PllEffectMaterialColorChannel Channel)
    {
        if (!Renderer || !Renderer.transform.IsChildOf(Root.transform) && Renderer.gameObject != Root)
            throw new InvalidOperationException("Material color requires a renderer owned by the effect root.");
        var Material = ValidateRenderer(Renderer, MaterialSlot, Channel);
        foreach (var Visual in OwnerRoot.GetComponentsInChildren<PllVisualAuthoring>(true))
            if (Visual.Root && Visual.PropertyName.Length != 0
                && (Renderer.transform.IsChildOf(Visual.Root.transform) || Renderer.gameObject == Visual.Root)
                && (Visual.Material ? Visual.Material == Material : Visual.Shader == Material.shader))
                throw new InvalidOperationException("The color renderer already belongs to a shader-property visual.");
        return Material;
    }

    public static Material ValidateRenderer(Renderer Renderer, int MaterialSlot, PllEffectMaterialColorChannel Channel)
    {
        var Property = MaterialProperty(Channel);
        var Materials = Renderer.sharedMaterials;
        if (Property.Length == 0 || MaterialSlot < 0 || MaterialSlot >= Materials.Length
            || !Materials[MaterialSlot] || !Materials[MaterialSlot].shader)
            throw new InvalidOperationException("Choose an existing material slot and a supported color channel.");
        var Material = Materials[MaterialSlot];
        var Shader = Material.shader;
        var PropertyIndex = Shader.FindPropertyIndex(Property);
        if (PropertyIndex < 0 || Shader.GetPropertyType(PropertyIndex) != ShaderPropertyType.Color)
            throw new InvalidOperationException($"The selected material shader does not declare '{Property}' as a color.");
        return Material;
    }

    public static void ValidateColor(Color Color, string Label)
    {
        if (!PllAuthoringValueContract.CanEncode(Color.r) || !PllAuthoringValueContract.CanEncode(Color.g)
            || !PllAuthoringValueContract.CanEncode(Color.b) || !PllAuthoringValueContract.CanEncode(Color.a)
            || Color.a < 0f || Color.a > 1f)
            throw new InvalidOperationException(Label + " must be finite with alpha from zero to one.");
    }
}
