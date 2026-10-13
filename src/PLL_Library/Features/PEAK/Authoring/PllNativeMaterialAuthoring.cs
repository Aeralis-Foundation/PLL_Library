using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

[Serializable]
public struct PllNativeTextureReference
{
    public string Property;
    public string SourceMaterial;
    public string SourceProperty;
}

[AddComponentMenu("PLL/Visuals/Native Material")]
public sealed class PllNativeMaterialAuthoring : MonoBehaviour
#if !PLL_CREATOR_SDK
    , ISerializationCallbackReceiver
#endif
{
    const int TextureReferenceWidth = 3;
    public int MaterialSlot;
    public string NativeMaterial = "";
    public bool UsePreviewProperties;
    public PllNativeTextureReference[] Textures = Array.Empty<PllNativeTextureReference>();
    [SerializeField, HideInInspector] string[]? BundleTextureReferences = null;

#if PLL_CREATOR_SDK
    internal void SealBundleSerialization()
    {
        var Encoded = new string[Textures.Length * TextureReferenceWidth];
        for (var i = 0; i < Textures.Length; i++)
        {
            Encoded[i * TextureReferenceWidth] = Textures[i].Property;
            Encoded[i * TextureReferenceWidth + 1] = Textures[i].SourceMaterial;
            Encoded[i * TextureReferenceWidth + 2] = Textures[i].SourceProperty;
        }
        BundleTextureReferences = Encoded;
    }
#else
    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (BundleTextureReferences is not { } Encoded) return;
        if (Encoded.Length % TextureReferenceWidth != 0)
            throw new InvalidOperationException("Compiled native texture references contain an incomplete entry.");
        var Decoded = new PllNativeTextureReference[Encoded.Length / TextureReferenceWidth];
        for (var i = 0; i < Decoded.Length; i++)
            Decoded[i] = new PllNativeTextureReference
            {
                Property = Encoded[i * TextureReferenceWidth],
                SourceMaterial = Encoded[i * TextureReferenceWidth + 1],
                SourceProperty = Encoded[i * TextureReferenceWidth + 2]
            };
        Textures = Decoded;
    }
#endif

    public PllNativeMaterialSelection Capture()
    {
        var Renderers = GetComponents<Renderer>();
        if (Renderers.Length != 1)
            throw new ArgumentException("Native Material needs one renderer on its GameObject.");
        var Materials = Renderers[0].sharedMaterials;
        if (MaterialSlot < 0 || MaterialSlot >= Materials.Length || !Materials[MaterialSlot])
            throw new ArgumentException("Native Material needs an existing material slot.");
        if (NativeMaterial is null || Textures is null
            || NativeMaterial.Length == 0 && Textures.Length == 0)
            throw new ArgumentException($"Native Material '{name}' needs a native material name or texture references.");
        if (!string.Equals(NativeMaterial, NativeMaterial.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Native material names must not have surrounding whitespace.");
        var Properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var Texture in Textures)
        {
            if (string.IsNullOrWhiteSpace(Texture.Property)
                || string.IsNullOrWhiteSpace(Texture.SourceMaterial)
                || string.IsNullOrWhiteSpace(Texture.SourceProperty)
                || Texture.Property != Texture.Property.Trim()
                || Texture.SourceMaterial != Texture.SourceMaterial.Trim()
                || Texture.SourceProperty != Texture.SourceProperty.Trim())
                throw new ArgumentException("Native texture references need exact property and material names.");
            if (!Properties.Add(Texture.Property))
                throw new ArgumentException("Native texture target properties must be unique.");
            if (NativeMaterial.Length == 0)
                PllNativeMaterialSelection.RequireTextureProperty(Materials[MaterialSlot].shader, Texture.Property);
        }
        return new PllNativeMaterialSelection(Renderers[0], MaterialSlot,
            Materials[MaterialSlot], NativeMaterial, (PllNativeTextureReference[])Textures.Clone(), UsePreviewProperties);
    }
}

public sealed class PllNativeMaterialSelection
{
    internal PllNativeMaterialSelection(Renderer Renderer, int Slot, Material Preview,
        string NativeMaterial, PllNativeTextureReference[] Textures, bool UsePreviewProperties)
    {
        this.Renderer = Renderer; this.Slot = Slot; this.Preview = Preview;
        this.NativeMaterial = NativeMaterial; this.Textures = Array.AsReadOnly(Textures);
        this.UsePreviewProperties = UsePreviewProperties;
    }

    public Renderer Renderer { get; }
    public int Slot { get; }
    public Material Preview { get; }
    public string NativeMaterial { get; }
    public bool UsePreviewProperties { get; }
    public IReadOnlyList<PllNativeTextureReference> Textures { get; }

    public bool HasSameReferences(PllNativeMaterialSelection Other)
    {
        if (Slot != Other.Slot || NativeMaterial != Other.NativeMaterial
            || UsePreviewProperties != Other.UsePreviewProperties || Textures.Count != Other.Textures.Count) return false;
        for (var Index = 0; Index < Textures.Count; Index++)
        {
            var Left = Textures[Index]; var Right = Other.Textures[Index];
            if (Left.Property != Right.Property || Left.SourceMaterial != Right.SourceMaterial
                || Left.SourceProperty != Right.SourceProperty) return false;
        }
        return true;
    }

    public static int RequireTextureProperty(Shader Shader, string Property)
    {
        var Index = Shader.FindPropertyIndex(Property);
        if (Index < 0 || Shader.GetPropertyType(Index) != ShaderPropertyType.Texture)
            throw new ArgumentException($"Shader '{Shader.name}' has no texture property '{Property}'.");
        return Index;
    }

    public void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Slot); Writer.Write(NativeMaterial); Writer.Write(UsePreviewProperties); Writer.Write(Textures.Count);
        foreach (var Texture in Textures)
        {
            Writer.Write(Texture.Property); Writer.Write(Texture.SourceMaterial);
            Writer.Write(Texture.SourceProperty);
        }
    }
}
