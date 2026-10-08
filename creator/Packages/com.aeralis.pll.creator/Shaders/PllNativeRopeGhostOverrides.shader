Shader "PLL/Native Rope Ghost Overrides"
{
    Properties
    {
        [HDR] _Color0("Native Ghost Color Override (Flat Preview)", Color) = (0,2.0602896213531494,10.68062686920166,0)
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
                float4 _Color0;
            CBUFFER_END

            float4 Vert(float4 Position : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(Position.xyz);
            }

            float4 Fragment() : SV_Target
            {
                return float4(_Color0.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
