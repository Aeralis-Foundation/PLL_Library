using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PLL.Library;

internal sealed class RuntimeBiomeAuthoring
{
    readonly MapRun Run;
    readonly int StableEffectCount;
    readonly int StableEffectZoneCount;
    readonly int StableVisualCount;
    readonly int StableSoundEmitterCount;
    readonly int StableFogZoneCount;
    readonly VisualEnvironmentRuntime VisualEnvironment;
    readonly ClimbSurfaceRuntime ClimbSurfaces;
    readonly string[] OccupancyHooks;
    readonly Dictionary<int, List<FogZoneState>> FogCommandSources = new();
    (NativeFlowRuntime Flow, int TargetIndex, int GenerationRevision, int SourceTargetIndex)[] FlowPublications =
        Array.Empty<(NativeFlowRuntime, int, int, int)>();
    int[] OccupancyCounts;
    int[] NextOccupancyCounts;
    int EffectSoundIndex;
    bool AutomaticStingersPending;

    internal RuntimeBiomeAuthoring(
        MapRun Run, MapSlot Slot, BiomeAuthoringContract Contract, ResolvedBiomeAuthoring Resolved,
        NativeParticlePrototypePool NativeParticles)
    {
        this.Run = Run;
        this.Slot = Slot;
        this.Contract = Contract;
        this.Resolved = Resolved;
        Id = Contract.Id;
        var OccupancyHookSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var Zone in Resolved.EffectZones)
            if (Zone.OccupancyHook.Length != 0)
                OccupancyHookSet.Add(Zone.OccupancyHook);
        OccupancyHooks = new string[OccupancyHookSet.Count];
        OccupancyHookSet.CopyTo(OccupancyHooks);
        OccupancyCounts = new int[OccupancyHooks.Length];
        NextOccupancyCounts = new int[OccupancyHooks.Length];
        Effects = new EffectState[Resolved.Effects.Length];
        EffectZones = new EffectZoneState[Resolved.EffectZones.Length];
        NativeResolvers = new NativeResolverState[Resolved.NativeResolvers.Length];
        Visuals = new VisualState[Resolved.Visuals.Length];
        SoundEmitters = new SoundEmitterState[Resolved.SoundEmitters.Length];
        Stingers = new StingerState[Resolved.Stingers.Length];
        FogSources = new (FogZoneState State, int TargetIndex)[Resolved.FogZones.Length];
        var StableFogIndex = 0;
        for (var i = 0; i < Contract.FogZoneContracts.Count; i++)
            if (!Contract.FogZoneContracts[i].Generated)
            {
                FogSources[StableFogIndex] = (new FogZoneState(Resolved.FogZones[StableFogIndex],
                    Slot.Occurrence.RequestIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "/stable"), i);
                StableFogIndex++;
            }
        StableFogZoneCount = FogSources.Length;
        BindFogCommandSources();
        var CreatedEffects = 0;
        var CreatedZones = 0;
        var CreatedResolvers = 0;
        var CreatedVisuals = 0;
        var CreatedEmitters = 0;
        var CreatedStingers = 0;
        VisualEnvironmentRuntime? CreatedEnvironment = null;
        ClimbSurfaceRuntime? CreatedClimbSurfaces = null;
        try
        {
            CreatedClimbSurfaces = new ClimbSurfaceRuntime(
                Contract.ResolveClimbSurfaces(Slot));
            ClimbSurfaces = CreatedClimbSurfaces;
            for (; CreatedEffects < Effects.Length; CreatedEffects++)
                Effects[CreatedEffects] = new EffectState(this, Resolved.Effects[CreatedEffects]);
            for (; CreatedZones < EffectZones.Length; CreatedZones++)
            {
                var ResolvedZone = Resolved.EffectZones[CreatedZones];
                var OccupancyKey = ResolvedZone.OccupancyHook.Length == 0
                    ? -1
                    : Array.BinarySearch(
                        OccupancyHooks, ResolvedZone.OccupancyHook,
                        StringComparer.Ordinal);
                if (ResolvedZone.OccupancyHook.Length != 0 && OccupancyKey < 0)
                    throw new InvalidOperationException(
                        $"Biome '{Id}' effect-zone occupancy hook is absent from its sealed catalog.");
                EffectZones[CreatedZones] = new EffectZoneState(
                    ResolvedZone, Effects, OccupancyKey);
            }
            for (; CreatedResolvers < NativeResolvers.Length; CreatedResolvers++)
                NativeResolvers[CreatedResolvers] = NativeResolverState.CreateConfigured(Resolved.NativeResolvers[CreatedResolvers], Effects);
            for (; CreatedVisuals < Visuals.Length; CreatedVisuals++)
                Visuals[CreatedVisuals] = new VisualState(Resolved.Visuals[CreatedVisuals]);
            for (; CreatedEmitters < SoundEmitters.Length; CreatedEmitters++)
                SoundEmitters[CreatedEmitters] = new SoundEmitterState(Resolved.SoundEmitters[CreatedEmitters]);
            for (; CreatedStingers < Stingers.Length; CreatedStingers++)
                Stingers[CreatedStingers] = new StingerState(Resolved.Stingers[CreatedStingers]);
            CreatedEnvironment = new VisualEnvironmentRuntime(Slot, Contract, Resolved, NativeParticles);
            VisualEnvironment = CreatedEnvironment;
            StableEffectCount = Effects.Length;
            StableEffectZoneCount = EffectZones.Length;
            StableVisualCount = Visuals.Length;
            StableSoundEmitterCount = SoundEmitters.Length;
            RefreshGeneratedPresentation();
        }
        catch (Exception Failure)
        {
            List<Exception>? CleanupFailures = null;

            if (CreatedEnvironment is not null) AuthoringCleanup.Capture(ref CleanupFailures, CreatedEnvironment.Release);
            if (CreatedClimbSurfaces is not null)
                AuthoringCleanup.Capture(ref CleanupFailures, CreatedClimbSurfaces.Deactivate);
            for (var i = CreatedStingers - 1; i >= 0; i--) AuthoringCleanup.Capture(ref CleanupFailures, Stingers[i].Restore);
            for (var i = CreatedEmitters - 1; i >= 0; i--) AuthoringCleanup.Capture(ref CleanupFailures, SoundEmitters[i].Restore);
            for (var i = CreatedVisuals - 1; i >= 0; i--) AuthoringCleanup.Capture(ref CleanupFailures, Visuals[i].Restore);
            for (var i = CreatedResolvers - 1; i >= 0; i--) AuthoringCleanup.Capture(ref CleanupFailures, NativeResolvers[i].Restore);
            for (var i = CreatedZones - 1; i >= 0; i--)
            {
                var Zone = EffectZones[i];
                AuthoringCleanup.Capture(ref CleanupFailures, () => Zone.SetActive(false, 0f, 0d));
            }
            for (var i = CreatedEffects - 1; i >= 0; i--) AuthoringCleanup.Capture(ref CleanupFailures, Effects[i].Restore);
            if (CleanupFailures is not null)
            {
                CleanupFailures.Insert(0, Failure);
                throw new AggregateException("Biome authoring construction and rollback both failed.", CleanupFailures);
            }
            throw;
        }
    }

    internal MapSlot Slot { get; }
    internal BiomeId Id { get; }
    internal BiomeAuthoringContract Contract { get; }
    internal ResolvedBiomeAuthoring Resolved { get; }
    internal EffectState[] Effects { get; private set; }
    internal (FogZoneState State, int TargetIndex)[] FogSources { get; private set; }
    EffectZoneState[] EffectZones;
    readonly NativeResolverState[] NativeResolvers;
    NativeFlowRuntime[] FixedFlows = Array.Empty<NativeFlowRuntime>();
    NativeFlowRuntime[] GeneratedFlows = Array.Empty<NativeFlowRuntime>();
    VisualState[] Visuals;
    SoundEmitterState[] SoundEmitters;
    readonly StingerState[] Stingers;
    internal bool IsCurrent { get; private set; }
    double PresentationClock => Run.PresentationClock;

    internal void SetCurrent(bool Value)
    {
        if (Value && IsCurrent) return;
        IsCurrent = Value;
        if (!Value)
        {
            AutomaticStingersPending = false;
            List<Exception>? Failures = null;

            AuthoringCleanup.Capture(ref Failures, CommitZeroOccupancy);
            AuthoringCleanup.Capture(ref Failures, ClimbSurfaces.Deactivate);
            AuthoringCleanup.Capture(ref Failures, () => VisualEnvironment.Deactivate(PresentationClock));
            for (var i = NativeResolvers.Length - 1; i >= 0; i--)
            {
                var Resolver = NativeResolvers[i];
                AuthoringCleanup.Capture(ref Failures, () => Resolver.SetCurrent(false, Time.time, PresentationClock));
            }
            for (var i = EffectZones.Length - 1; i >= 0; i--)
            {
                var Zone = EffectZones[i];
                AuthoringCleanup.Capture(ref Failures, () => Zone.SetActive(false, Time.time, PresentationClock));
            }
            foreach (var Effect in Effects)
                AuthoringCleanup.Capture(ref Failures, () => Effect.Deactivate(PresentationClock));
            foreach (var Visual in Visuals)
                AuthoringCleanup.Capture(ref Failures, () => Visual.Apply(false, PresentationClock));
            foreach (var Emitter in SoundEmitters)
                AuthoringCleanup.Capture(ref Failures, () => Emitter.Deactivate(PresentationClock));
            if (Sounds.TryGetStingerRoute(out var Group))
                foreach (var Stinger in Stingers)
                    AuthoringCleanup.Capture(ref Failures, () => Stinger.Stop(Group, PresentationClock));
            else foreach (var Stinger in Stingers) AuthoringCleanup.Capture(ref Failures, Stinger.Restore);
            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is { Count: > 1 })
                throw new AggregateException("Biome authoring deactivation failed.", Failures);
            return;
        }
        var Clock = PresentationClock;
        try
        {
            ClimbSurfaces.Activate();
            VisualEnvironment.Activate(Clock);
            foreach (var Effect in Effects)
                if (Effect.Authoring.Mode == PllEffectMode.Timed) Effect.Play(Time.time, Clock);
            foreach (var Resolver in NativeResolvers) Resolver.SetCurrent(true, Time.time, Clock);
            RefreshVisuals();
            AutomaticStingersPending = false;
            foreach (var Stinger in Stingers)
                if (Stinger.StartsOnEnter)
                {
                    AutomaticStingersPending = true;
                    break;
                }
        }
        catch (Exception ActivationFailure)
        {
            try { Restore(); }
            catch (Exception CleanupFailure)
            {
                throw new AggregateException(
                    "Biome authoring activation and rollback both failed.", ActivationFailure, CleanupFailure);
            }
            throw;
        }
    }

    internal void AdoptStingerRoute(AudioMixerGroup Group)
    {
        if (!IsCurrent) return;
        try
        {
            foreach (var Stinger in Stingers) Stinger.Bind(Group);
            if (AutomaticStingersPending)
                foreach (var Stinger in Stingers)
                    if (Stinger.StartsOnEnter) Stinger.Restart(Group, PresentationClock);
            AutomaticStingersPending = false;
        }
        catch (Exception Failure)
        {
            List<Exception>? CleanupFailures = null;
            foreach (var Stinger in Stingers)
                AuthoringCleanup.Capture(ref CleanupFailures, Stinger.Restore);
            if (CleanupFailures is not null)
            {
                CleanupFailures.Insert(0, Failure);
                throw new AggregateException("Biome stinger route adoption and rollback both failed.", CleanupFailures);
            }
            throw;
        }
    }

    internal int EmitStinger(string Hook, AudioMixerGroup Group)
    {
        var Count = 0;
        foreach (var Stinger in Stingers)
            if (Stinger.AcceptsHook(Hook) && Stinger.Restart(Group, PresentationClock)) Count++;
        return Count;
    }

    internal int StopStingers(string Hook, AudioMixerGroup Group)
    {
        var Count = 0;
        foreach (var Stinger in Stingers)
            if (Stinger.AcceptsHook(Hook) && Stinger.Stop(Group, PresentationClock)) Count++;
        return Count;
    }

    internal int EmitClimbSurface(string Hook) =>
        IsCurrent && !Slot.Released ? ClimbSurfaces.Emit(Hook) : 0;

    internal int EmitClimbSurface(
        string Hook, float StaminaUsageMultiplier,
        float SpeedMultiplier) =>
        IsCurrent && !Slot.Released
            ? ClimbSurfaces.Emit(
                Hook, StaminaUsageMultiplier, SpeedMultiplier)
            : 0;

    internal int StopClimbSurface(string Hook) =>
        IsCurrent && !Slot.Released ? ClimbSurfaces.Stop(Hook) : 0;

    internal void ClearStingerRoute()
    {
        AutomaticStingersPending = false;
        List<Exception>? Failures = null;
        foreach (var Stinger in Stingers)
            AuthoringCleanup.Capture(ref Failures, Stinger.Restore);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Biome stinger occurrence cleanup failed.", Failures);
    }

    internal void RefreshGeneratedPresentation()
    {
        RemoveGeneratedPresentation();
        if (Slot.Released)
        {
            GeneratedFlows = Array.Empty<NativeFlowRuntime>();
            FixedFlows = Array.Empty<NativeFlowRuntime>();
            FlowPublications = Array.Empty<(NativeFlowRuntime, int, int, int)>();
            if (FogSources.Length != StableFogZoneCount)
            {
                var Sources = new (FogZoneState State, int TargetIndex)[StableFogZoneCount];
                Array.Copy(FogSources, Sources, StableFogZoneCount);
                FogSources = Sources;
            }
            FogCommandSources.Clear();
            PublishGeneratedZones(Array.Empty<(EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)>());
            return;
        }
        var Generated = GeneratedPresentationRuntime.Resolve(Slot, Contract);
        var NextEffects = new EffectState[StableEffectCount + Generated.Effects.Length];
        Array.Copy(Effects, NextEffects, StableEffectCount);
        var NextZones = new EffectZoneState[StableEffectZoneCount + Generated.EffectZones.Length];
        Array.Copy(EffectZones, NextZones, StableEffectZoneCount);
        var NextVisuals = new VisualState[StableVisualCount + Generated.Visuals.Length];
        Array.Copy(Visuals, NextVisuals, StableVisualCount);
        var NextEmitters = new SoundEmitterState[StableSoundEmitterCount + Generated.SoundEmitters.Length];
        Array.Copy(SoundEmitters, NextEmitters, StableSoundEmitterCount);
        var CreatedEffects = 0;
        var CreatedVisuals = 0;
        var CreatedEmitters = 0;
        try
        {
            for (; CreatedEffects < Generated.Effects.Length; CreatedEffects++)
                NextEffects[StableEffectCount + CreatedEffects] = new EffectState(this, Generated.Effects[CreatedEffects]);
            for (var i = 0; i < Generated.EffectZones.Length; i++)
                NextZones[StableEffectZoneCount + i] = new EffectZoneState(
                    Generated.EffectZones[i], NextEffects, -1);
            for (; CreatedVisuals < Generated.Visuals.Length; CreatedVisuals++)
                NextVisuals[StableVisualCount + CreatedVisuals] = new VisualState(Generated.Visuals[CreatedVisuals]);
            for (; CreatedEmitters < Generated.SoundEmitters.Length; CreatedEmitters++)
                NextEmitters[StableSoundEmitterCount + CreatedEmitters] = new SoundEmitterState(Generated.SoundEmitters[CreatedEmitters]);
            ClimbSurfaces.Refresh(Contract.ResolveClimbSurfaces(Slot));
        }
        catch (Exception Failure)
        {
            List<Exception>? CleanupFailures = null;

            for (var i = CreatedEmitters - 1; i >= 0; i--)
                AuthoringCleanup.Capture(ref CleanupFailures, NextEmitters[StableSoundEmitterCount + i].Restore);
            for (var i = CreatedVisuals - 1; i >= 0; i--)
                AuthoringCleanup.Capture(ref CleanupFailures, NextVisuals[StableVisualCount + i].Restore);
            for (var i = CreatedEffects - 1; i >= 0; i--)
                AuthoringCleanup.Capture(ref CleanupFailures, NextEffects[StableEffectCount + i].Restore);
            if (CleanupFailures is not null)
            {
                CleanupFailures.Insert(0, Failure);
                throw new AggregateException("Generated presentation construction and rollback both failed.", CleanupFailures);
            }
            throw;
        }
        Effects = NextEffects;
        EffectZones = NextZones;
        Visuals = NextVisuals;
        SoundEmitters = NextEmitters;
        var NextFogSources = new (FogZoneState State, int TargetIndex)[StableFogZoneCount + Generated.FogZones.Length];
        Array.Copy(FogSources, NextFogSources, StableFogZoneCount);
        var ExistingFog = new Dictionary<(GameObject Root, int TargetIndex), FogZoneState>();
        for (var i = StableFogZoneCount; i < FogSources.Length; i++)
            if (FogSources[i].State.Root)
                ExistingFog.Add((FogSources[i].State.Root, FogSources[i].TargetIndex), FogSources[i].State);
        for (var i = 0; i < Generated.FogZones.Length; i++)
        {
            var Source = Generated.FogZones[i];
            NextFogSources[StableFogZoneCount + i] = (
                ExistingFog.TryGetValue((Source.Source.Root, Source.ContractIndex), out var State) ? State
                    : new FogZoneState(Source.Source,
                        Slot.Occurrence.RequestIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "/" + Source.StableInstanceIdentity), Source.ContractIndex);
        }
        FogSources = NextFogSources;
        BindFogCommandSources();
        PublishGeneratedZones(Generated.ZoneMemberships);
        GeneratedFlows = Generated.NativeFlows;
        FixedFlows = Slot.FeaturesReady ? Slot.Occurrence.FixedFlows : Array.Empty<NativeFlowRuntime>();
        FlowPublications = new (NativeFlowRuntime, int, int, int)[GeneratedFlows.Length + FixedFlows.Length];
        for (var i = 0; i < GeneratedFlows.Length; i++)
        {
            var Flow = GeneratedFlows[i];
            FlowPublications[i] = Run.HookedGeneration.TryGetSourceLifetime(Flow.SourceTransform, out var Target, out var Revision)
                ? (Flow, Target.TargetIndex, Revision, Contract.NativeSourceTargetIndex(Flow.Contract))
                : (Flow, -1, 0, Contract.NativeSourceTargetIndex(Flow.Contract));
        }
        for (var i = 0; i < FixedFlows.Length; i++)
            FlowPublications[GeneratedFlows.Length + i] = (FixedFlows[i], -1, 0, Contract.NativeSourceTargetIndex(FixedFlows[i].Contract));

        if (!IsCurrent) return;
        for (var i = StableEffectCount; i < Effects.Length; i++)
            if (Effects[i].Authoring.Mode == PllEffectMode.Timed) Effects[i].Play(Time.time, PresentationClock);
        RefreshVisuals();
    }

    internal void Update(float Now, double Clock)
    {
        var VisualsChanged = false;
        var HasOccupancy = OccupancyHooks.Length != 0;
        if (HasOccupancy)
            Array.Clear(
                NextOccupancyCounts, 0,
                NextOccupancyCounts.Length);
        var LocalCharacter = HasOccupancy
            ? Character.localCharacter : null;
        var ObservedCharacter = Character.observedCharacter;
        var HasCharacter = ObservedCharacter;
        var Center = HasCharacter ? ObservedCharacter!.Center : default;
        var SameCharacter = LocalCharacter
            && ReferenceEquals(ObservedCharacter, LocalCharacter);
        var LocalCenter = SameCharacter
            ? Center
            : LocalCharacter ? LocalCharacter.Center : default;
        VisualEnvironment.Update(ObservedCharacter, Center, Clock);
        for (var Index = 0; Index < EffectZones.Length; Index++)
        {
            var Zone = EffectZones[Index];
            var RootActive = Zone.Root && Zone.Root.activeInHierarchy;
            var ZoneChanged = Zone.SetActive(IsCurrent && HasCharacter && RootActive
                && PllZoneMath.Contains(
                    Zone.Root!.transform, Zone.Volume, Center), Now, Clock);
            if (ZoneChanged && Zone.HasEffects) VisualsChanged = true;
            if (Zone.OccupancyKey >= 0 && IsCurrent && LocalCharacter
                && RootActive && (SameCharacter ? Zone.Active
                    : PllZoneMath.Contains(
                        Zone.Root!.transform, Zone.Volume, LocalCenter)))
                NextOccupancyCounts[Zone.OccupancyKey]++;
        }
        if (HasOccupancy)
        {
            CommitOccupancy();
            if (!IsCurrent || Slot.Released) return;
        }
        foreach (var Resolver in NativeResolvers)
            if (Resolver.Update(Now, Clock)) VisualsChanged = true;
        if (VisualsChanged) RefreshVisuals();
        TickPresentation(Now, Clock);
    }

    internal void TickPresentation(float Now, double Clock)
    {
        var EnvironmentalFogless = 0f;
        foreach (var Zone in EffectZones)
        {
            Zone.TickEnvironment(Clock);
            EnvironmentalFogless = Math.Max(EnvironmentalFogless, Zone.EnvironmentalFogCoverage);
        }
        VisualEnvironment.SetZoneEnvironmentFog(EnvironmentalFogless);
        VisualEnvironment.TickEnvironment(Clock);
        var VisualsChanged = false;
        foreach (var Effect in Effects)
            if (Effect.Update(Now, Clock)) VisualsChanged = true;
        foreach (var Emitter in SoundEmitters) Emitter.Update(IsCurrent, Clock);
        if (VisualsChanged) RefreshVisuals();
        foreach (var Visual in Visuals) Visual.Tick(Clock);
        foreach (var Stinger in Stingers) Stinger.Tick(Clock);
        foreach (var Source in FlowPublications) Source.Flow.Tick(Clock);
    }

    internal void AdoptFlowPublications(double Clock)
    {
        foreach (var Source in FlowPublications)
            if (Run.HookedGeneration.TryGetFlowPresentationStart(Slot.Occurrence,
                Source.TargetIndex, Source.GenerationRevision, out var Start))
            {
                Run.HookedGeneration.TryGetNativeSourceCommand(Slot.Occurrence, Source.SourceTargetIndex, out var Command);
                Source.Flow.AdoptPublication(Start, Command, Source.TargetIndex + 1, Clock);
            }
    }

    internal void AdoptFogCommands(double Clock)
    {
        if (Slot.Released) return;
        foreach (var Entry in FogCommandSources)
            if (Run.HookedGeneration.TryGetFogCommand(Slot.Occurrence, Entry.Key, out var Command))
                foreach (var State in Entry.Value)
                    State.Adopt(Command.Target, Command.StartCoverage, Command.StartClock,
                        Command.CoverDelaySeconds, Command.Revision, Clock);
    }

    void BindFogCommandSources()
    {
        FogCommandSources.Clear();
        foreach (var Source in FogSources)
        {
            if (!FogCommandSources.TryGetValue(Source.TargetIndex, out var States))
                FogCommandSources.Add(Source.TargetIndex, States = new List<FogZoneState>());
            States.Add(Source.State);
        }
    }

    void PublishGeneratedZones(IReadOnlyList<(EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)> Zones)
    {
        if (Contract.FogZoneContracts.Count != 0 || Slot.Occurrence.HookedTargets.Length != 0)
            Run.HookedGeneration.ReplaceGeneratedZones(Slot.Occurrence, Zones);
    }

    internal int GetOccupiedEffectZoneCount(string Hook)
    {
        if (!IsCurrent) return 0;
        var Key = Array.BinarySearch(
            OccupancyHooks, Hook, StringComparer.Ordinal);
        return Key < 0 ? 0 : OccupancyCounts[Key];
    }

    void CommitZeroOccupancy()
    {
        if (OccupancyHooks.Length == 0) return;
        Array.Clear(
            NextOccupancyCounts, 0,
            NextOccupancyCounts.Length);
        CommitOccupancy();
    }

    void CommitOccupancy()
    {
        var PreviousCounts = OccupancyCounts;
        OccupancyCounts = NextOccupancyCounts;
        NextOccupancyCounts = PreviousCounts;
        for (var Key = 0; Key < OccupancyHooks.Length; Key++)
            if (OccupancyCounts[Key] != NextOccupancyCounts[Key])
                AuthoringRuntime.PublishEffectZoneOccupancy(
                    Id, OccupancyHooks[Key], OccupancyCounts[Key]);
    }

    internal void RefreshVisuals()
    {
        foreach (var Visual in Visuals)
        {
            var EffectActive = Visual.EffectIndex < 0
                || Visual.EffectIndex < Effects.Length && Effects[Visual.EffectIndex].IsPlaying;
            Visual.Apply(IsCurrent && EffectActive && Visual.Requested, PresentationClock);
        }
    }

    internal AudioClip? NextEffectSound()
    {
        if (Contract.EffectSounds.Count == 0) return null;
        return Contract.EffectSounds[EffectSoundIndex++ % Contract.EffectSounds.Count];
    }

    internal void Restore()
    {
        IsCurrent = false;
        FogSources = Array.Empty<(FogZoneState State, int TargetIndex)>();
        FogCommandSources.Clear();
        List<Exception>? Failures = null;

        AuthoringCleanup.Capture(ref Failures, () => PublishGeneratedZones(
            Array.Empty<(EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)>()));
        AuthoringCleanup.Capture(ref Failures, CommitZeroOccupancy);
        AuthoringCleanup.Capture(ref Failures, ClimbSurfaces.Deactivate);
        AuthoringCleanup.Capture(ref Failures, VisualEnvironment.Release);
        foreach (var Flow in GeneratedFlows)
            AuthoringCleanup.Capture(ref Failures, Flow.Stop);
        foreach (var Flow in FixedFlows)
            AuthoringCleanup.Capture(ref Failures, Flow.Stop);
        GeneratedFlows = Array.Empty<NativeFlowRuntime>();
        FixedFlows = Array.Empty<NativeFlowRuntime>();
        FlowPublications = Array.Empty<(NativeFlowRuntime, int, int, int)>();
        for (var i = NativeResolvers.Length - 1; i >= 0; i--) AuthoringCleanup.Capture(ref Failures, NativeResolvers[i].Restore);
        for (var i = EffectZones.Length - 1; i >= 0; i--)
        {
            var Zone = EffectZones[i];
            AuthoringCleanup.Capture(ref Failures, () => Zone.SetActive(false, 0f, 0d));
            AuthoringCleanup.Capture(ref Failures, Zone.StopEnvironment);
        }
        foreach (var Visual in Visuals) AuthoringCleanup.Capture(ref Failures, Visual.Restore);
        foreach (var Effect in Effects) AuthoringCleanup.Capture(ref Failures, Effect.Restore);
        foreach (var Emitter in SoundEmitters) AuthoringCleanup.Capture(ref Failures, Emitter.Restore);
        foreach (var Stinger in Stingers) AuthoringCleanup.Capture(ref Failures, Stinger.Restore);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Biome authoring occurrence cleanup failed.", Failures);
    }

    void RemoveGeneratedPresentation()
    {
        for (var i = EffectZones.Length - 1; i >= StableEffectZoneCount; i--)
        {
            EffectZones[i].SetActive(false, 0f, 0d);
            EffectZones[i].StopEnvironment();
        }
        for (var i = Visuals.Length - 1; i >= StableVisualCount; i--) Visuals[i].Restore();
        for (var i = Effects.Length - 1; i >= StableEffectCount; i--) Effects[i].Restore();
        for (var i = SoundEmitters.Length - 1; i >= StableSoundEmitterCount; i--) SoundEmitters[i].Restore();
        if (EffectZones.Length != StableEffectZoneCount)
        {
            var Values = new EffectZoneState[StableEffectZoneCount];
            Array.Copy(EffectZones, Values, Values.Length);
            EffectZones = Values;
        }
        if (Visuals.Length != StableVisualCount)
        {
            var Values = new VisualState[StableVisualCount];
            Array.Copy(Visuals, Values, Values.Length);
            Visuals = Values;
        }
        if (Effects.Length != StableEffectCount)
        {
            var Values = new EffectState[StableEffectCount];
            Array.Copy(Effects, Values, Values.Length);
            Effects = Values;
        }
        if (SoundEmitters.Length != StableSoundEmitterCount)
        {
            var Values = new SoundEmitterState[StableSoundEmitterCount];
            Array.Copy(SoundEmitters, Values, Values.Length);
            SoundEmitters = Values;
        }
    }
}
