using System;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;

namespace PLL.Library;

internal sealed class SoundEmitterContract
{
    readonly AuthoringBindingOwner owner;
    readonly ComponentBinding<PllSoundEmitterAuthoring> component;
    readonly ComponentBinding<AudioSource> audioSource;
    readonly AudioFingerprint fingerprint;
    readonly float CoverDelaySeconds;

    SoundEmitterContract(
        AuthoringBindingOwner owner,
        ComponentBinding<PllSoundEmitterAuthoring> component,
        ObjectBinding root,
        ComponentBinding<AudioSource> audioSource,
        AudioFingerprint fingerprint)
    {
        this.owner = owner;
        this.component = component;
        Root = root;
        this.audioSource = audioSource;
        this.fingerprint = fingerprint;
        CoverDelaySeconds = component.Source.Transition.CoverDelaySeconds;
    }

    internal PllSoundEmitterAuthoring Source => component.Source;
    internal AuthoringBindingOwner Owner => owner;
    internal bool Generated => owner.Generated;
    internal ObjectBinding Root { get; }

    internal static SoundEmitterContract Create(
        Transform template,
        AuthoringCandidateGraph graph,
        BiomeId id,
        PllSoundEmitterAuthoring source)
    {
        var owner = AuthoringBindingOwner.Create(template, graph, source.transform, id, "sound emitter");
        var bindingRoot = owner.BindingRoot;
        var root = source.Root;
        var audio = source.AudioSource;
        if (!root || !TransformOwnership.Contains(bindingRoot, source.transform)
            || !TransformOwnership.Contains(bindingRoot, root ? root.transform : null)
            || root && root.transform == bindingRoot
            || root && !TransformOwnership.DiscoverableWhenActivated(bindingRoot, root.transform))
            throw new ArgumentException($"Biome '{id}' sound emitter '{source.name}' must own a discoverable non-template child root.", nameof(source));
        if (!audio || audio.gameObject != root)
            throw new ArgumentException($"Biome '{id}' sound emitter '{source.name}' must own an AudioSource on its target root.", nameof(source));
        if (PllAuthoringValueContract.TransitionFailure(source.Transition.CoverDelaySeconds) is not null)
            throw new ArgumentException($"Biome '{id}' sound emitter '{source.name}' has an invalid Cover Delay.", nameof(source));
        if (source.Transition.CoverDelaySeconds > 0f && !audio.loop)
            throw new ArgumentException($"Biome '{id}' sound emitter '{source.name}' needs looping audio for Cover Delay.", nameof(source));
        var fingerprint = AudioFingerprint.Create(audio, id, source.name);
        return new SoundEmitterContract(
            owner,
            ComponentBinding<PllSoundEmitterAuthoring>.Create(bindingRoot, source),
            ObjectBinding.Create(bindingRoot, root), ComponentBinding<AudioSource>.Create(bindingRoot, audio), fingerprint);
    }

    internal ResolvedSoundEmitter ResolveStable(Transform root, BiomeId id)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{id}' cannot resolve a generated sound emitter as stable authoring.");
        return Resolve(root, id);
    }

    internal bool TryResolveGenerated(
        PllSoundEmitterAuthoring Source,
        BiomeId Id,
        out ResolvedSoundEmitter Resolved)
    {
        Resolved = default;
        if (!TryMatchGenerated(Source, Id, out var Root)) return false;
        Resolved = Resolve(Root, Id);
        return true;
    }

    internal bool TryMatchGenerated(PllSoundEmitterAuthoring Source, BiomeId Id, out Transform Root) =>
        component.TryMatchGenerated(
            owner, Source, Id, "generated sound emitter authoring", out Root);

    ResolvedSoundEmitter Resolve(Transform root, BiomeId id)
    {
        var value = component.Resolve(root, id, "sound emitter authoring");
        var target = Root.Resolve(root, id, "sound emitter root");
        var audio = audioSource.Resolve(root, id, "sound emitter audio source");
        if (!TransformOwnership.DiscoverableWhenActivated(root, target.transform)
            || value.Root != target || value.AudioSource != audio || audio.gameObject != target
            || !value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' runtime sound emitter metadata does not match its registered contract.");
        fingerprint.Validate(audio, id, value.name, "runtime");
        return new ResolvedSoundEmitter(value, target, audio);
    }

    internal void Validate(Transform template, BiomeId id, AuthoringCandidateGraph graph)
    {
        owner.Validate(template, graph, component.Source.transform, id, "sound emitter");
        var root = owner.BindingRoot;
        component.Validate(root, id, "sound emitter authoring"); Root.Validate(root, id, "sound emitter root");
        audioSource.Validate(root, id, "sound emitter audio source");
        var value = Source;
        if (!TransformOwnership.DiscoverableWhenActivated(root, Root.Source.transform)
            || value.Root != Root.Source || value.AudioSource != audioSource.Source || audioSource.Source.gameObject != Root.Source
            || !value.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' changed sound emitter '{value.name}' before sealing.");
        fingerprint.Validate(audioSource.Source, id, value.name, "source");
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        owner.WriteManifest(writer);
        component.WriteManifest(writer); Root.WriteManifest(writer); audioSource.WriteManifest(writer);
        fingerprint.WriteManifest(writer);
        ManifestEncoding.WriteScalar(writer, CoverDelaySeconds);
    }
}

internal readonly struct ResolvedSoundEmitter
{
    internal ResolvedSoundEmitter(PllSoundEmitterAuthoring authoring, GameObject root, AudioSource audioSource)
    {
        Authoring = authoring; Root = root; AudioSource = audioSource;
    }

    internal PllSoundEmitterAuthoring Authoring { get; }
    internal GameObject Root { get; }
    internal AudioSource AudioSource { get; }
}

internal sealed class SoundEmitterState
{
    readonly AudioSource Source;
    readonly Transform Target;
    readonly float Gain;
    readonly float CoverDelaySeconds;
    PllPresentationCoverage Transition;
    bool Started;

    internal SoundEmitterState(ResolvedSoundEmitter Resolved)
    {
        Source = Resolved.AudioSource;
        Target = Resolved.Root.transform;
        Gain = Source.volume;
        CoverDelaySeconds = Resolved.Authoring.Transition.CoverDelaySeconds;
        Source.enabled = false;
    }

    internal void Update(bool IsCurrent, double PresentationClock)
    {
        if (!Source || !Target)
        {
            Restore();
            return;
        }
        var LocalCharacter = Character.localCharacter;
        if (!SetActive(IsCurrent && LocalCharacter
            && Vector3.Distance(LocalCharacter.Center, Target.position) <= Source.maxDistance,
            PresentationClock) && Transition.Tick(PresentationClock)) ApplyCoverage();
    }

    internal void Deactivate(double PresentationClock)
    {
        if (!Source || !Target) Restore();
        else SetActive(false, PresentationClock);
    }

    internal void Restore()
    {
        Transition.Complete(false);
        Started = false;
        if (!Source) return;
        try { Source.Stop(); }
        finally
        {
            Source.volume = Gain;
            Source.enabled = true;
        }
    }

    bool SetActive(bool Value, double PresentationClock)
    {
        if (!Transition.SetTarget(Value, PresentationClock, CoverDelaySeconds)) return false;
        if (Value)
        {
            Source.volume = Gain * Transition.Coverage;
            Source.enabled = true;
            if (Started) Source.UnPause();
            else
            {
                Source.Play();
                Started = true;
            }
        }
        ApplyCoverage();
        return true;
    }

    void ApplyCoverage()
    {
        Source.volume = Gain * Transition.Coverage;
        if (Transition.Target || Transition.Active) return;
        try { Source.Pause(); }
        finally { Source.enabled = false; }
    }
}

internal readonly struct AudioFingerprint
{
    readonly AudioClip clip;
    readonly AudioMixerRouteFingerprint mixerRoute;
    readonly bool enabled;
    readonly bool playing;
    readonly int timeSamples;
    readonly bool loop;
    readonly float volume;
    readonly float pitch;
    readonly int priority;
    readonly float panStereo;
    readonly float spatialBlend;
    readonly float reverbZoneMix;
    readonly float dopplerLevel;
    readonly float spread;
    readonly AudioRolloffMode rolloffMode;
    readonly float minDistance;
    readonly float maxDistance;
    readonly bool mute;
    readonly bool bypassEffects;
    readonly bool bypassListenerEffects;
    readonly bool bypassReverbZones;
    readonly bool ignoreListenerPause;
    readonly bool ignoreListenerVolume;
    readonly bool playOnAwake;
    readonly bool spatialize;
    readonly bool spatializePostEffects;
    readonly AudioVelocityUpdateMode velocityUpdateMode;
    readonly AudioCurveFingerprint customRolloff;
    readonly AudioCurveFingerprint spatialBlendCurve;
    readonly AudioCurveFingerprint reverbZoneMixCurve;
    readonly AudioCurveFingerprint spreadCurve;

    AudioFingerprint(AudioSource source, BiomeId id, string name, string kind)
    {
        clip = source.clip; mixerRoute = AudioMixerRouteFingerprint.Create(source.outputAudioMixerGroup, id, name, kind);
        enabled = source.enabled; playing = source.isPlaying; timeSamples = source.timeSamples; loop = source.loop;
        volume = source.volume; pitch = source.pitch; priority = source.priority;
        panStereo = source.panStereo; spatialBlend = source.spatialBlend; reverbZoneMix = source.reverbZoneMix;
        dopplerLevel = source.dopplerLevel; spread = source.spread; rolloffMode = source.rolloffMode;
        minDistance = source.minDistance; maxDistance = source.maxDistance; mute = source.mute;
        bypassEffects = source.bypassEffects; bypassListenerEffects = source.bypassListenerEffects;
        bypassReverbZones = source.bypassReverbZones; ignoreListenerPause = source.ignoreListenerPause;
        ignoreListenerVolume = source.ignoreListenerVolume; playOnAwake = source.playOnAwake;
        spatialize = source.spatialize; spatializePostEffects = source.spatializePostEffects;
        velocityUpdateMode = source.velocityUpdateMode;
        customRolloff = AudioCurveFingerprint.Create(source.GetCustomCurve(AudioSourceCurveType.CustomRolloff), id, name, kind, "custom rolloff");
        spatialBlendCurve = AudioCurveFingerprint.Create(source.GetCustomCurve(AudioSourceCurveType.SpatialBlend), id, name, kind, "spatial blend");
        reverbZoneMixCurve = AudioCurveFingerprint.Create(source.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix), id, name, kind, "reverb zone mix");
        spreadCurve = AudioCurveFingerprint.Create(source.GetCustomCurve(AudioSourceCurveType.Spread), id, name, kind, "spread");
    }

    internal static AudioFingerprint Create(AudioSource source, BiomeId id, string name)
    {
        const string kind = "sound emitter";
        ValidateShared(source, id, name, kind);
        if (source.minDistance < 0f
            || source.maxDistance <= 0f || source.maxDistance < source.minDistance)
            throw new ArgumentException($"Biome '{id}' sound emitter '{name}' needs a finite positive AudioSource maxDistance.", nameof(source));
        if (!source.spatialBlend.Equals(1f))
            throw new ArgumentException($"Biome '{id}' sound emitter '{name}' needs a finite fully-3D AudioSource spatialBlend.", nameof(source));
        return new AudioFingerprint(source, id, name, kind);
    }

    internal static AudioFingerprint CreateStinger(AudioSource source, BiomeId id, string name)
    {
        const string kind = "stinger";
        ValidateShared(source, id, name, kind);
        if (source.clip.samples <= 0 || source.clip.channels <= 0 || source.clip.frequency <= 0)
            throw new ArgumentException($"Biome '{id}' stinger '{name}' needs a nonempty AudioClip.", nameof(source));
        if (!source.spatialBlend.Equals(0f) || source.spatialize || source.spatializePostEffects)
            throw new ArgumentException($"Biome '{id}' stinger '{name}' must use a fully-2D non-spatialized AudioSource.", nameof(source));
        if (source.pitch <= 0f)
            throw new ArgumentException($"Biome '{id}' stinger '{name}' needs a positive finite AudioSource pitch.", nameof(source));
        if (!ReferenceEquals(source.outputAudioMixerGroup, null))
            throw new ArgumentException($"Biome '{id}' stinger '{name}' cannot author an output AudioMixerGroup.", nameof(source));
        var result = new AudioFingerprint(source, id, name, kind);
        if (!result.spatialBlendCurve.IsConstantZero())
            throw new ArgumentException($"Biome '{id}' stinger '{name}' needs a constant-zero AudioSource spatial blend curve.", nameof(source));
        return result;
    }

    internal void Validate(AudioSource source, BiomeId id, string name, string stage)
    {
        if (!Matches(Create(source, id, name)))
            throw new InvalidOperationException($"Biome '{id}' {stage} sound emitter '{name}' AudioSource changed its sealed fingerprint.");
    }

    internal void ValidateStinger(AudioSource source, BiomeId id, string name, string stage)
    {
        if (!Matches(CreateStinger(source, id, name)))
            throw new InvalidOperationException($"Biome '{id}' {stage} stinger '{name}' AudioSource changed its sealed fingerprint.");
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        BiomeAuthoringContract.WriteClip(writer, clip); mixerRoute.WriteManifest(writer);
        writer.Write(enabled); writer.Write(playing); writer.Write(timeSamples); writer.Write(loop);
        ManifestEncoding.WriteScalar(writer, volume); ManifestEncoding.WriteScalar(writer, pitch); writer.Write(priority);
        ManifestEncoding.WriteScalar(writer, panStereo); ManifestEncoding.WriteScalar(writer, spatialBlend);
        ManifestEncoding.WriteScalar(writer, reverbZoneMix); ManifestEncoding.WriteScalar(writer, dopplerLevel);
        ManifestEncoding.WriteScalar(writer, spread); writer.Write((int)rolloffMode);
        ManifestEncoding.WriteScalar(writer, minDistance); ManifestEncoding.WriteScalar(writer, maxDistance);
        writer.Write(mute); writer.Write(bypassEffects); writer.Write(bypassListenerEffects); writer.Write(bypassReverbZones);
        writer.Write(ignoreListenerPause); writer.Write(ignoreListenerVolume); writer.Write(playOnAwake);
        writer.Write(spatialize); writer.Write(spatializePostEffects); writer.Write((int)velocityUpdateMode);
        customRolloff.WriteManifest(writer); spatialBlendCurve.WriteManifest(writer);
        reverbZoneMixCurve.WriteManifest(writer); spreadCurve.WriteManifest(writer);
    }

    bool Matches(AudioFingerprint current) => current.clip == clip && current.mixerRoute.Matches(mixerRoute)
        && current.enabled == enabled && current.playing == playing && current.timeSamples == timeSamples && current.loop == loop
        && current.volume.Equals(volume) && current.pitch.Equals(pitch)
        && current.priority == priority && current.panStereo.Equals(panStereo) && current.spatialBlend.Equals(spatialBlend)
        && current.reverbZoneMix.Equals(reverbZoneMix) && current.dopplerLevel.Equals(dopplerLevel)
        && current.spread.Equals(spread) && current.rolloffMode == rolloffMode && current.minDistance.Equals(minDistance)
        && current.maxDistance.Equals(maxDistance) && current.mute == mute && current.bypassEffects == bypassEffects
        && current.bypassListenerEffects == bypassListenerEffects && current.bypassReverbZones == bypassReverbZones
        && current.ignoreListenerPause == ignoreListenerPause && current.ignoreListenerVolume == ignoreListenerVolume
        && current.playOnAwake == playOnAwake && current.spatialize == spatialize
        && current.spatializePostEffects == spatializePostEffects && current.velocityUpdateMode == velocityUpdateMode
        && current.customRolloff.Matches(customRolloff) && current.spatialBlendCurve.Matches(spatialBlendCurve)
        && current.reverbZoneMixCurve.Matches(reverbZoneMixCurve) && current.spreadCurve.Matches(spreadCurve);

    static void ValidateShared(AudioSource source, BiomeId id, string name, string kind)
    {
        if (!source.clip || source.resource != source.clip)
            throw new ArgumentException($"Biome '{id}' {kind} '{name}' must use a direct AudioClip resource.", nameof(source));
        if (!source.enabled || source.playOnAwake || source.isPlaying || source.timeSamples != 0)
            throw new ArgumentException($"Biome '{id}' {kind} '{name}' must be enabled, stopped at its beginning, and have playOnAwake disabled before runtime binding.", nameof(source));
        if (!ManifestEncoding.CanEncode(source.volume) || !ManifestEncoding.CanEncode(source.pitch) || !ManifestEncoding.CanEncode(source.panStereo)
            || !ManifestEncoding.CanEncode(source.spatialBlend) || !ManifestEncoding.CanEncode(source.reverbZoneMix)
            || !ManifestEncoding.CanEncode(source.dopplerLevel) || !ManifestEncoding.CanEncode(source.spread)
            || !ManifestEncoding.CanEncode(source.minDistance) || !ManifestEncoding.CanEncode(source.maxDistance))
            throw new ArgumentException($"Biome '{id}' {kind} '{name}' exceeds the synchronized numeric range.", nameof(source));
    }

}

internal readonly struct AudioMixerRouteFingerprint
{
    readonly AudioMixerGroup? group;
    readonly bool routed;
    readonly string mixerName;
    readonly string groupName;
    readonly int groupIndex;

    AudioMixerRouteFingerprint(AudioMixerGroup? group, bool routed, string mixerName, string groupName, int groupIndex)
    {
        this.group = group; this.routed = routed; this.mixerName = mixerName;
        this.groupName = groupName; this.groupIndex = groupIndex;
    }

    internal static AudioMixerRouteFingerprint Create(AudioMixerGroup? group, BiomeId id, string name, string kind)
    {
        if (group is null) return new AudioMixerRouteFingerprint(null, false, "", "", -1);
        if (!group)
            throw new ArgumentException($"Biome '{id}' {kind} '{name}' references a destroyed output AudioMixerGroup.", "source");
        var mixer = group.audioMixer;
        if (!mixer)
            throw new ArgumentException($"Biome '{id}' {kind} '{name}' has an invalid output AudioMixerGroup.", "source");
        var groups = mixer.FindMatchingGroups(string.Empty);
        var index = Array.IndexOf(groups, group);
        if (index < 0)
            throw new ArgumentException($"Biome '{id}' {kind} '{name}' output AudioMixerGroup is not discoverable in its AudioMixer.", "source");
        return new AudioMixerRouteFingerprint(group, true, mixer.name ?? "", group.name ?? "", index);
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(routed);
        if (!routed) return;
        writer.Write(mixerName); writer.Write(groupName); writer.Write(groupIndex);
    }

    internal bool Matches(AudioMixerRouteFingerprint other) =>
        routed == other.routed && (!routed || group == other.group
            && mixerName == other.mixerName && groupName == other.groupName && groupIndex == other.groupIndex);
}

internal sealed class AudioCurveFingerprint
{
    readonly bool present;
    readonly WrapMode preWrapMode;
    readonly WrapMode postWrapMode;
    readonly Keyframe[] keys;

    AudioCurveFingerprint(bool present, WrapMode preWrapMode, WrapMode postWrapMode, Keyframe[] keys)
    {
        this.present = present; this.preWrapMode = preWrapMode; this.postWrapMode = postWrapMode; this.keys = keys;
    }

    internal static AudioCurveFingerprint Create(
        AnimationCurve? curve,
        BiomeId id,
        string name,
        string kind,
        string curveName)
    {
        if (curve is null) return new AudioCurveFingerprint(false, default, default, Array.Empty<Keyframe>());
        var keys = curve.keys;
        foreach (var key in keys)
            if (!ManifestEncoding.CanEncode(key.time) || !ManifestEncoding.CanEncode(key.value) || !Tangent(key.inTangent) || !Tangent(key.outTangent)
                || !ManifestEncoding.CanEncode(key.inWeight) || !ManifestEncoding.CanEncode(key.outWeight))
                throw new ArgumentException($"Biome '{id}' {kind} '{name}' {curveName} curve exceeds the synchronized numeric range.", "source");
        return new AudioCurveFingerprint(true, curve.preWrapMode, curve.postWrapMode, keys);
    }

    internal bool IsConstantZero() => AudioCurveContract.IsConstantZero(keys);

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(present);
        if (!present) return;
        writer.Write((int)preWrapMode); writer.Write((int)postWrapMode); writer.Write(keys.Length);
        foreach (var key in keys)
        {
            ManifestEncoding.WriteScalar(writer, key.time); ManifestEncoding.WriteScalar(writer, key.value);
            WriteTangent(writer, key.inTangent); WriteTangent(writer, key.outTangent);
            ManifestEncoding.WriteScalar(writer, key.inWeight); ManifestEncoding.WriteScalar(writer, key.outWeight);
            writer.Write((int)key.weightedMode);
        }
    }

    internal bool Matches(AudioCurveFingerprint? other)
    {
        if (other is null || present != other.present) return false;
        if (!present) return true;
        if (preWrapMode != other.preWrapMode || postWrapMode != other.postWrapMode || keys.Length != other.keys.Length) return false;
        for (var i = 0; i < keys.Length; i++)
        {
            var left = keys[i]; var right = other.keys[i];
            if (!left.time.Equals(right.time) || !left.value.Equals(right.value)
                || !left.inTangent.Equals(right.inTangent) || !left.outTangent.Equals(right.outTangent)
                || !left.inWeight.Equals(right.inWeight) || !left.outWeight.Equals(right.outWeight)
                || left.weightedMode != right.weightedMode) return false;
        }
        return true;
    }

    static bool Tangent(float Value) => !float.IsNaN(Value) && (float.IsInfinity(Value) || ManifestEncoding.CanEncode(Value));

    static void WriteTangent(BinaryWriter writer, float value)
    {
        if (float.IsNegativeInfinity(value)) writer.Write((byte)0);
        else if (float.IsPositiveInfinity(value)) writer.Write((byte)2);
        else
        {
            writer.Write((byte)1);
            ManifestEncoding.WriteScalar(writer, value);
        }
    }
}
