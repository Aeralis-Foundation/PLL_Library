using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public sealed class PllNativeEntityAppearance
{
    public string NativeDescriptorFingerprint = string.Empty;
    public PllNativeEntityRenderer[] Renderers = Array.Empty<PllNativeEntityRenderer>();
}

[Serializable]
public sealed class PllNativeEntityRenderer
{
    public string RendererPath = string.Empty;
    public Mesh Mesh = null!;
    public Material[] Materials = Array.Empty<Material>();
    public string[] BonePaths = Array.Empty<string>();
    public Bounds LocalBounds;
}

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllNativeEntityAppearanceValueContract
{
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static PllNativeEntityAppearance? Appearance(PllEntityZoneSource Source) => Source switch
    {
        PllBeetleZoneSource Beetles => Beetles.Appearance,
        PllCondorZoneSource Condor => Condor.Appearance,
        PllAuthoredEntityZoneSource Authored when Authored.Prefab
            && Authored.Prefab.TryGetComponent<PllEntityAuthoring>(out var Entity) => Entity.Behavior switch
            {
                PllBeetleBehavior Beetle => Beetle.Appearance,
                PllCondorBehavior Condor => Condor.Appearance,
                _ => null
            },
        PllAuthoredEntityZoneSource => null,
        PllMushroomZombieZoneSource or PllPluginEntityZoneSource => null,
        _ => throw new ArgumentException("The Entity Zone source is unsupported.", nameof(Source))
    };

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllNativeEntityAppearance? Appearance)
    {
        if (Appearance is null) return null;
        var Descriptor = Appearance.NativeDescriptorFingerprint;
        if (Descriptor is null || Descriptor.Length != 0 && Descriptor.Length != 64)
            return "Entity appearance requires an empty or uppercase SHA-256 native rig fingerprint.";
        foreach (var Character in Descriptor)
            if (!(Character >= '0' && Character <= '9' || Character >= 'A' && Character <= 'F'))
                return "Entity appearance requires an empty or uppercase SHA-256 native rig fingerprint.";
        if (Appearance.Renderers is null || Appearance.Renderers.Length == 0)
            return "Entity appearance requires an explicit renderer binding.";
        var Paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var Renderer in Appearance.Renderers)
        {
            if (Renderer is null || !IsPath(Renderer.RendererPath) || !Paths.Add(Renderer.RendererPath))
                return "Entity appearance renderer paths must be distinct and explicit.";
            var Mesh = Renderer.Mesh;
            if (!Mesh || !Mesh.isReadable || Mesh.vertexCount == 0 || Mesh.subMeshCount == 0)
                return "Entity appearance requires a nonempty mesh with Read/Write enabled.";
            if (Mesh.blendShapeCount != 0)
                return "Entity appearance does not support replacement blend shapes.";
            if (Renderer.Materials is null || Renderer.Materials.Length != Mesh.subMeshCount)
                return "Entity appearance requires one material per submesh.";
            foreach (var Material in Renderer.Materials)
                if (!Material || !Material.shader)
                    return "Entity appearance materials require a shader.";
            var Bindposes = Mesh.bindposes;
            if (Renderer.BonePaths is null || Renderer.BonePaths.Length == 0
                || Renderer.BonePaths.Length != Bindposes.Length)
                return "Entity appearance requires one explicit native bone path per mesh bindpose.";
            var Bones = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Path in Renderer.BonePaths)
                if (!IsPath(Path) || !Bones.Add(Path))
                    return "Entity appearance bone paths must be distinct and explicit.";
            foreach (var Bindpose in Bindposes)
                for (var i = 0; i < 16; i++)
                    if (!Finite(Bindpose[i])) return "Entity appearance bindposes must be finite.";
            var Bounds = Renderer.LocalBounds;
            if (!Finite(Bounds.center) || !Finite(Bounds.extents)
                || Bounds.extents.x < 0f || Bounds.extents.y < 0f || Bounds.extents.z < 0f)
                return "Entity appearance bounds must be finite with nonnegative extents.";
            foreach (var Vertex in Mesh.vertices)
                if (!Finite(Vertex) || !Bounds.Contains(Vertex))
                    return "Entity appearance bounds must contain every finite rest-pose vertex.";
            var Counts = Mesh.GetBonesPerVertex();
            var Weights = Mesh.GetAllBoneWeights();
            if (Counts.Length != Mesh.vertexCount)
                return "Entity appearance requires bone weights for every vertex.";
            var Offset = 0;
            foreach (var Count in Counts)
            {
                if (Count == 0 || Count > Weights.Length - Offset)
                    return "Entity appearance has an unweighted vertex or incomplete bone weights.";
                var Total = 0f;
                for (var i = 0; i < Count; i++)
                {
                    var Weight = Weights[Offset++];
                    if (Weight.boneIndex < 0 || Weight.boneIndex >= Bindposes.Length
                        || !Finite(Weight.weight) || Weight.weight <= 0f || Weight.weight > 1f)
                        return "Entity appearance bone weights must reference explicit bones with positive finite weights.";
                    Total += Weight.weight;
                }
                if (Mathf.Abs(Total - 1f) > 0.0001f)
                    return "Entity appearance vertex bone weights must sum to one.";
            }
            if (Offset != Weights.Length) return "Entity appearance contains unused bone weights.";
        }
        return null;
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static bool CompatibleBindpose(Matrix4x4 Actual, Matrix4x4 Expected)
    {
        for (var i = 0; i < 16; i++)
            if (!Finite(Actual[i]) || !Finite(Expected[i])
                || Mathf.Abs(Actual[i] - Expected[i]) > 0.00001f * Mathf.Max(1f, Mathf.Abs(Expected[i])))
                return false;
        return true;
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string ReferencePath(Transform Root, Transform Target)
    {
        if (!Root) throw new ArgumentException("The entity reference actor root is missing.", nameof(Root));
        if (Target == Root) return ".";
        if (!Target || !Target.IsChildOf(Root))
            throw new ArgumentException("The entity reference binding leaves its actor root.", nameof(Target));
        var Parts = new List<string>();
        for (var Current = Target; Current != Root; Current = Current.parent)
        {
            if (Current.name.Length == 0 || Current.name is "." or ".." || Current.name.Contains('/'))
                throw new ArgumentException("Entity reference names cannot be empty, dots or contain a slash.", nameof(Target));
            foreach (Transform Sibling in Current.parent)
                if (Sibling != Current && Sibling.name == Current.name)
                    throw new ArgumentException("Entity reference paths cannot contain duplicate sibling names.", nameof(Target));
            Parts.Add(Current.name);
        }
        Parts.Reverse();
        return string.Join("/", Parts);
    }

    static bool IsPath(string? Path)
    {
        if (string.IsNullOrEmpty(Path)) return false;
        if (Path == ".") return true;
        var Remaining = Path.AsSpan();
        while (true)
        {
            var End = Remaining.IndexOf('/');
            var Part = End < 0 ? Remaining : Remaining[..End];
            if (Part.IsEmpty || Part.SequenceEqual(".".AsSpan()) || Part.SequenceEqual("..".AsSpan())) return false;
            if (End < 0) return true;
            Remaining = Remaining[(End + 1)..];
        }
    }

    static bool Finite(float Value) => !float.IsNaN(Value) && !float.IsInfinity(Value);
    static bool Finite(Vector3 Value) => Finite(Value.x) && Finite(Value.y) && Finite(Value.z);
}
