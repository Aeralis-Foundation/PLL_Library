using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeMaterialContract
{
    internal readonly PllNativeMaterialSelection Selection;
    readonly byte[] PreviewFingerprint;
    readonly ComponentBinding<Renderer> Renderer;
    Material? Prepared;
    byte[]? ExternalFingerprint;

    NativeMaterialContract(AuthoringBindingOwner Owner,
        ComponentBinding<PllNativeMaterialAuthoring> Binding, PllNativeMaterialSelection Selection)
    {
        this.Owner = Owner; this.Binding = Binding; this.Selection = Selection;
        Renderer = ComponentBinding<Renderer>.Create(Owner.BindingRoot, Selection.Renderer);
        PreviewFingerprint = VisualFingerprintWriter.HashSerializedObject(Selection.Preview, "PLL/NativeMaterialPreview/v1");
    }

    internal AuthoringBindingOwner Owner { get; }
    internal ComponentBinding<PllNativeMaterialAuthoring> Binding { get; }
    internal bool Generated => Owner.Generated;
    internal int Slot => Selection.Slot;
    internal Material Material => Prepared ?? throw new InvalidOperationException("Native Material is not prepared.");

    internal static NativeMaterialContract[] Capture(Transform Template, AuthoringCandidateGraph Graph, BiomeId Id)
    {
        var Sources = AuthoringBindingOwner.GraphComponents<PllNativeMaterialAuthoring>(Template, Id, Graph, "native material");
        var Result = new NativeMaterialContract[Sources.Count];
        var Slots = new HashSet<(Renderer, int)>();
        for (var Index = 0; Index < Result.Length; Index++)
        {
            var Source = Sources[Index]; var Selection = Source.Value.Capture();
            if (!Slots.Add((Selection.Renderer, Selection.Slot)))
                throw new ArgumentException("Native Material assigns a renderer slot more than once.");
            Result[Index] = new NativeMaterialContract(Source.Owner,
                ComponentBinding<PllNativeMaterialAuthoring>.Create(Source.Owner.BindingRoot, Source.Value), Selection);
        }
        return Result;
    }

    internal void ResolveExternal(NativeMaterialCatalog Catalog)
    {
        var Source = Selection.NativeMaterial.Length == 0 ? Selection.Preview : Catalog.Require(Selection.NativeMaterial);
        var Shader = NativeFogRenderRuntime.CanonicalShader(Source.shader);
        var Textures = new Texture[Selection.Textures.Count];
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            if (Selection.NativeMaterial.Length != 0) Writer.Write(Catalog.Fingerprint(Source));
            for (var Index = 0; Index < Textures.Length; Index++)
            {
                var Reference = Selection.Textures[Index];
                var TargetProperty = PllNativeMaterialSelection.RequireTextureProperty(Shader, Reference.Property);
                var Native = Catalog.Require(Reference.SourceMaterial);
                PllNativeMaterialSelection.RequireTextureProperty(Native.shader, Reference.SourceProperty);
                var Texture = Native.GetTexture(Reference.SourceProperty);
                if (!Texture)
                    throw new ArgumentException($"PEAK material '{Reference.SourceMaterial}' has no texture assigned to '{Reference.SourceProperty}'.");
                if (Texture.dimension != Shader.GetPropertyTextureDimension(TargetProperty))
                    throw new ArgumentException($"Native texture '{Reference.SourceMaterial}/{Reference.SourceProperty}' does not match the target texture dimension.");
                Textures[Index] = Texture; Writer.Write(Catalog.Fingerprint(Native));
            }
        }
        var Owned = new Material(Source) { name = "PLL Native Material", shader = Shader, renderQueue = Source.renderQueue };
        try
        {
            for (var Index = 0; Index < Textures.Length; Index++)
                Owned.SetTexture(Selection.Textures[Index].Property, Textures[Index]);
            using var Sha = System.Security.Cryptography.SHA256.Create();
            ExternalFingerprint = Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
            Prepared = Owned;
        }
        catch { UnityEngine.Object.Destroy(Owned); throw; }
    }

    internal void ClearExternal()
    {
        if (Prepared) UnityEngine.Object.Destroy(Prepared);
        Prepared = null; ExternalFingerprint = null;
    }

    internal Renderer Resolve(Transform Root, BiomeId Id) => Renderer.Resolve(Root, Id, "native material renderer");
    internal bool TryMatchGenerated(PllNativeMaterialAuthoring Source, BiomeId Id, out Transform Root) =>
        Binding.TryMatchGenerated(Owner, Source, Id, "native material", out Root);

    internal void Validate(BiomeId Id)
    {
        Binding.Validate(Owner.BindingRoot, Id, "native material");
        Renderer.Validate(Owner.BindingRoot, Id, "native material renderer");
        var Current = Binding.Source.Capture();
        if (Current.Renderer != Selection.Renderer || Current.Preview != Selection.Preview
            || !Current.HasSameReferences(Selection)
            || !VisualFingerprintWriter.HashSerializedObject(Current.Preview, "PLL/NativeMaterialPreview/v1").AsSpan().SequenceEqual(PreviewFingerprint))
            throw new InvalidOperationException("Native Material changed before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer, bool External)
    {
        Owner.WriteManifest(Writer); Binding.WriteManifest(Writer); Renderer.WriteManifest(Writer);
        Selection.WriteManifest(Writer); Writer.Write(PreviewFingerprint);
        if (External) Writer.Write(ExternalFingerprint ?? throw new InvalidOperationException("Native Material is not sealed."));
    }
}
