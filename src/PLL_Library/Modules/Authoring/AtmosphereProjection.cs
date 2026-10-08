using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal static class AtmosphereProjection
{
    static readonly int FogTextureId = Shader.PropertyToID("FogTexture");
    static AmbienceManager? SkyboxOwner;
    static Material? AppliedSkybox;
    static Material? RestoreSkybox;
    static Texture2D? AppliedFogTexture;
    static Texture? RestoreFogTexture;
    static bool OwnsSkybox;
    static bool OwnsFogTexture;

    internal static void Apply(ResolvedAtmosphere? Atmosphere)
    {
        if (OwnsSkybox || OwnsFogTexture)
            throw new InvalidOperationException("An atmosphere projection is already active.");
        if (Atmosphere is null) return;
        try
        {
            if (Atmosphere.SkyboxMaterial is { } SkyboxMaterial)
            {
                var Ambience = AmbienceManager.instance;
                if (!Ambience)
                    throw new InvalidOperationException(
                        "The current atmosphere cannot apply a skybox before AmbienceManager exists.");
                SkyboxOwner = Ambience;
                RestoreSkybox = Ambience.skyboxMaterial;
                AppliedSkybox = SkyboxMaterial;
                OwnsSkybox = true;
                Ambience.skyboxMaterial = SkyboxMaterial;
            }
            if (Atmosphere.FogTexture is { } FogTexture)
            {
                RestoreFogTexture = Shader.GetGlobalTexture(FogTextureId);
                AppliedFogTexture = FogTexture;
                OwnsFogTexture = true;
                Shader.SetGlobalTexture(FogTextureId, FogTexture);
            }
        }
        catch (Exception Failure)
        {
            try { Release(); }
            catch (Exception CleanupFailure)
            {
                throw new AggregateException(
                    "Atmosphere activation and rollback both failed.",
                    Failure,
                    CleanupFailure);
            }
            throw;
        }
    }

    internal static void Release()
    {
        var SkyboxOwnerValue = SkyboxOwner;
        var RestoreSkyboxValue = RestoreSkybox;
        var AppliedSkyboxValue = AppliedSkybox;
        var RestoreFogTextureValue = RestoreFogTexture;
        var AppliedFogTextureValue = AppliedFogTexture;
        var HadSkybox = OwnsSkybox;
        var HadFogTexture = OwnsFogTexture;
        ClearState();

        List<Exception>? Failures = null;
        if (HadSkybox && SkyboxOwnerValue
            && ReferenceEquals(SkyboxOwnerValue!.skyboxMaterial, AppliedSkyboxValue))
            AuthoringCleanup.Capture(
                ref Failures,
                () => SkyboxOwnerValue!.skyboxMaterial = RestoreSkyboxValue);
        if (HadFogTexture
            && ReferenceEquals(Shader.GetGlobalTexture(FogTextureId), AppliedFogTextureValue))
            AuthoringCleanup.Capture(
                ref Failures,
                () => Shader.SetGlobalTexture(FogTextureId, RestoreFogTextureValue));
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Atmosphere cleanup failed.", Failures);
    }

    static void ClearState()
    {
        SkyboxOwner = null;
        AppliedSkybox = null;
        RestoreSkybox = null;
        AppliedFogTexture = null;
        RestoreFogTexture = null;
        OwnsSkybox = false;
        OwnsFogTexture = false;
    }
}
