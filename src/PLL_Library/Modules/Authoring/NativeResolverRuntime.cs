using System;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class NativeResolverRuntime
{
    internal static void ConfigureWater(
        GameObject Root,
        PllZoneVolume Volume,
        WaterZone Zone,
        MeshRenderer Plane,
        float MovementShallow,
        float MovementDeep,
        float AnimationShallow,
        float AnimationDeep)
    {
        var Up = Root.transform.TransformVector(Vector3.up);
        if (!PositiveFinite(Up.magnitude) || Vector3.Angle(Up, Vector3.up) > 0.01f)
            throw new InvalidOperationException("A Water resolver's placed positive-Y axis is not horizontal.");
        var Bounds = NativeResolverVolume.WorldBounds(Root.transform, Volume);
        Zone.transform.position = Bounds.center;
        Zone.waterDepth = Bounds.size.y;
        Zone.zoneBounds = Bounds;
        Zone.movementModifierShallow = MovementShallow;
        Zone.movementModifierDeep = MovementDeep;
        Zone.animModShallow = AnimationShallow;
        Zone.animModDeep = AnimationDeep;
        if (!Zone.waterPlane || Zone.waterPlane != Plane)
            throw new InvalidOperationException("A Water resolver lost its creator-owned surface renderer.");
    }

    internal static void ConfigureWeather(
        PLLNativeResolverKind Kind,
        GameObject Root,
        PllZoneVolume Volume,
        PhotonView View,
        WindChillZone Zone)
    {
        NativeResolverSourceCatalog.CopyWeather(Kind, Zone);
        var Bounds = NativeResolverVolume.WorldBounds(Root.transform, Volume);
        Zone.transform.position = Bounds.center;
        Zone.windZoneBounds = Bounds;
        ResetWeather(Zone);
        if (!View || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
            throw new InvalidOperationException("A weather resolver PhotonView was not published before activation.");
    }

    internal static void ConfigureSporeCloud(
        GameObject Root,
        PllZoneVolume Volume,
        WindAffectedStatusEmitter Emitter,
        DisableBasedOnRunSettings Setting)
    {
        var Radius = NativeResolverVolume.WorldSphereRadius(Root.transform, Volume);
        var SourceRadius = NativeResolverSourceCatalog.SporeCloudRadius;
        if (Mathf.Abs(Radius - SourceRadius) > 0.01f)
            throw new InvalidOperationException(
                $"A SporeCloud resolver has world radius {Radius}, expected {SourceRadius}.");
        NativeResolverSourceCatalog.CopySporeCloud(Emitter, Setting);
        Emitter.transform.position = Root.transform.TransformPoint(Volume.Center);
        Emitter.transform.rotation = Root.transform.rotation;
        CleanupSporeCloud(Emitter);
    }

    internal static void ResetWeather(WindChillZone Zone)
    {
        Zone.StormProgress = 0f;
        Zone.timeUntilStorm = 0f;
        Zone.currentWindDirection = Vector3.zero;
        Zone.timeUntilNextWind = 0f;
        Zone.untilSwitch = 0f;
        Zone.hasBeenActiveFor = 0f;
        Zone.localCharacterInsideBounds = false;
        Zone.observedCharacterInsideBounds = false;
        Zone.windActive = false;
        Zone.windPlayerFactor = 0f;
        Zone.windIntensity = 0f;
        Zone.currentForceMult = 0f;
        Zone.hitInfo = default;
    }

    internal static void CleanupSporeCloud(WindAffectedStatusEmitter Emitter)
    {
        if (Emitter is null) return;
        var Identifier = Emitter.overlapIdentifier ?? string.Empty;
        var Overlaps = StatusEmitter.overlapPreventedStatusEmitters;
        var OwnsOverlap = Overlaps is not null && Overlaps.TryGetValue(Identifier, out var Owner)
            && ReferenceEquals(Owner, Emitter);
        Exception? Failure = null;
        if (Emitter.inZone && OwnsOverlap && GUIManager.instance && GUIManager.instance.sporesWarning)
            try { GUIManager.instance.sporesWarning.EndFX(); }
            catch (Exception Exception) { Failure = Exception; }
        if (OwnsOverlap) Overlaps!.Remove(Identifier);
        Emitter.inZone = false;
        Emitter.isOverlapPriority = false;
        Emitter.emitterDisabledByWind = false;
        Emitter.debug = 0f;
        Emitter.timeSinceLastTick = 0f;
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    static bool IsTranslatedWeather(WindChillZone Value)
    {
        if (!Value || !Value.transform.parent
            || !string.Equals(Value.name, "__PLL_NativeWeather", StringComparison.Ordinal)) return false;
        var Resolver = Value.transform.parent.GetComponent<PLLNativeResolver>();
        return Resolver && Resolver.Root == Resolver.gameObject
            && Resolver.Kind is PLLNativeResolverKind.Rain or PLLNativeResolverKind.Wind or PLLNativeResolverKind.Snow;
    }
    static bool PositiveFinite(float Value) => Value > 0f && float.IsFinite(Value);

    [HarmonyPatch(typeof(WindChillZone), "Awake")]
    static class PreserveWeatherSingletonPatch
    {
        static void Prefix(out WindChillZone? __state) => __state = WindChillZone.instance;

        static void Postfix(WindChillZone __instance, WindChillZone? __state)
        {
            if (IsTranslatedWeather(__instance) && ReferenceEquals(WindChillZone.instance, __instance))
                WindChillZone.instance = __state;
        }
    }
}
