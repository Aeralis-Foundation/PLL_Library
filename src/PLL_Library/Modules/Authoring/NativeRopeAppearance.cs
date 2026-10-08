using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

internal sealed class NativeRopeAppearance
{
    readonly PllNativeEntityAppearance Appearance;
    readonly NativeEntityAppearance Prepared;
    readonly Material Ghost;
    readonly byte[] GhostFingerprint;
    TransformBinding? Renderer;
    TransformBinding[]? Bones;
    NativeHangingRopeSource? Native;
    Action<Material>[]? RopeOverrides, GhostOverrides;

    NativeRopeAppearance(PllHangingRopeAuthoring Source)
    {
        Appearance = new PllNativeEntityAppearance
        {
            NativeDescriptorFingerprint = Source.RopeNativeRigFingerprint,
            Renderers = new[] { new PllNativeEntityRenderer
            {
                RendererPath = Source.RopeRendererPath, Mesh = Source.RopeMesh,
                BonePaths = (string[])Source.RopeBonePaths.Clone(),
                Materials = new[] { Source.RopeMaterial }, LocalBounds = Source.RopeBounds
            } }
        };
        Ghost = Source.RopeGhostMaterial;
        if (Appearance.NativeDescriptorFingerprint.Length == 0)
            throw new ArgumentException("Custom Rope needs its captured native rig fingerprint.", nameof(Source));
        Prepared = NativeEntityAppearance.Create(Appearance)!;
        GhostFingerprint = VisualFingerprintWriter.HashSerializedObject(Ghost, "PLL/RopeGhostAppearance/v1");
    }

    internal static NativeRopeAppearance? Create(PllHangingRopeAuthoring Source) =>
        Source.RopeAppearance == PllRopeAppearance.Native ? null : new(Source);

    internal void Seal(NativeHangingRopeSource Native)
    {
        if (Appearance.NativeDescriptorFingerprint != Native.SkinDescriptor.DescriptorFingerprint)
            throw new ArgumentException("Custom Rope does not match the captured native rig fingerprint.");
        var Replacement = Appearance.Renderers[0];
        var Skin = Native.SkinDescriptor.Require(Replacement.RendererPath);
        if (Skin.BlendShapeCount != 0)
            throw new ArgumentException("Custom Rope cannot replace a native blend-shape renderer.");
        var Bindposes = Replacement.Mesh.bindposes;
        var Bindings = new TransformBinding[Replacement.BonePaths.Length];
        for (var i = 0; i < Bindings.Length; i++)
        {
            var Slot = Array.IndexOf(Skin.BonePaths, Replacement.BonePaths[i]);
            if (Slot < 0 || !PllNativeEntityAppearanceValueContract.CompatibleBindpose(Bindposes[i], Skin.Bindposes[Slot]))
                throw new ArgumentException("Custom Rope has a bone without a compatible native bindpose.");
            Bindings[i] = Skin.Bones[Slot];
        }
        var RopeOverrides = CaptureOverrides(Replacement.Materials[0], Native.RopeMaterial);
        var GhostOverrides = CaptureOverrides(Ghost, Native.GhostMaterial);
        Renderer = Skin.Renderer; Bones = Bindings;
        this.RopeOverrides = RopeOverrides; this.GhostOverrides = GhostOverrides;
        this.Native = Native;
    }

    internal void Clear() { Renderer = null; Bones = null; Native = null; RopeOverrides = null; GhostOverrides = null; }

    internal bool Matches(PllHangingRopeAuthoring Source)
    {
        var Replacement = Appearance.Renderers[0];
        return Source.RopeAppearance != PllRopeAppearance.Native && Source.RopeMesh == Replacement.Mesh
            && Source.RopeMaterial == Replacement.Materials[0] && Source.RopeGhostMaterial == Ghost
            && Source.RopeRendererPath == Replacement.RendererPath
            && Source.RopeNativeRigFingerprint == Appearance.NativeDescriptorFingerprint
            && Source.RopeBounds.Equals(Replacement.LocalBounds)
            && Source.RopeBonePaths.AsSpan().SequenceEqual(Replacement.BonePaths);
    }

    internal void Apply(GameObject Root)
    {
        var Native = this.Native ?? throw new InvalidOperationException("Custom Rope has no sealed native rig.");
        var Replacement = Appearance.Renderers[0];
        var Renderer = this.Renderer!.Resolve(Root.transform).GetComponent<SkinnedMeshRenderer>();
        var Bones = new Transform[this.Bones!.Length];
        for (var i = 0; i < Bones.Length; i++) Bones[i] = this.Bones[i].Resolve(Root.transform);
        var Visual = Root.GetComponent<RopeBoneVisualizer>();
        foreach (var Apply in RopeOverrides!) Apply(Visual.ropeMaterial);
        foreach (var Apply in GhostOverrides!) Apply(Visual.ghostMaterial);
        Renderer.sharedMesh = Replacement.Mesh; Renderer.bones = Bones;
        var Bounds = Native.VisualBounds; Bounds.Encapsulate(Replacement.LocalBounds);
        Renderer.localBounds = Bounds;
        Renderer.sharedMaterial = Root.GetComponent<Rope>().attachmenState == Rope.ATTACHMENT.inSpool
            ? Visual.ghostMaterial : Visual.ropeMaterial;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Prepared.WriteManifest(Writer);
        Writer.Write(GhostFingerprint.Length); Writer.Write(GhostFingerprint);
    }

    static Action<Material>[] CaptureOverrides(Material Source, Material Native)
    {
        var SourceShader = Source.shader; var TargetShader = Native.shader;
        var Overrides = new Action<Material>[SourceShader.GetPropertyCount()];
        for (var i = 0; i < Overrides.Length; i++)
        {
            var Name = SourceShader.GetPropertyName(i);
            var Target = TargetShader.FindPropertyIndex(Name);
            var Type = SourceShader.GetPropertyType(i);
            if (Target < 0 || Type != TargetShader.GetPropertyType(Target))
                throw new ArgumentException($"Rope material property '{Name}' does not match its native override schema.");
            var Id = Shader.PropertyToID(Name);
            switch (Type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    var Scalar = Source.GetFloat(Id);
                    if (!ManifestEncoding.CanEncode(Scalar))
                        throw new ArgumentException($"Rope material property '{Name}' must be finite.");
                    Overrides[i] = Material => Material.SetFloat(Id, Scalar); break;
                case ShaderPropertyType.Int:
                    var Integer = Source.GetInteger(Id);
                    Overrides[i] = Material => Material.SetInteger(Id, Integer); break;
                case ShaderPropertyType.Color:
                    var Color = Source.GetColor(Id);
                    if (!Finite(new Vector4(Color.r, Color.g, Color.b, Color.a)))
                        throw new ArgumentException($"Rope material property '{Name}' must be finite.");
                    Overrides[i] = Material => Material.SetColor(Id, Color); break;
                case ShaderPropertyType.Vector:
                    var Vector = Source.GetVector(Id);
                    if (!Finite(Vector)) throw new ArgumentException($"Rope material property '{Name}' must be finite.");
                    Overrides[i] = Material => Material.SetVector(Id, Vector); break;
                case ShaderPropertyType.Texture:
                    var Texture = Source.GetTexture(Id);
                    var Dimension = TargetShader.GetPropertyTextureDimension(Target);
                    var Scale = Source.GetTextureScale(Id); var Offset = Source.GetTextureOffset(Id);
                    if (PllAuthoringValueContract.DestroyedReference(Texture)
                        || Texture && Texture.dimension != Dimension
                        || SourceShader.GetPropertyTextureDimension(i) != Dimension
                        || !Finite(new Vector4(Scale.x, Scale.y, Offset.x, Offset.y)))
                        throw new ArgumentException($"Rope texture property '{Name}' has an invalid dimension or UV transform.");
                    Texture = Texture ? Texture : null;
                    Overrides[i] = Material =>
                    {
                        Material.SetTexture(Id, Texture);
                        Material.SetTextureScale(Id, Scale); Material.SetTextureOffset(Id, Offset);
                    };
                    break;
                default: throw new InvalidOperationException("The admitted native rope property type is unsupported.");
            }
        }
        return Overrides;
    }

    static bool Finite(Vector4 Value) => ManifestEncoding.CanEncode(Value.x) && ManifestEncoding.CanEncode(Value.y)
        && ManifestEncoding.CanEncode(Value.z) && ManifestEncoding.CanEncode(Value.w);
}
