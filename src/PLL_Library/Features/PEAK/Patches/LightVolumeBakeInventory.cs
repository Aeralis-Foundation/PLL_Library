using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Peak;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RadeonRays;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace PLL.Library;

internal sealed class LightVolumeBakeInventory
{
    const long PositionBytesPerVertex = 12;
    const int InitialPositionCapacity = 1000;
    const int LightBytes = 52;
    const int RestructureDispatchBytes = 24;

    readonly MeshRenderer[] RendererValues;
    readonly BakedVolumeLight[] LightValues;

    LightVolumeBakeInventory(
        MeshRenderer[] Renderers,
        BakedVolumeLight[] Lights,
        int BlasInitialBytes,
        long AccelerationResidentBytes,
        long AccelerationPeakBytes,
        long BuildScratchBytes,
        long TopLevelBytes,
        Bounds Bounds)
    {
        RendererValues = Renderers;
        LightValues = Lights;
        this.BlasInitialBytes = BlasInitialBytes;
        this.AccelerationResidentBytes = AccelerationResidentBytes;
        this.AccelerationPeakBytes = AccelerationPeakBytes;
        this.BuildScratchBytes = BuildScratchBytes;
        this.TopLevelBytes = TopLevelBytes;
        this.Bounds = Bounds;
    }

    internal int BlasInitialBytes { get; }
    internal long AccelerationResidentBytes { get; }
    internal long AccelerationPeakBytes { get; }
    internal long BuildScratchBytes { get; }
    internal long TopLevelBytes { get; }
    internal long LightBufferBytes =>
        checked((long)Math.Max(LightValues.Length, 1) * LightBytes);
    internal Bounds Bounds { get; }

    internal void IterateRenderers(Action<MeshRenderer> Action)
    {
        if (Action is null) throw new ArgumentNullException(nameof(Action));
        foreach (var Renderer in RendererValues) Action(Renderer);
    }

    internal void IterateLights(
        Func<BakedVolumeLight, bool>? IsValid,
        Action<BakedVolumeLight> Action)
    {
        if (Action is null) throw new ArgumentNullException(nameof(Action));
        foreach (var Light in LightValues)
            if (IsValid?.Invoke(Light) != false) Action(Light);
    }

    internal static bool TryCapture(
        global::LightVolume Volume,
        MapSlot TargetSlot,
        bool Compute,
        out LightVolumeBakeInventory? Inventory,
        out string? Rejection)
    {
        Inventory = null;
        Rejection = null;
        var Occurrence = TargetSlot.Occurrence;
        if (Occurrence.Slots.Count == 0)
            throw new InvalidOperationException("A light-volume target occurrence has no layout slots.");

        var Renderers = new List<MeshRenderer>();
        var Lights = new List<BakedVolumeLight>();
        var RendererCandidates = new List<MeshRenderer>();
        var LightCandidates = new List<BakedVolumeLight>();
        var SeenRoots = new HashSet<int>();
        var SeenRenderers = new HashSet<int>();
        var SeenLights = new HashSet<int>();
        var UniqueSubmeshes = new HashSet<(int Mesh, int Submesh)>();
        var PositionCapacity = (long)InitialPositionCapacity;
        var PositionUsed = 0L;
        var PositionPeakBytes = PositionCapacity * PositionBytesPerVertex;
        var MaximumPositionCapacity = Compute
            ? Math.Min(
                int.MaxValue,
                GraphicsHelpers.MaxGraphicsBufferSizeInBytes / PositionBytesPerVertex)
            : 0;
        var BuildScratchBytes = Compute ? 16L : 0L;
        var InstanceCount = 0;
        var TriangleCount = 0L;
        var Bounds = default(Bounds);
        var BoundsFound = false;
        string? Failure = null;
        foreach (var Root in Occurrence.Roots) CaptureRoot(Root);
        foreach (var Slot in Occurrence.Slots) CaptureRoot(Slot.PeakAttachment);
        if (Failure is not null)
        {
            Rejection = Failure;
            return false;
        }
        if (!BoundsFound || InstanceCount == 0 || TriangleCount == 0)
        {
            Rejection = "the target occurrence contains no supported light-volume triangles";
            return false;
        }
        if (!Compute)
        {
            Inventory = new LightVolumeBakeInventory(
                Renderers.ToArray(), Lights.ToArray(),
                0, 0, 0, 0, 0, Bounds);
            return true;
        }
        if (TriangleCount > int.MaxValue)
        {
            Rejection = "the target occurrence exceeds the compute backend's element limit";
            return false;
        }
        if (!TryCalculateBlasBytes(
                TriangleCount, out var BlasInitialBytes, out var BlasResidentBytes))
        {
            Rejection = "the target occurrence exceeds the compute BLAS buffer limit";
            return false;
        }

        var InternalNodeBytes = RadeonRaysAPI.BvhInternalNodeSizeInBytes();
        var PositionResidentBytes = checked(PositionCapacity * PositionBytesPerVertex);
        var AccelerationResidentBytes = checked(BlasResidentBytes + PositionResidentBytes);
        var AccelerationPeakBytes = checked(BlasResidentBytes + PositionPeakBytes);
        var TopLevelScratchBytes = checked(
            (long)HlbvhTopLevelBuilder.ScratchBufferLayout.Create((uint)InstanceCount).TotalSize * 4);
        BuildScratchBytes = Math.Max(BuildScratchBytes, TopLevelScratchBytes);
        var TopLevelBytes = checked(
            (long)InstanceCount
            * (Marshal.SizeOf<InstanceInfo>() + InternalNodeBytes));
        Inventory = new LightVolumeBakeInventory(
            Renderers.ToArray(), Lights.ToArray(),
            BlasInitialBytes, AccelerationResidentBytes, AccelerationPeakBytes,
            BuildScratchBytes, TopLevelBytes, Bounds);
        return true;

        void CaptureRoot(UnityEngine.Transform? Root)
        {
            if (Failure is not null || !Root || !Root.gameObject.activeInHierarchy
                || !SeenRoots.Add(Root.GetInstanceID())) return;
            Root.GetComponentsInChildren(false, RendererCandidates);
            foreach (var Renderer in RendererCandidates)
            {
                if (!Renderer || !SeenRenderers.Add(Renderer.GetInstanceID())) continue;
                if (!TryGetSupportedMesh(
                        Volume, Renderer, out var Mesh, out var RendererBounds,
                        out var RendererRejection))
                {
                    Failure ??= RendererRejection;
                    if (Failure is not null) break;
                    continue;
                }

                var SubmeshCount = Mesh!.subMeshCount;
                InstanceCount = checked(InstanceCount + SubmeshCount);
                for (var SubmeshIndex = 0; SubmeshIndex < SubmeshCount; SubmeshIndex++)
                {
                    if (!UniqueSubmeshes.Add((Mesh.GetInstanceID(), SubmeshIndex))) continue;
                    var Submesh = Mesh.GetSubMesh(SubmeshIndex);
                    var Triangles = Submesh.indexCount / 3;
                    TriangleCount = checked(TriangleCount + Triangles);
                    if (!Compute) continue;
                    var TriangleCountValue = checked((uint)Triangles);
                    var BuildBytes = checked(
                        (long)HlbvhBuilder.ScratchBufferLayout.Create(TriangleCountValue).TotalSize * 4);
                    var RestructureBytes = checked(
                        (long)RestructureBvh.ScratchBufferLayout.Create(TriangleCountValue).TotalSize * 4);
                    BuildScratchBytes = Math.Max(
                        BuildScratchBytes,
                        Math.Max(BuildBytes, RestructureBytes));
                    if (TryAllocatePositions(
                            checked((uint)Submesh.vertexCount), MaximumPositionCapacity,
                            ref PositionUsed, ref PositionCapacity, ref PositionPeakBytes)) continue;
                    Failure = $"eligible mesh '{Mesh.name}' exceeds the compute position-buffer limit";
                    break;
                }
                if (Failure is not null) break;
                Renderers.Add(Renderer);
                if (BoundsFound) Bounds.Encapsulate(RendererBounds);
                else
                {
                    Bounds = RendererBounds;
                    BoundsFound = true;
                }
            }
            RendererCandidates.Clear();
            if (Failure is not null) return;

            Root.GetComponentsInChildren(false, LightCandidates);
            foreach (var Light in LightCandidates)
                if (Light && Light.enabled && SeenLights.Add(Light.GetInstanceID())) Lights.Add(Light);
            LightCandidates.Clear();
        }
    }

    static bool TryGetSupportedMesh(
        global::LightVolume Volume,
        MeshRenderer Renderer,
        out Mesh? Mesh,
        out Bounds Bounds,
        out string? Rejection)
    {
        Mesh = null;
        Bounds = default;
        Rejection = null;
        if (!Volume.CheckRendererValid(Renderer)) return false;
        var Parent = Renderer.transform.parent;
        if (Parent && Parent.name is "WaterMesh" or "Water") return false;
        var Filter = Renderer.GetComponent<MeshFilter>();
        var Candidate = Filter ? Filter.sharedMesh : null;
        if (!Candidate || !Candidate.isReadable
            || !Candidate.HasVertexAttribute(VertexAttribute.Position)
            || Candidate.subMeshCount == 0) return false;
        for (var SubmeshIndex = 0; SubmeshIndex < Candidate.subMeshCount; SubmeshIndex++)
        {
            var Submesh = Candidate.GetSubMesh(SubmeshIndex);
            if (Submesh.topology != MeshTopology.Triangles
                || Submesh.vertexCount == 0 || Submesh.indexCount < 3
                || Submesh.indexCount % 3 != 0) return false;
        }
        var CandidateBounds = Renderer.bounds;
        if (!UnityValueContract.Finite(CandidateBounds.min)
            || !UnityValueContract.Finite(CandidateBounds.max))
        {
            Rejection = $"eligible renderer '{Renderer.name}' has invalid world bounds";
            return false;
        }
        Mesh = Candidate;
        Bounds = CandidateBounds;
        return true;
    }

    static bool TryCalculateBlasBytes(
        long TriangleCount,
        out int InitialBytes,
        out long ResidentBytes)
    {
        var Initial = checked(
            TriangleCount * RadeonRaysAPI.BvhInternalNodeSizeInBytes());
        if (Initial > int.MaxValue)
        {
            InitialBytes = 0;
            ResidentBytes = 0;
            return false;
        }
        InitialBytes = (int)Initial;
        ResidentBytes = checked(
            Initial + TriangleCount * RadeonRaysAPI.BvhLeafNodeSizeInBytes()
            + RestructureDispatchBytes);
        return true;
    }

    static bool TryAllocatePositions(
        uint Count,
        long MaximumCapacity,
        ref long Used,
        ref long Capacity,
        ref long PeakBytes)
    {
        var Required = checked(Used + Count);
        if (Required > Capacity)
        {
            var GeometricCapacity = Capacity > MaximumCapacity - Capacity / 2
                ? MaximumCapacity
                : Capacity + Capacity / 2;
            var NewCapacity = Math.Max(Required, GeometricCapacity);
            if (NewCapacity > MaximumCapacity) return false;
            PeakBytes = Math.Max(
                PeakBytes,
                checked((Capacity + NewCapacity) * PositionBytesPerVertex));
            Capacity = NewCapacity;
        }
        Used = Required;
        PeakBytes = Math.Max(PeakBytes, checked(Capacity * PositionBytesPerVertex));
        return true;
    }
}
