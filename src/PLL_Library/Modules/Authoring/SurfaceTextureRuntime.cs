using System;
using UnityEngine;

namespace PLL.Library;

internal sealed class SurfaceTextureRuntime : IDisposable
{
    readonly Renderer Renderer;
    readonly int Slot;
    readonly Material? Previous;
    readonly Material Owned;
    readonly bool OwnsMaterial;
    bool Released;

    internal SurfaceTextureRuntime(Renderer Renderer, int Slot, Material Profile,
        PllSurfaceCoordinates Coordinates, Transform BiomeFrame, bool AllowFluidOwner = false)
    {
        this.Renderer = Renderer; this.Slot = Slot;
        var Materials = Renderer.sharedMaterials;
        Previous = Materials[Slot];
        OwnsMaterial = !AllowFluidOwner || !NativeFlowRuntime.OwnsSurfaceSlot(Renderer, Slot);
        Owned = OwnsMaterial ? new Material(Profile) : Materials[Slot];
        Owned.SetFloat("_PllCoordinates", (float)Coordinates);
        Owned.SetMatrix("_PllWorldToFrame", Coordinates == PllSurfaceCoordinates.Biome
            ? BiomeFrame.worldToLocalMatrix : Matrix4x4.identity);
        Owned.SetMatrix("_PllNormalToFrame", Coordinates == PllSurfaceCoordinates.Biome
            ? BiomeFrame.localToWorldMatrix.transpose : Matrix4x4.identity);
        Owned.SetMatrix("_PllFrameNormalToWorld", Coordinates == PllSurfaceCoordinates.Biome
            ? BiomeFrame.worldToLocalMatrix.transpose : Matrix4x4.identity);
        Materials[Slot] = Owned; Renderer.sharedMaterials = Materials;
    }

    internal Renderer Target => Renderer;
    internal Material Material => Owned;

    public void Dispose()
    {
        if (Released) return;
        Released = true;
        if (OwnsMaterial && Renderer)
        {
            var Materials = Renderer.sharedMaterials;
            if (Slot < Materials.Length && Materials[Slot]
                && NativeFogRenderRuntime.CanonicalMaterial(Materials[Slot]) == Owned)
            {
                Materials[Slot] = Previous!; Renderer.sharedMaterials = Materials;
            }
        }
        if (OwnsMaterial) UnityEngine.Object.Destroy(Owned);
    }
}
