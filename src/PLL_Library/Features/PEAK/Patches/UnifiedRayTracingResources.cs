using System;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace PLL.Library;

internal static class UnifiedRayTracingResources
{
    const string PackageName = "Unified Ray Tracing";
    static readonly string BundlePath = Path.Combine(
        Paths.PluginPath,
        "AeralisFoundation-Unified_Ray_Tracing",
        "unifiedraytracing");

    static AssetBundle? Bundle;
    static string? FailureReason;

    internal static bool TryPrepare(out string? Rejection)
    {
        Rejection = FailureReason;
        if (Rejection is not null) return false;
        if (Bundle) return true;
        if (!File.Exists(BundlePath))
        {
            FailureReason = $"{PackageName} is not installed at '{BundlePath}'";
            Rejection = FailureReason;
            return false;
        }

        AssetBundle? Staged = null;
        try
        {
            Staged = AssetBundle.LoadFromFile(BundlePath);
            if (!Staged)
                throw new InvalidDataException("Unity rejected the ray-tracing AssetBundle.");
            var Resources = new RayTracingResources();
            Resources.LoadFromAssetBundle(Staged);
            Validate(Resources);
            Bundle = Staged;
            Staged = null;
            return true;
        }
        catch (Exception Failure)
        {
            Exception? CleanupFailure = null;
            try { if (Staged) Staged.Unload(true); }
            catch (Exception Exception) { CleanupFailure = Exception; }
            FailureReason = CleanupFailure is null
                ? $"{PackageName} could not load: {Failure.Message}"
                : $"{PackageName} could not load: {Failure.Message};"
                    + $" cleanup failed: {CleanupFailure.Message}";
            Rejection = FailureReason;
            return false;
        }
    }

    internal static void Populate(RayTracingResources Resources)
    {
        if (Resources is null) throw new ArgumentNullException(nameof(Resources));
        if (!TryPrepare(out var Rejection))
            throw new InvalidOperationException(Rejection);
        Resources.LoadFromAssetBundle(Bundle);
    }

    internal static void Shutdown()
    {
        var Loaded = Bundle;
        Bundle = null;
        if (Loaded) Loaded.Unload(true);
    }

    static void Validate(RayTracingResources Resources)
    {
        Require(Resources.geometryPoolKernels,
            "MainUpdateIndexBuffer16", "MainUpdateIndexBuffer32", "MainUpdateVertexBuffer");
        Require(Resources.copyBuffer, "CopyBuffer");
        Require(Resources.copyPositions, "CopyVertexBuffer");
        Require(Resources.bitHistogram, "BitHistogram");
        Require(Resources.scatter, "Scatter");
        Require(Resources.blockReducePart, "BlockReducePart");
        Require(Resources.blockScan, "BlockScanAdd");
        Require(Resources.buildHlbvh,
            "Init", "CalculateAabb", "CalculateMortonCodes", "BuildTreeBottomUp");
        Require(Resources.restructureBvh,
            "InitPrimitiveCounts", "FindTreeletRoots",
            "PrepareTreeletsDispatchSize", "Restructure");
    }

    static void Require(ComputeShader Shader, params string[] Kernels)
    {
        if (!Shader)
            throw new InvalidDataException("The ray-tracing bundle omitted a compute shader.");
        foreach (var Kernel in Kernels)
            if (!Shader.HasKernel(Kernel))
                throw new InvalidDataException(
                    $"The ray-tracing bundle omitted kernel '{Kernel}'.");
    }
}