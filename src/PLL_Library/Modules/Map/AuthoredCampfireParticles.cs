using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Library;

internal static class AuthoredCampfireParticles
{
    static readonly string[] Modules =
    {
        "main", "emission", "shape", "velocityOverLifetime", "limitVelocityOverLifetime", "inheritVelocity",
        "lifetimeByEmitterSpeed", "forceOverLifetime", "colorOverLifetime", "colorBySpeed", "sizeOverLifetime",
        "sizeBySpeed", "rotationOverLifetime", "rotationBySpeed", "noise", "textureSheetAnimation",
    };

    static readonly HashSet<string> Kept = new(StringComparer.Ordinal)
    {
        "playOnAwake", "customSimulationSpace", "meshRenderer", "skinnedMeshRenderer", "spriteRenderer",
    };

    static global::Campfire? cachedNative;

    [HarmonyPatch(typeof(global::Campfire), "Awake")]
    static class CampfireAwakePatch
    {
        [HarmonyPrefix]
        static void Prefix(global::Campfire __instance)
        {
            try { Apply(__instance); }
            catch (Exception Failure)
            {
                Plugin.PLLLog.LogWarning($"Campfire particles kept as authored: {Failure.Message}");
            }
        }
    }

    static void Apply(global::Campfire Campfire)
    {
        if (!Campfire || !Campfire.GetComponent<PllCampfireAuthoring>()) return;
        if (!NeedsLook(Campfire.fireParticles) && !NeedsLook(Campfire.smokeParticlesOff)
            && !NeedsLook(Campfire.smokeParticlesLit)) return;
        var Native = FindNative();
        if (!Native) return;
        CopyLook(Native!.fireParticles, Campfire.fireParticles);
        CopyLook(Native.smokeParticlesOff, Campfire.smokeParticlesOff);
        CopyLook(Native.smokeParticlesLit, Campfire.smokeParticlesLit);
    }

    static bool NeedsLook(ParticleSystem Target) =>
        Target && Target.TryGetComponent<ParticleSystemRenderer>(out var Renderer) && !Renderer.sharedMaterial;

    static global::Campfire? FindNative()
    {
        if (cachedNative && HasLook(cachedNative!)) return cachedNative;
        cachedNative = null;
        foreach (var Candidate in Object.FindObjectsByType<global::Campfire>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (Candidate && !Candidate.GetComponent<PllCampfireAuthoring>() && HasLook(Candidate))
                return cachedNative = Candidate;
        return null;
    }

    static bool HasLook(global::Campfire Campfire) =>
        Campfire.fireParticles && Campfire.smokeParticlesOff && Campfire.smokeParticlesLit
        && !NeedsLook(Campfire.fireParticles) && !NeedsLook(Campfire.smokeParticlesOff)
        && !NeedsLook(Campfire.smokeParticlesLit);

    static void CopyLook(ParticleSystem Source, ParticleSystem Target)
    {
        if (!Source || !NeedsLook(Target)) return;
        var WasPlaying = Target.isPlaying;
        if (WasPlaying) Target.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var Name in Modules)
        {
            var Property = typeof(ParticleSystem).GetProperty(Name, BindingFlags.Public | BindingFlags.Instance);
            if (Property == null) continue;
            CopyProperties(Property.GetValue(Source), Property.GetValue(Target));
        }
        var Bursts = new ParticleSystem.Burst[Source.emission.burstCount];
        Source.emission.GetBursts(Bursts);
        Target.emission.SetBursts(Bursts);
        var SourceRenderer = Source.GetComponent<ParticleSystemRenderer>();
        var TargetRenderer = Target.GetComponent<ParticleSystemRenderer>();
        if (SourceRenderer && TargetRenderer)
        {
            foreach (var Property in typeof(ParticleSystemRenderer).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                CopyProperty(Property, SourceRenderer, TargetRenderer);
            TargetRenderer.sharedMaterials = SourceRenderer.sharedMaterials;
            TargetRenderer.shadowCastingMode = SourceRenderer.shadowCastingMode;
            TargetRenderer.receiveShadows = SourceRenderer.receiveShadows;
            TargetRenderer.sortingOrder = SourceRenderer.sortingOrder;
        }
        if (WasPlaying) Target.Play(true);
    }

    static void CopyProperties(object? Source, object? Target)
    {
        if (Source == null || Target == null) return;
        foreach (var Property in Source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            CopyProperty(Property, Source, Target);
    }

    static void CopyProperty(PropertyInfo Property, object Source, object Target)
    {
        if (!Property.CanRead || !Property.CanWrite || Property.GetIndexParameters().Length != 0
            || Kept.Contains(Property.Name)
            || typeof(Component).IsAssignableFrom(Property.PropertyType)
            || typeof(GameObject).IsAssignableFrom(Property.PropertyType)
            || Property.GetCustomAttribute<ObsoleteAttribute>() != null) return;
        try { Property.SetValue(Target, Property.GetValue(Source)); }
        catch (Exception) { /* a setting this Unity build refuses at run time keeps its authored value */ }
    }
}
