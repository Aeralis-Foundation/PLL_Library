using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class AtmosphereAuthoringContract
{
    const string MaterialDomain = "PLL/Atmosphere/Material/v1";
    const string ShaderDomain = "PLL/Atmosphere/Shader/v1";
    const string TextureDomain = "PLL/Atmosphere/Texture/v1";

    readonly ComponentBinding<PllAtmosphereAuthoring> Component;
    readonly Material? SkyboxMaterialValue;
    readonly Texture2D? FogTextureValue;
    readonly byte[]? SkyboxMaterialHash;
    readonly byte[]? SkyboxShaderHash;
    readonly byte[]? FogTextureHash;

    AtmosphereAuthoringContract(
        ComponentBinding<PllAtmosphereAuthoring> Component,
        Material? SkyboxMaterialValue,
        Texture2D? FogTextureValue)
    {
        this.Component = Component;
        this.SkyboxMaterialValue = SkyboxMaterialValue;
        this.FogTextureValue = FogTextureValue;
        SkyboxMaterialHash = SkyboxMaterialValue
            ? VisualFingerprintWriter.HashSerializedObject(SkyboxMaterialValue, MaterialDomain)
            : null;
        SkyboxShaderHash = SkyboxMaterialValue
            ? VisualFingerprintWriter.HashSerializedObject(SkyboxMaterialValue.shader, ShaderDomain)
            : null;
        FogTextureHash = FogTextureValue
            ? VisualFingerprintWriter.HashSerializedObject(FogTextureValue, TextureDomain)
            : null;
    }

    internal static AtmosphereAuthoringContract? CreateOptional(
        Transform Template,
        AuthoringCandidateGraph Candidates,
        BiomeId Id)
    {
        foreach (var Candidate in Candidates.Ordered)
            if (Candidate.GetComponentsInChildren<PllAtmosphereAuthoring>(true).Length != 0)
                throw new ArgumentException(
                    $"Biome '{Id}' places atmosphere authoring inside a generated candidate.",
                    nameof(Template));
        var Values = Template.GetComponentsInChildren<PllAtmosphereAuthoring>(true);
        if (Values.Length == 0) return null;
        if (Values.Length != 1 || Values[0].gameObject != Template.gameObject
            || Template.GetComponents<PllAtmosphereAuthoring>().Length != 1)
            throw new ArgumentException(
                $"Biome '{Id}' must place at most one atmosphere authoring component directly on its root.",
                nameof(Template));
        ValidateAssets(Values[0], Id, true);
        return new AtmosphereAuthoringContract(
            ComponentBinding<PllAtmosphereAuthoring>.Create(Template, Values[0]),
            Values[0].SkyboxMaterial,
            Values[0].FogTexture);
    }

    internal ResolvedAtmosphere Resolve(Transform Root, BiomeId Id)
    {
        var Authoring = Component.Resolve(Root, Id, "atmosphere authoring");
        if (Authoring.gameObject != Root.gameObject
            || !ReferenceEquals(Authoring.SkyboxMaterial, SkyboxMaterialValue)
            || !ReferenceEquals(Authoring.FogTexture, FogTextureValue))
            throw new InvalidOperationException($"Biome '{Id}' changed its atmosphere after sealing.");
        return new ResolvedAtmosphere(SkyboxMaterialValue, FogTextureValue);
    }

    internal void Validate(Transform Template, BiomeId Id)
    {
        Component.Validate(Template, Id, "atmosphere authoring");
        if (Component.Source.gameObject != Template.gameObject
            || Template.GetComponentsInChildren<PllAtmosphereAuthoring>(true).Length != 1
            || Template.GetComponents<PllAtmosphereAuthoring>().Length != 1)
            throw new InvalidOperationException(
                $"Biome '{Id}' changed its atmosphere authoring ownership before sealing.");
        ValidateAssets(Component.Source, Id, false);
        if (!ReferenceEquals(Component.Source.SkyboxMaterial, SkyboxMaterialValue)
            || !ReferenceEquals(Component.Source.FogTexture, FogTextureValue))
            throw new InvalidOperationException(
                $"Biome '{Id}' changed its atmosphere assets before sealing.");
        ValidateHashes(Id);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Component.WriteManifest(Writer);
        WriteAsset(Writer, SkyboxMaterialValue, SkyboxMaterialHash);
        WriteAsset(Writer, SkyboxMaterialValue ? SkyboxMaterialValue.shader : null, SkyboxShaderHash);
        WriteAsset(Writer, FogTextureValue, FogTextureHash);
    }

    void ValidateHashes(BiomeId Id)
    {
        if ((!SkyboxMaterialValue || DeterministicObjectIdentity.SameHash(
                VisualFingerprintWriter.HashSerializedObject(SkyboxMaterialValue, MaterialDomain),
                SkyboxMaterialHash!))
            && (!SkyboxMaterialValue || DeterministicObjectIdentity.SameHash(
                VisualFingerprintWriter.HashSerializedObject(SkyboxMaterialValue.shader, ShaderDomain),
                SkyboxShaderHash!))
            && (!FogTextureValue || DeterministicObjectIdentity.SameHash(
                VisualFingerprintWriter.HashSerializedObject(FogTextureValue, TextureDomain),
                FogTextureHash!))) return;
        throw new InvalidOperationException($"Biome '{Id}' changed its atmosphere asset content before sealing.");
    }

    static void ValidateAssets(PllAtmosphereAuthoring Source, BiomeId Id, bool Argument)
    {
        var SkyboxMaterial = Source.SkyboxMaterial;
        var FogTexture = Source.FogTexture;
        if (PllAuthoringValueContract.DestroyedReference(SkyboxMaterial)
            || PllAuthoringValueContract.DestroyedReference(FogTexture)
            || !SkyboxMaterial && !FogTexture
            || SkyboxMaterial && !SkyboxMaterial.shader)
        {
            if (Argument)
                throw new ArgumentException(
                    $"Biome '{Id}' atmosphere requires a live skybox material or fog texture, and every material requires a live shader.",
                    "template");
            throw new InvalidOperationException($"Biome '{Id}' changed its atmosphere assets before sealing.");
        }
    }

    static void WriteAsset(BinaryWriter Writer, UnityEngine.Object? Value, byte[]? Hash)
    {
        Writer.Write(Value ? true : false);
        if (!Value) return;
        Writer.Write(DeterministicObjectIdentity.StableTypeName(Value.GetType()));
        Writer.Write(Value.name ?? string.Empty);
        Writer.Write(Hash!.Length);
        Writer.Write(Hash);
    }

}

internal sealed class ResolvedAtmosphere
{
    internal ResolvedAtmosphere(Material? SkyboxMaterial, Texture2D? FogTexture)
    {
        this.SkyboxMaterial = SkyboxMaterial;
        this.FogTexture = FogTexture;
    }

    internal Material? SkyboxMaterial { get; }
    internal Texture2D? FogTexture { get; }
}
