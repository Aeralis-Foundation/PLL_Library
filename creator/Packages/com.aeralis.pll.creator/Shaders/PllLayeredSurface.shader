Shader "PLL/Surfaces/Layered"
{
    Properties
    {
        _SurfaceMode("Render Mode", Float) = 0
        _Cutoff("Cutout Threshold", Range(0,1)) = 0.5
        _Opacity("Fluid Opacity", Range(0,1)) = 1
        _CurvatureInput("Curvature Input", Float) = 0
        _CurvatureContrast("Curvature Contrast", Float) = 1
        _CurvatureTexture("Imported Curvature", 2D) = "gray" {}
        _CurvatureUV("Curvature UV", Float) = 0
        _CurvatureTransform("Curvature Tiling / Offset", Vector) = (1,1,0,0)
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
        [HideInInspector] _PllCoordinates("Coordinates", Float) = 0
        [HideInInspector] _PllFlowCoverage("Coverage", Float) = 1
        _Layer0Albedo("Layer 0 Albedo", 2D) = "white" {}
        _Layer0Tint("Layer 0 Tint", Color) = (1,1,1,1)
        [Normal] _Layer0Normal("Layer 0 Normal", 2D) = "bump" {}
        _Layer0NormalStrength("Layer 0 Normal Strength", Float) = 1
        _Layer0Smoothness("Layer 0 Smoothness", Range(0,1)) = 0
        [Enum(UV,0,Triplanar,1)] _Layer0Projection("Layer 0 Projection", Float) = 0
        _Layer0UV("Layer 0 UV", Float) = 0
        _Layer0Transform("Layer 0 Tiling / Offset", Vector) = (1,1,0,0)
        _Layer0TileSize("Layer 0 Tile Size", Float) = 1
        _Layer0Blend("Layer 0 Triplanar Blend", Float) = 4
        _Layer1Albedo("Layer 1 Albedo", 2D) = "white" {}
        _Layer1Tint("Layer 1 Tint", Color) = (1,1,1,1)
        [Normal] _Layer1Normal("Layer 1 Normal", 2D) = "bump" {}
        _Layer1NormalStrength("Layer 1 Normal Strength", Float) = 1
        _Layer1Smoothness("Layer 1 Smoothness", Range(0,1)) = 0
        [Enum(UV,0,Triplanar,1)] _Layer1Projection("Layer 1 Projection", Float) = 0
        _Layer1UV("Layer 1 UV", Float) = 0
        _Layer1Transform("Layer 1 Tiling / Offset", Vector) = (1,1,0,0)
        _Layer1TileSize("Layer 1 Tile Size", Float) = 1
        _Layer1Blend("Layer 1 Triplanar Blend", Float) = 4
        [Toggle] _Layer1Enabled("Overlay 1 Enabled", Float) = 0
        _Layer1Strength("Overlay 1 Strength", Range(0,1)) = 1
        _Layer1Mask("Overlay 1 Filters", Float) = 1
        _Layer1Slope("Overlay 1 Slope Degrees", Vector) = (0,45,5,0)
        _Layer1Height("Overlay 1 Height", Vector) = (0,10,1,0)
        _Layer1Curvature("Overlay 1 Curvature", Vector) = (0,1,0.1,0)
        _Layer1VertexChannel("Overlay 1 Vertex Channel", Float) = 0
        _Layer1MaskTexture("Overlay 1 Mask Texture", 2D) = "white" {}
        _Layer1MaskChannel("Overlay 1 Mask Channel", Float) = 0
        _Layer1MaskUV("Overlay 1 Mask UV", Float) = 0
        _Layer1MaskTransform("Overlay 1 Mask Tiling / Offset", Vector) = (1,1,0,0)
        _Layer2Albedo("Layer 2 Albedo", 2D) = "white" {}
        _Layer2Tint("Layer 2 Tint", Color) = (1,1,1,1)
        [Normal] _Layer2Normal("Layer 2 Normal", 2D) = "bump" {}
        _Layer2NormalStrength("Layer 2 Normal Strength", Float) = 1
        _Layer2Smoothness("Layer 2 Smoothness", Range(0,1)) = 0
        [Enum(UV,0,Triplanar,1)] _Layer2Projection("Layer 2 Projection", Float) = 0
        _Layer2UV("Layer 2 UV", Float) = 0
        _Layer2Transform("Layer 2 Tiling / Offset", Vector) = (1,1,0,0)
        _Layer2TileSize("Layer 2 Tile Size", Float) = 1
        _Layer2Blend("Layer 2 Triplanar Blend", Float) = 4
        [Toggle] _Layer2Enabled("Overlay 2 Enabled", Float) = 0
        _Layer2Strength("Overlay 2 Strength", Range(0,1)) = 1
        _Layer2Mask("Overlay 2 Filters", Float) = 1
        _Layer2Slope("Overlay 2 Slope Degrees", Vector) = (0,45,5,0)
        _Layer2Height("Overlay 2 Height", Vector) = (0,10,1,0)
        _Layer2Curvature("Overlay 2 Curvature", Vector) = (0,1,0.1,0)
        _Layer2VertexChannel("Overlay 2 Vertex Channel", Float) = 0
        _Layer2MaskTexture("Overlay 2 Mask Texture", 2D) = "white" {}
        _Layer2MaskChannel("Overlay 2 Mask Channel", Float) = 0
        _Layer2MaskUV("Overlay 2 Mask UV", Float) = 0
        _Layer2MaskTransform("Overlay 2 Mask Tiling / Offset", Vector) = (1,1,0,0)
        _Layer3Albedo("Layer 3 Albedo", 2D) = "white" {}
        _Layer3Tint("Layer 3 Tint", Color) = (1,1,1,1)
        [Normal] _Layer3Normal("Layer 3 Normal", 2D) = "bump" {}
        _Layer3NormalStrength("Layer 3 Normal Strength", Float) = 1
        _Layer3Smoothness("Layer 3 Smoothness", Range(0,1)) = 0
        [Enum(UV,0,Triplanar,1)] _Layer3Projection("Layer 3 Projection", Float) = 0
        _Layer3UV("Layer 3 UV", Float) = 0
        _Layer3Transform("Layer 3 Tiling / Offset", Vector) = (1,1,0,0)
        _Layer3TileSize("Layer 3 Tile Size", Float) = 1
        _Layer3Blend("Layer 3 Triplanar Blend", Float) = 4
        [Toggle] _Layer3Enabled("Overlay 3 Enabled", Float) = 0
        _Layer3Strength("Overlay 3 Strength", Range(0,1)) = 1
        _Layer3Mask("Overlay 3 Filters", Float) = 1
        _Layer3Slope("Overlay 3 Slope Degrees", Vector) = (0,45,5,0)
        _Layer3Height("Overlay 3 Height", Vector) = (0,10,1,0)
        _Layer3Curvature("Overlay 3 Curvature", Vector) = (0,1,0.1,0)
        _Layer3VertexChannel("Overlay 3 Vertex Channel", Float) = 0
        _Layer3MaskTexture("Overlay 3 Mask Texture", 2D) = "white" {}
        _Layer3MaskChannel("Overlay 3 Mask Channel", Float) = 0
        _Layer3MaskUV("Overlay 3 Mask UV", Float) = 0
        _Layer3MaskTransform("Overlay 3 Mask Tiling / Offset", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #pragma target 5.0
        #pragma only_renderers d3d11
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "PllLayeredSurface.hlsl"
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
    CustomEditor "PLL.Creator.Editor.PllSurfaceMaterialInspector"
}
