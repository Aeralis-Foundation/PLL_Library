using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class AuthoringPresentationRuntime
{
    static AudioSource? AmbienceSource;
    static AudioSource? OneShotSource;
    static int AmbienceIndex;
    static bool AmbientApplied;
    static Color AmbientBase;

    internal static void Initialize(GameObject Owner)
    {
        AmbienceSource = Owner.AddComponent<AudioSource>();
        AmbienceSource.playOnAwake = false;
        AmbienceSource.loop = false;
        AmbienceSource.spatialBlend = 0f;
        OneShotSource = Owner.AddComponent<AudioSource>();
        OneShotSource.playOnAwake = false;
        OneShotSource.loop = false;
        OneShotSource.spatialBlend = 0f;
    }

    internal static void Bind(RuntimeBiomeAuthoring? Occurrence)
    {
        StopAmbience();
        AtmosphereProjection.Apply(Occurrence?.Resolved.Atmosphere);
        if (Occurrence is null) return;
        PlayNextAmbience(Occurrence);
    }

    internal static FogZoneState[] CaptureFogSources(Dictionary<int, RuntimeBiomeAuthoring>.ValueCollection Biomes)
    {
        var Count = 0;
        foreach (var Biome in Biomes)
            if (!Biome.Slot.Released) Count += Biome.FogSources.Length;
        var Sources = new FogZoneState[Count];
        var Index = 0;
        foreach (var Biome in Biomes)
            if (!Biome.Slot.Released)
                foreach (var Source in Biome.FogSources) Sources[Index++] = Source.State;
        return Sources;
    }

    internal static void Tick(RuntimeBiomeAuthoring Occurrence)
    {
        if (AmbienceSource && Occurrence.Contract.Ambience.Count > 1 && !AmbienceSource.isPlaying)
            PlayNextAmbience(Occurrence);
    }

    internal static void Leave()
    {
        List<Exception>? Failures = null;
        AuthoringCleanup.Capture(ref Failures, StopAmbience);
        if (OneShotSource) AuthoringCleanup.Capture(ref Failures, () => OneShotSource.Stop());
        AuthoringCleanup.Capture(ref Failures, AtmosphereProjection.Release);
        AuthoringCleanup.Capture(ref Failures, RestoreAmbientColor);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Visual transition cleanup failed.", Failures);
    }

    internal static void PlayOneShot(AudioClip Clip)
    {
        if (Clip && OneShotSource) OneShotSource.PlayOneShot(Clip);
    }

    internal static void RestoreAmbientColor()
    {
        if (!AmbientApplied) return;
        RenderSettings.ambientLight = AmbientBase;
        AmbientApplied = false;
    }

    internal static void Shutdown()
    {
        try
        {
            if (AmbienceSource) UnityEngine.Object.Destroy(AmbienceSource);
            if (OneShotSource) UnityEngine.Object.Destroy(OneShotSource);
        }
        finally
        {
            AmbienceSource = null;
            OneShotSource = null;
        }
    }

    static void StopAmbience()
    {
        if (!AmbienceSource) return;
        AmbienceSource.Stop();
        AmbienceSource.clip = null;
        AmbienceIndex = 0;
    }

    static void PlayNextAmbience(RuntimeBiomeAuthoring Occurrence)
    {
        if (!AmbienceSource || Occurrence.Contract.Ambience.Count == 0) return;
        AmbienceSource.clip = Occurrence.Contract.Ambience[AmbienceIndex++ % Occurrence.Contract.Ambience.Count];
        AmbienceSource.loop = Occurrence.Contract.Ambience.Count == 1;
        AmbienceSource.Play();
    }

    static void ApplyAmbientColor(RuntimeBiomeAuthoring? Occurrence)
    {
        if (Occurrence is null || !Occurrence.IsCurrent) return;
        AmbientBase = RenderSettings.ambientLight;
        var Value = Multiply(AmbientBase, Occurrence.Contract.Color);
        foreach (var Effect in Occurrence.Effects)
            if (Effect.Coverage > 0f) Value = Multiply(Value,
                Color.LerpUnclamped(Color.white, Effect.Authoring.Color, Effect.Coverage));
        RenderSettings.ambientLight = Value;
        AmbientApplied = true;
    }

    static Color Multiply(Color Left, Color Right) => new(
        Left.r * Right.r,
        Left.g * Right.g,
        Left.b * Right.b,
        Left.a * Right.a);

    [HarmonyPatch(typeof(AmbienceManager), "Update")]
    static class AmbientColorPatch
    {
        [HarmonyPrefix]
        static void Prefix() => RestoreAmbientColor();

        [HarmonyPostfix]
        static void Postfix() => ApplyAmbientColor(AuthoringRuntime.PresentationOccurrence);
    }
}
