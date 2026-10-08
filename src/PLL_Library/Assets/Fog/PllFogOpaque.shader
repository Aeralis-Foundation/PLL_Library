Shader "PLL/Fog/Opaque"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #define PLL_FOG_BLEND_MODE 5
            #include "PllFogComposition.hlsl"

            float4 Fragment(Varyings Input) : SV_Target
            {
                return PllFogApply(Input.positionCS,
                    SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, Input.texcoord, 0));
            }
            ENDHLSL
        }
    }
}
