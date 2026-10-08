using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class SurfaceTextureAuthoringContract
{
    readonly ComponentBinding<Renderer>[] Renderers;
    readonly int[] Slots;
    readonly ComponentBinding<PLLNativeResolver>? FluidBinding;
    readonly Material Profile;
    readonly PllSurfaceCoordinates Coordinates;
    readonly byte[] ProfileFingerprint;

    SurfaceTextureAuthoringContract(AuthoringBindingOwner Owner,
        ComponentBinding<PllSurfaceTextureAuthoring> Binding,
        ComponentBinding<Renderer>[] Renderers, int[] Slots,
        ComponentBinding<PLLNativeResolver>? FluidBinding, Material Profile,
        PllSurfaceCoordinates Coordinates)
    {
        this.Owner = Owner; this.Binding = Binding; this.Renderers = Renderers;
        this.Slots = Slots; this.FluidBinding = FluidBinding;
        this.Profile = Profile; this.Coordinates = Coordinates;
        ProfileFingerprint = VisualFingerprintWriter.HashSerializedObject(Profile, "PLL/SurfaceProfile/v1");
    }

    internal AuthoringBindingOwner Owner { get; }
    internal ComponentBinding<PllSurfaceTextureAuthoring> Binding { get; }
    internal PllSurfaceTextureAuthoring Source => Binding.Source;
    internal PLLNativeResolver? FluidSource => FluidBinding?.Source;
    internal Material Material => Profile;
    internal bool Generated => Owner.Generated;
    internal bool TryMatchGenerated(PllSurfaceTextureAuthoring Source, BiomeId Id, out Transform Root) =>
        Binding.TryMatchGenerated(Owner, Source, Id, "surface textures", out Root);

    internal static SurfaceTextureAuthoringContract Create(Transform Template,
        AuthoringCandidateGraph Graph, BiomeId Id, PllSurfaceTextureAuthoring Source,
        Dictionary<(Renderer, int), PllSurfaceTextureAuthoring> SurfaceSlots)
    {
        if (Source.Coordinates is not (PllSurfaceCoordinates.Object or PllSurfaceCoordinates.Biome or PllSurfaceCoordinates.World))
            throw new ArgumentException("Surface coordinates are unsupported.", nameof(Source));
        PllSurfaceProfileContract.Validate(Source.Material!);
        var Owner = AuthoringBindingOwner.Create(Template, Graph, Source.transform, Id, "surface textures");
        ComponentBinding<Renderer>[] Renderers;
        int[] Slots;
        ComponentBinding<PLLNativeResolver>? FluidBinding = null;
        if (Source.Target == PllSurfaceTarget.AuthoredRendererSlots)
        {
            if (Source.Renderers.Count == 0 || Source.Renderers.Count != Source.Slots.Count || Source.FluidSource)
                throw new ArgumentException("Surface Textures needs explicit renderer slots only.", nameof(Source));
            Renderers = new ComponentBinding<Renderer>[Source.Renderers.Count];
            Slots = new int[Renderers.Length];
            var NeedsBake = PllSurfaceProfileContract.NeedsBake(Source.Material!);
            for (var Index = 0; Index < Renderers.Length; Index++)
            {
                var Renderer = Source.Renderers[Index]; var Slot = Source.Slots[Index];
                if (!Renderer || !TransformOwnership.Contains(Owner.BindingRoot, Renderer.transform)
                    || Slot < 0 || Slot >= Renderer.sharedMaterials.Length)
                    throw new ArgumentException("Surface renderer slot is not owned or valid.", nameof(Source));
                if (!SurfaceSlots.TryAdd((Renderer, Slot), Source))
                {
                    if (SurfaceSlots[(Renderer, Slot)] == Source)
                        throw new ArgumentException("Surface renderer slots must be unique.", nameof(Source));
                    throw new ArgumentException("Surface Textures assigns one renderer slot more than once.");
                }
                var Mesh = Renderer is SkinnedMeshRenderer Skinned ? Skinned.sharedMesh
                    : Renderer is MeshRenderer ? Renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                PllSurfaceProfileContract.ValidateMesh(Source.Material!, Mesh!, Renderer is SkinnedMeshRenderer);
                if (NeedsBake
                    && (!Mesh!.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord7)
                        || !Source.HasPreparedCurvature(Mesh)))
                    throw new ArgumentException("Surface curvature is not baked. Build the biome with Creator.", nameof(Source));
                Renderers[Index] = ComponentBinding<Renderer>.Create(Owner.BindingRoot, Renderer);
                Slots[Index] = Slot;
            }
        }
        else if (Source.Target == PllSurfaceTarget.GeneratedFluidSurface)
        {
            if (!Source.FluidSource || Source.FluidSource.Form != PllNativeResolverForm.Source
                || !TransformOwnership.Contains(Owner.BindingRoot, Source.FluidSource.transform)
                || Source.Renderers.Count != 0 || Source.Slots.Count != 0)
                throw new ArgumentException("Generated Surface Textures needs one owned fluid source only.", nameof(Source));
            FluidBinding = ComponentBinding<PLLNativeResolver>.Create(Owner.BindingRoot, Source.FluidSource);
            Renderers = Array.Empty<ComponentBinding<Renderer>>(); Slots = Array.Empty<int>();
        }
        else throw new ArgumentException("Surface target kind is unsupported.", nameof(Source));
        return new SurfaceTextureAuthoringContract(Owner,
            ComponentBinding<PllSurfaceTextureAuthoring>.Create(Owner.BindingRoot, Source),
            Renderers, Slots, FluidBinding, Source.Material!, Source.Coordinates);
    }

    internal SurfaceTextureRuntime[] BindAuthored(Transform RuntimeRoot, Transform BiomeFrame, BiomeId Id)
    {
        var Result = new SurfaceTextureRuntime[Renderers.Length];
        var Installed = 0;
        try
        {
            for (; Installed < Result.Length; Installed++)
                Result[Installed] = new SurfaceTextureRuntime(
                    Renderers[Installed].Resolve(RuntimeRoot, Id, "surface renderer"),
                    Slots[Installed], Profile, Coordinates, BiomeFrame, true);
            return Result;
        }
        catch
        {
            while (Installed > 0) Result[--Installed].Dispose();
            throw;
        }
    }

    internal SurfaceTextureRuntime BindGenerated(Renderer Renderer, int Slot, Mesh Mesh, Transform BiomeFrame)
    {
        if (FluidBinding is null) throw new InvalidOperationException("Surface binding is not a generated fluid target.");
        PllSurfaceProfileContract.ValidateMesh(Profile, Mesh, false);
        if (PllSurfaceProfileContract.NeedsBake(Profile))
            Mesh.SetUVs(PllSurfaceProfileContract.CurvatureChannel, PllSurfaceCurvature.Bake(Mesh));
        return new SurfaceTextureRuntime(Renderer, Slot, Profile, Coordinates, BiomeFrame);
    }

    internal void ValidateUnchanged(BiomeId Id)
    {
        Binding.Validate(Owner.BindingRoot, Id, "surface textures");
        if (Source.Target != (FluidBinding is null
                ? PllSurfaceTarget.AuthoredRendererSlots : PllSurfaceTarget.GeneratedFluidSurface))
            throw new InvalidOperationException("Surface target changed before sealing.");
        if (Source.Material != Profile || Source.Coordinates != Coordinates
            || Source.Renderers.Count != Renderers.Length || Source.Slots.Count != Slots.Length
            || Source.FluidSource != FluidSource
            || !VisualFingerprintWriter.HashSerializedObject(Profile, "PLL/SurfaceProfile/v1").AsSpan().SequenceEqual(ProfileFingerprint))
            throw new InvalidOperationException("Surface material changed before sealing.");
        for (var Index = 0; Index < Renderers.Length; Index++)
        {
            if (Source.Renderers[Index] != Renderers[Index].Source || Source.Slots[Index] != Slots[Index])
                throw new InvalidOperationException("Surface renderer slot changed before sealing.");
            Renderers[Index].Validate(Owner.BindingRoot, Id, "surface renderer");
        }
        FluidBinding?.Validate(Owner.BindingRoot, Id, "surface fluid source");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); Binding.WriteManifest(Writer);
        Writer.Write((byte)Coordinates); Writer.Write(FluidBinding is not null);
        Writer.Write(ProfileFingerprint.Length); Writer.Write(ProfileFingerprint);
        Writer.Write(PllSurfaceProfileContract.BakeDomain);
        Writer.Write(Renderers.Length);
        for (var Index = 0; Index < Renderers.Length; Index++)
        {
            Renderers[Index].WriteManifest(Writer); Writer.Write(Slots[Index]);
        }
        FluidBinding?.WriteManifest(Writer);
    }
}
