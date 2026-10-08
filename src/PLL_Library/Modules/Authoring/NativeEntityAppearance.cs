using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeEntitySkinDescriptor
{
    readonly Skin[] Skins;
    readonly byte[] Fingerprint;

    NativeEntitySkinDescriptor(GameObject Prefab, Skin[] Skins)
    {
        this.Prefab = Prefab; this.Skins = Skins;
        Fingerprint = NativeEntityAppearance.Hash(Writer =>
        {
            Writer.Write("PLL/NativeEntitySkin/v2"); Writer.Write(Skins.Length);
            foreach (var Skin in Skins)
            {
                Writer.Write(Skin.Path); Skin.Renderer.WriteManifest(Writer);
                Writer.Write(Skin.RootBonePath);
                Writer.Write(Skin.RootBone is not null); Skin.RootBone?.WriteManifest(Writer);
                Writer.Write(Skin.Bones.Length);
                for (var i = 0; i < Skin.Bones.Length; i++)
                {
                    Writer.Write(Skin.BonePaths[i]); Skin.Bones[i].WriteManifest(Writer);
                    NativeEntityAppearance.WriteMatrix(Writer, Skin.Bindposes[i]);
                }
                Writer.Write(Skin.BlendShapeCount);
            }
        });
    }

    internal GameObject Prefab { get; }
    internal string DescriptorFingerprint => NativeContentArtifact.Hex(Fingerprint);

    internal static NativeEntitySkinDescriptor Capture(GameObject Prefab)
    {
        var Renderers = Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var Skins = new Skin[Renderers.Length];
        for (var i = 0; i < Renderers.Length; i++)
        {
            var Renderer = Renderers[i];
            var Mesh = Renderer.sharedMesh;
            if (Renderer.GetComponents<SkinnedMeshRenderer>().Length != 1 || !Mesh)
                throw new InvalidOperationException("PEAK's native entity skin has an ambiguous renderer or missing mesh.");
            var Bones = Renderer.bones;
            var Bindposes = Mesh.bindposes;
            if (Bones.Length == 0 || Bones.Length != Bindposes.Length)
                throw new InvalidOperationException("PEAK's native entity skin has incomplete bindposes.");
            var Paths = new string[Bones.Length];
            var Bindings = new TransformBinding[Bones.Length];
            for (var j = 0; j < Bones.Length; j++)
            {
                Paths[j] = PllNativeEntityAppearanceValueContract.ReferencePath(Prefab.transform, Bones[j]);
                Bindings[j] = TransformBinding.Create(Prefab.transform, Bones[j]);
            }
            var RootBone = Renderer.rootBone;
            Skins[i] = new Skin(
                PllNativeEntityAppearanceValueContract.ReferencePath(Prefab.transform, Renderer.transform),
                TransformBinding.Create(Prefab.transform, Renderer.transform), Paths, Bindings,
                Bindposes, RootBone ? PllNativeEntityAppearanceValueContract.ReferencePath(Prefab.transform, RootBone) : "", RootBone
                    ? TransformBinding.Create(Prefab.transform, RootBone) : null,
                Mesh.blendShapeCount);
        }
        Array.Sort(Skins, (Left, Right) => StringComparer.Ordinal.Compare(Left.Path, Right.Path));
        return new NativeEntitySkinDescriptor(Prefab, Skins);
    }

    internal Skin Require(string Path)
    {
        foreach (var Skin in Skins)
            if (Skin.Path == Path) return Skin;
        throw new ArgumentException($"Native entity renderer '{Path}' does not exist.", nameof(Path));
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }

    internal NativeContentRig Project(string Actor)
    {
        var Result = new NativeContentSkin[Skins.Length];
        var Transforms = new Dictionary<string, NativeContentTransform>(StringComparer.Ordinal);
        for (var i = 0; i < Skins.Length; i++)
        {
            var Skin = Skins[i];
            Skin.Renderer.ProjectPoses(Skin.Path, Transforms);
            for (var j = 0; j < Skin.Bones.Length; j++)
                Skin.Bones[j].ProjectPoses(Skin.BonePaths[j], Transforms);
            Skin.RootBone?.ProjectPoses(Skin.RootBonePath, Transforms);
            var Bindposes = new float[Skin.Bindposes.Length * 16];
            for (var j = 0; j < Skin.Bindposes.Length; j++)
                for (var k = 0; k < 16; k++) Bindposes[j * 16 + k] = Skin.Bindposes[j][k];
            Result[i] = new NativeContentSkin
            {
                RendererPath = Skin.Path, BonePaths = (string[])Skin.BonePaths.Clone(),
                Bindposes = Bindposes, RootBonePath = Skin.RootBonePath,
                BlendShapeCount = Skin.BlendShapeCount
            };
        }
        var Poses = new NativeContentTransform[Transforms.Count];
        Transforms.Values.CopyTo(Poses, 0);
        Array.Sort(Poses, (Left, Right) => StringComparer.Ordinal.Compare(Left.Path, Right.Path));
        return new NativeContentRig
        {
            Actor = Actor, Fingerprint = DescriptorFingerprint, Skins = Result, Transforms = Poses
        };
    }

    internal sealed class Skin
    {
        internal readonly string Path;
        internal readonly TransformBinding Renderer;
        internal readonly string[] BonePaths;
        internal readonly TransformBinding[] Bones;
        internal readonly Matrix4x4[] Bindposes;
        internal readonly TransformBinding? RootBone;
        internal readonly string RootBonePath;
        internal readonly int BlendShapeCount;

        internal Skin(string Path, TransformBinding Renderer, string[] BonePaths,
            TransformBinding[] Bones, Matrix4x4[] Bindposes, string RootBonePath,
            TransformBinding? RootBone, int BlendShapeCount)
        {
            this.Path = Path; this.Renderer = Renderer; this.BonePaths = BonePaths;
            this.Bones = Bones; this.Bindposes = Bindposes; this.RootBone = RootBone;
            this.RootBonePath = RootBonePath;
            this.BlendShapeCount = BlendShapeCount;
        }
    }
}

internal sealed class NativeEntityAppearance
{
    readonly Replacement[] Replacements;
    readonly byte[] Fingerprint;
    readonly string NativeDescriptorFingerprint;

    NativeEntityAppearance(Replacement[] Replacements, string NativeDescriptorFingerprint)
    {
        this.Replacements = Replacements;
        this.NativeDescriptorFingerprint = NativeDescriptorFingerprint;
        Fingerprint = Hash(Writer =>
        {
            Writer.Write("PLL/NativeEntityAppearance/v2"); Writer.Write(NativeDescriptorFingerprint);
            Writer.Write(Replacements.Length);
            var Meshes = new Dictionary<Mesh, byte[]>();
            var Materials = new Dictionary<Material, byte[]>();
            foreach (var Replacement in Replacements)
            {
                Writer.Write(Replacement.Path); WriteVector(Writer, Replacement.Bounds.center);
                WriteVector(Writer, Replacement.Bounds.extents); Writer.Write(Replacement.Bones.Length);
                foreach (var Bone in Replacement.Bones) Writer.Write(Bone);
                if (!Meshes.TryGetValue(Replacement.Mesh, out var MeshHash))
                {
                    MeshHash = VisualFingerprintWriter.HashEntityAppearanceMesh(Replacement.Mesh);
                    Meshes.Add(Replacement.Mesh, MeshHash);
                }
                Writer.Write(MeshHash.Length); Writer.Write(MeshHash);
                Writer.Write(Replacement.Materials.Length);
                foreach (var Material in Replacement.Materials)
                {
                    if (!Materials.TryGetValue(Material, out var MaterialHash))
                    {
                        MaterialHash = VisualFingerprintWriter.HashSerializedObject(Material, "PLL/EntityAppearanceMaterial/v1");
                        Materials.Add(Material, MaterialHash);
                    }
                    Writer.Write(MaterialHash.Length); Writer.Write(MaterialHash);
                }
            }
        });
    }

    internal static NativeEntityAppearance? Create(PllNativeEntityAppearance? Source)
    {
        if (Source is null) return null;
        if (PllNativeEntityAppearanceValueContract.Failure(Source) is { } Failure)
            throw new ArgumentException(Failure, nameof(Source));
        var Replacements = new Replacement[Source.Renderers.Length];
        for (var i = 0; i < Replacements.Length; i++)
        {
            var Renderer = Source.Renderers[i];
            Replacements[i] = new Replacement(Renderer.RendererPath, Renderer.Mesh,
                (Material[])Renderer.Materials.Clone(), (string[])Renderer.BonePaths.Clone(), Renderer.LocalBounds);
        }
        Array.Sort(Replacements, (Left, Right) => StringComparer.Ordinal.Compare(Left.Path, Right.Path));
        return new NativeEntityAppearance(Replacements, Source.NativeDescriptorFingerprint);
    }

    internal bool Matches(PllNativeEntityAppearance? Source)
    {
        if (Source?.Renderers is not { } Renderers || Renderers.Length != Replacements.Length
            || Source.NativeDescriptorFingerprint != NativeDescriptorFingerprint) return false;
        foreach (var Replacement in Replacements)
        {
            PllNativeEntityRenderer? Match = null;
            foreach (var Renderer in Renderers)
                if (Renderer is not null && Renderer.RendererPath == Replacement.Path)
                {
                    if (Match is not null) return false;
                    Match = Renderer;
                }
            if (Match is null || Match.Mesh != Replacement.Mesh || !Match.LocalBounds.Equals(Replacement.Bounds)
                || Match.Materials is null || Match.Materials.Length != Replacement.Materials.Length
                || Match.BonePaths is null || Match.BonePaths.Length != Replacement.Bones.Length) return false;
            for (var i = 0; i < Match.Materials.Length; i++)
                if (Match.Materials[i] != Replacement.Materials[i]) return false;
            for (var i = 0; i < Match.BonePaths.Length; i++)
                if (Match.BonePaths[i] != Replacement.Bones[i]) return false;
        }
        return true;
    }

    internal NativeEntityAppearanceBinding Bind(NativeEntitySkinDescriptor Native)
    {
        if (NativeDescriptorFingerprint.Length != 0
            && NativeDescriptorFingerprint != Native.DescriptorFingerprint)
            throw new ArgumentException("The entity appearance does not match the captured native rig fingerprint.");
        var Bindings = new NativeEntityAppearanceBinding.RendererBinding[Replacements.Length];
        for (var i = 0; i < Replacements.Length; i++)
        {
            var Replacement = Replacements[i];
            var Skin = Native.Require(Replacement.Path);
            if (Skin.BlendShapeCount != 0)
                throw new ArgumentException($"Native renderer '{Replacement.Path}' requires blend shapes and cannot be replaced.");
            var Bones = new TransformBinding[Replacement.Bones.Length];
            var Bindposes = Replacement.Mesh.bindposes;
            for (var j = 0; j < Bones.Length; j++)
            {
                var Slot = Array.IndexOf(Skin.BonePaths, Replacement.Bones[j]);
                if (Slot < 0 || !PllNativeEntityAppearanceValueContract.CompatibleBindpose(Bindposes[j], Skin.Bindposes[Slot]))
                    throw new ArgumentException($"Entity renderer '{Replacement.Path}' bone '{Replacement.Bones[j]}' has no compatible native bindpose.");
                Bones[j] = Skin.Bones[Slot];
            }
            Bindings[i] = new NativeEntityAppearanceBinding.RendererBinding(
                Skin.Renderer, Bones, Replacement.Mesh, Replacement.Materials, Replacement.Bounds);
        }
        return new NativeEntityAppearanceBinding(Native, Bindings);
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }

    internal static byte[] Hash(Action<BinaryWriter> Write)
    {
        using var Sha = SHA256.Create();
        using (var Stream = new CryptoStream(System.IO.Stream.Null, Sha, CryptoStreamMode.Write))
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true))) Write(Writer);
        return Sha.Hash!;
    }

    internal static void WriteMatrix(BinaryWriter Writer, Matrix4x4 Matrix)
    {
        for (var i = 0; i < 16; i++) WriteFloat(Writer, Matrix[i]);
    }

    static void WriteVector(BinaryWriter Writer, Vector3 Value)
    {
        WriteFloat(Writer, Value.x); WriteFloat(Writer, Value.y); WriteFloat(Writer, Value.z);
    }

    static void WriteFloat(BinaryWriter Writer, float Value)
    {
        if (float.IsNaN(Value) || float.IsInfinity(Value))
            throw new ArgumentException("Entity appearance values must be finite.");
        Writer.Write(Value == 0f ? 0f : Value);
    }

    sealed class Replacement
    {
        internal readonly string Path;
        internal readonly Mesh Mesh;
        internal readonly Material[] Materials;
        internal readonly string[] Bones;
        internal readonly Bounds Bounds;

        internal Replacement(string Path, Mesh Mesh, Material[] Materials, string[] Bones, Bounds Bounds)
        {
            this.Path = Path; this.Mesh = Mesh; this.Materials = Materials;
            this.Bones = Bones; this.Bounds = Bounds;
        }
    }
}

internal sealed class NativeEntityAppearanceBinding
{
    readonly NativeEntitySkinDescriptor Native;
    readonly RendererBinding[] Renderers;

    internal NativeEntityAppearanceBinding(NativeEntitySkinDescriptor Native, RendererBinding[] Renderers)
    {
        this.Native = Native; this.Renderers = Renderers;
    }

    internal void Apply(Transform CloneRoot)
    {
        if (CloneRoot.IsChildOf(Native.Prefab.transform) || CloneRoot.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Entity appearance requires a private inactive native clone.");
        foreach (var Binding in Renderers)
        {
            var Renderer = Binding.Renderer.Resolve(CloneRoot).GetComponent<SkinnedMeshRenderer>();
            var Bones = new Transform[Binding.Bones.Length];
            for (var i = 0; i < Bones.Length; i++) Bones[i] = Binding.Bones[i].Resolve(CloneRoot);
            Renderer.sharedMesh = Binding.Mesh; Renderer.sharedMaterials = Binding.Materials;
            Renderer.bones = Bones; Renderer.localBounds = Binding.Bounds;
        }
    }

    internal void WriteManifest(BinaryWriter Writer) => Native.WriteManifest(Writer);

    internal sealed class Prototype : IDisposable
    {
        GameObject? Root;

        Prototype(GameObject Root, GameObject Prefab) { this.Root = Root; this.Prefab = Prefab; }

        internal GameObject Prefab { get; }

        internal static Prototype Create(NativeEntityAppearanceBinding Appearance)
        {
            var Root = new GameObject("__PLL_EntityAppearanceTemplate");
            Root.SetActive(false);
            try
            {
                var Prefab = UnityEngine.Object.Instantiate(Appearance.Native.Prefab, Root.transform, false);
                Prefab.name = Appearance.Native.Prefab.name;
                Appearance.Apply(Prefab.transform);
                return new Prototype(Root, Prefab);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(Root);
                throw;
            }
        }

        public void Dispose()
        {
            var Owned = Root; Root = null;
            if (Owned) UnityEngine.Object.DestroyImmediate(Owned);
        }
    }

    internal sealed class RendererBinding
    {
        internal readonly TransformBinding Renderer;
        internal readonly TransformBinding[] Bones;
        internal readonly Mesh Mesh;
        internal readonly Material[] Materials;
        internal readonly Bounds Bounds;

        internal RendererBinding(TransformBinding Renderer, TransformBinding[] Bones,
            Mesh Mesh, Material[] Materials, Bounds Bounds)
        {
            this.Renderer = Renderer; this.Bones = Bones; this.Mesh = Mesh;
            this.Materials = Materials; this.Bounds = Bounds;
        }
    }
}
