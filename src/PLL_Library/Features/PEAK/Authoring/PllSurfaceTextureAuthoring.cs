using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllSurfaceTarget : byte { AuthoredRendererSlots, GeneratedFluidSurface }
public enum PllSurfaceCoordinates : byte { Object, Biome, World }
public enum PllSurfaceRenderMode : byte { Opaque, Cutout, FluidTransparent }
public enum PllSurfaceCurvatureInput : byte { AutomaticMeshBake, ImportedTexture }
[Flags]
public enum PllSurfaceMask : byte { Slope = 1, Height = 2, Curvature = 4, Vertex = 8, Texture = 16 }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Visuals/Surface Textures")]
public sealed class PllSurfaceTextureAuthoring : MonoBehaviour
{
    [SerializeField] PllSurfaceTarget TargetValue = PllSurfaceTarget.AuthoredRendererSlots;
    [SerializeField] PllSurfaceCoordinates CoordinatesValue = PllSurfaceCoordinates.Object;
    [SerializeField] Material? MaterialValue = null;
    [SerializeField] Renderer[] RenderersValue = Array.Empty<Renderer>();
    [SerializeField] int[] SlotsValue = Array.Empty<int>();
    [SerializeField] PLLNativeResolver? FluidSourceValue = null;
    [SerializeField, HideInInspector] Mesh[] BundleBakedMeshes = Array.Empty<Mesh>();
    [SerializeField, HideInInspector] int BundleBakeVersion = 0;

    public PllSurfaceTarget Target => TargetValue;
    public PllSurfaceCoordinates Coordinates => CoordinatesValue;
    public Material? Material => MaterialValue;
    public IReadOnlyList<Renderer> Renderers => RenderersValue;
    public IReadOnlyList<int> Slots => SlotsValue;
    public PLLNativeResolver? FluidSource => FluidSourceValue;
    public bool HasPreparedCurvature(Mesh Mesh) => BundleBakeVersion == PllSurfaceProfileContract.BakeVersion
        && Array.IndexOf(BundleBakedMeshes, Mesh) >= 0;

#if PLL_CREATOR_SDK
    public void SetPreparedCurvature(Mesh[] Meshes)
    {
        BundleBakedMeshes = (Mesh[])Meshes.Clone();
        BundleBakeVersion = PllSurfaceProfileContract.BakeVersion;
    }
#endif
}
