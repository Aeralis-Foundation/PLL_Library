using System;
using UnityEngine;

namespace PLL.Library;

public static class PllSurfaceProfileContract
{
    public const string ShaderName = "PLL/Surfaces/Layered";
    public const int LayerCount = 4, CurvatureChannel = 7, BakeVersion = 1;
    public const string BakeDomain = "PLL/CotangentMixedArea/ReferenceBounds/v1";
    public static string Property(int Layer, string Field) => "_Layer" + Layer + Field;

    public static bool UsesCurvature(Material Profile)
    {
        for (var Layer = 1; Layer < LayerCount; Layer++)
            if (Profile.GetFloat(Property(Layer, "Enabled")) == 1f
                && ((PllSurfaceMask)(int)Profile.GetFloat(Property(Layer, "Mask")) & PllSurfaceMask.Curvature) != 0)
                return true;
        return false;
    }

    public static bool NeedsBake(Material Profile) => UsesCurvature(Profile)
        && Profile.GetFloat("_CurvatureInput") == (float)PllSurfaceCurvatureInput.AutomaticMeshBake;

    public static void ConfigureMode(Material Profile, PllSurfaceRenderMode Mode)
    {
        var State = RenderState(Mode);
        Profile.SetFloat("_SurfaceMode", (float)Mode);
        Profile.SetFloat("_SrcBlend", State.Source);
        Profile.SetFloat("_DstBlend", State.Destination);
        Profile.SetFloat("_ZWrite", State.Depth ? 1f : 0f);
        Profile.SetShaderPassEnabled("DepthOnly", State.Depth);
        Profile.SetShaderPassEnabled("ShadowCaster", State.Depth);
        Profile.SetOverrideTag("RenderType", State.Tag); Profile.renderQueue = State.Queue;
    }

    public static void Validate(Material Profile)
    {
        if (!Profile || !Profile.shader || Profile.shader.name != ShaderName)
            throw new ArgumentException("Surface Textures needs a PLL layered material.", nameof(Profile));
        Integer(Profile, "_SurfaceMode", 0, 2);
        var Transparent = Profile.GetFloat("_SurfaceMode") == 2f;
        var State = RenderState((PllSurfaceRenderMode)(int)Profile.GetFloat("_SurfaceMode"));
        if (Profile.GetFloat("_SrcBlend") != State.Source
            || Profile.GetFloat("_DstBlend") != State.Destination
            || Profile.GetFloat("_ZWrite") != (State.Depth ? 1f : 0f)
            || Profile.GetShaderPassEnabled("DepthOnly") != State.Depth
            || Profile.GetShaderPassEnabled("ShadowCaster") != State.Depth
            || Profile.renderQueue != State.Queue || Profile.GetTag("RenderType", false) != State.Tag)
            throw new ArgumentException("Surface render state does not match its mode. Edit the material with the Surface Inspector.", nameof(Profile));
        if (Profile.GetFloat("_SurfaceMode") == 1f) Range(Profile, "_Cutoff", 0f, 1f);
        if (Transparent) Range(Profile, "_Opacity", 0f, 1f);
        for (var Layer = 0; Layer < LayerCount; Layer++)
        {
            var Prefix = "_Layer" + Layer;
            if (Layer != 0)
            {
                Integer(Profile, Prefix + "Enabled", 0, 1);
                if (Profile.GetFloat(Prefix + "Enabled") == 0f) continue;
                Integer(Profile, Prefix + "Mask", 1, 31);
                Range(Profile, Prefix + "Strength", 0f, 1f);
                var Mask = (PllSurfaceMask)(int)Profile.GetFloat(Prefix + "Mask");
                if ((Mask & PllSurfaceMask.Slope) != 0) Interval(Profile, Prefix + "Slope", 0f, 180f);
                if ((Mask & PllSurfaceMask.Height) != 0) Interval(Profile, Prefix + "Height", float.MinValue, float.MaxValue);
                if ((Mask & PllSurfaceMask.Curvature) != 0) Interval(Profile, Prefix + "Curvature", -1f, 1f);
                if ((Mask & PllSurfaceMask.Vertex) != 0) Integer(Profile, Prefix + "VertexChannel", 0, 3);
                if ((Mask & PllSurfaceMask.Texture) != 0)
                {
                    Integer(Profile, Prefix + "MaskChannel", 0, 3);
                    Integer(Profile, Prefix + "MaskUV", 0, 6);
                    LinearTexture(Profile, Prefix + "MaskTexture");
                    Vector(Profile, Prefix + "MaskTransform");
                }
            }
            Integer(Profile, Prefix + "Projection", 0, 1);
            if (Profile.GetFloat(Prefix + "Projection") == 0f)
            {
                Integer(Profile, Prefix + "UV", 0, 6); Vector(Profile, Prefix + "Transform");
            }
            else
            {
                Range(Profile, Prefix + "TileSize", float.Epsilon, float.MaxValue);
                Range(Profile, Prefix + "Blend", 0f, float.MaxValue);
            }
            Range(Profile, Prefix + "Smoothness", 0f, 1f);
            Range(Profile, Prefix + "NormalStrength", 0f, float.MaxValue);
            OptionalTexture(Prefix + "Albedo"); OptionalTexture(Prefix + "Normal");
            var Tint = Profile.GetColor(Prefix + "Tint");
            if (!Finite(Tint.r) || !Finite(Tint.g) || !Finite(Tint.b) || !Finite(Tint.a))
                throw new ArgumentException("Surface layer tint must be finite.", nameof(Profile));
        }
        if (!UsesCurvature(Profile)) return;
        Integer(Profile, "_CurvatureInput", 0, 1);
        Range(Profile, "_CurvatureContrast", 0f, float.MaxValue);
        if (Profile.GetFloat("_CurvatureInput") == 1f)
        {
            Integer(Profile, "_CurvatureUV", 0, 6); Vector(Profile, "_CurvatureTransform");
            LinearTexture(Profile, "_CurvatureTexture");
        }

        void OptionalTexture(string Name)
        {
            var Texture = Profile.GetTexture(Name);
            if (PllAuthoringValueContract.DestroyedReference(Texture) || Texture && Texture is not Texture2D)
                throw new ArgumentException($"Surface property '{Name}' needs a 2D texture or no texture.", nameof(Profile));
        }
    }

    public static void ValidateMesh(Material Profile, Mesh Mesh, bool Deforming)
    {
        if (!Mesh) throw new ArgumentException("Surface Textures target has no mesh.", nameof(Mesh));
        if (!Mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal))
            throw new ArgumentException("Surface Textures needs geometric mesh normals.", nameof(Mesh));
        if (NeedsBake(Profile) && Deforming)
            throw new ArgumentException("Deforming surfaces require imported curvature. Choose an imported mask.", nameof(Mesh));
        for (var Layer = 0; Layer < LayerCount; Layer++)
        {
            var Prefix = "_Layer" + Layer;
            if (Layer != 0 && Profile.GetFloat(Prefix + "Enabled") == 0f) continue;
            if (Profile.GetFloat(Prefix + "Projection") == 0f)
            {
                RequireUV((int)Profile.GetFloat(Prefix + "UV"));
                if (Profile.GetTexture(Prefix + "Normal")
                    && !Mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
                    throw new ArgumentException("UV normal maps need mesh tangents.", nameof(Mesh));
            }
            if (Layer == 0) continue;
            var Mask = (PllSurfaceMask)(int)Profile.GetFloat(Prefix + "Mask");
            if ((Mask & PllSurfaceMask.Vertex) != 0 && !Mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                throw new ArgumentException("Surface vertex mask needs mesh colors.", nameof(Mesh));
            if ((Mask & PllSurfaceMask.Texture) != 0) RequireUV((int)Profile.GetFloat(Prefix + "MaskUV"));
        }
        if (UsesCurvature(Profile) && !NeedsBake(Profile)) RequireUV((int)Profile.GetFloat("_CurvatureUV"));

        void RequireUV(int Channel)
        {
            if (!Mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + Channel))
                throw new ArgumentException($"Surface Textures needs UV channel {Channel}.", nameof(Mesh));
        }
    }

    static void LinearTexture(Material Profile, string Name)
    {
        if (Profile.GetTexture(Name) is not Texture2D Texture
            || UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(Texture.graphicsFormat))
            throw new ArgumentException($"Surface mask '{Name}' needs a linear 2D texture.", nameof(Profile));
    }
    static void Integer(Material Profile, string Name, int Min, int Max)
    {
        var Value = Profile.GetFloat(Name);
        if (!Finite(Value) || Value < Min || Value > Max || Value != (int)Value)
            throw new ArgumentException($"Surface property '{Name}' must be an integer from {Min} to {Max}.", nameof(Profile));
    }
    static void Range(Material Profile, string Name, float Min, float Max)
    {
        var Value = Profile.GetFloat(Name);
        if (!Finite(Value) || Value < Min || Value > Max)
            throw new ArgumentException($"Surface property '{Name}' is outside its supported range.", nameof(Profile));
    }
    static void Vector(Material Profile, string Name)
    {
        var Value = Profile.GetVector(Name);
        if (!Finite(Value.x) || !Finite(Value.y) || !Finite(Value.z) || !Finite(Value.w))
            throw new ArgumentException($"Surface property '{Name}' must be finite.", nameof(Profile));
    }
    static void Interval(Material Profile, string Name, float Min, float Max)
    {
        Vector(Profile, Name);
        var Value = Profile.GetVector(Name);
        if (Value.x < Min || Value.y > Max || Value.x > Value.y || Value.z < 0f)
            throw new ArgumentException($"Surface interval '{Name}' is invalid.", nameof(Profile));
    }
    static bool Finite(float Value) => !float.IsNaN(Value) && !float.IsInfinity(Value);
    static (int Source, int Destination, bool Depth, int Queue, string Tag) RenderState(PllSurfaceRenderMode Mode) => Mode switch
    {
        PllSurfaceRenderMode.Opaque => (1, 0, true, 2000, "Opaque"),
        PllSurfaceRenderMode.Cutout => (1, 0, true, 2450, "TransparentCutout"),
        PllSurfaceRenderMode.FluidTransparent => (5, 10, false, 3000, "Transparent"),
        _ => throw new ArgumentOutOfRangeException(nameof(Mode))
    };
}
