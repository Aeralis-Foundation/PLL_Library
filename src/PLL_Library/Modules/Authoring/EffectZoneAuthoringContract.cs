using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class EffectZoneAuthoringContract
{
    readonly AuthoringBindingOwner OwnerBinding;
    readonly ComponentBinding<PllEffectZoneAuthoring> ZoneBinding;
    readonly ComponentBinding<PllEffectAuthoring>[] EffectBindings;
    readonly int[] EffectIndices;
    readonly PllZoneVolume Volume;
    readonly string OccupancyHook;
    readonly PllEffectZoneEnvironmentFog EnvironmentFog;
    readonly PllHookedPropGenerationSourceContract.Action[] GenerationActions;
    readonly ComponentBinding<PllHookedPropGenerationAuthoring>[] TargetBindings;
    readonly bool LoopedValue;
    readonly FogAction[] FogActions;
    readonly (ComponentBinding<PllFogZoneAuthoring> Binding, bool SameOwner)[] FogTargets;
    readonly NativeSourceAction[] SourceActions;
    readonly (ComponentBinding<PLLNativeResolver> Binding, bool SameOwner)[] NativeSourceTargets;
    readonly ActorTarget[] ActorTargets;
    readonly InteractionAction[] InteractionActions;
    readonly ActorTarget[] InteractionTargets;
    readonly float CoverDelaySeconds;

    internal readonly struct FogAction
    {
        internal FogAction(PllEffectZoneTransition Transition, PllFogZoneOperation Operation, int TargetIndex)
        {
            this.Transition = Transition; this.Operation = Operation; this.TargetIndex = TargetIndex;
        }
        internal PllEffectZoneTransition Transition { get; }
        internal PllFogZoneOperation Operation { get; }
        internal int TargetIndex { get; }
    }

    internal readonly struct NativeSourceAction
    {
        internal NativeSourceAction(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation, int TargetIndex)
        {
            this.Transition = Transition; this.Operation = Operation; this.TargetIndex = TargetIndex;
        }
        internal PllEffectZoneTransition Transition { get; }
        internal PllNativeSourceOperation Operation { get; }
        internal int TargetIndex { get; }
    }

    internal readonly struct InteractionAction
    {
        internal InteractionAction(PllEffectZoneTransition Transition, PllPropOperation Operation, int TargetIndex, bool Enabled)
        { this.Transition = Transition; this.Operation = Operation; this.TargetIndex = TargetIndex; this.Enabled = Enabled; }
        internal PllEffectZoneTransition Transition { get; }
        internal PllPropOperation Operation { get; }
        internal int TargetIndex { get; }
        internal bool Enabled { get; }
    }

    EffectZoneAuthoringContract(
        AuthoringBindingOwner OwnerBinding,
        ComponentBinding<PllEffectZoneAuthoring> ZoneBinding,
        ObjectBinding RootBinding,
        ComponentBinding<PllEffectAuthoring>[] EffectBindings,
        int[] EffectIndices,
        PllZoneVolume Volume,
        string OccupancyHook,
        PllEffectZoneEnvironmentFog EnvironmentFog,
        PllHookedPropGenerationSourceContract.Action[] GenerationActions, ComponentBinding<PllHookedPropGenerationAuthoring>[] TargetBindings, bool Looped,
        FogAction[] FogActions, (ComponentBinding<PllFogZoneAuthoring> Binding, bool SameOwner)[] FogTargets,
        NativeSourceAction[] SourceActions, (ComponentBinding<PLLNativeResolver> Binding, bool SameOwner)[] NativeSourceTargets,
        float CoverDelaySeconds, ActorTarget[] ActorTargets, InteractionAction[] InteractionActions, ActorTarget[] InteractionTargets)
    {
        this.OwnerBinding = OwnerBinding;
        this.ZoneBinding = ZoneBinding;
        Root = RootBinding;
        this.EffectBindings = EffectBindings;
        this.EffectIndices = EffectIndices;
        this.Volume = Volume;
        this.OccupancyHook = OccupancyHook;
        this.EnvironmentFog = EnvironmentFog;
        this.GenerationActions = GenerationActions; this.TargetBindings = TargetBindings; LoopedValue = Looped;
        this.FogActions = FogActions; this.FogTargets = FogTargets;
        this.SourceActions = SourceActions; this.NativeSourceTargets = NativeSourceTargets;
        this.CoverDelaySeconds = CoverDelaySeconds;
        this.ActorTargets = ActorTargets;
        this.InteractionActions = InteractionActions; this.InteractionTargets = InteractionTargets;
    }

    internal IReadOnlyList<PllHookedPropGenerationSourceContract.Action> PropTriggers => GenerationActions;
    internal IReadOnlyList<FogAction> FogTriggers => FogActions;
    internal IReadOnlyList<NativeSourceAction> NativeSourceTriggers => SourceActions;
    internal IReadOnlyList<InteractionAction> InteractionTriggers => InteractionActions;
    internal int ActorSourceActionCount => ActorTargets.Length;
    internal bool Looped => LoopedValue;
    internal ObjectBinding Root { get; }
    internal AuthoringBindingOwner Owner => OwnerBinding;
    internal PllEffectZoneAuthoring Source => ZoneBinding.Source;
    internal bool Generated => OwnerBinding.Generated;
    internal bool RequiresNativeFog =>
        EnvironmentFog == PllEffectZoneEnvironmentFog.EnvironmentalFogless;

    internal static EffectZoneAuthoringContract[] CreateAll(
        Transform Template,
        BiomeId Biome,
        AuthoringCandidateGraph Graph,
        EffectAuthoringContract[] EffectContracts, HookedPropGenerationContract[] GenerationTargets,
        FogZoneAuthoringContract[] FogContracts, NativeFlowContract[] NativeSources,
        HazardAuthoringContract[] Hazards, EntityAuthoringContract[] Entities, PropInteractionContract[] PropInteractions)
    {
        var Targets = new PllHookedPropGenerationAuthoring[GenerationTargets.Length];
        for (var i = 0; i < Targets.Length; i++) Targets[i] = GenerationTargets[i].Source;
        var TargetOwners = new Dictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring>();
        var FogIndexBySource = new Dictionary<PllFogZoneAuthoring, int>();
        for (var i = 0; i < FogContracts.Length; i++) FogIndexBySource.Add(FogContracts[i].Source, i);
        var NativeSourceIndex = new Dictionary<PLLNativeResolver, int>();
        for (var i = 0; i < NativeSources.Length; i++) NativeSourceIndex.Add(NativeSources[i].Source, i);
        var EffectIndexBySource = new Dictionary<PllEffectAuthoring, int>();
        for (var Index = 0; Index < EffectContracts.Length; Index++) EffectIndexBySource.Add(EffectContracts[Index].Source, Index);
        var EffectOwners = new HashSet<PllEffectAuthoring>();
        var Contracts = new List<EffectZoneAuthoringContract>();
        var ZoneSources = AuthoringBindingOwner.GraphComponents<PllEffectZoneAuthoring>(
            Template, Biome, Graph, "effect-zone authoring");
        foreach (var Entry in ZoneSources)
        {
            var Source = Entry.Value;
            var BindingRoot = Entry.Owner.BindingRoot;
            if (!Source || !Source.enabled || Source.Root != Source.gameObject
                || !ValidOwnerRoot(BindingRoot, Source.transform))
                throw new ArgumentException($"Biome '{Biome}' has an invalid effect-zone root.", "template");
            var EnvironmentFog = Source.EnvironmentFog;
            if (!PllAuthoringValueContract.IsDefined(EnvironmentFog))
                throw new ArgumentException(
                    $"Biome '{Biome}' effect zone '{Source.name}' has an invalid environment-fog mode.",
                    "template");
            ZoneVolumeContract.Validate(Source.Volume, Biome, Source.name, true);
            if (PllAuthoringValueContract.TransitionFailure(Source.Transition.CoverDelaySeconds) is { } TransitionFailure)
                throw new ArgumentException(TransitionFailure, "template");
            var OccupancyHook = Source.OccupancyHook;
            if (OccupancyHook.Length != 0)
            {
                if (Entry.Owner.Generated)
                    throw new ArgumentException(
                        $"Biome '{Biome}' generated effect zone '{Source.name}' cannot publish occupancy.",
                        "template");
                try
                {
                    HookedPropRuntime.ValidateHook(
                        OccupancyHook,
                        nameof(PllEffectZoneAuthoring.OccupancyHook));
                }
                catch (ArgumentException Failure)
                {
                    throw new ArgumentException(
                        $"Biome '{Biome}' effect zone '{Source.name}' has an invalid occupancy hook.",
                        "template", Failure);
                }
            }
            var GenerationActions = PllHookedPropGenerationSourceContract.ValidateActions(Source, Entry.Owner.Generated, Targets, TargetOwners);
            var FogActions = new FogAction[Source.FogTriggers.Count];
            var FogTargets = new (ComponentBinding<PllFogZoneAuthoring> Binding, bool SameOwner)[FogActions.Length];
            var FogEdgeTargets = new HashSet<(PllEffectZoneTransition, int)>();
            for (var i = 0; i < FogActions.Length; i++)
            {
                var Trigger = Source.FogTriggers[i];
                if (Trigger is null || !Trigger.Target
                    || Trigger.Transition is not PllEffectZoneTransition.Enter and not PllEffectZoneTransition.Exit
                    || Trigger.Operation is not PllFogZoneOperation.Activate and not PllFogZoneOperation.Deactivate
                    || !FogIndexBySource.TryGetValue(Trigger.Target, out var TargetIndex))
                    throw new ArgumentException($"Biome '{Biome}' effect zone '{Source.name}' has an invalid or unregistered Fog Zone action.");
                if (!FogEdgeTargets.Add((Trigger.Transition, TargetIndex)))
                    throw new ArgumentException($"Biome '{Biome}' effect zone '{Source.name}' has duplicate or conflicting Fog Zone actions on one edge.");
                FogActions[i] = new FogAction(Trigger.Transition, Trigger.Operation, TargetIndex);
                var Target = FogContracts[TargetIndex];
                FogTargets[i] = (Target.SourceBinding,
                    ReferenceEquals(Entry.Owner.SourceRoot, Target.Owner.SourceRoot));
            }
            var SourceActions = new NativeSourceAction[Source.NativeSourceTriggers.Count + Source.ActorSourceTriggers.Count];
            var SourceTargets = new (ComponentBinding<PLLNativeResolver> Binding, bool SameOwner)[Source.NativeSourceTriggers.Count];
            var SourceEdges = new HashSet<(PllEffectZoneTransition, int)>();
            for (var i = 0; i < Source.NativeSourceTriggers.Count; i++)
            {
                var Trigger = Source.NativeSourceTriggers[i];
                if (Trigger is null || !Trigger.Target
                    || Trigger.Transition is not PllEffectZoneTransition.Enter and not PllEffectZoneTransition.Exit
                    || Trigger.Operation is not PllNativeSourceOperation.Activate and not PllNativeSourceOperation.Deactivate
                    || !NativeSourceIndex.TryGetValue(Trigger.Target, out var TargetIndex))
                    throw new ArgumentException($"Biome '{Biome}' effect zone '{Source.name}' has an invalid or unregistered native source action.");
                if (Trigger.Target.SourceSettings.SpawnLights)
                    throw new ArgumentException("Action-controlled fluid sources cannot use baked lights.");
                if (!SourceEdges.Add((Trigger.Transition, TargetIndex)))
                    throw new ArgumentException($"Biome '{Biome}' effect zone '{Source.name}' has duplicate or conflicting native source actions on one edge.");
                SourceActions[i] = new NativeSourceAction(Trigger.Transition, Trigger.Operation, FogContracts.Length + TargetIndex);
                var Target = NativeSources[TargetIndex];
                SourceTargets[i] = (Target.SourceBinding, ReferenceEquals(Entry.Owner.SourceRoot.transform, Target.Template));
            }
            var ActorTargets = new ActorTarget[Source.ActorSourceTriggers.Count];
            for (var i = 0; i < ActorTargets.Length; i++)
            {
                var Trigger = Source.ActorSourceTriggers[i];
                if (Trigger is null || !Trigger.Target
                    || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                    || Trigger.Operation is not (PllNativeSourceOperation.Activate or PllNativeSourceOperation.Deactivate))
                    throw new ArgumentException("Effect Zone actor source action is missing or unsupported.");
                int TargetIndex;
                if (Trigger.Target is PllHazardAuthoring Hazard)
                {
                    var Index = Array.FindIndex(Hazards, Value => ReferenceEquals(Value.Source, Hazard));
                    if (Index < 0 || Hazard.Behavior is PllPluginHazardBehavior)
                        throw new ArgumentException("Hazard source action needs an admitted reversible native definition.");
                    ActorTargets[i] = new ActorTarget<PllHazardAuthoring>(Hazards[Index].SourceBinding,
                        ReferenceEquals(Entry.Owner.SourceRoot, Hazards[Index].Owner.SourceRoot));
                    TargetIndex = FogContracts.Length + NativeSources.Length + Index;
                }
                else if (Trigger.Target is PllEntityAuthoring Entity)
                {
                    var Index = Array.FindIndex(Entities, Value => ReferenceEquals(Value.Source, Entity));
                    if (Index < 0 || Entity.Behavior is PllPluginEntityBehavior)
                        throw new ArgumentException("Entity source action needs an admitted reversible native definition.");
                    ActorTargets[i] = new ActorTarget<PllEntityAuthoring>(Entities[Index].SourceBinding,
                        ReferenceEquals(Entry.Owner.SourceRoot, Entities[Index].Owner.SourceRoot));
                    TargetIndex = FogContracts.Length + NativeSources.Length + Hazards.Length + Index;
                }
                else throw new ArgumentException("Effect Zone actor source target is unsupported.");
                if (!SourceEdges.Add((Trigger.Transition, TargetIndex)))
                    throw new ArgumentException("Effect Zone has duplicate or conflicting actor source actions on one edge.");
                SourceActions[Source.NativeSourceTriggers.Count + i] = new NativeSourceAction(Trigger.Transition, Trigger.Operation, TargetIndex);
            }
            var TargetBindings = new ComponentBinding<PllHookedPropGenerationAuthoring>[Source.PropTriggers.Count];
            for (var i = 0; i < TargetBindings.Length; i++)
                TargetBindings[i] = ComponentBinding<PllHookedPropGenerationAuthoring>.Create(BindingRoot, Source.PropTriggers[i].Target);
            var InteractionActions = new InteractionAction[Source.InteractionTriggers.Count];
            var InteractionTargets = new ActorTarget[InteractionActions.Length];
            var InteractionEdges = new HashSet<(PllEffectZoneTransition, int, int)>();
            for (var i = 0; i < InteractionActions.Length; i++)
            {
                var Trigger = Source.InteractionTriggers[i];
                if (Trigger is null || !Trigger.Target || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit))
                    throw new ArgumentException("Effect Zone prop interaction action is missing or unsupported.");
                var TargetIndex = Array.FindIndex(PropInteractions, Value => Value.Source == Trigger.Target.transform);
                if (TargetIndex < 0) throw new ArgumentException("Effect Zone prop interaction target has no admitted definition.");
                var Target = PropInteractions[TargetIndex];
                var SameOwner = ReferenceEquals(Entry.Owner.SourceRoot, Target.SourceRoot);
                if (Trigger.Target is PllBreakablePropAuthoring Break && Target.HasBreak
                    && Trigger.Operation is PllPropOperation.Break or PllPropOperation.SetBreakableEnabled)
                    InteractionTargets[i] = new ActorTarget<PllBreakablePropAuthoring>(
                        ComponentBinding<PllBreakablePropAuthoring>.Create(Target.SourceRoot.transform, Break), SameOwner);
                else if (Trigger.Target is PllSwingingPropAuthoring Motion && Target.HasMotion
                    && Target.Motion != PllPropMotion.NativeBridgeResponse
                    && Trigger.Operation is PllPropOperation.StartMotion or PllPropOperation.PauseMotion or PllPropOperation.ResumeMotion)
                    InteractionTargets[i] = new ActorTarget<PllSwingingPropAuthoring>(
                        ComponentBinding<PllSwingingPropAuthoring>.Create(Target.SourceRoot.transform, Motion), SameOwner);
                else throw new ArgumentException("Effect Zone prop operation is not supported by its target definition.");
                var Dimension = Trigger.Operation switch
                {
                    PllPropOperation.Break => 0,
                    PllPropOperation.SetBreakableEnabled => 1,
                    _ => 2
                };
                if (!InteractionEdges.Add((Trigger.Transition, TargetIndex, Dimension)))
                    throw new ArgumentException("Effect Zone has duplicate or conflicting prop interaction actions on one edge.");
                InteractionActions[i] = new InteractionAction(Trigger.Transition, Trigger.Operation, TargetIndex, Trigger.Enabled);
            }
            var Indices = BindEffects(BindingRoot, Entry.Owner.SourceRoot, Biome, Source.Root, Source.Effects,
                EffectIndexBySource, EffectContracts, EffectOwners, out var Bindings);
            Contracts.Add(new EffectZoneAuthoringContract(
                Entry.Owner,
                ComponentBinding<PllEffectZoneAuthoring>.Create(BindingRoot, Source),
                ObjectBinding.Create(BindingRoot, Source.Root), Bindings, Indices,
                Source.Volume, OccupancyHook, EnvironmentFog, GenerationActions, TargetBindings, Source.Looped,
                FogActions, FogTargets, SourceActions, SourceTargets, Source.Transition.CoverDelaySeconds, ActorTargets,
                InteractionActions, InteractionTargets));
        }
        foreach (var Source in Template.GetComponentsInChildren<PLLNativeResolver>(true))
        {
            if (Source && Source.Presentation && !EffectOwners.Add(Source.Presentation))
                throw new ArgumentException(
                    $"Biome '{Biome}' Zone effect '{Source.Presentation.name}' has more than one zone owner.", "template");
        }
        for (var Index = 0; Index < Contracts.Count; Index++)
            for (var OtherIndex = Index + 1; OtherIndex < Contracts.Count; OtherIndex++)
                if (ReferenceEquals(Contracts[Index].OwnerBinding.SourceRoot, Contracts[OtherIndex].OwnerBinding.SourceRoot)
                    && (TransformOwnership.Contains(Contracts[Index].Root.Source.transform, Contracts[OtherIndex].Root.Source.transform)
                    || TransformOwnership.Contains(Contracts[OtherIndex].Root.Source.transform, Contracts[Index].Root.Source.transform))
                    )
                    throw new ArgumentException($"Biome '{Biome}' effect-zone roots must be disjoint.", "template");
        foreach (var Effect in EffectContracts)
            if (Effect.Source.Mode == PllEffectMode.Zone && !EffectOwners.Contains(Effect.Source))
                throw new ArgumentException($"Biome '{Biome}' Zone effect '{Effect.Source.name}' has no exclusive effect-zone or native-resolver owner.", "template");
        return Contracts.ToArray();
    }

    internal ResolvedEffectZone ResolveStable(Transform RuntimeRoot, BiomeId Biome)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{Biome}' cannot resolve a generated effect zone as stable authoring.");
        return Resolve(RuntimeRoot, Biome);
    }

    internal bool TryResolveGenerated(
        PllEffectZoneAuthoring Source,
        BiomeId Biome,
        out ResolvedEffectZone Resolved)
    {
        Resolved = default;
        if (!TryMatchGenerated(Source, Biome, out var RuntimeRoot)) return false;
        Resolved = Resolve(RuntimeRoot, Biome, Source);
        return true;
    }

    internal bool TryMatchGenerated(PllEffectZoneAuthoring Source, BiomeId Biome, out Transform RuntimeRoot)
    {
        RuntimeRoot = null!;
        if (!Source || !OwnerBinding.TryGetAdmittedGeneratedRoot(Source.transform, out RuntimeRoot)) return false;
        if (ZoneBinding.Resolve(RuntimeRoot, Biome, "generated effect-zone authoring") == Source) return true;
        RuntimeRoot = null!;
        return false;
    }

    internal ResolvedEffectZone ResolveGenerated(Transform AdmittedCandidateRoot, BiomeId Biome)
    {
        if (!Generated) throw new InvalidOperationException($"Biome '{Biome}' cannot resolve a stable effect zone as generated authoring.");
        return Resolve(AdmittedCandidateRoot, Biome);
    }

    ResolvedEffectZone Resolve(Transform RuntimeRoot, BiomeId Biome, PllEffectZoneAuthoring? Source = null)
    {
        var OwnerRoot = Root.Resolve(RuntimeRoot, Biome, "effect-zone owner root");
        var ResolvedEffects = new PllEffectAuthoring[EffectBindings.Length];
        for (var Index = 0; Index < EffectBindings.Length; Index++)
            ResolvedEffects[Index] = EffectBindings[Index].Resolve(RuntimeRoot, Biome, "effect-zone effect");
        if (!ValidOwnerRoot(RuntimeRoot, OwnerRoot.transform, CheckActivePath: false))
            throw new InvalidOperationException($"Biome '{Biome}' runtime effect-zone owner became invalid.");
        Source ??= ZoneBinding.Resolve(RuntimeRoot, Biome, "effect-zone authoring");
        if (Source.Root != OwnerRoot || !Source.Volume.Equals(Volume)
            || !string.Equals(Source.OccupancyHook, OccupancyHook, StringComparison.Ordinal)
            || Source.EnvironmentFog != EnvironmentFog
            || Source.Transition.CoverDelaySeconds != CoverDelaySeconds
            || Source.Looped != LoopedValue || !SameActions(Source, RuntimeRoot, Biome)
            || !SameFogActions(Source, RuntimeRoot, Biome)
            || !SameNativeSourceActions(Source, RuntimeRoot, Biome)
            || !SameInteractionActions(Source, RuntimeRoot, Biome)
            || !Same(Source.Effects, ResolvedEffects))
            throw new InvalidOperationException($"Biome '{Biome}' runtime effect zone changed its sealed contract.");
        return new ResolvedEffectZone(
            OwnerRoot, Volume, EffectIndices, OccupancyHook, EnvironmentFog, CoverDelaySeconds);
    }

    internal void Validate(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        OwnerBinding.Validate(Template, Graph, ZoneBinding.Source.transform, Biome, "effect-zone authoring");
        var BindingRoot = OwnerBinding.BindingRoot;
        Root.Validate(BindingRoot, Biome, "effect-zone owner root");
        foreach (var Effect in EffectBindings) Effect.Validate(BindingRoot, Biome, "effect-zone effect");
        if (!ValidOwnerRoot(BindingRoot, Root.Source.transform))
            throw new InvalidOperationException($"Biome '{Biome}' changed an effect-zone owner root before sealing.");
        ZoneBinding.Validate(BindingRoot, Biome, "effect-zone authoring");
        var Source = ZoneBinding.Source;
        if (!Source.enabled || Source.Root != Root.Source || !Source.Volume.Equals(Volume)
            || !string.Equals(Source.OccupancyHook, OccupancyHook, StringComparison.Ordinal)
            || Source.EnvironmentFog != EnvironmentFog
            || Source.Transition.CoverDelaySeconds != CoverDelaySeconds
            || Source.Looped != LoopedValue || !SameActions(Source, null, Biome)
            || !SameFogActions(Source, null, Biome)
            || !SameNativeSourceActions(Source, null, Biome)
            || !SameInteractionActions(Source, null, Biome)
            || !Same(Source.Effects, EffectBindings))
            throw new InvalidOperationException($"Biome '{Biome}' changed effect zone '{Source.name}' before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        OwnerBinding.WriteManifest(Writer);
        ZoneBinding.WriteManifest(Writer);
        Root.WriteManifest(Writer);
        ZoneVolumeContract.WriteManifest(Writer, Volume);
        Writer.Write(OccupancyHook);
        Writer.Write((byte)EnvironmentFog);
        Writer.Write(LoopedValue); Writer.Write(GenerationActions.Length);
        foreach (var Action in GenerationActions) { Writer.Write((byte)Action.Transition); Writer.Write(Action.TargetIndex); }
        Writer.Write(EffectBindings.Length);
        for (var Index = 0; Index < EffectBindings.Length; Index++)
        {
            EffectBindings[Index].WriteManifest(Writer);
            Writer.Write(EffectIndices[Index]);
        }
        if (FogActions.Length != 0)
        {
            Writer.Write("PLL/EffectZoneFogActions/v1"); Writer.Write(FogActions.Length);
            foreach (var Action in FogActions)
            {
                Writer.Write((byte)Action.Transition); Writer.Write((byte)Action.Operation); Writer.Write(Action.TargetIndex);
            }
        }
        ManifestEncoding.WriteScalar(Writer, CoverDelaySeconds);
        if (SourceActions.Length != 0)
        {
            Writer.Write("PLL/EffectZoneNativeSourceActions/v1"); Writer.Write(SourceActions.Length);
            foreach (var Action in SourceActions)
            {
                Writer.Write((byte)Action.Transition); Writer.Write((byte)Action.Operation); Writer.Write(Action.TargetIndex);
            }
        }
        if (InteractionActions.Length != 0)
        {
            Writer.Write("PLL/EffectZonePropInteractions/v1"); Writer.Write(InteractionActions.Length);
            foreach (var Action in InteractionActions)
            {
                Writer.Write((byte)Action.Transition); Writer.Write((byte)Action.Operation);
                Writer.Write(Action.TargetIndex); Writer.Write(Action.Enabled);
            }
            foreach (var Target in InteractionTargets) Target.WriteManifest(Writer);
        }
    }

    bool SameInteractionActions(PllEffectZoneAuthoring Source, Transform? RuntimeRoot, BiomeId Biome)
    {
        if (Source.InteractionTriggers.Count != InteractionActions.Length) return false;
        for (var i = 0; i < InteractionActions.Length; i++)
        {
            var Trigger = Source.InteractionTriggers[i]; var Action = InteractionActions[i];
            if (Trigger is null || Trigger.Transition != Action.Transition || Trigger.Operation != Action.Operation
                || Trigger.Enabled != Action.Enabled || Trigger.Target != InteractionTargets[i].Resolve(RuntimeRoot, Biome)) return false;
        }
        return true;
    }

    bool SameNativeSourceActions(PllEffectZoneAuthoring Source, Transform? RuntimeRoot, BiomeId Biome)
    {
        if (Source.NativeSourceTriggers.Count != NativeSourceTargets.Length
            || Source.ActorSourceTriggers.Count != ActorTargets.Length) return false;
        for (var i = 0; i < NativeSourceTargets.Length; i++)
        {
            var Trigger = Source.NativeSourceTriggers[i];
            if (Trigger is null || !Trigger.Target || Trigger.Transition != SourceActions[i].Transition
                || Trigger.Operation != SourceActions[i].Operation
                || Trigger.Target != (RuntimeRoot is null || !NativeSourceTargets[i].SameOwner ? NativeSourceTargets[i].Binding.Source
                    : NativeSourceTargets[i].Binding.Resolve(RuntimeRoot, Biome, "effect-zone native source target"))) return false;
        }
        for (var i = 0; i < ActorTargets.Length; i++)
        {
            var Trigger = Source.ActorSourceTriggers[i];
            var Action = SourceActions[NativeSourceTargets.Length + i];
            if (Trigger is null || Trigger.Transition != Action.Transition || Trigger.Operation != Action.Operation
                || !ReferenceEquals(Trigger.Target, ActorTargets[i].Resolve(RuntimeRoot, Biome))) return false;
        }
        return true;
    }

    abstract class ActorTarget
    {
        internal abstract MonoBehaviour Resolve(Transform? RuntimeRoot, BiomeId Biome);
        internal abstract void WriteManifest(BinaryWriter Writer);
    }
    sealed class ActorTarget<T> : ActorTarget where T : MonoBehaviour
    {
        readonly ComponentBinding<T> Binding;
        readonly bool SameOwner;
        internal ActorTarget(ComponentBinding<T> Binding, bool SameOwner) { this.Binding = Binding; this.SameOwner = SameOwner; }
        internal override MonoBehaviour Resolve(Transform? RuntimeRoot, BiomeId Biome) =>
            RuntimeRoot is null || !SameOwner ? Binding.Source : Binding.Resolve(RuntimeRoot, Biome, "effect-zone actor source target");
        internal override void WriteManifest(BinaryWriter Writer) { Binding.WriteManifest(Writer); Writer.Write(SameOwner); }
    }

    bool SameFogActions(PllEffectZoneAuthoring Source, Transform? RuntimeRoot, BiomeId Biome)
    {
        if (Source.FogTriggers.Count != FogActions.Length) return false;
        for (var i = 0; i < FogActions.Length; i++)
        {
            var Trigger = Source.FogTriggers[i];
            if (Trigger is null || !Trigger.Target || Trigger.Transition != FogActions[i].Transition
                || Trigger.Operation != FogActions[i].Operation
                || Trigger.Target != (RuntimeRoot is null || !FogTargets[i].SameOwner ? FogTargets[i].Binding.Source
                    : FogTargets[i].Binding.Resolve(RuntimeRoot, Biome, "effect-zone fog target"))) return false;
        }
        return true;
    }

    bool SameActions(PllEffectZoneAuthoring Source, Transform? RuntimeRoot, BiomeId Biome)
    {
        if (Source.PropTriggers.Count != GenerationActions.Length) return false;
        for (var i = 0; i < GenerationActions.Length; i++)
            if (Source.PropTriggers[i] is not { } Trigger
                || Trigger.Transition != GenerationActions[i].Transition
                || Trigger.Target != (RuntimeRoot is null ? TargetBindings[i].Source
                    : TargetBindings[i].Resolve(RuntimeRoot, Biome, "effect-zone generation target"))) return false;
        return true;
    }
    static int[] BindEffects(
        Transform BindingRoot,
        GameObject FeatureRoot,
        BiomeId Biome,
        GameObject ZoneRoot,
        IReadOnlyList<PllEffectAuthoring> SourceEffects,
        IReadOnlyDictionary<PllEffectAuthoring, int> EffectIndexBySource,
        IReadOnlyList<EffectAuthoringContract> EffectContracts,
        ISet<PllEffectAuthoring> EffectOwners,
        out ComponentBinding<PllEffectAuthoring>[] Bindings)
    {
        var Indices = new int[SourceEffects.Count];
        Bindings = new ComponentBinding<PllEffectAuthoring>[SourceEffects.Count];
        var LocalEffects = new HashSet<PllEffectAuthoring>();
        for (var Index = 0; Index < SourceEffects.Count; Index++)
        {
            var Effect = SourceEffects[Index];
            if (!Effect || !LocalEffects.Add(Effect) || !EffectIndexBySource.TryGetValue(Effect, out Indices[Index])
                || Effect.Mode != PllEffectMode.Zone || !EffectOwners.Add(Effect)
                || !ReferenceEquals(EffectContracts[Indices[Index]].Owner.SourceRoot, FeatureRoot)
                || Effect.Root == ZoneRoot || !TransformOwnership.Contains(ZoneRoot.transform, Effect.Root ? Effect.Root.transform : null))
                throw new ArgumentException($"Biome '{Biome}' zone owner '{ZoneRoot.name}' contains a missing, duplicate, unregistered, non-Zone, shared, or unowned effect.", "sourceEffects");
            Bindings[Index] = ComponentBinding<PllEffectAuthoring>.Create(BindingRoot, Effect);
        }
        return Indices;
    }

    static bool Same(IReadOnlyList<PllEffectAuthoring> Values, PllEffectAuthoring[] Expected)
    {
        if (Values.Count != Expected.Length) return false;
        for (var Index = 0; Index < Expected.Length; Index++)
            if (Values[Index] != Expected[Index]) return false;
        return true;
    }

    static bool Same(IReadOnlyList<PllEffectAuthoring> Values, ComponentBinding<PllEffectAuthoring>[] Expected)
    {
        if (Values.Count != Expected.Length) return false;
        for (var Index = 0; Index < Expected.Length; Index++)
            if (Values[Index] != Expected[Index].Source) return false;
        return true;
    }

    static bool ValidOwnerRoot(Transform BindingRoot, Transform Value, bool CheckActivePath = true) =>
        Value != BindingRoot && TransformOwnership.Contains(BindingRoot, Value)
        && (!CheckActivePath || TransformOwnership.DiscoverableWhenActivated(BindingRoot, Value))
        && !TransformOwnership.HasPropSpawnerInPath(BindingRoot, Value);
}

internal readonly struct ResolvedEffectZone
{
    internal ResolvedEffectZone(
        GameObject Root, PllZoneVolume Volume, int[] EffectIndices,
        string OccupancyHook, PllEffectZoneEnvironmentFog EnvironmentFog, float CoverDelaySeconds)
    {
        this.Root = Root;
        this.Volume = Volume;
        this.EffectIndices = EffectIndices;
        this.OccupancyHook = OccupancyHook;
        this.EnvironmentFog = EnvironmentFog;
        this.CoverDelaySeconds = CoverDelaySeconds;
    }
    internal GameObject Root { get; }
    internal PllZoneVolume Volume { get; }
    internal int[] EffectIndices { get; }
    internal string OccupancyHook { get; }
    internal PllEffectZoneEnvironmentFog EnvironmentFog { get; }
    internal float CoverDelaySeconds { get; }
}
