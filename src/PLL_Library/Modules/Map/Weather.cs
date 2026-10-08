using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal sealed class WeatherProjection
{
    internal WeatherState State { get; } = new();
    internal void Release() => State.Clear();

    internal void Quiesce()
    {
        RestoreWeatherProjection();
        foreach (var Storm in State.Storms)
        {
            var Visual = Storm.Visual;
            if (!Visual || State.QuiescedStorms.ContainsKey(Visual)) continue;
            State.QuiescedStorms.Add(Visual, Visual.enabled);
            QuiesceVisual(Visual);
        }
        ClearStormGlobals();
        foreach (var Visual in State.QuiescedStorms.Keys)
            if (Visual) Visual.enabled = false;
    }

    internal void ClearEnvironment(bool RestoreReusableOriginals)
    {
        if (RestoreReusableOriginals)
            foreach (var Storm in State.Storms)
                if (Storm.ReusableOriginal && Storm.Visual
                    && State.QuiescedStorms.TryGetValue(Storm.Visual, out var Enabled))
                    Storm.Visual.enabled = Enabled;
        State.Clear();
        ClearStormGlobals();
    }

    static void QuiesceVisual(StormVisual Visual)
    {
        Visual.observedPlayerInWindZone = false;
        foreach (var Part in Visual.part)
            if (Part) Part.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (Visual.quadRend) Visual.quadRend.enabled = false;
    }

    static void ClearStormGlobals()
    {
        Shader.SetGlobalFloat(GlobalWind, 0f);
        var DayNight = DayNightManager.instance;
        if (!DayNight) return;
        DayNight.rainstormWindFactor = 0f;
        DayNight.snowstormWindFactor = 0f;
    }

    internal readonly struct StormEntry
    {
        internal StormEntry(StormVisual Visual, WindChillZone Zone, bool ReusableOriginal)
        {
            this.Visual = Visual;
            this.Zone = Zone;
            this.ReusableOriginal = ReusableOriginal;
        }

        internal StormVisual Visual { get; }
        internal WindChillZone Zone { get; }
        internal bool ReusableOriginal { get; }
    }

    internal sealed class EnvironmentInventory
    {
        internal EnvironmentInventory(
            Transform Container,
            StormEntry[] Storms,
            (DisableIfWindActive Value, RootsWind Owner)[] DisabledWindOwners,
            (WindAffectedStatusEmitter Value, RootsWind Owner)[] WindEmitterOwners)
        {
            this.Container = Container;
            this.Storms = Storms;
            this.DisabledWindOwners = DisabledWindOwners;
            this.WindEmitterOwners = WindEmitterOwners;
        }

        internal Transform Container { get; }
        internal StormEntry[] Storms { get; }
        internal (DisableIfWindActive Value, RootsWind Owner)[] DisabledWindOwners { get; }
        internal (WindAffectedStatusEmitter Value, RootsWind Owner)[] WindEmitterOwners { get; }
    }

    internal sealed class WeatherState
    {
        internal StormEntry[] Storms = Array.Empty<StormEntry>();
        internal int StormFrame = -1;
        internal int SelectedStorm = -1;
        internal readonly Dictionary<int, EnvironmentInventory> Environments = new();
        internal readonly Dictionary<DisableIfWindActive, RootsWind> DisabledWindOwners = new();
        internal readonly Dictionary<WindAffectedStatusEmitter, RootsWind> WindEmitterOwners = new();
        internal readonly Dictionary<StormVisual, bool> QuiescedStorms = new();

        internal void Clear()
        {
            Storms = Array.Empty<StormEntry>();
            StormFrame = -1;
            SelectedStorm = -1;
            Environments.Clear();
            DisabledWindOwners.Clear();
            WindEmitterOwners.Clear();
            QuiescedStorms.Clear();
        }
    }

    static WeatherState Weather => Map.CurrentRun!.Weather.State;
    internal static readonly int GlobalWind = Shader.PropertyToID("GlobalWind");
    static readonly int WeatherBlend = Shader.PropertyToID("_WeatherBlend");
    static FogConfig? OwnedFog;
    static FogConfig? RollbackFog;
    static FogConfig? RollbackOwnedFog;
    static float RollbackWeatherBlend;
    static bool RollbackWeatherCaptured;
    internal static void BindEnvironment()
    {
        var Entries = new List<StormEntry>();
        var Visuals = new HashSet<int>();
        var Containers = new HashSet<int>();
        Weather.DisabledWindOwners.Clear();
        Weather.WindEmitterOwners.Clear();
        foreach (var Slot in Map.Slots)
        {
            if (Slot.Released) continue;
            var Root = Slot.Container;
            if (!Root) continue;
            var Id = Root.GetInstanceID();
            if (!Containers.Add(Id)) continue;
            var Cached = Weather.Environments.TryGetValue(Id, out var Inventory);
            if (Cached && !ReferenceEquals(Inventory.Container, Root))
            {
                Weather.Environments.Remove(Id);
                Cached = false;
            }
            if (!Cached)
            {
                if (Map.CurrentRun!.IsDeferredSlot(Slot.Index) && !LazyEnvironmentReady(Slot.Occurrence)) continue;
                Inventory = CaptureEnvironment(Root, !Slot.IsClone);
                Weather.Environments[Id] = Inventory;
            }
            foreach (var Storm in Inventory.Storms)
                if (Storm.Visual && Visuals.Add(Storm.Visual.GetInstanceID())) Entries.Add(Storm);
            foreach (var Pair in Inventory.DisabledWindOwners)
                if (Pair.Value && Pair.Owner) Weather.DisabledWindOwners[Pair.Value] = Pair.Owner;
            foreach (var Pair in Inventory.WindEmitterOwners)
                if (Pair.Value && Pair.Owner) Weather.WindEmitterOwners[Pair.Value] = Pair.Owner;
        }
        List<int>? Stale = null;
        foreach (var Pair in Weather.Environments)
            if (!Containers.Contains(Pair.Key) || !Pair.Value.Container)
                (Stale ??= new List<int>()).Add(Pair.Key);
        if (Stale is not null)
            foreach (var Id in Stale) Weather.Environments.Remove(Id);
        Weather.Storms = Entries.ToArray();
        Weather.StormFrame = -1;
        Weather.SelectedStorm = -1;
    }

    internal static void InvalidateEnvironment(FeatureOccurrence Occurrence)
    {
        var Id = Occurrence.Container.GetInstanceID();
        if (Weather.Environments.TryGetValue(Id, out var Inventory)
            && !ReferenceEquals(Inventory.Container, Occurrence.Container))
            throw new InvalidOperationException("A weather occurrence changed its captured container.");
        Weather.Environments.Remove(Id); BindEnvironment();
    }

    static bool LazyEnvironmentReady(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots)
            if (!Slot.FeaturesReady) return false;
        foreach (var Root in Occurrence.Roots)
            if (Root && Root.gameObject.activeInHierarchy) return true;
        return false;
    }

    static EnvironmentInventory CaptureEnvironment(Transform Root, bool ReusableOriginal)
    {
        var Storms = new List<StormEntry>();
        foreach (var Visual in Root.GetComponentsInChildren<StormVisual>(true))
        {
            if (!Visual) continue;
            var Zone = Visual.zone;
            if (!Zone) Zone = Visual.GetComponentInParent<WindChillZone>(true);
            if (Zone) Storms.Add(new StormEntry(Visual, Zone, ReusableOriginal));
        }

        var Roots = Root.GetComponentsInChildren<RootsWind>(true);
        if (Roots.Length == 0)
            return new EnvironmentInventory(
                Root, Storms.ToArray(),
                Array.Empty<(DisableIfWindActive, RootsWind)>(),
                Array.Empty<(WindAffectedStatusEmitter, RootsWind)>());
        var DisabledWindOwners = new List<(DisableIfWindActive Value, RootsWind Owner)>();
        foreach (var Value in Root.GetComponentsInChildren<DisableIfWindActive>(true))
            if (Value) DisabledWindOwners.Add((Value, Closest(Value.transform, Roots)));
        var WindEmitterOwners = new List<(WindAffectedStatusEmitter Value, RootsWind Owner)>();
        foreach (var Value in Root.GetComponentsInChildren<WindAffectedStatusEmitter>(true))
            if (Value) WindEmitterOwners.Add((Value, Closest(Value.transform, Roots)));
        return new EnvironmentInventory(
            Root, Storms.ToArray(), DisabledWindOwners.ToArray(), WindEmitterOwners.ToArray());
    }

    static RootsWind Closest(Transform Value, IReadOnlyList<RootsWind> Roots)
    {
        var Result = Roots[0];
        var Distance = (Result.transform.position - Value.position).sqrMagnitude;
        for (var i = 1; i < Roots.Count; i++)
        {
            var Current = (Roots[i].transform.position - Value.position).sqrMagnitude;
            if (Current >= Distance) continue;
            Result = Roots[i];
            Distance = Current;
        }
        return Result;
    }

    static bool ValidStorm(int Index) => Index >= 0 && Index < Weather.Storms.Length
        && Weather.Storms[Index].Visual && Weather.Storms[Index].Visual.isActiveAndEnabled
        && Weather.Storms[Index].Zone && Weather.Storms[Index].Zone.isActiveAndEnabled;

    internal static StormVisual? SelectStorm()
    {
        if (!Map.HasPublishedLayout) return null;
        if (Weather.StormFrame == Time.frameCount) return ValidStorm(Weather.SelectedStorm) ? Weather.Storms[Weather.SelectedStorm].Visual : null;
        Weather.StormFrame = Time.frameCount;
        var Character = global::Character.observedCharacter;
        var Selected = -1;
        var SelectedActive = false;
        var Distance = float.PositiveInfinity;
        if (Character)
        {
            var Center = Character.Center;
            for (var i = 0; i < Weather.Storms.Length; i++)
            {
                if (!ValidStorm(i)) continue;
                var Bounds = Weather.Storms[i].Zone.windZoneBounds;
                if (!Bounds.Contains(Center)) continue;
                var Active = Weather.Storms[i].Zone.windActive;
                var Current = (Bounds.center - Center).sqrMagnitude;
                if (Selected >= 0 && (SelectedActive && !Active || SelectedActive == Active && Current >= Distance)) continue;
                Selected = i;
                SelectedActive = Active;
                Distance = Current;
            }
        }
        if (Character) Weather.SelectedStorm = Selected;
        else if (!ValidStorm(Weather.SelectedStorm)) Weather.SelectedStorm = -1;
        return Weather.SelectedStorm >= 0 ? Weather.Storms[Weather.SelectedStorm].Visual : null;
    }

    internal static void PublishStorm()
    {
        if (!Map.Active) return;
        var Visual = SelectStorm();
        var Type = Visual ? Visual.stormType : (StormVisual.StormType)(-1);
        if (DayNightManager.instance)
        {
            DayNightManager.instance.rainstormWindFactor = Type is StormVisual.StormType.Rain or StormVisual.StormType.Wind
                ? Visual!.windFactor
                : 0f;
            DayNightManager.instance.snowstormWindFactor = Type == StormVisual.StormType.Snow
                ? Visual!.windFactor
                : 0f;
        }
        Shader.SetGlobalFloat(GlobalWind, Type == StormVisual.StormType.Wind
            ? Visual!.windIntensity
            : 0f);

        var Character = global::Character.observedCharacter;
        if (!Character || !Visual) return;
        var Audio = Character.refs.animations.stormAudio;
        if (!Audio) return;
        if (Type == StormVisual.StormType.Rain) Audio.rainVisual = Visual;
        else if (Type == StormVisual.StormType.Snow) Audio.stormVisual = Visual;
        else if (Type == StormVisual.StormType.Wind) Audio.windVisual = Visual;
    }

    internal static void CaptureWeather()
    {
        RollbackFog = FogConfig.currentFog;
        RollbackOwnedFog = OwnedFog;
        RollbackWeatherBlend = Shader.GetGlobalFloat(WeatherBlend);
        RollbackWeatherCaptured = true;
    }

    internal static void BindWeather()
    {
        if (!Map.HasPublishedLayout) RestoreWeatherProjection();
    }

    internal static void CommitWeather()
    {
        RollbackFog = null;
        RollbackOwnedFog = null;
        RollbackWeatherCaptured = false;
    }

    internal static void RollbackWeather()
    {
        Exception? Failure = null;
        try { RestoreWeatherProjection(); }
        catch (Exception Exception) { Failure = Exception; }
        try
        {
            if (RollbackWeatherCaptured)
            {
                FogConfig.currentFog = RollbackFog!;
                OwnedFog = RollbackOwnedFog;
                Shader.SetGlobalFloat(WeatherBlend, RollbackWeatherBlend);
            }
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("Weather projection and snapshot rollback both failed.", Failure, Exception);
        }
        finally
        {
            RollbackFog = null;
            RollbackOwnedFog = null;
            RollbackWeatherCaptured = false;
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    static FogConfig? FogFor(StormVisual? Visual)
    {
        if (!Visual) return null;
        var Fog = Visual!.fogConfig;
        return Fog ? Fog : Visual!.GetComponentInParent<FogConfig>(true);
    }

    static void SuppressStormFog(StormVisual Visual)
    {
        var Fog = FogFor(Visual);
        if (!Fog || FogConfig.currentFog != Fog) return;
        FogConfig.currentFog = null!;
        OwnedFog = null;
        Shader.SetGlobalFloat(WeatherBlend, 0f);
    }

    static void RestoreWeatherProjection()
    {
        if (OwnedFog is null) return;
        if (FogConfig.currentFog == OwnedFog || !FogConfig.currentFog)
        {
            FogConfig.currentFog = null!;
            Shader.SetGlobalFloat(WeatherBlend, 0f);
        }
        OwnedFog = null;
    }

    [HarmonyPatch(typeof(FogConfig), nameof(FogConfig.SetFog))]
    static class FogConfigSetPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FogConfig __instance)
        {
            if (!Map.HasPublishedLayout)
            {
                OwnedFog = null;
                return true;
            }
            var Visual = SelectStorm();
            if (FogFor(Visual) != __instance) return false;
            OwnedFog = __instance;
            return true;
        }
    }

    [HarmonyPatch(typeof(FogConfig), "Update")]
    static class FogConfigUpdatePatch
    {
        [HarmonyPrefix]
        static void Prefix()
        {
            if (Map.HasPublishedLayout || OwnedFog is null) return;
            RestoreWeatherProjection();
        }
    }

    [HarmonyPatch(typeof(StormVisual), "LateUpdate")]
    static class StormVisualUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(StormVisual __instance)
        {
            if (global::Character.observedCharacter) return true;
            SuppressStormFog(__instance);
            QuiesceVisual(__instance);
            ClearStormGlobals();
            return false;
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            if (global::Character.observedCharacter) PublishStorm();
        }
    }

    [HarmonyPatch(typeof(StormVisual), "OnDisable")]
    static class StormVisualDisablePatch
    {
        [HarmonyPostfix]
        static void Postfix(StormVisual __instance)
        {
            if (!Map.HasPublishedLayout) return;
            __instance.observedPlayerInWindZone = false;
            SuppressStormFog(__instance);
            Weather.StormFrame = -1;
            PublishStorm();
        }
    }

    [HarmonyPatch(typeof(DisableIfWindActive), "FixedUpdate")]
    static class DisableIfWindActivePatch
    {
        [HarmonyPrefix]
        static void Prefix(DisableIfWindActive __instance, out RootsWind? __state)
        {
            __state = RootsWind.instance;
            if (Map.Active && Weather.DisabledWindOwners.TryGetValue(__instance, out var Owner) && Owner)
                RootsWind.instance = Owner;
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception, RootsWind? __state)
        {
            RootsWind.instance = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WindAffectedStatusEmitter), "FixedUpdate")]
    static class WindAffectedStatusEmitterPatch
    {
        [HarmonyPrefix]
        static void Prefix(WindAffectedStatusEmitter __instance, out RootsWind? __state)
        {
            __state = RootsWind.instance;
            if (Map.Active && Weather.WindEmitterOwners.TryGetValue(__instance, out var Owner) && Owner)
                RootsWind.instance = Owner;
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception, RootsWind? __state)
        {
            RootsWind.instance = __state;
            return __exception;
        }
    }
}
