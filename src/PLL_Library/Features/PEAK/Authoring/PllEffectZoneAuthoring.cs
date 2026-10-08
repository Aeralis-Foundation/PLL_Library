using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Effects/Effect Zone")]
public sealed class PllEffectZoneAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume volume = default;
#endif
    [SerializeField, HideInInspector] PllZoneShape BundleShape = default;
    [SerializeField, HideInInspector] Vector3 BundleCenter = default;
    [SerializeField, HideInInspector] Vector3 BundleSize = default;
    [SerializeField, HideInInspector] float BundleRadius = 0f;
    [SerializeField] string OccupancyHookValue = "";
    [SerializeField] PllEffectZoneEnvironmentFog EnvironmentFogValue =
        PllEffectZoneEnvironmentFog.Inherit;
    [SerializeField] PllEffectAuthoring[] effects = Array.Empty<PllEffectAuthoring>();
    [SerializeField] bool LoopedValue = false;
    [SerializeField] PllEffectZonePropTrigger[] PropTriggersValue = Array.Empty<PllEffectZonePropTrigger>();
    [SerializeField, HideInInspector] int[] BundlePropTransitions = Array.Empty<int>();
    [SerializeField, HideInInspector] UnityEngine.Object[] BundlePropTargets = Array.Empty<UnityEngine.Object>();
    [SerializeField] PllEffectZoneFogTrigger[] FogTriggersValue = Array.Empty<PllEffectZoneFogTrigger>();
    [SerializeField, HideInInspector] int[] BundleFogTransitions = Array.Empty<int>();
    [SerializeField, HideInInspector] int[] BundleFogOperations = Array.Empty<int>();
    [SerializeField, HideInInspector] UnityEngine.Object[] BundleFogTargets = Array.Empty<UnityEngine.Object>();
#if PLL_CREATOR_SDK
    [SerializeField] PllPresentationTransition TransitionValue = default;
#endif
    [SerializeField, HideInInspector] float BundleCoverDelaySeconds = 0f;
    [SerializeField] PllEffectZoneNativeSourceTrigger[] NativeSourceTriggersValue = Array.Empty<PllEffectZoneNativeSourceTrigger>();
    [SerializeField, HideInInspector] int[] BundleNativeSourceTransitions = Array.Empty<int>();
    [SerializeField, HideInInspector] int[] BundleNativeSourceOperations = Array.Empty<int>();
    [SerializeField, HideInInspector] UnityEngine.Object[] BundleNativeSourceTargets = Array.Empty<UnityEngine.Object>();
#if PLL_CREATOR_SDK
    [SerializeReference] PllEffectZoneActorSourceTrigger[] ActorSourceTriggersValue = Array.Empty<PllEffectZoneActorSourceTrigger>();
    [SerializeReference] PllEffectZoneInteractionTrigger[] InteractionTriggersValue = Array.Empty<PllEffectZoneInteractionTrigger>();
#endif
    [SerializeField, HideInInspector] int[] BundleInteractionTransitions = Array.Empty<int>();
    [SerializeField, HideInInspector] int[] BundleInteractionOperations = Array.Empty<int>();
    [SerializeField, HideInInspector] UnityEngine.Object[] BundleInteractionTargets = Array.Empty<UnityEngine.Object>();
    [SerializeField, HideInInspector] bool[] BundleInteractionEnabled = Array.Empty<bool>();
    [SerializeField, HideInInspector] int[] BundleActorSourceTransitions = Array.Empty<int>();
    [SerializeField, HideInInspector] int[] BundleActorSourceOperations = Array.Empty<int>();
    [SerializeField, HideInInspector] UnityEngine.Object[] BundleActorSourceTargets = Array.Empty<UnityEngine.Object>();
#if !PLL_CREATOR_SDK
    IReadOnlyList<PllEffectZonePropTrigger>? ParsedPropTriggers;
    IReadOnlyList<PllEffectZoneFogTrigger>? ParsedFogTriggers;
    IReadOnlyList<PllEffectZoneNativeSourceTrigger>? ParsedNativeSourceTriggers;
    IReadOnlyList<PllEffectZoneActorSourceTrigger>? ParsedActorSourceTriggers;
    IReadOnlyList<PllEffectZoneInteractionTrigger>? ParsedInteractionTriggers;
#endif

    public GameObject Root => root;
#if PLL_CREATOR_SDK
    public PllZoneVolume Volume => volume;

    internal void SealBundleSerialization()
    {
        BundleCoverDelaySeconds = TransitionValue.CoverDelaySeconds;
        BundleShape = volume.Shape; BundleCenter = volume.Center;
        BundleSize = volume.Size; BundleRadius = volume.Radius;
        var Triggers = PropTriggers;
        var Transitions = new int[Triggers.Count]; var Targets = new UnityEngine.Object[Triggers.Count];
        for (var i = 0; i < Triggers.Count; i++)
        {
            var Trigger = Triggers[i] ?? throw new InvalidOperationException("Effect Zone generation action is missing.");
            Transitions[i] = (int)Trigger.Transition; Targets[i] = Trigger.Target;
        }
        BundlePropTransitions = Transitions; BundlePropTargets = Targets;
        var FogActions = FogTriggers;
        var FogTransitions = new int[FogActions.Count]; var Operations = new int[FogActions.Count];
        var FogTargets = new UnityEngine.Object[FogActions.Count];
        for (var i = 0; i < FogActions.Count; i++)
        {
            var Trigger = FogActions[i] ?? throw new InvalidOperationException("Effect Zone fog action is missing.");
            FogTransitions[i] = (int)Trigger.Transition; Operations[i] = (int)Trigger.Operation; FogTargets[i] = Trigger.Target;
        }
        BundleFogTransitions = FogTransitions; BundleFogOperations = Operations; BundleFogTargets = FogTargets;
        var SourceActions = NativeSourceTriggers;
        var SourceTransitions = new int[SourceActions.Count]; var SourceOperations = new int[SourceActions.Count];
        var SourceTargets = new UnityEngine.Object[SourceActions.Count];
        for (var i = 0; i < SourceActions.Count; i++)
        {
            var Trigger = SourceActions[i] ?? throw new InvalidOperationException("Effect Zone native source action is missing.");
            SourceTransitions[i] = (int)Trigger.Transition; SourceOperations[i] = (int)Trigger.Operation; SourceTargets[i] = Trigger.Target;
        }
        BundleNativeSourceTransitions = SourceTransitions; BundleNativeSourceOperations = SourceOperations;
        BundleNativeSourceTargets = SourceTargets;
        var ActorActions = ActorSourceTriggers;
        var ActorTransitions = new int[ActorActions.Count]; var ActorOperations = new int[ActorActions.Count];
        var ActorTargets = new UnityEngine.Object[ActorActions.Count];
        for (var i = 0; i < ActorActions.Count; i++)
        {
            var Trigger = ActorActions[i] ?? throw new InvalidOperationException("Effect Zone actor source action is missing.");
            ActorTransitions[i] = (int)Trigger.Transition; ActorOperations[i] = (int)Trigger.Operation; ActorTargets[i] = Trigger.Target;
        }
        BundleActorSourceTransitions = ActorTransitions; BundleActorSourceOperations = ActorOperations; BundleActorSourceTargets = ActorTargets;
        var Interactions = InteractionTriggers;
        BundleInteractionTransitions = new int[Interactions.Count]; BundleInteractionOperations = new int[Interactions.Count];
        BundleInteractionTargets = new UnityEngine.Object[Interactions.Count]; BundleInteractionEnabled = new bool[Interactions.Count];
        for (var i = 0; i < Interactions.Count; i++)
        {
            var Trigger = Interactions[i] ?? throw new InvalidOperationException("Effect Zone prop interaction action is missing.");
            BundleInteractionTransitions[i] = (int)Trigger.Transition; BundleInteractionOperations[i] = (int)Trigger.Operation;
            BundleInteractionTargets[i] = Trigger.Target; BundleInteractionEnabled[i] = Trigger.Enabled;
        }
    }

    public void ValidateBundleSerialization()
    {
        if (!BundleCoverDelaySeconds.Equals(Transition.CoverDelaySeconds))
            throw new InvalidOperationException("Effect Zone compiled Cover Delay changed.");
        var Compiled = ReadBundleTriggers(); var Source = PropTriggers;
        if (Compiled.Count != Source.Count) throw new InvalidOperationException("Effect Zone compiled action count changed.");
        for (var i = 0; i < Compiled.Count; i++)
            if (Compiled[i].Transition != Source[i].Transition || Compiled[i].Target != Source[i].Target)
                throw new InvalidOperationException("Effect Zone compiled action changed.");
        var CompiledFog = ReadBundleFogTriggers(); var SourceFog = FogTriggers;
        if (CompiledFog.Count != SourceFog.Count) throw new InvalidOperationException("Effect Zone compiled fog action count changed.");
        for (var i = 0; i < CompiledFog.Count; i++)
            if (CompiledFog[i].Transition != SourceFog[i].Transition || CompiledFog[i].Operation != SourceFog[i].Operation
                || CompiledFog[i].Target != SourceFog[i].Target)
                throw new InvalidOperationException("Effect Zone compiled fog action changed.");
        var CompiledSources = ReadBundleNativeSourceTriggers(); var Sources = NativeSourceTriggers;
        if (CompiledSources.Count != Sources.Count) throw new InvalidOperationException("Effect Zone compiled native source action count changed.");
        for (var i = 0; i < CompiledSources.Count; i++)
            if (CompiledSources[i].Transition != Sources[i].Transition || CompiledSources[i].Operation != Sources[i].Operation
                || CompiledSources[i].Target != Sources[i].Target)
                throw new InvalidOperationException("Effect Zone compiled native source action changed.");
        var CompiledActors = ReadActorSourceTriggers(); var Actors = ActorSourceTriggers;
        if (CompiledActors.Count != Actors.Count) throw new InvalidOperationException("Effect Zone compiled actor source action count changed.");
        for (var i = 0; i < Actors.Count; i++)
            if (CompiledActors[i].Transition != Actors[i].Transition || CompiledActors[i].Operation != Actors[i].Operation
                || CompiledActors[i].Target != Actors[i].Target)
                throw new InvalidOperationException("Effect Zone compiled actor source action changed.");
        var CompiledInteractions = ReadInteractionTriggers(); var Interactions = InteractionTriggers;
        if (CompiledInteractions.Count != Interactions.Count) throw new InvalidOperationException("Effect Zone compiled prop interaction action count changed.");
        for (var i = 0; i < Interactions.Count; i++)
            if (CompiledInteractions[i].Transition != Interactions[i].Transition || CompiledInteractions[i].Operation != Interactions[i].Operation
                || CompiledInteractions[i].Target != Interactions[i].Target || CompiledInteractions[i].Enabled != Interactions[i].Enabled)
                throw new InvalidOperationException("Effect Zone compiled prop interaction action changed.");
    }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
#endif
    public string OccupancyHook => OccupancyHookValue ?? string.Empty;
#if PLL_CREATOR_SDK
    public IReadOnlyList<PllEffectZoneActorSourceTrigger> ActorSourceTriggers => ActorSourceTriggersValue ?? Array.Empty<PllEffectZoneActorSourceTrigger>();
    public IReadOnlyList<PllEffectZoneInteractionTrigger> InteractionTriggers => InteractionTriggersValue ?? Array.Empty<PllEffectZoneInteractionTrigger>();
#else
    public IReadOnlyList<PllEffectZoneActorSourceTrigger> ActorSourceTriggers => ParsedActorSourceTriggers ??= ReadActorSourceTriggers();
    public IReadOnlyList<PllEffectZoneInteractionTrigger> InteractionTriggers => ParsedInteractionTriggers ??= ReadInteractionTriggers();
#endif
    public PllEffectZoneEnvironmentFog EnvironmentFog => EnvironmentFogValue;
#if PLL_CREATOR_SDK
    public PllPresentationTransition Transition => TransitionValue;
#else
    public PllPresentationTransition Transition => new(BundleCoverDelaySeconds);
#endif
    public IReadOnlyList<PllEffectAuthoring> Effects => effects ?? Array.Empty<PllEffectAuthoring>();
    public bool Looped => LoopedValue;
    public long ActionCount => (long)PropTriggers.Count + FogTriggers.Count + NativeSourceTriggers.Count
        + ActorSourceTriggers.Count + InteractionTriggers.Count;
#if PLL_CREATOR_SDK
    public IReadOnlyList<PllEffectZonePropTrigger> PropTriggers => PropTriggersValue ?? Array.Empty<PllEffectZonePropTrigger>();
    public IReadOnlyList<PllEffectZoneFogTrigger> FogTriggers => FogTriggersValue ?? Array.Empty<PllEffectZoneFogTrigger>();
    public IReadOnlyList<PllEffectZoneNativeSourceTrigger> NativeSourceTriggers => NativeSourceTriggersValue ?? Array.Empty<PllEffectZoneNativeSourceTrigger>();
#else
    public IReadOnlyList<PllEffectZonePropTrigger> PropTriggers => ParsedPropTriggers ??= ReadBundleTriggers();
    public IReadOnlyList<PllEffectZoneFogTrigger> FogTriggers => ParsedFogTriggers ??= ReadBundleFogTriggers();
    public IReadOnlyList<PllEffectZoneNativeSourceTrigger> NativeSourceTriggers => ParsedNativeSourceTriggers ??= ReadBundleNativeSourceTriggers();
#endif

    IReadOnlyList<PllEffectZoneNativeSourceTrigger> ReadBundleNativeSourceTriggers()
    {
        if (BundleNativeSourceTransitions is null || BundleNativeSourceOperations is null || BundleNativeSourceTargets is null
            || BundleNativeSourceTransitions.Length != BundleNativeSourceTargets.Length
            || BundleNativeSourceOperations.Length != BundleNativeSourceTargets.Length
            || BundleNativeSourceTargets.Length > PllBundleSerialization.MaximumHookedActions)
            throw new InvalidOperationException("Effect Zone compiled native source actions are malformed.");
        var Triggers = new PllEffectZoneNativeSourceTrigger[BundleNativeSourceTargets.Length];
        for (var i = 0; i < Triggers.Length; i++)
        {
            if ((uint)BundleNativeSourceTransitions[i] > (uint)PllEffectZoneTransition.Exit
                || (uint)BundleNativeSourceOperations[i] > (uint)PllNativeSourceOperation.Deactivate)
                throw new InvalidOperationException("Effect Zone compiled native source action has an invalid transition or operation.");
            Triggers[i] = new PllEffectZoneNativeSourceTrigger((PllEffectZoneTransition)BundleNativeSourceTransitions[i],
                (PllNativeSourceOperation)BundleNativeSourceOperations[i], BundleNativeSourceTargets[i] as PLLNativeResolver
                    ?? throw new InvalidOperationException("Effect Zone compiled native source target is missing."));
        }
        return Array.AsReadOnly(Triggers);
    }

    IReadOnlyList<PllEffectZoneFogTrigger> ReadBundleFogTriggers()
    {
        if (BundleFogTransitions is null || BundleFogOperations is null || BundleFogTargets is null
            || BundleFogTransitions.Length != BundleFogTargets.Length || BundleFogOperations.Length != BundleFogTargets.Length
            || BundleFogTargets.Length > PllBundleSerialization.MaximumHookedActions)
            throw new InvalidOperationException("Effect Zone compiled fog actions are malformed.");
        var Triggers = new PllEffectZoneFogTrigger[BundleFogTargets.Length];
        for (var i = 0; i < Triggers.Length; i++)
        {
            if ((uint)BundleFogTransitions[i] > (uint)PllEffectZoneTransition.Exit
                || (uint)BundleFogOperations[i] > (uint)PllFogZoneOperation.Deactivate)
                throw new InvalidOperationException("Effect Zone compiled fog action has an invalid transition or operation.");
            Triggers[i] = new PllEffectZoneFogTrigger((PllEffectZoneTransition)BundleFogTransitions[i],
                (PllFogZoneOperation)BundleFogOperations[i], BundleFogTargets[i] as PllFogZoneAuthoring
                    ?? throw new InvalidOperationException("Effect Zone compiled fog target is missing."));
        }
        return Array.AsReadOnly(Triggers);
    }

    IReadOnlyList<PllEffectZoneActorSourceTrigger> ReadActorSourceTriggers()
    {
        var Transitions = BundleActorSourceTransitions ?? Array.Empty<int>();
        var Operations = BundleActorSourceOperations ?? Array.Empty<int>();
        var Targets = BundleActorSourceTargets ?? Array.Empty<UnityEngine.Object>();
        if (Transitions.Length != Operations.Length || Transitions.Length != Targets.Length
            || Transitions.Length > PllBundleSerialization.MaximumHookedActions)
            throw new InvalidOperationException("Effect Zone compiled actor source actions are malformed.");
        if (Transitions.Length == 0) return Array.Empty<PllEffectZoneActorSourceTrigger>();
        var Result = new PllEffectZoneActorSourceTrigger[Transitions.Length];
        for (var i = 0; i < Result.Length; i++)
        {
            if (Transitions[i] is < 0 or > 1 || Operations[i] is < 0 or > 1)
                throw new InvalidOperationException("Effect Zone compiled actor source operation is unsupported.");
            var Transition = (PllEffectZoneTransition)Transitions[i]; var Operation = (PllNativeSourceOperation)Operations[i];
            Result[i] = Targets[i] switch
            {
                PllHazardAuthoring Hazard => new PllEffectZoneHazardSourceTrigger(Transition, Operation, Hazard),
                PllEntityAuthoring Entity => new PllEffectZoneEntitySourceTrigger(Transition, Operation, Entity),
                _ => throw new InvalidOperationException("Effect Zone compiled actor source target is missing or unsupported.")
            };
        }
        return Array.AsReadOnly(Result);
    }

    IReadOnlyList<PllEffectZoneInteractionTrigger> ReadInteractionTriggers()
    {
        var Transitions = BundleInteractionTransitions ?? Array.Empty<int>();
        var Operations = BundleInteractionOperations ?? Array.Empty<int>();
        var Targets = BundleInteractionTargets ?? Array.Empty<UnityEngine.Object>();
        var Enabled = BundleInteractionEnabled ?? Array.Empty<bool>();
        if (Transitions.Length != Operations.Length || Transitions.Length != Targets.Length || Transitions.Length != Enabled.Length
            || Transitions.Length > PllBundleSerialization.MaximumHookedActions)
            throw new InvalidOperationException("Effect Zone compiled prop interaction actions are malformed.");
        if (Transitions.Length == 0) return Array.Empty<PllEffectZoneInteractionTrigger>();
        var Result = new PllEffectZoneInteractionTrigger[Transitions.Length];
        for (var i = 0; i < Result.Length; i++)
        {
            if (Transitions[i] is < 0 or > 1 || Enabled[i] && Operations[i] != (int)PllPropOperation.SetBreakableEnabled)
                throw new InvalidOperationException("Effect Zone compiled prop interaction transition or policy is unsupported.");
            var Transition = (PllEffectZoneTransition)Transitions[i];
            Result[i] = ((PllPropOperation)Operations[i], Targets[i]) switch
            {
                (PllPropOperation.Break, PllBreakablePropAuthoring Target) => new PllEffectZoneBreakProp(Transition, Target),
                (PllPropOperation.SetBreakableEnabled, PllBreakablePropAuthoring Target) => new PllEffectZoneSetBreakableEnabled(Transition, Target, Enabled[i]),
                (PllPropOperation.StartMotion, PllSwingingPropAuthoring Target) => new PllEffectZoneStartMotion(Transition, Target),
                (PllPropOperation.PauseMotion, PllSwingingPropAuthoring Target) => new PllEffectZonePauseMotion(Transition, Target),
                (PllPropOperation.ResumeMotion, PllSwingingPropAuthoring Target) => new PllEffectZoneResumeMotion(Transition, Target),
                _ => throw new InvalidOperationException("Effect Zone compiled prop interaction operation or target is unsupported.")
            };
        }
        return Array.AsReadOnly(Result);
    }

    IReadOnlyList<PllEffectZonePropTrigger> ReadBundleTriggers()
    {
        if (BundlePropTransitions is null || BundlePropTargets is null
            || BundlePropTransitions.Length != BundlePropTargets.Length
            || BundlePropTransitions.Length > PllBundleSerialization.MaximumHookedActions)
            throw new InvalidOperationException("Effect Zone compiled generation actions are malformed.");
        var Triggers = new PllEffectZonePropTrigger[BundlePropTransitions.Length];
        for (var i = 0; i < Triggers.Length; i++)
        {
            if ((uint)BundlePropTransitions[i] > byte.MaxValue)
                throw new InvalidOperationException("Effect Zone compiled transition exceeds its enum representation.");
            Triggers[i] = new PllEffectZonePropTrigger((PllEffectZoneTransition)BundlePropTransitions[i],
                BundlePropTargets[i] as PllHookedPropGenerationAuthoring
                    ?? throw new InvalidOperationException("Effect Zone compiled generation target is missing."));
        }
        return Array.AsReadOnly(Triggers);
    }
}

public enum PllEffectZoneTransition : byte { Enter = 0, Exit = 1 }
public enum PllFogZoneOperation : byte { Activate = 0, Deactivate = 1 }
public enum PllNativeSourceOperation : byte { Activate = 0, Deactivate = 1 }

[Serializable]
public abstract class PllEffectZoneInteractionTrigger
{
    private protected PllEffectZoneInteractionTrigger() { }
    private protected PllEffectZoneInteractionTrigger(PllEffectZoneTransition Transition) => TransitionValue = Transition;
    [SerializeField] PllEffectZoneTransition TransitionValue = PllEffectZoneTransition.Enter;
    public PllEffectZoneTransition Transition => TransitionValue;
    public abstract PllPropOperation Operation { get; }
    public abstract MonoBehaviour Target { get; }
    public virtual bool Enabled => false;
}

[Serializable]
public sealed class PllEffectZoneBreakProp : PllEffectZoneInteractionTrigger
{
    public PllEffectZoneBreakProp() { }
    internal PllEffectZoneBreakProp(PllEffectZoneTransition Transition, PllBreakablePropAuthoring Target) : base(Transition) => TargetValue = Target;
    [SerializeField] PllBreakablePropAuthoring TargetValue = null!;
    public override PllPropOperation Operation => PllPropOperation.Break;
    public override MonoBehaviour Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZoneSetBreakableEnabled : PllEffectZoneInteractionTrigger
{
    public PllEffectZoneSetBreakableEnabled() { }
    internal PllEffectZoneSetBreakableEnabled(PllEffectZoneTransition Transition, PllBreakablePropAuthoring Target, bool Enabled)
        : base(Transition) { TargetValue = Target; EnabledValue = Enabled; }
    [SerializeField] PllBreakablePropAuthoring TargetValue = null!;
    [SerializeField] bool EnabledValue = true;
    public override PllPropOperation Operation => PllPropOperation.SetBreakableEnabled;
    public override MonoBehaviour Target => TargetValue;
    public override bool Enabled => EnabledValue;
}

[Serializable]
public sealed class PllEffectZoneStartMotion : PllEffectZoneInteractionTrigger
{
    public PllEffectZoneStartMotion() { }
    internal PllEffectZoneStartMotion(PllEffectZoneTransition Transition, PllSwingingPropAuthoring Target) : base(Transition) => TargetValue = Target;
    [SerializeField] PllSwingingPropAuthoring TargetValue = null!;
    public override PllPropOperation Operation => PllPropOperation.StartMotion;
    public override MonoBehaviour Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZonePauseMotion : PllEffectZoneInteractionTrigger
{
    public PllEffectZonePauseMotion() { }
    internal PllEffectZonePauseMotion(PllEffectZoneTransition Transition, PllSwingingPropAuthoring Target) : base(Transition) => TargetValue = Target;
    [SerializeField] PllSwingingPropAuthoring TargetValue = null!;
    public override PllPropOperation Operation => PllPropOperation.PauseMotion;
    public override MonoBehaviour Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZoneResumeMotion : PllEffectZoneInteractionTrigger
{
    public PllEffectZoneResumeMotion() { }
    internal PllEffectZoneResumeMotion(PllEffectZoneTransition Transition, PllSwingingPropAuthoring Target) : base(Transition) => TargetValue = Target;
    [SerializeField] PllSwingingPropAuthoring TargetValue = null!;
    public override PllPropOperation Operation => PllPropOperation.ResumeMotion;
    public override MonoBehaviour Target => TargetValue;
}

[Serializable]
public abstract class PllEffectZoneActorSourceTrigger
{
    private protected PllEffectZoneActorSourceTrigger() { }
    private protected PllEffectZoneActorSourceTrigger(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation)
    {
        TransitionValue = Transition; OperationValue = Operation;
    }
    [SerializeField] PllEffectZoneTransition TransitionValue = PllEffectZoneTransition.Enter;
    [SerializeField] PllNativeSourceOperation OperationValue = PllNativeSourceOperation.Activate;
    public PllEffectZoneTransition Transition => TransitionValue;
    public PllNativeSourceOperation Operation => OperationValue;
    public abstract MonoBehaviour Target { get; }
}

[Serializable]
public sealed class PllEffectZoneHazardSourceTrigger : PllEffectZoneActorSourceTrigger
{
    public PllEffectZoneHazardSourceTrigger() { }
    internal PllEffectZoneHazardSourceTrigger(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation, PllHazardAuthoring Target)
        : base(Transition, Operation) => TargetValue = Target;
    [SerializeField] PllHazardAuthoring TargetValue = null!;
    public override MonoBehaviour Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZoneEntitySourceTrigger : PllEffectZoneActorSourceTrigger
{
    public PllEffectZoneEntitySourceTrigger() { }
    internal PllEffectZoneEntitySourceTrigger(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation, PllEntityAuthoring Target)
        : base(Transition, Operation) => TargetValue = Target;
    [SerializeField] PllEntityAuthoring TargetValue = null!;
    public override MonoBehaviour Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZoneNativeSourceTrigger
{
    public PllEffectZoneNativeSourceTrigger() { }
    internal PllEffectZoneNativeSourceTrigger(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation, PLLNativeResolver Target)
    {
        TransitionValue = Transition; OperationValue = Operation; TargetValue = Target;
    }
    [SerializeField] PllEffectZoneTransition TransitionValue = PllEffectZoneTransition.Enter;
    [SerializeField] PllNativeSourceOperation OperationValue = PllNativeSourceOperation.Activate;
    [SerializeField] PLLNativeResolver TargetValue = null!;
    public PllEffectZoneTransition Transition => TransitionValue;
    public PllNativeSourceOperation Operation => OperationValue;
    public PLLNativeResolver Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZoneFogTrigger
{
    public PllEffectZoneFogTrigger() { }
    internal PllEffectZoneFogTrigger(PllEffectZoneTransition Transition, PllFogZoneOperation Operation, PllFogZoneAuthoring Target)
    {
        TransitionValue = Transition; OperationValue = Operation; TargetValue = Target;
    }
    [SerializeField] PllEffectZoneTransition TransitionValue = PllEffectZoneTransition.Enter;
    [SerializeField] PllFogZoneOperation OperationValue = PllFogZoneOperation.Activate;
    [SerializeField] PllFogZoneAuthoring TargetValue = null!;
    public PllEffectZoneTransition Transition => TransitionValue;
    public PllFogZoneOperation Operation => OperationValue;
    public PllFogZoneAuthoring Target => TargetValue;
}

[Serializable]
public sealed class PllEffectZonePropTrigger
{
    public PllEffectZonePropTrigger() { }
    internal PllEffectZonePropTrigger(PllEffectZoneTransition Transition, PllHookedPropGenerationAuthoring Target)
    {
        TransitionValue = Transition; TargetValue = Target;
    }
    [SerializeField] PllEffectZoneTransition TransitionValue = PllEffectZoneTransition.Enter;
    [SerializeField] PllHookedPropGenerationAuthoring TargetValue = null!;
    public PllEffectZoneTransition Transition => TransitionValue;
    public PllHookedPropGenerationAuthoring Target => TargetValue;
}
