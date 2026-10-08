#ifndef PLL_FOG_COMPOSITION_INCLUDED
#define PLL_FOG_COMPOSITION_INCLUDED

#ifdef PLL_FOG_ASSEMBLY_KERNEL
#define PLL_FOG_VIEW_BINDING : register(b13)
#define PLL_FOG_CONTRIBUTION_BINDING : register(t126)
#define PLL_FOG_DEPTH_BINDING : register(t127)
#else
#define PLL_FOG_VIEW_BINDING
#define PLL_FOG_CONTRIBUTION_BINDING
#define PLL_FOG_DEPTH_BINDING
#endif

cbuffer PllFogView PLL_FOG_VIEW_BINDING
{
    float4 PllFogViewport;
    float4 PllFogInverseViewProjection0;
    float4 PllFogInverseViewProjection1;
    float4 PllFogInverseViewProjection2;
    float4 PllFogInverseViewProjection3;
    float4 PllFogCamera;
};

Texture2D<float4> PllFogContributions PLL_FOG_CONTRIBUTION_BINDING;
Texture2D<float> PllFogDepth PLL_FOG_DEPTH_BINDING;

float4 PllFogApply(float4 Position, float4 NativeColor)
{
    uint Count = (uint)PllFogCamera.w;
    if (Count == 0) return NativeColor;
    float Depth = Position.z;
#if PLL_FOG_BLEND_MODE >= 4
    Depth = PllFogDepth.Load(int3(Position.xy, 0));
    bool Sky = Depth == PllFogViewport.w;
#endif
    float4 Clip = float4(Position.x * PllFogViewport.x * 2 - 1,
        1 - Position.y * PllFogViewport.y * 2, Depth, 1);
    float4 World = float4(dot(PllFogInverseViewProjection0, Clip),
        dot(PllFogInverseViewProjection1, Clip),
        dot(PllFogInverseViewProjection2, Clip),
        dot(PllFogInverseViewProjection3, Clip));
    float Distance = length(World.xyz / World.w - PllFogCamera.xyz);
#if PLL_FOG_BLEND_MODE >= 4
    if (Sky) Distance = 3.402823466e+38f;
#endif
    float4 Fog = 0;
    float Remaining = 1;
    uint Width = (uint)PllFogViewport.z;
    [loop]
    for (uint i = Count; i > 0 && Remaining != 0;)
    {
        i--;
        uint Texel = i * 2;
        float4 Contribution = PllFogContributions.Load(int3(Texel % Width, Texel / Width, 0));
        Texel++;
        float Limit = PllFogContributions.Load(int3(Texel % Width, Texel / Width, 0)).x;
        float x = saturate(Distance / Limit);
        float Smooth = x * x * (3 - 2 * x);
        float Opacity = Contribution.w * (1 - (1 - Smooth) * (1 - Smooth));
        Fog.rgb += Contribution.rgb * (Opacity * Remaining);
        Remaining *= 1 - Opacity;
    }
    Fog.a = 1 - Remaining;
#if PLL_FOG_BLEND_MODE == 1
    NativeColor.rgb = NativeColor.rgb * (1 - Fog.a) + Fog.rgb * NativeColor.a;
#elif PLL_FOG_BLEND_MODE == 2
    NativeColor.rgb *= 1 - Fog.a;
#elif PLL_FOG_BLEND_MODE == 3
    NativeColor.rgb = NativeColor.rgb * (1 - Fog.a) + Fog.a;
#else
    NativeColor.rgb = NativeColor.rgb * (1 - Fog.a) + Fog.rgb;
#endif
    return NativeColor;
}

#endif
