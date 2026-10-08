Shader "PLL/Native Rope Overrides"
{
    Properties
    {
        [HDR] _Tint("Native Tint Override (Flat Preview)", Color) = (0.6226415038108826,0.6226415038108826,0.6226415038108826,1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
            CBUFFER_END

            float4 Vert(float4 Position : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(Position.xyz);
            }

            float4 Fragment() : SV_Target
            {
                return float4(_Tint.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
