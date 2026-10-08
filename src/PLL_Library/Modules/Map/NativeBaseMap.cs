using System;
using System.Collections.Generic;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal sealed class NativeBaseMap
{
    readonly struct FogSphereState
    {
        internal FogSphereState(FogSphere Value, bool DisableObject)
        {
            Sphere = Value;
            this.DisableObject = DisableObject;
            Active = Value.gameObject.activeSelf;
            Enable = Value.ENABLE;
            RevealAmount = Value.REVEAL_AMOUNT;
        }

        internal FogSphere Sphere { get; }
        internal bool DisableObject { get; }
        internal bool Active { get; }
        internal float Enable { get; }
        internal float RevealAmount { get; }
    }

    static NativeBaseMap? Current;
    readonly MapHandler Handler;
    readonly Dictionary<GameObject, bool> ActiveStates = new();
    readonly List<FogSphereState> FogSpheres = new();
    OrbFogHandler? FogOwner;
    bool Released;

    NativeBaseMap(MapHandler HandlerValue) => Handler = HandlerValue;

    internal static bool FoglessActive => Current?.Fogless == true;
    internal static bool Owns(MapHandler Handler) => Current?.OwnsHandler(Handler) == true;
    internal static bool OwnsScene(int SceneHandle) => Current?.OwnsSceneValue(SceneHandle) == true;
    internal static bool OwnsFog(OrbFogHandler Handler) => Current?.OwnsFogValue(Handler) == true;
    internal static void ApplyCurrent() => Current?.Apply();
    internal static bool ApplyFogless(OrbFogHandler Handler) => Current?.ApplyFoglessValue(Handler) == true;
    internal static bool ApplyFogless(FogSphere Sphere) => Current?.ApplyFoglessValue(Sphere) == true;
    internal static bool RequiresProcessing(ResolvedRunConfiguration Configuration) => RequiresProcessing(
        Configuration.Environment.Fogless, Configuration.Environment.NoRisingGhosts,
        Configuration.Environment.NoSleepFog, Configuration.Environment.NoWater,
        Configuration.Environment.NoVoidWater, Configuration.Environment.NoPlane);

    internal static bool RequiresLocalProcessing() => RequiresProcessing(
        FileManager.Fogless.Value, FileManager.NoRisingGhosts.Value,
        FileManager.NoSleepFog.Value, FileManager.NoWater.Value,
        FileManager.NoVoidWater.Value, FileManager.NoPlane.Value);

    static bool RequiresProcessing(
        bool Fogless, bool NoRisingGhosts, bool NoSleepFog,
        bool NoWater, bool NoVoidWater, bool NoPlane) =>
        Fogless || NoRisingGhosts || NoSleepFog || NoWater || NoVoidWater || NoPlane;

    internal static void Activate(
        MapHandler Handler,
        IReadOnlyDictionary<string, LayoutSegmentSource> Catalog,
        ResolvedRunConfiguration Configuration)
    {
        if (RequiresProcessing(Configuration)) Current = Create(Handler, Catalog, Configuration);
    }

    internal static void ReleaseCurrent()
    {
        var Owner = Current;
        if (Owner is null) return;
        Current = null;
        try { Owner.Release(); }
        finally { BiomeRegistration.ClearLiveCatalogs(); }
    }

    static NativeBaseMap Create(
        MapHandler Handler,
        IReadOnlyDictionary<string, LayoutSegmentSource> Catalog,
        ResolvedRunConfiguration Configuration)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Configuration is null) throw new ArgumentNullException(nameof(Configuration));
        var Result = new NativeBaseMap(Handler);
        try
        {
            if (Configuration.Environment.Fogless) Result.CaptureFog();
            if (Configuration.Environment.NoRisingGhosts)
            {
                Result.CaptureRisingHazards(RequireSource(Catalog, "Citadel").Container);
                Result.CaptureRisingHazards(RequireSource(Catalog, "Nadir").Container);
            }
            if (Configuration.Environment.NoSleepFog)
                Result.CaptureSleepFog(RequireSource(Catalog, "Citadel").Container);
            if (Configuration.Environment.NoWater)
                foreach (var Value in MapEnvironmentProjection.FindWaterObjects(Handler)) Result.CaptureActive(Value);
            if (Configuration.Environment.NoVoidWater)
            {
                var Nadir = RequireSource(Catalog, "Nadir") as NadirLayoutSegmentSource
                    ?? throw new InvalidOperationException("PEAK's native Nadir source is unavailable.");
                Result.CaptureActive(Nadir.VoidWaterVisual.gameObject);
                Result.CaptureActive(Nadir.VoidWaterZone.gameObject);
            }
            if (Configuration.Environment.NoPlane)
            {
                NativeFixtures.FindStartKitRoots(
                    RequireSource(Catalog, "Beach"), out var Plane, out _, out _);
                Result.CaptureActive(Plane.gameObject);
            }
            Result.Apply();
            return Result;
        }
        catch
        {
            Result.Release();
            throw;
        }
    }

    static LayoutSegmentSource RequireSource(
        IReadOnlyDictionary<string, LayoutSegmentSource> Catalog,
        string Name) => Catalog.TryGetValue(Name, out var Source) && Source is not null
            ? Source
            : throw new InvalidOperationException($"PEAK's native {Name} source is unavailable.");

    bool Fogless => !Released && FogOwner;
    bool OwnsHandler(MapHandler HandlerValue) =>
        !Released && Handler && HandlerValue is not null
        && Handler.GetInstanceID() == HandlerValue.GetInstanceID();
    bool OwnsSceneValue(int SceneHandle) =>
        !Released && Handler && Handler.gameObject.scene.handle == SceneHandle;
    bool OwnsFogValue(OrbFogHandler HandlerValue) =>
        Fogless && HandlerValue is not null
        && FogOwner!.GetInstanceID() == HandlerValue.GetInstanceID();

    void CaptureFog()
    {
        var Owner = Singleton<OrbFogHandler>.Instance;
        if (!Owner || Owner.gameObject.scene != Handler.gameObject.scene)
            throw new InvalidOperationException("PEAK's native fog handler does not belong to the map scene.");
        FogOwner = Owner;
        var Spheres = Owner.GetComponentsInChildren<FogSphere>(true);
        if (Spheres.Length == 0)
            throw new InvalidOperationException("PEAK's native fog handler owns no fog sphere.");
        foreach (var Sphere in Spheres)
        {
            if (!Sphere)
                throw new InvalidOperationException("PEAK's native fog sphere is unavailable.");
            FogSpheres.Add(new FogSphereState(Sphere, Sphere.gameObject != Owner.gameObject));
        }
    }

    void CaptureRisingHazards(Transform Root)
    {
        foreach (var Rising in Root.GetComponentsInChildren<LavaRising>(true))
            if (NativeFogHazards.IsNativeRisingGhost(Rising)) CaptureActive(Rising.gameObject);
    }

    void CaptureSleepFog(Transform Root)
    {
        foreach (var Manager in Root.GetComponentsInChildren<Peak.Island.GloomVoiceConfuser>(true))
            CaptureActive(NativeFogHazards.RequireNativeSleepFog(Manager, Root));
    }

    void CaptureActive(GameObject Value)
    {
        if (!Value) throw new InvalidOperationException("A native base option target is unavailable.");
        if (!ActiveStates.ContainsKey(Value)) ActiveStates.Add(Value, Value.activeSelf);
    }

    void Apply()
    {
        if (Released || !Handler) return;
        foreach (var State in ActiveStates)
            if (State.Key) State.Key.SetActive(false);
        ApplyFoglessValue();
    }

    bool ApplyFoglessValue(OrbFogHandler HandlerValue)
    {
        if (!OwnsFogValue(HandlerValue)) return false;
        ApplyFoglessValue();
        return true;
    }

    bool ApplyFoglessValue(FogSphere SphereValue)
    {
        if (!Fogless || SphereValue is null) return false;
        var SphereId = SphereValue.GetInstanceID();
        foreach (var State in FogSpheres)
        {
            if (State.Sphere.GetInstanceID() != SphereId) continue;
            ApplyFogless(State, FogOwner!.sphere);
            return true;
        }
        return false;
    }

    void ApplyFoglessValue()
    {
        if (!Fogless) return;
        var NativeSphereAssigned = (bool)FogOwner!.sphere;
        foreach (var State in FogSpheres) ApplyFogless(State, NativeSphereAssigned);
    }

    static void ApplyFogless(FogSphereState State, bool NativeSphereAssigned)
    {
        var Sphere = State.Sphere;
        if (!Sphere) return;
        Sphere.ENABLE = 0f;
        Sphere.REVEAL_AMOUNT = 0f;
        if (NativeSphereAssigned && State.DisableObject && Sphere.gameObject.activeSelf) Sphere.gameObject.SetActive(false);
    }

    void Release()
    {
        if (Released) return;
        Released = true;
        foreach (var State in ActiveStates)
            if (State.Key) State.Key.SetActive(State.Value);
        foreach (var State in FogSpheres)
        {
            if (!State.Sphere) continue;
            State.Sphere.ENABLE = State.Enable;
            State.Sphere.REVEAL_AMOUNT = State.RevealAmount;
            if (State.DisableObject) State.Sphere.gameObject.SetActive(State.Active);
        }
        ActiveStates.Clear();
        FogSpheres.Clear();
        FogOwner = null;
    }
}
