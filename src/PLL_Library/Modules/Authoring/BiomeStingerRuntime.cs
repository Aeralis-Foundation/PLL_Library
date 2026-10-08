using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;

namespace PLL.Library;

internal sealed class StingerContract
{
    readonly ComponentBinding<PllBiomeStingerAuthoring> component;
    readonly ComponentBinding<AudioSource> audioSource;
    readonly AudioFingerprint fingerprint;
    readonly PllBiomeStingerMode mode;
    readonly string hook;
    readonly float CoverDelaySeconds;

    StingerContract(
        ComponentBinding<PllBiomeStingerAuthoring> component,
        ComponentBinding<AudioSource> audioSource,
        AudioFingerprint fingerprint,
        PllBiomeStingerMode mode,
        string hook)
    {
        this.component = component;
        this.audioSource = audioSource;
        this.fingerprint = fingerprint;
        this.mode = mode;
        this.hook = hook;
        CoverDelaySeconds = component.Source.Transition.CoverDelaySeconds;
    }

    internal PllBiomeStingerAuthoring Source => component.Source;
    internal Transform Root => component.Source.transform;
    internal PllBiomeStingerMode Mode => mode;
    internal string Hook => hook;
    internal float TransitionSeconds => CoverDelaySeconds;

    internal static StingerContract Create(Transform root, BiomeId id, PllBiomeStingerAuthoring source)
    {
        if (!source) throw new ArgumentException($"Biome '{id}' contains a missing stinger definition.", nameof(source));
        var audio = RequireSource(root, source, id);
        ValidateMode(source.Mode, source.EmitHook, id, source.name);
        if (PllAuthoringValueContract.TransitionFailure(source.Transition.CoverDelaySeconds) is not null)
            throw new ArgumentException($"Biome '{id}' stinger '{source.name}' has an invalid Cover Delay.", nameof(source));
        if (source.Transition.CoverDelaySeconds > 0f && !audio.loop)
            throw new ArgumentException($"Biome '{id}' stinger '{source.name}' needs looping audio for Cover Delay.", nameof(source));
        return new StingerContract(
            ComponentBinding<PllBiomeStingerAuthoring>.Create(root, source),
            ComponentBinding<AudioSource>.Create(root, audio),
            AudioFingerprint.CreateStinger(audio, id, source.name),
            source.Mode,
            source.EmitHook);
    }

    internal static void ValidateOwnership(
        PllBiomeDefinition Definition,
        Transform Root,
        IReadOnlyList<StingerContract> Stingers,
        BiomeId Id,
        bool Argument)
    {
        if (Stingers.Count == 0) return;
        var Segment = Root.GetComponent<CustomSegmentRuntime>();
        var Ports = Root.GetComponentsInChildren<SegmentPort>(true);
        var Effects = Root.GetComponentsInChildren<PllEffectAuthoring>(true);
        var EffectZones = Root.GetComponentsInChildren<PllEffectZoneAuthoring>(true);
        var Visuals = Root.GetComponentsInChildren<PllVisualAuthoring>(true);
        var CustomVisuals = Root.GetComponentsInChildren<PllCustomVisualAuthoring>(true);
        var SoundEmitters = Root.GetComponentsInChildren<PllSoundEmitterAuthoring>(true);
        var NativeResolvers = Root.GetComponentsInChildren<PLLNativeResolver>(true);
        var Luggage = Root.GetComponentsInChildren<PllLuggageAuthoring>(true);
        var Statues = Root.GetComponentsInChildren<PllStatueAuthoring>(true);
        var Campfires = Root.GetComponentsInChildren<PllCampfireAuthoring>(true);
        var ItemSpawners = Root.GetComponentsInChildren<PllItemSpawnerAuthoring>(true);
        var PropZones = Root.GetComponentsInChildren<PllPropZoneAuthoring>(true);
        var ChildProps = Root.GetComponentsInChildren<PllChildPropAuthoring>(true);
        var NoPropSpawns = Root.GetComponentsInChildren<PLLNoPropSpawn>(true);
        var FinalGeneration = Root.GetComponentsInChildren<PLLFinalGeneration>(true);
        var ForceSpawners = Root.GetComponentsInChildren<PLLForceSpawner>(true);
        var HookedProps = Root.GetComponentsInChildren<PLLHookedProp>(true);
        var GenerationSteps = Root.GetComponentsInChildren<LevelGenStep>(true);
        foreach (var Stinger in Stingers)
        {
            var StingerRoot = Stinger.Root;
            if (TransformOwnership.HasPropSpawnerInPath(Root, StingerRoot))
                Fail(Stinger, "is inside a PropSpawner hierarchy");
            RejectObject(Stinger, Definition.Anchors.FloorRoot, "FloorRoot");
            RejectObject(Stinger, Definition.Anchors.CampfireRoot, "CampfireRoot");
            RejectObject(Stinger, Definition.Anchors.StatueRoot, "StatueRoot");
            Reject(Stinger, Definition.Anchors.StartAnchor, "StartAnchor");
            RejectObject(Stinger, Definition.Anchors.PlayerFormationRoot, "PlayerFormationRoot");
            Reject(Stinger, Definition.Anchors.PlaneAnchor, "PlaneAnchor");
            Reject(Stinger, Definition.Anchors.FogBeginAnchor, "FogBeginAnchor");
            Reject(Stinger, Definition.Anchors.FogCutoffAnchor, "FogCutoffAnchor");
            if (Segment)
            {
                Reject(Stinger, Segment.FogOrigin.transform, "FogOrigin");
                Reject(Stinger, Segment.FogCutout.transform, "FogCutout");
                Reject(Stinger, Segment.ProgressPoint.transform, "ProgressPoint");
            }
            foreach (var Value in Ports) Reject(Stinger, Value.transform, "segment port");
            foreach (var Value in Effects)
            {
                RejectObject(Stinger, Value.Root, "effect root");
                RejectObject(Stinger, Value.PresentationRoot, "effect presentation root");
            }
            foreach (var Value in EffectZones) RejectObject(Stinger, Value.Root, "effect-zone root");
            foreach (var Value in Visuals) RejectObject(Stinger, Value.Root, "visual root");
            foreach (var Value in CustomVisuals) Reject(Stinger, Value.transform, "custom-visual root");
            foreach (var Value in SoundEmitters) RejectObject(Stinger, Value.Root, "sound-emitter root");
            foreach (var Value in NativeResolvers) RejectObject(Stinger, Value.Root, "native-resolver root");
            foreach (var Value in Luggage) Reject(Stinger, Value.transform, "luggage root");
            foreach (var Value in Statues) Reject(Stinger, Value.transform, "statue root");
            foreach (var Value in Campfires) RejectObject(Stinger, Value.Root, "campfire root");
            foreach (var Value in ItemSpawners) RejectObject(Stinger, Value.Root, "item-spawner root");
            foreach (var Value in PropZones) RejectObject(Stinger, Value.Root, "prop-zone root");
            foreach (var Value in ChildProps) RejectObject(Stinger, Value.Root, "child-prop root");
            foreach (var Value in NoPropSpawns) Reject(Stinger, Value.transform, "no-prop root");
            foreach (var Value in FinalGeneration) Reject(Stinger, Value.transform, "final-generation root");
            foreach (var Value in ForceSpawners) Reject(Stinger, Value.transform, "force-spawner root");
            foreach (var Value in HookedProps) Reject(Stinger, Value.transform, "hooked-prop root");
            foreach (var Value in GenerationSteps)
                if (Value.transform != Root) Reject(Stinger, Value.transform, "generation step");
            foreach (var Variation in Definition.VariationDefinitions)
                if (Variation)
                    foreach (var Entry in Variation.Entries)
                        if (Entry is not null) RejectObject(Stinger, Entry.Root, "variation entry root");
        }

        void RejectObject(StingerContract Stinger, GameObject? Other, string Role)
        {
            if (Other && TransformOwnership.Overlaps(Stinger.Root, Other.transform)) Fail(Stinger, $"overlaps the {Role}");
        }

        void Reject(StingerContract Stinger, Transform? Other, string Role)
        {
            if (Other && TransformOwnership.Overlaps(Stinger.Root, Other)) Fail(Stinger, $"overlaps the {Role}");
        }

        void Fail(StingerContract Stinger, string Reason)
        {
            var Message = $"Biome '{Id}' stinger '{Stinger.Source.name}' {Reason}.";
            if (Argument) throw new ArgumentException(Message, "source");
            throw new InvalidOperationException(Message);
        }

    }

    internal ResolvedStinger Resolve(Transform root, BiomeId id)
    {
        var value = component.Resolve(root, id, "stinger authoring");
        var audio = audioSource.Resolve(root, id, "stinger audio source");
        RequireSource(root, value, audio, id);
        if (value.Mode != mode || !string.Equals(value.EmitHook, hook, StringComparison.Ordinal)
            || !value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' runtime stinger '{value.name}' changed its sealed mode or hook.");
        fingerprint.ValidateStinger(audio, id, value.name, "runtime");
        return new ResolvedStinger(this, audio);
    }

    internal void Validate(Transform root, BiomeId id)
    {
        component.Validate(root, id, "stinger authoring");
        audioSource.Validate(root, id, "stinger audio source");
        var value = Source;
        RequireSource(root, value, audioSource.Source, id);
        if (value.Mode != mode || !string.Equals(value.EmitHook, hook, StringComparison.Ordinal)
            || !value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' stinger '{value.name}' changed its mode or hook before sealing.");
        fingerprint.ValidateStinger(audioSource.Source, id, value.name, "source");
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        component.WriteManifest(writer); audioSource.WriteManifest(writer);
        writer.Write((byte)mode); writer.Write(hook);
        fingerprint.WriteManifest(writer);
        ManifestEncoding.WriteScalar(writer, CoverDelaySeconds);
    }

    static AudioSource RequireSource(Transform root, PllBiomeStingerAuthoring source, BiomeId id)
    {
        var audio = source.GetComponent<AudioSource>();
        if (!ValidStructure(root, source, audio))
            throw new ArgumentException(
                $"Biome '{id}' stinger '{source.name}' must be one enabled active strict-descendant leaf with exactly one sibling AudioSource and no other components.",
                nameof(source));
        return audio;
    }

    static void RequireSource(
        Transform root,
        PllBiomeStingerAuthoring source,
        AudioSource audio,
        BiomeId id)
    {
        if (!ValidStructure(root, source, audio))
            throw new InvalidOperationException($"Biome '{id}' stinger '{source.name}' changed its sealed leaf structure.");
    }

    static bool ValidStructure(Transform root, PllBiomeStingerAuthoring source, AudioSource? audio)
    {
        if (!root || !source || !audio || source.transform == root || !source.transform.IsChildOf(root)
            || !source.enabled || source.transform.childCount != 0 || audio.gameObject != source.gameObject)
            return false;
        for (var current = source.transform; current && current != root; current = current.parent)
            if (!current.gameObject.activeSelf) return false;
        var components = source.GetComponents<Component>();
        if (components.Length != 3) return false;
        var transformCount = 0;
        var sourceCount = 0;
        var audioCount = 0;
        foreach (var value in components)
        {
            if (!value) return false;
            var type = value.GetType();
            if (type == typeof(Transform)) transformCount++;
            else if (type == typeof(PllBiomeStingerAuthoring)) sourceCount++;
            else if (type == typeof(AudioSource)) audioCount++;
            else return false;
        }
        return transformCount == 1 && sourceCount == 1 && audioCount == 1;
    }

    static void ValidateMode(PllBiomeStingerMode mode, string hook, BiomeId id, string name)
    {
        switch (mode)
        {
            case PllBiomeStingerMode.OnEnter:
                if (hook.Length == 0) return;
                Fail($"Biome '{id}' OnEnter stinger '{name}' cannot declare an emit hook.");
                return;
            case PllBiomeStingerMode.EmitHook:
            case PllBiomeStingerMode.OnEnterAndEmitHook:
                try { HookedPropRuntime.ValidateHook(hook, "source"); }
                catch (ArgumentException exception)
                {
                    Fail($"Biome '{id}' stinger '{name}' has an invalid emit hook. {exception.Message}");
                }
                return;
            default:
                Fail($"Biome '{id}' stinger '{name}' has an unsupported mode.");
                return;
        }

        void Fail(string message)
        {
            throw new ArgumentException(message, "source");
        }
    }
}

internal readonly struct ResolvedStinger
{
    internal ResolvedStinger(StingerContract contract, AudioSource audioSource)
    {
        Contract = contract;
        AudioSource = audioSource;
    }

    internal StingerContract Contract { get; }
    internal AudioSource AudioSource { get; }
}

internal sealed class StingerState
{
    readonly AudioSource source;
    readonly float Gain;
    PllPresentationCoverage Transition;

    internal StingerState(ResolvedStinger resolved)
    {
        Contract = resolved.Contract;
        source = resolved.AudioSource;
        Gain = source.volume;
    }

    internal StingerContract Contract { get; }
    internal bool StartsOnEnter => Contract.Mode is PllBiomeStingerMode.OnEnter
        or PllBiomeStingerMode.OnEnterAndEmitHook;
    internal bool AcceptsHook(string hook) =>
        (Contract.Mode is PllBiomeStingerMode.EmitHook or PllBiomeStingerMode.OnEnterAndEmitHook)
        && string.Equals(Contract.Hook, hook, StringComparison.Ordinal);

    internal void Bind(AudioMixerGroup group)
    {
        if (!source) throw new InvalidOperationException("A biome stinger AudioSource was destroyed before route binding.");
        var current = source.outputAudioMixerGroup;
        if (!ReferenceEquals(current, null) && current != group)
            throw new InvalidOperationException("A biome stinger AudioSource changed its run-owned output route.");
        source.outputAudioMixerGroup = group;
    }

    internal bool Restart(AudioMixerGroup Group, double PresentationClock)
    {
        if (!source || source.outputAudioMixerGroup != Group) return false;
        if (source.loop)
        {
            if (!Transition.SetTarget(true, PresentationClock, Contract.TransitionSeconds)) return false;
            source.volume = Gain * Transition.Coverage;
            if (!source.isPlaying) source.Play();
            return true;
        }
        source.Stop();
        source.timeSamples = 0;
        source.Play();
        return true;
    }

    internal bool Stop(AudioMixerGroup Group, double PresentationClock)
    {
        if (!source || source.outputAudioMixerGroup != Group) return false;
        if (source.loop)
        {
            if (!Transition.SetTarget(false, PresentationClock, Contract.TransitionSeconds)) return false;
            ApplyCoverage();
            return true;
        }
        if (!source.isPlaying) return false;
        source.Stop();
        source.timeSamples = 0;
        return true;
    }

    internal void Restore()
    {
        Transition.Complete(false);
        if (!source) return;
        try
        {
            source.Stop();
            source.timeSamples = 0;
        }
        finally
        {
            source.outputAudioMixerGroup = null;
            source.volume = Gain;
        }
    }

    internal void Tick(double PresentationClock)
    {
        if (!Transition.Active) return;
        if (!source)
        {
            Transition.Complete(false);
            return;
        }
        if (Transition.Tick(PresentationClock)) ApplyCoverage();
    }

    void ApplyCoverage()
    {
        source.volume = Gain * Transition.Coverage;
        if (Transition.Target || Transition.Active) return;
        source.Stop();
        source.timeSamples = 0;
    }
}
