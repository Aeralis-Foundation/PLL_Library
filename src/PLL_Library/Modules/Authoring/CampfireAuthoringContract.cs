using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class CampfireAuthoringContract
{
    const float MoraleBoostRadius = 12f;
    const float MoraleBoostBaseline = 0.25f;
    const float MoraleBoostPerAdditionalScout = 0f;
    readonly Entry? stable;
    readonly Entry[] candidates;

    CampfireAuthoringContract(Entry? stable, Entry[] candidates)
    {
        this.stable = stable;
        this.candidates = candidates;
    }

    internal static CampfireAuthoringContract? CreateOptional(
        Transform template,
        Transform featureRoot,
        BiomeId id,
        AuthoringCandidateGraph graph)
    {
        var stableSources = template.GetComponentsInChildren<PllCampfireAuthoring>(true);
        if (stableSources.Length > 1)
            throw new ArgumentException($"Biome '{id}' contains more than one stable PllCampfireAuthoring.", nameof(template));
        var stable = stableSources.Length == 0
            ? null
            : Entry.Create(template, featureRoot, stableSources[0], false, id);
        var candidates = new List<Entry>();
        foreach (var candidate in graph.Ordered)
        {
            var sources = candidate.GetComponentsInChildren<PllCampfireAuthoring>(true);
            if (sources.Length == 0) continue;
            if (sources.Length != 1 || !sources[0])
                throw new ArgumentException($"Biome '{id}' campfire candidate '{candidate.name}' must contain exactly one valid PllCampfireAuthoring.", nameof(template));
            candidates.Add(Entry.Create(candidate.transform, candidate.transform, sources[0], true, id));
        }
        return stable is null && candidates.Count == 0
            ? null
            : new CampfireAuthoringContract(stable, candidates.ToArray());
    }

    internal void Resolve(Transform template, BiomeId id) => stable?.Resolve(template, id);

    internal void Validate(
        Transform template, Transform featureRoot, BiomeId id, AuthoringCandidateGraph Graph)
    {
        var currentStable = template.GetComponentsInChildren<PllCampfireAuthoring>(true);
        if (stable is null ? currentStable.Length != 0
            : currentStable.Length != 1 || !ReferenceEquals(currentStable[0], stable.Source))
            throw new InvalidOperationException($"Biome '{id}' changed its stable model-first campfire authoring before sealing.");
        stable?.Validate(template, featureRoot, id);

        var current = new List<(GameObject Root, PllCampfireAuthoring Source)>();
        foreach (var candidate in Graph.Ordered)
        {
            var sources = candidate.GetComponentsInChildren<PllCampfireAuthoring>(true);
            foreach (var source in sources)
                if (source) current.Add((candidate, source));
        }
        if (current.Count != candidates.Length)
            throw new InvalidOperationException($"Biome '{id}' changed its generated campfire candidate count before sealing.");
        for (var i = 0; i < candidates.Length; i++)
        {
            if (!ReferenceEquals(current[i].Root, candidates[i].Owner)
                || !ReferenceEquals(current[i].Source, candidates[i].Source))
                throw new InvalidOperationException($"Biome '{id}' changed its generated campfire candidate order before sealing.");
            candidates[i].Validate(current[i].Root.transform, current[i].Root.transform, id);
        }
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(stable is not null);
        stable?.WriteManifest(writer);
        writer.Write(candidates.Length);
        foreach (var candidate in candidates) candidate.WriteManifest(writer);
    }

    sealed class Entry
    {
        readonly bool generated;
        readonly ComponentBinding<PllCampfireAuthoring> component;
        readonly ComponentBinding<global::Campfire> campfire;
        readonly ComponentBinding<PhotonView> view;
        readonly ObjectBinding root;
        readonly ObjectBinding modelRoot;
        readonly ComponentBinding<ParticleSystem> fireParticles;
        readonly ComponentBinding<ParticleSystem> smokeParticlesOff;
        readonly ComponentBinding<ParticleSystem> smokeParticlesLit;
        readonly TransformBinding logRoot;
        readonly ObjectBinding? enableWhenLit;
        readonly ObjectBinding? disableWhenLit;
        readonly ComponentBinding<AudioSource>? loop;

        Entry(
            bool generated,
            GameObject owner,
            ComponentBinding<PllCampfireAuthoring> component,
            ComponentBinding<global::Campfire> campfire,
            ComponentBinding<PhotonView> view,
            ObjectBinding root,
            ObjectBinding modelRoot,
            ComponentBinding<ParticleSystem> fireParticles,
            ComponentBinding<ParticleSystem> smokeParticlesOff,
            ComponentBinding<ParticleSystem> smokeParticlesLit,
            TransformBinding logRoot,
            ObjectBinding? enableWhenLit,
            ObjectBinding? disableWhenLit,
            ComponentBinding<AudioSource>? loop)
        {
            this.generated = generated; Owner = owner; this.component = component;
            this.campfire = campfire; this.view = view; this.root = root; this.modelRoot = modelRoot;
            this.fireParticles = fireParticles; this.smokeParticlesOff = smokeParticlesOff;
            this.smokeParticlesLit = smokeParticlesLit; this.logRoot = logRoot;
            this.enableWhenLit = enableWhenLit; this.disableWhenLit = disableWhenLit; this.loop = loop;
        }

        internal GameObject Owner { get; }
        internal PllCampfireAuthoring Source => component.Source;

        internal static Entry Create(
            Transform bindingRoot,
            Transform modeOwner,
            PllCampfireAuthoring source,
            bool generated,
            BiomeId id)
        {
            if (!source || !source.enabled || !bindingRoot || !modeOwner)
                throw new ArgumentException($"Biome '{id}' model-first campfire authoring is missing or disabled.", nameof(source));
            var wrapper = source.Root;
            var particles = new[] { source.FireParticles, source.SmokeParticlesOff, source.SmokeParticlesLit };
            if (!wrapper || source.gameObject != wrapper || source.GetComponents<PllCampfireAuthoring>().Length != 1
                || !TransformOwnership.Contains(modeOwner, wrapper.transform)
                || !TransformOwnership.DiscoverableWhenActivated(modeOwner, wrapper.transform)
                || TransformOwnership.HasPropSpawnerInPath(modeOwner, wrapper.transform)
                || generated && !bindingRoot.gameObject.activeSelf
                || !source.ModelRoot || source.ModelRoot == wrapper
                || !TransformOwnership.Contains(wrapper.transform, source.ModelRoot.transform)
                || !FinalForceAuthoringContract.ActiveBelow(source.ModelRoot.transform, wrapper.transform)
                || !HasRenderer(source.ModelRoot) || !HasNonTriggerCollider(source.ModelRoot) || !source.LogRoot
                || source.LogRoot == wrapper.transform || source.ModelRoot.transform == source.LogRoot
                || !TransformOwnership.Contains(wrapper.transform, source.LogRoot)
                || !TransformOwnership.Contains(source.LogRoot, source.ModelRoot.transform)
                || !FinalForceAuthoringContract.ActiveBelow(source.LogRoot, wrapper.transform))
                throw new ArgumentException($"Biome '{id}' model-first campfire root or presentation is invalid.", nameof(source));
            var seenParticles = new HashSet<ParticleSystem>();
            foreach (var particle in particles)
                if (!particle || !seenParticles.Add(particle)
                    || !TransformOwnership.Contains(wrapper.transform, particle.transform))
                    throw new ArgumentException($"Biome '{id}' model-first campfire has a missing, duplicate, or unowned particle system.", nameof(source));
            if (!ExactParticleState(source.FireParticles, false, true)
                || !ExactParticleState(source.SmokeParticlesOff, true, true)
                || !ExactParticleState(source.SmokeParticlesLit, true, false))
                throw new ArgumentException($"Biome '{id}' model-first campfire particle state is invalid.", nameof(source));
            var campfires = wrapper.GetComponents<global::Campfire>();
            var views = wrapper.GetComponents<PhotonView>();
            if (modeOwner.GetComponentsInChildren<global::Campfire>(true).Length != 1
                || campfires.Length != 1 || !campfires[0] || !campfires[0].enabled
                || views.Length != 1 || !views[0] || !views[0].enabled || views[0].ViewID != 0
                || !Matches(source, campfires[0])
                || campfires[0].fireStart is null || campfires[0].extinguish is null || campfires[0].moraleBoost is null
                || source.EnableWhenLit != source.FireParticles.gameObject
                || source.DisableWhenLit != source.SmokeParticlesOff.gameObject
                || !ValidSourceLoop(wrapper.transform, source.Loop))
                throw new ArgumentException($"Biome '{id}' model-first campfire does not match its staged PEAK Campfire contract.", nameof(source));
            return new Entry(
                generated, bindingRoot.gameObject,
                ComponentBinding<PllCampfireAuthoring>.Create(bindingRoot, source),
                ComponentBinding<global::Campfire>.Create(bindingRoot, campfires[0]),
                ComponentBinding<PhotonView>.Create(bindingRoot, views[0]),
                ObjectBinding.Create(bindingRoot, wrapper), ObjectBinding.Create(bindingRoot, source.ModelRoot),
                ComponentBinding<ParticleSystem>.Create(bindingRoot, source.FireParticles),
                ComponentBinding<ParticleSystem>.Create(bindingRoot, source.SmokeParticlesOff),
                ComponentBinding<ParticleSystem>.Create(bindingRoot, source.SmokeParticlesLit),
                TransformBinding.Create(bindingRoot, source.LogRoot),
                source.EnableWhenLit ? ObjectBinding.Create(bindingRoot, source.EnableWhenLit) : null,
                source.DisableWhenLit ? ObjectBinding.Create(bindingRoot, source.DisableWhenLit) : null,
                source.Loop ? ComponentBinding<AudioSource>.Create(bindingRoot, source.Loop) : null);
        }

        internal void Resolve(Transform bindingRoot, BiomeId id)
        {
            var source = component.Resolve(bindingRoot, id, "campfire authoring");
            var runtimeRoot = root.Resolve(bindingRoot, id, "campfire root");
            var runtimeModel = modelRoot.Resolve(bindingRoot, id, "campfire model root");
            var runtimeCampfire = campfire.Resolve(bindingRoot, id, "PEAK Campfire");
            var runtimeView = view.Resolve(bindingRoot, id, "campfire PhotonView");
            var fire = fireParticles.Resolve(bindingRoot, id, "campfire fire particles");
            var smokeOff = smokeParticlesOff.Resolve(bindingRoot, id, "campfire unlit smoke particles");
            var smokeLit = smokeParticlesLit.Resolve(bindingRoot, id, "campfire lit smoke particles");
            var logs = logRoot.ResolveValidated(bindingRoot, id.Value, "campfire log root");
            var enable = enableWhenLit?.Resolve(bindingRoot, id, "campfire lit object");
            var disable = disableWhenLit?.Resolve(bindingRoot, id, "campfire unlit object");
            var audio = loop?.Resolve(bindingRoot, id, "campfire loop source");
            if (source.Root != runtimeRoot || source.ModelRoot != runtimeModel || source.FireParticles != fire
                || source.SmokeParticlesOff != smokeOff || source.SmokeParticlesLit != smokeLit
                || source.LogRoot != logs || source.EnableWhenLit != enable || source.DisableWhenLit != disable
                || source.Loop != audio || runtimeCampfire.gameObject != runtimeRoot
                || runtimeModel.transform == logs || !TransformOwnership.Contains(logs, runtimeModel.transform)
                || enable != fire.gameObject || disable != smokeOff.gameObject
                || !ValidParticleConfiguration(fire, false, true)
                || !ValidParticleConfiguration(smokeOff, true, true)
                || !ValidParticleConfiguration(smokeLit, true, false)
                || !ValidLoopConfiguration(runtimeRoot.transform, audio)
                || !Matches(source, runtimeCampfire) || runtimeCampfire.fireStart is null
                || runtimeCampfire.extinguish is null || runtimeCampfire.moraleBoost is null
                || runtimeView.gameObject != runtimeRoot || !runtimeView.enabled
                || !HasRenderer(runtimeModel) || !HasNonTriggerCollider(runtimeModel))
                throw new InvalidOperationException($"Biome '{id}' runtime model-first campfire changed its staged contract.");
        }

        internal void Validate(Transform bindingRoot, Transform modeOwner, BiomeId id)
        {
            component.Validate(bindingRoot, id, "campfire authoring"); campfire.Validate(bindingRoot, id, "PEAK Campfire");
            view.Validate(bindingRoot, id, "campfire PhotonView"); root.Validate(bindingRoot, id, "campfire root");
            modelRoot.Validate(bindingRoot, id, "campfire model root");
            fireParticles.Validate(bindingRoot, id, "campfire fire particles");
            smokeParticlesOff.Validate(bindingRoot, id, "campfire unlit smoke particles");
            smokeParticlesLit.Validate(bindingRoot, id, "campfire lit smoke particles");
            logRoot.Validate(bindingRoot, id.Value, "campfire log root");
            enableWhenLit?.Validate(bindingRoot, id, "campfire lit object");
            disableWhenLit?.Validate(bindingRoot, id, "campfire unlit object");
            loop?.Validate(bindingRoot, id, "campfire loop source");
            if (generated && !bindingRoot.gameObject.activeSelf
                || modeOwner.GetComponentsInChildren<global::Campfire>(true).Length != 1
                || view.Source.ViewID != 0)
                throw new InvalidOperationException($"Biome '{id}' changed its staged model-first campfire mode before sealing.");
            Resolve(bindingRoot, id);
        }

        internal void WriteManifest(BinaryWriter writer)
        {
            writer.Write(generated); component.WriteManifest(writer); campfire.WriteManifest(writer);
            view.WriteManifest(writer); root.WriteManifest(writer); modelRoot.WriteManifest(writer);
            fireParticles.WriteManifest(writer); smokeParticlesOff.WriteManifest(writer);
            smokeParticlesLit.WriteManifest(writer); logRoot.WriteManifest(writer);
            writer.Write(enableWhenLit is not null); enableWhenLit?.WriteManifest(writer);
            writer.Write(disableWhenLit is not null); disableWhenLit?.WriteManifest(writer);
            writer.Write(loop is not null); loop?.WriteManifest(writer);
        }

        static bool Matches(PllCampfireAuthoring source, global::Campfire value) =>
            value.fireParticles == source.FireParticles && value.smokeParticlesOff == source.SmokeParticlesOff
            && value.smokeParticlesLit == source.SmokeParticlesLit && value.logRoot == source.LogRoot
            && value.enableWhenLit == source.EnableWhenLit && value.disableWhenLit == source.DisableWhenLit
            && value.loop == source.Loop && value.moraleBoostRadius.Equals(MoraleBoostRadius)
            && value.moraleBoostBaseline.Equals(MoraleBoostBaseline)
            && value.moraleBoostPerAdditionalScout.Equals(MoraleBoostPerAdditionalScout);
    }

    static bool HasRenderer(GameObject root)
    {
        foreach (var value in root.GetComponentsInChildren<Renderer>(true))
            if (value && value.enabled && FinalForceAuthoringContract.ActiveBelow(value.transform, root.transform)) return true;
        return false;
    }

    static bool HasNonTriggerCollider(GameObject Root)
    {
        foreach (var Value in Root.GetComponentsInChildren<Collider>(true))
            if (Value && Value.enabled && !Value.isTrigger
                && FinalForceAuthoringContract.ActiveBelow(Value.transform, Root.transform)) return true;
        return false;
    }

    static bool ExactParticleState(ParticleSystem Value, bool Active, bool PlayOnAwake) =>
        ValidParticleConfiguration(Value, Active, PlayOnAwake)
        && Value.isStopped && Value.particleCount == 0;

    static bool ValidParticleConfiguration(ParticleSystem Value, bool Active, bool PlayOnAwake) =>
        Value && Value.gameObject.activeSelf == Active && Value.main.playOnAwake == PlayOnAwake;

    static bool ValidSourceLoop(Transform Root, AudioSource? Value) =>
        !Value || ValidLoopConfiguration(Root, Value) && !Value.isPlaying && Value.timeSamples == 0;

    static bool ValidLoopConfiguration(Transform Root, AudioSource? Value) =>
        !Value || TransformOwnership.Contains(Root, Value.transform)
        && FinalForceAuthoringContract.ActiveBelow(Value.transform, Root)
        && Value.enabled && Value.clip && Value.loop && Value.playOnAwake && Value.volume.Equals(0f);
}
