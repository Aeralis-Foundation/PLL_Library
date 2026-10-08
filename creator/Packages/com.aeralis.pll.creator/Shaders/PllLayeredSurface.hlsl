#ifndef PLL_LAYERED_SURFACE_INCLUDED
#define PLL_LAYERED_SURFACE_INCLUDED

TEXTURE2D(_CurvatureTexture);
TEXTURE2D(_Layer0Albedo); TEXTURE2D(_Layer0Normal);
TEXTURE2D(_Layer1Albedo); TEXTURE2D(_Layer1Normal);
TEXTURE2D(_Layer1MaskTexture);
TEXTURE2D(_Layer2Albedo); TEXTURE2D(_Layer2Normal);
TEXTURE2D(_Layer2MaskTexture);
TEXTURE2D(_Layer3Albedo); TEXTURE2D(_Layer3Normal);
TEXTURE2D(_Layer3MaskTexture);
TEXTURE3D(_LightMap); SAMPLER(sampler_LightMap);
float3 _GridRes, _GridOffset;
float _RaySpacing, _AmbienceMin, _AmbienceStrength;
float3 _LightDirection, _LightPosition;

CBUFFER_START(UnityPerMaterial)
    float _SurfaceMode, _Cutoff, _Opacity, _CurvatureInput, _CurvatureContrast, _CurvatureUV;
    float4 _CurvatureTransform;
    float _PllCoordinates, _PllFlowCoverage;
    float4x4 _PllWorldToFrame, _PllNormalToFrame, _PllFrameNormalToWorld;
    float4 _Layer0Tint, _Layer0Transform;
    float _Layer0NormalStrength, _Layer0Smoothness, _Layer0Projection, _Layer0UV, _Layer0TileSize, _Layer0Blend;
    float4 _Layer1Tint, _Layer1Transform;
    float _Layer1NormalStrength, _Layer1Smoothness, _Layer1Projection, _Layer1UV, _Layer1TileSize, _Layer1Blend;
    float _Layer1Enabled, _Layer1Strength, _Layer1Mask, _Layer1VertexChannel, _Layer1MaskChannel, _Layer1MaskUV;
    float4 _Layer1Slope, _Layer1Height, _Layer1Curvature, _Layer1MaskTransform;
    float4 _Layer2Tint, _Layer2Transform;
    float _Layer2NormalStrength, _Layer2Smoothness, _Layer2Projection, _Layer2UV, _Layer2TileSize, _Layer2Blend;
    float _Layer2Enabled, _Layer2Strength, _Layer2Mask, _Layer2VertexChannel, _Layer2MaskChannel, _Layer2MaskUV;
    float4 _Layer2Slope, _Layer2Height, _Layer2Curvature, _Layer2MaskTransform;
    float4 _Layer3Tint, _Layer3Transform;
    float _Layer3NormalStrength, _Layer3Smoothness, _Layer3Projection, _Layer3UV, _Layer3TileSize, _Layer3Blend;
    float _Layer3Enabled, _Layer3Strength, _Layer3Mask, _Layer3VertexChannel, _Layer3MaskChannel, _Layer3MaskUV;
    float4 _Layer3Slope, _Layer3Height, _Layer3Curvature, _Layer3MaskTransform;
CBUFFER_END

#define PLL_FOG_BLEND_MODE 0
#include "PllFogComposition.hlsl"

struct Attributes
{
    float4 PositionOS : POSITION;
    float3 NormalOS : NORMAL;
    float4 TangentOS : TANGENT;
    float2 UV0 : TEXCOORD0;
    float2 UV1 : TEXCOORD1;
    float2 UV2 : TEXCOORD2;
    float2 UV3 : TEXCOORD3;
    float2 UV4 : TEXCOORD4;
    float2 UV5 : TEXCOORD5;
    float2 UV6 : TEXCOORD6;
    float2 UV7 : TEXCOORD7;
    float4 Color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct Varyings
{
    float4 PositionCS : SV_POSITION;
    float3 PositionWS : TEXCOORD0;
    float3 NormalWS : TEXCOORD1;
    float4 TangentWS : TEXCOORD2;
    float4 UV01 : TEXCOORD3;
    float4 UV23 : TEXCOORD4;
    float4 UV45 : TEXCOORD5;
    float3 UV6Curvature : TEXCOORD6;
    float4 Color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
struct Surface
{
    float4 Albedo;
    float3 NormalWS;
    float Smoothness;
};

Varyings Vertex(Attributes Input)
{
    Varyings Output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(Input);
    UNITY_TRANSFER_INSTANCE_ID(Input, Output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(Output);
    VertexPositionInputs Position = GetVertexPositionInputs(Input.PositionOS.xyz);
    VertexNormalInputs Normal = GetVertexNormalInputs(Input.NormalOS, Input.TangentOS);
    Output.PositionCS = Position.positionCS; Output.PositionWS = Position.positionWS;
    Output.NormalWS = Normal.normalWS;
    Output.TangentWS = float4(Normal.tangentWS, Input.TangentOS.w * GetOddNegativeScale());
    Output.UV01 = float4(Input.UV0, Input.UV1); Output.UV23 = float4(Input.UV2, Input.UV3);
    Output.UV45 = float4(Input.UV4, Input.UV5); Output.UV6Curvature = float3(Input.UV6, Input.UV7.x);
    Output.Color = Input.Color;
    return Output;
}
Varyings ShadowVertex(Attributes Input)
{
    Varyings Output = Vertex(Input);
    float3 Direction = _LightDirection;
#ifdef _CASTING_PUNCTUAL_LIGHT_SHADOW
    Direction = normalize(_LightPosition - Output.PositionWS);
#endif
    Output.PositionCS = TransformWorldToHClip(ApplyShadowBias(Output.PositionWS, Output.NormalWS, Direction));
#if UNITY_REVERSED_Z
    Output.PositionCS.z = min(Output.PositionCS.z, UNITY_NEAR_CLIP_VALUE * Output.PositionCS.w);
#else
    Output.PositionCS.z = max(Output.PositionCS.z, UNITY_NEAR_CLIP_VALUE * Output.PositionCS.w);
#endif
    return Output;
}
float2 UV(Varyings Input, int Channel)
{
    switch (Channel)
    {
        case 0: return Input.UV01.xy;
        case 1: return Input.UV01.zw;
        case 2: return Input.UV23.xy;
        case 3: return Input.UV23.zw;
        case 4: return Input.UV45.xy;
        case 5: return Input.UV45.zw;
        default: return Input.UV6Curvature.xy;
    }
}
float Channel(float4 Value, int Index)
{
    return Index == 0 ? Value.x : Index == 1 ? Value.y : Index == 2 ? Value.z : Value.w;
}
float Window(float Value, float4 Interval)
{
    if (Interval.z == 0) return Value >= Interval.x && Value <= Interval.y ? 1 : 0;
    return saturate(smoothstep(Interval.x - Interval.z, Interval.x, Value)
        * (1 - smoothstep(Interval.y, Interval.y + Interval.z, Value)));
}
void Coordinates(Varyings Input, out float3 Position, out float3 Normal)
{
    if (_PllCoordinates == 0)
    {
        Position = TransformWorldToObject(Input.PositionWS);
        Normal = normalize(mul(Input.NormalWS, (float3x3)unity_ObjectToWorld));
    }
    else
    {
        Position = mul(_PllWorldToFrame, float4(Input.PositionWS, 1)).xyz;
        Normal = normalize(mul((float3x3)_PllNormalToFrame, Input.NormalWS));
    }
}
float3 WorldNormal(float3 Normal)
{
    return _PllCoordinates == 0 ? normalize(mul(Normal, (float3x3)unity_WorldToObject))
        : normalize(mul((float3x3)_PllFrameNormalToWorld, Normal));
}
float3 DecodeNormal(float4 Value, float Strength)
{
    if (Strength == 0) return float3(0,0,1);
    float3 Normal = UnpackNormal(Value);
    return normalize(Strength > 1 ? float3(Normal.xy, Normal.z / Strength)
        : float3(Normal.xy * Strength, Normal.z));
}
Surface SampleLayer(Varyings Input, float3 Position, float3 GeometricNormal,
    TEXTURE2D_PARAM(Albedo, AlbedoSampler), TEXTURE2D_PARAM(NormalMap, NormalSampler),
    float4 Tint, float NormalStrength, float Smoothness, float Projection,
    int UVChannel, float4 Transform, float TileSize, float Blend)
{
    Surface Result;
    Result.Smoothness = Smoothness;
    if (Projection == 0)
    {
        float2 Texcoord = UV(Input, UVChannel) * Transform.xy + Transform.zw;
        Result.Albedo = SAMPLE_TEXTURE2D(Albedo, AlbedoSampler, Texcoord) * Tint;
        float3 NormalTS = DecodeNormal(SAMPLE_TEXTURE2D(NormalMap, NormalSampler, Texcoord), NormalStrength);
        Result.NormalWS = normalize(Input.NormalWS);
        if (any(NormalTS.xy != 0))
        {
            float3 Tangent = normalize(Input.TangentWS.xyz);
            float3 Bitangent = normalize(cross(Input.NormalWS, Tangent)) * Input.TangentWS.w;
            Result.NormalWS = normalize(Tangent * NormalTS.x + Bitangent * NormalTS.y + Result.NormalWS * NormalTS.z);
        }
    }
    else
    {
        float3 Weights = abs(GeometricNormal);
        Weights = pow(Weights / max(Weights.x, max(Weights.y, Weights.z)), Blend);
        Weights /= Weights.x + Weights.y + Weights.z;
        float3 P = Position / TileSize;
        Result.Albedo = (SAMPLE_TEXTURE2D(Albedo, AlbedoSampler, P.zy) * Weights.x
            + SAMPLE_TEXTURE2D(Albedo, AlbedoSampler, P.xz) * Weights.y
            + SAMPLE_TEXTURE2D(Albedo, AlbedoSampler, P.xy) * Weights.z) * Tint;
        float3 X = DecodeNormal(SAMPLE_TEXTURE2D(NormalMap, NormalSampler, P.zy), NormalStrength);
        float3 Y = DecodeNormal(SAMPLE_TEXTURE2D(NormalMap, NormalSampler, P.xz), NormalStrength);
        float3 Z = DecodeNormal(SAMPLE_TEXTURE2D(NormalMap, NormalSampler, P.xy), NormalStrength);
        X = float3(X.xy + GeometricNormal.zy, abs(X.z) * GeometricNormal.x);
        Y = float3(Y.xy + GeometricNormal.xz, abs(Y.z) * GeometricNormal.y);
        Z = float3(Z.xy + GeometricNormal.xy, abs(Z.z) * GeometricNormal.z);
        Result.NormalWS = WorldNormal(normalize(X.zyx * Weights.x + Y.xzy * Weights.y + Z * Weights.z));
    }
    return Result;
}
float MaskWeight(Varyings Input, float3 Position, float3 Normal, float Curvature,
    uint Filters, float4 Slope, float4 Height, float4 CurvatureWindow, int VertexChannel,
    TEXTURE2D_PARAM(Mask, MaskSampler), int MaskChannel, int MaskUV, float4 MaskTransform)
{
    float Weight = 1;
    if ((Filters & 1) != 0) Weight *= Window(degrees(acos(clamp(Normal.y, -1, 1))), Slope);
    if ((Filters & 2) != 0) Weight *= Window(Position.y, Height);
    if ((Filters & 4) != 0) Weight *= Window(Curvature, CurvatureWindow);
    if ((Filters & 8) != 0) Weight *= saturate(Channel(Input.Color, VertexChannel));
    if ((Filters & 16) != 0)
        Weight *= saturate(Channel(SAMPLE_TEXTURE2D(Mask, MaskSampler,
            UV(Input, MaskUV) * MaskTransform.xy + MaskTransform.zw), MaskChannel));
    return Weight;
}
Surface Evaluate(Varyings Input)
{
    float3 Position, Normal;
    Coordinates(Input, Position, Normal);
    float Curvature = 0;
    if ((_Layer1Enabled != 0 && ((uint)_Layer1Mask & 4) != 0)
        || (_Layer2Enabled != 0 && ((uint)_Layer2Mask & 4) != 0)
        || (_Layer3Enabled != 0 && ((uint)_Layer3Mask & 4) != 0))
    {
        Curvature = Input.UV6Curvature.z;
        if (_CurvatureInput == 1)
            Curvature = SAMPLE_TEXTURE2D(_CurvatureTexture, sampler_LinearRepeat,
                UV(Input, (int)_CurvatureUV) * _CurvatureTransform.xy + _CurvatureTransform.zw).r * 2 - 1;
        Curvature *= _CurvatureContrast;
    }
    Surface Result = SampleLayer(Input, Position, Normal, TEXTURE2D_ARGS(_Layer0Albedo, sampler_LinearRepeat), TEXTURE2D_ARGS(_Layer0Normal, sampler_LinearRepeat), _Layer0Tint, _Layer0NormalStrength, _Layer0Smoothness, _Layer0Projection, _Layer0UV, _Layer0Transform, _Layer0TileSize, _Layer0Blend);
    if (_Layer1Enabled != 0)
    {
        float Weight = MaskWeight(Input, Position, Normal, Curvature, (uint)_Layer1Mask,
            _Layer1Slope, _Layer1Height, _Layer1Curvature, (int)_Layer1VertexChannel,
            TEXTURE2D_ARGS(_Layer1MaskTexture, sampler_LinearRepeat), (int)_Layer1MaskChannel,
            (int)_Layer1MaskUV, _Layer1MaskTransform) * _Layer1Strength;
        if (Weight > 0)
        {
            Surface Overlay = SampleLayer(Input, Position, Normal, TEXTURE2D_ARGS(_Layer1Albedo, sampler_LinearRepeat), TEXTURE2D_ARGS(_Layer1Normal, sampler_LinearRepeat), _Layer1Tint, _Layer1NormalStrength, _Layer1Smoothness, _Layer1Projection, _Layer1UV, _Layer1Transform, _Layer1TileSize, _Layer1Blend);
            Result.Albedo = lerp(Result.Albedo, Overlay.Albedo, Weight);
            Result.NormalWS = normalize(lerp(Result.NormalWS, Overlay.NormalWS, Weight));
            Result.Smoothness = lerp(Result.Smoothness, Overlay.Smoothness, Weight);
        }
    }
    if (_Layer2Enabled != 0)
    {
        float Weight = MaskWeight(Input, Position, Normal, Curvature, (uint)_Layer2Mask,
            _Layer2Slope, _Layer2Height, _Layer2Curvature, (int)_Layer2VertexChannel,
            TEXTURE2D_ARGS(_Layer2MaskTexture, sampler_LinearRepeat), (int)_Layer2MaskChannel,
            (int)_Layer2MaskUV, _Layer2MaskTransform) * _Layer2Strength;
        if (Weight > 0)
        {
            Surface Overlay = SampleLayer(Input, Position, Normal, TEXTURE2D_ARGS(_Layer2Albedo, sampler_LinearRepeat), TEXTURE2D_ARGS(_Layer2Normal, sampler_LinearRepeat), _Layer2Tint, _Layer2NormalStrength, _Layer2Smoothness, _Layer2Projection, _Layer2UV, _Layer2Transform, _Layer2TileSize, _Layer2Blend);
            Result.Albedo = lerp(Result.Albedo, Overlay.Albedo, Weight);
            Result.NormalWS = normalize(lerp(Result.NormalWS, Overlay.NormalWS, Weight));
            Result.Smoothness = lerp(Result.Smoothness, Overlay.Smoothness, Weight);
        }
    }
    if (_Layer3Enabled != 0)
    {
        float Weight = MaskWeight(Input, Position, Normal, Curvature, (uint)_Layer3Mask,
            _Layer3Slope, _Layer3Height, _Layer3Curvature, (int)_Layer3VertexChannel,
            TEXTURE2D_ARGS(_Layer3MaskTexture, sampler_LinearRepeat), (int)_Layer3MaskChannel,
            (int)_Layer3MaskUV, _Layer3MaskTransform) * _Layer3Strength;
        if (Weight > 0)
        {
            Surface Overlay = SampleLayer(Input, Position, Normal, TEXTURE2D_ARGS(_Layer3Albedo, sampler_LinearRepeat), TEXTURE2D_ARGS(_Layer3Normal, sampler_LinearRepeat), _Layer3Tint, _Layer3NormalStrength, _Layer3Smoothness, _Layer3Projection, _Layer3UV, _Layer3Transform, _Layer3TileSize, _Layer3Blend);
            Result.Albedo = lerp(Result.Albedo, Overlay.Albedo, Weight);
            Result.NormalWS = normalize(lerp(Result.NormalWS, Overlay.NormalWS, Weight));
            Result.Smoothness = lerp(Result.Smoothness, Overlay.Smoothness, Weight);
        }
    }
    return Result;
}
void ClipSurface(float Alpha)
{
    if (_SurfaceMode == 1) clip(Alpha - _Cutoff);
}
half4 DepthFragment(Varyings Input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(Input);
    ClipSurface(Evaluate(Input).Albedo.a);
    return Input.PositionCS.z;
}
half4 Fragment(Varyings Input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(Input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(Input);
    Surface Value = Evaluate(Input);
    ClipSurface(Value.Albedo.a);
    InputData Lighting = (InputData)0;
    Lighting.positionWS = Input.PositionWS;
    Lighting.normalWS = Value.NormalWS;
    Lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(Input.PositionWS);
    Lighting.shadowCoord = TransformWorldToShadowCoord(Input.PositionWS);
    Lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(Input.PositionCS);
    Lighting.shadowMask = 1;
    Lighting.vertexLighting = VertexLighting(Input.PositionWS, Value.NormalWS);
    Lighting.fogCoord = ComputeFogFactor(TransformWorldToHClip(Input.PositionWS).z);
    Lighting.bakedGI = SampleSH(Value.NormalWS);
    if (all(_GridRes > 0) && _RaySpacing > 0)
    {
        float3 SamplePosition = ((Input.PositionWS + normalize(Input.NormalWS) * 2 - _GridOffset) / _RaySpacing + 0.5) / _GridRes;
        float4 Volume = SAMPLE_TEXTURE3D(_LightMap, sampler_LightMap, SamplePosition);
        Volume = Volume * _AmbienceStrength * (1 - _AmbienceMin) + _AmbienceMin;
        Lighting.bakedGI = Volume.rgb + Lighting.bakedGI * Volume.a;
    }
    SurfaceData Lit = (SurfaceData)0;
    Lit.albedo = Value.Albedo.rgb; Lit.normalTS = float3(0,0,1);
    Lit.smoothness = Value.Smoothness; Lit.occlusion = 1;
    Lit.alpha = _SurfaceMode == 2 ? saturate(Value.Albedo.a * _Opacity * _PllFlowCoverage) : 1;
#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(Input.PositionCS, Lit, Lighting);
#endif
    half4 Color = UniversalFragmentPBR(Lighting, Lit);
    Color.rgb = MixFog(Color.rgb, Lighting.fogCoord);
    if (_SurfaceMode == 2) Color = PllFogApply(Input.PositionCS, Color);
    return Color;
}
#endif
