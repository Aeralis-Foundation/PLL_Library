using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PLL.Library;

internal static class AuthoringRuntime
{
    static readonly Dictionary<int, RuntimeBiomeAuthoring> BiomesBySlot = new();
    static AuthoringRuntimeHost? Host;
    static NativeParticlePrototypePool? NativeParticlePrototypes;
    static RuntimeBiomeAuthoring? Current;
    static Action<PllEffectZoneOccupancyChange>?
        EffectZoneOccupancyChanged;
    static VisualTransitionState TransitionState;

    internal static RuntimeBiomeAuthoring? PresentationOccurrence =>
        TransitionState == VisualTransitionState.Idle ? Current : null;

    enum VisualTransitionState : byte
    {
        Idle,
        Open,
        Failed
    }

    internal static void Initialize(GameObject Owner)
    {
        if (Host) return;
        HookedPropRuntime.Initialize();
        Host = Owner.AddComponent<AuthoringRuntimeHost>();
        Host.enabled = false;
        AuthoringPresentationRuntime.Initialize(Owner);
    }

    internal static void Bind(MapRun Run)
    {
        Clear();
        TransitionState = VisualTransitionState.Open;
        try
        {
            var NativeParticles = new NativeParticlePrototypePool(Host!.transform);
            NativeParticlePrototypes = NativeParticles;
            foreach (var Slot in Run.Slots)
            {
                if (Slot.Released || Slot.CustomResolved is null
                    || !BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract)) continue;
                if (BiomesBySlot.ContainsKey(Slot.Index))
                    throw new InvalidOperationException($"Authoring runtime has duplicate slot index {Slot.Index}.");
                var Runtime = new RuntimeBiomeAuthoring(
                    Run,
                    Slot,
                    Contract,
                    Contract.Resolve(Slot.CustomResolved.Root.Value),
                    NativeParticles);
                BiomesBySlot.Add(Slot.Index, Runtime);
            }
            LuggageRuntime.Bind(BiomesBySlot, Run.Slots);
            ItemSpawnerRuntime.Bind(Run.Slots);
            HookedPropRuntime.Bind(Run.Slots);
            NativeFogRenderRuntime.BindSources(Run.ReadPresentationClock,
                AuthoringPresentationRuntime.CaptureFogSources(BiomesBySlot.Values));
            TransitionState = VisualTransitionState.Idle;
            if (Host) Host.enabled = BiomesBySlot.Count != 0;
        }
        catch (Exception Failure)
        {
            try { Clear(); }
            catch (Exception Cleanup)
            {
                throw new AggregateException("Authoring runtime binding and rollback both failed.", Failure, Cleanup);
            }
            throw;
        }
    }

    internal static void RefreshSlot(int Index)
    {
        if (BiomesBySlot.TryGetValue(Index, out var Biome)) Biome.RefreshGeneratedPresentation();
        NativeFogRenderRuntime.ReplaceSources(
            AuthoringPresentationRuntime.CaptureFogSources(BiomesBySlot.Values));
        LuggageRuntime.RefreshSlot(Index);
        ItemSpawnerRuntime.RefreshSlot(Index);
        HookedPropRuntime.RefreshSlot(Index);
        Map.Slots[Index].Occurrence.PublishCurrentEntityPopulation();
        Tick();
    }

    internal static void Tick()
    {
        if (TransitionState != VisualTransitionState.Idle || BiomesBySlot.Count == 0) return;
        if (Map.CurrentRun is not { PresentationClockReady: true } Run) return;
        var Clock = Run.PresentationClock;
        foreach (var Occurrence in BiomesBySlot.Values)
        {
            Occurrence.AdoptFogCommands(Clock);
            Occurrence.AdoptFlowPublications(Clock);
        }
        var Biome = CurrentBiome();
        if (!ReferenceEquals(Current, Biome))
        {
            BeginTransition();
            CommitTransition(Biome);
        }

        var ActiveBiome = Current;
        var Now = Time.time;
        ActiveBiome?.Update(Now, Clock);
        foreach (var Occurrence in BiomesBySlot.Values)
            if (!ReferenceEquals(Occurrence, ActiveBiome)) Occurrence.TickPresentation(Now, Clock);
        if (ActiveBiome is not null) AuthoringPresentationRuntime.Tick(ActiveBiome);
    }

    internal static void BeginTransition()
    {
        if (TransitionState == VisualTransitionState.Open)
            throw new InvalidOperationException("A visual transition is already open.");
        TransitionState = VisualTransitionState.Open;
        try { LeaveCurrent(); }
        catch
        {
            TransitionState = VisualTransitionState.Failed;
            throw;
        }
    }

    internal static void CommitTransition() => CommitTransition(CurrentBiome());

    internal static void AbortTransition()
    {
        TransitionState = VisualTransitionState.Failed;
        LeaveCurrent();
    }

    static RuntimeBiomeAuthoring? CurrentBiome()
    {
        if (Map.CurrentRun is not { PresentationClockReady: true } Run
            || Run.ReadyCurrentSlot is not { } Slot
            || !BiomesBySlot.TryGetValue(Slot.Index, out var Candidate)) return null;
        return Candidate;
    }

    static void CommitTransition(RuntimeBiomeAuthoring? Next)
    {
        if (TransitionState != VisualTransitionState.Open)
            throw new InvalidOperationException("No visual transition is open for commit.");
        Current = Next;
        try
        {
            Current?.SetCurrent(true);
            AuthoringPresentationRuntime.Bind(Current);
            TransitionState = VisualTransitionState.Idle;
            if (Current is not null && Sounds.TryGetStingerRoute(out var Group)) Current.AdoptStingerRoute(Group);
            Current?.Slot.Occurrence.PublishCurrentEntityPopulation();
        }
        catch (Exception TransitionFailure)
        {
            TransitionState = VisualTransitionState.Failed;
            try { LeaveCurrent(); }
            catch (Exception CleanupFailure)
            {
                throw new AggregateException(
                    "Visual transition activation and rollback both failed.", TransitionFailure, CleanupFailure);
            }
            throw;
        }
    }

    static void LeaveCurrent()
    {
        List<Exception>? Failures = null;
        var Active = Current;
        Current = null;
        if (Active is not null) AuthoringCleanup.Capture(ref Failures, () => Active.SetCurrent(false));
        AuthoringCleanup.Capture(ref Failures, AuthoringPresentationRuntime.Leave);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Visual transition cleanup failed.", Failures);
    }

    internal static int Emit(string Hook)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        if (TransitionState != VisualTransitionState.Idle) return 0;
        var Biome = Current;
        if (Biome is null) return 0;
        var Count = 0;
        foreach (var State in Biome.Effects)
        {
            if (State.Authoring.Mode != PllEffectMode.EmitHook
                || !string.Equals(State.Authoring.EmitHook, Hook, StringComparison.Ordinal)) continue;
            State.Play(Time.time, Map.CurrentRun!.PresentationClock);
            Count++;
        }
        if (Count != 0) Biome.RefreshVisuals();
        return Count;
    }

    internal static int Stop(string Hook)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        if (TransitionState != VisualTransitionState.Idle) return 0;
        var Biome = Current;
        if (Biome is null) return 0;
        var Count = 0;
        foreach (var State in Biome.Effects)
        {
            if (State.Authoring.Mode != PllEffectMode.EmitHook || !State.IsPlaying
                || !string.Equals(State.Authoring.EmitHook, Hook, StringComparison.Ordinal)) continue;
            State.Deactivate(Map.CurrentRun!.PresentationClock);
            Count++;
        }
        if (Count != 0) Biome.RefreshVisuals();
        return Count;
    }

    internal static int EmitStinger(string Hook)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        if (TransitionState != VisualTransitionState.Idle || Current is not { IsCurrent: true } Biome
            || !Sounds.TryGetStingerRoute(out var Group)) return 0;
        return Biome.EmitStinger(Hook, Group);
    }

    internal static int StopStingers(string Hook)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        if (TransitionState != VisualTransitionState.Idle || Current is not { IsCurrent: true } Biome
            || !Sounds.TryGetStingerRoute(out var Group)) return 0;
        return Biome.StopStingers(Hook, Group);
    }

    internal static int EmitClimbSurface(string Hook)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        HookedPropRuntime.RequireMainThread("Climb-surface activation");
        if (TransitionState != VisualTransitionState.Idle
            || Current is not { IsCurrent: true } Biome) return 0;
        return Biome.EmitClimbSurface(Hook);
    }

    internal static int EmitClimbSurface(
        string Hook, float StaminaUsageMultiplier,
        float SpeedMultiplier)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        ClimbSurfaceAuthoringContract.ValidateRuntimeMultiplier(
            StaminaUsageMultiplier, nameof(StaminaUsageMultiplier));
        ClimbSurfaceAuthoringContract.ValidateRuntimeMultiplier(
            SpeedMultiplier, nameof(SpeedMultiplier));
        HookedPropRuntime.RequireMainThread("Climb-surface activation");
        if (TransitionState != VisualTransitionState.Idle
            || Current is not { IsCurrent: true } Biome) return 0;
        return Biome.EmitClimbSurface(
            Hook, StaminaUsageMultiplier, SpeedMultiplier);
    }

    internal static int StopClimbSurface(string Hook)
    {
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        HookedPropRuntime.RequireMainThread("Climb-surface deactivation");
        if (TransitionState != VisualTransitionState.Idle
            || Current is not { IsCurrent: true } Biome) return 0;
        return Biome.StopClimbSurface(Hook);
    }

    internal static int GetOccupiedEffectZoneCount(
        BiomeId BiomeId, string Hook)
    {
        HookedPropRuntime.RequireMainThread(
            "Effect-zone occupancy query");
        if (!BiomeId.IsValid)
            throw new ArgumentException(
                "The biome ID must be a canonical lowercase qualified ID.",
                nameof(BiomeId));
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        if (TransitionState != VisualTransitionState.Idle
            || Current is not { IsCurrent: true } Biome
            || Biome.Id != BiomeId) return 0;
        return Biome.GetOccupiedEffectZoneCount(Hook);
    }

    internal static void AddEffectZoneOccupancyChanged(
        Action<PllEffectZoneOccupancyChange>? Handler)
    {
        HookedPropRuntime.RequireMainThread(
            "Effect-zone occupancy subscription");
        if (Handler is not null)
            EffectZoneOccupancyChanged += Handler;
    }

    internal static void RemoveEffectZoneOccupancyChanged(
        Action<PllEffectZoneOccupancyChange>? Handler)
    {
        HookedPropRuntime.RequireMainThread(
            "Effect-zone occupancy unsubscription");
        if (Handler is not null)
            EffectZoneOccupancyChanged -= Handler;
    }

    internal static void PublishEffectZoneOccupancy(
        BiomeId BiomeId, string Hook, int OccupiedZoneCount)
    {
        var Handlers = EffectZoneOccupancyChanged;
        if (Handlers is null) return;
        var Change = new PllEffectZoneOccupancyChange(
            BiomeId, Hook, OccupiedZoneCount);
        foreach (Action<PllEffectZoneOccupancyChange> Handler
            in Handlers.GetInvocationList())
            try { Handler(Change); }
            catch (Exception Failure)
            {
                Plugin.PLLLog.LogError(
                    $"Effect-zone occupancy handler failed for biome '{BiomeId}' hook '{Hook}'. {Failure}");
            }
    }

    internal static void AdoptStingerRoute(AudioMixerGroup Group)
    {
        if (TransitionState == VisualTransitionState.Idle && Current is { IsCurrent: true } Biome)
            Biome.AdoptStingerRoute(Group);
    }

    internal static void ClearStingerRoute()
    {
        List<Exception>? Failures = null;
        foreach (var Biome in BiomesBySlot.Values)
            AuthoringCleanup.Capture(ref Failures, Biome.ClearStingerRoute);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Biome stinger route cleanup failed.", Failures);
    }

    internal static void Clear()
    {
        TransitionState = VisualTransitionState.Open;
        try
        {
            ClearOwnedState();
            TransitionState = VisualTransitionState.Idle;
        }
        catch
        {
            TransitionState = VisualTransitionState.Failed;
            throw;
        }
    }

    static void ClearOwnedState()
    {
        List<Exception>? Failures = null;
        var Biomes = new List<RuntimeBiomeAuthoring>(BiomesBySlot.Values);
        var Prototypes = NativeParticlePrototypes;
        Current = null;
        BiomesBySlot.Clear();
        NativeParticlePrototypes = null;
        AuthoringCleanup.Capture(ref Failures, NativeFogRenderRuntime.ClearSources);
        if (Host) AuthoringCleanup.Capture(ref Failures, () => Host.enabled = false);
        AuthoringCleanup.Capture(ref Failures, AuthoringPresentationRuntime.Leave);
        AuthoringCleanup.Capture(ref Failures, LuggageRuntime.Clear);
        AuthoringCleanup.Capture(ref Failures, ItemSpawnerRuntime.Clear);
        AuthoringCleanup.Capture(ref Failures, HookedPropRuntime.Clear);
        foreach (var Biome in Biomes) AuthoringCleanup.Capture(ref Failures, Biome.Restore);
        if (Prototypes is not null) AuthoringCleanup.Capture(ref Failures, Prototypes.Release);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Authoring runtime cleanup failed.", Failures);
    }

    internal static void Shutdown()
    {
        if (!Host) return;
        Exception? Failure = null;
        try
        {
            Clear();
        }
        catch (Exception Exception)
        {
            Failure = Exception;
        }
        try
        {
            AuthoringPresentationRuntime.Shutdown();
            if (Host) UnityEngine.Object.Destroy(Host);
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("Authoring runtime shutdown and destruction both failed.", Failure, Exception);
        }
        finally
        {
            Host = null;
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }
}

internal sealed class AuthoringRuntimeHost : MonoBehaviour
{
    void Update()
    {
        var Run = Map.CurrentRun;
        var Index = Map.Index;
        try
        {
            AuthoringRuntime.Tick();
        }
        catch (Exception Failure)
        {
            enabled = false;
            if (Run?.Features.Aborted != true)
                LazySlotGenerator.AbortLazyGeneration(
                    Run, Index, "Runtime biome authoring update failed", Failure);
        }
    }
}
