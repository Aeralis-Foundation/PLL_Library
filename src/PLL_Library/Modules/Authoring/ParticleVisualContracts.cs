using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class CustomVisualContract
{
    readonly ComponentBinding<PllCustomVisualAuthoring> component;
    readonly ObjectBinding root;
    readonly ParticleVisualGraph graph;
    readonly PllCustomVisualSpace space;
    readonly Vector3 positionOffset;
    readonly float repositionDistance;
    readonly float CoverDelaySeconds;

    CustomVisualContract(
        ComponentBinding<PllCustomVisualAuthoring> component,
        ObjectBinding root,
        ParticleVisualGraph graph)
    {
        this.component = component;
        this.root = root;
        this.graph = graph;
        space = component.Source.Space;
        positionOffset = component.Source.PositionOffset;
        repositionDistance = component.Source.RepositionDistance;
        CoverDelaySeconds = component.Source.Transition.CoverDelaySeconds;
    }

    internal PllCustomVisualAuthoring Source => component.Source;
    internal GameObject SourceRoot => root.Source;

    internal static CustomVisualContract[] CreateAll(
        PllBiomeDefinition definition,
        Transform template,
        AuthoringCandidateGraph candidates,
        BiomeId id)
    {
        foreach (var candidate in candidates.Ordered)
            if (candidate.GetComponentsInChildren<PllCustomVisualAuthoring>(true).Length != 0)
                throw new ArgumentException($"Biome '{id}' places a custom visual inside a generated candidate.", nameof(definition));
        var sources = template.GetComponentsInChildren<PllCustomVisualAuthoring>(true);
        var ordered = new List<(string Key, PllCustomVisualAuthoring Value)>(sources.Length);
        foreach (var source in sources)
            ordered.Add((DeterministicObjectIdentity.RelativeComponentPath(source, template, 0), source));
        ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        var presentation = sources.Length == 0 ? null : RequirePresentationRoot(template, id);
        var result = new CustomVisualContract[ordered.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var source = ordered[i].Value;
            ValidateMetadata(definition, template, presentation!, source, id, true);
            if (source.Transition.CoverDelaySeconds > 0f)
                foreach (var Component in source.GetComponentsInChildren<Component>(true))
                {
                    if (Component is ParticleSystem Particle
                        && PllAuthoringValueContract.ParticleTransitionFailure(Particle) is { } Failure)
                        throw new ArgumentException(Failure, "source");
                    if (Component is Renderer and not ParticleSystemRenderer)
                        throw new ArgumentException("Cover Delay requires particle-only visual renderers.", "source");
                }
            result[i] = new CustomVisualContract(
                ComponentBinding<PllCustomVisualAuthoring>.Create(template, source),
                ObjectBinding.Create(template, source.gameObject),
                ParticleVisualGraph.Capture(source.gameObject, template, source, true));
        }
        for (var i = 0; i < result.Length; i++)
            for (var j = i + 1; j < result.Length; j++)
                if (TransformOwnership.Overlaps(result[i].Source.transform, result[j].Source.transform))
                    throw new ArgumentException($"Biome '{id}' custom visual roots must be distinct and non-overlapping.", nameof(definition));
        return result;
    }

    internal ResolvedCustomVisual Resolve(Transform runtimeRoot, BiomeId id)
    {
        var authoring = component.Resolve(runtimeRoot, id, "custom visual");
        var resolvedRoot = root.Resolve(runtimeRoot, id, "custom visual root");
        if (authoring.gameObject != resolvedRoot || authoring.Space != space
            || authoring.PositionOffset != positionOffset || authoring.RepositionDistance != repositionDistance
            || !authoring.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' changed a custom visual after sealing.");
        var resolved = graph.Resolve(resolvedRoot, runtimeRoot, authoring, true);
        return new ResolvedCustomVisual(
            authoring, resolvedRoot, resolved.AllParticles, resolved.RootParticles,
            space, positionOffset, repositionDistance);
    }

    internal void Validate(PllBiomeDefinition definition, Transform template, BiomeId id)
    {
        component.Validate(template, id, "custom visual");
        root.Validate(template, id, "custom visual root");
        var presentation = RequirePresentationRoot(template, id);
        ValidateMetadata(definition, template, presentation, component.Source, id, false);
        if (component.Source.Space != space || component.Source.PositionOffset != positionOffset
            || component.Source.RepositionDistance != repositionDistance
            || !component.Source.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' changed a custom visual configuration before sealing.");
        graph.Validate(root.Source, template, component.Source, true);
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        component.WriteManifest(writer);
        root.WriteManifest(writer);
        writer.Write((byte)space);
        ManifestEncoding.WriteVector(writer, positionOffset);
        ManifestEncoding.WriteScalar(writer, repositionDistance);
        graph.WriteManifest(writer);
        ManifestEncoding.WriteScalar(writer, CoverDelaySeconds);
    }

    static Transform RequirePresentationRoot(Transform template, BiomeId id)
    {
        Transform? result = null;
        for (var i = 0; i < template.childCount; i++)
        {
            var child = template.GetChild(i);
            if (!string.Equals(child.name, "Presentation", StringComparison.Ordinal)) continue;
            if (result)
                throw new ArgumentException($"Biome '{id}' custom visuals require one direct Presentation subtree.", nameof(template));
            result = child;
        }
        return result
            ? result
            : throw new ArgumentException($"Biome '{id}' custom visuals require one direct Presentation subtree.", nameof(template));
    }

    static void ValidateMetadata(
        PllBiomeDefinition definition,
        Transform template,
        Transform presentation,
        PllCustomVisualAuthoring source,
        BiomeId id,
        bool argument)
    {
        if (!source || source.gameObject == presentation.gameObject || !source.transform.IsChildOf(presentation)
            || source.GetComponents<PllCustomVisualAuthoring>().Length != 1)
            throw Invalid(id, "has invalid Presentation ownership", argument);
        if (!Enum.IsDefined(typeof(PllCustomVisualSpace), source.Space)
            || !ManifestEncoding.CanEncode(source.PositionOffset.x)
            || !ManifestEncoding.CanEncode(source.PositionOffset.y)
            || !ManifestEncoding.CanEncode(source.PositionOffset.z)
            || !ManifestEncoding.CanEncode(source.RepositionDistance) || source.RepositionDistance < 0f
            || PllAuthoringValueContract.TransitionFailure(source.Transition.CoverDelaySeconds) is not null)
            throw Invalid(id, "has invalid tracking configuration", argument);
        if (TransformOwnership.HasPropSpawnerInPath(template, source.transform))
            throw Invalid(id, "entered a PropSpawner hierarchy", argument);
        RejectOverlap(source.transform, definition.Anchors.FloorRoot.transform, id, "floor root", argument);
        RejectOverlap(source.transform, definition.Anchors.CampfireRoot.transform, id, "campfire root", argument);
        foreach (var value in definition.EffectDefinitions) RejectOverlap(source.transform, value.Root.transform, id, "effect root", argument);
        foreach (var value in definition.Visuals) RejectOverlap(source.transform, value.Root.transform, id, "renderer-gate root", argument);
        foreach (var value in definition.LuggageDefinitions) RejectOverlap(source.transform, value.transform, id, "luggage root", argument);
        foreach (var value in definition.SoundEmitters) RejectOverlap(source.transform, value.Root.transform, id, "sound-emitter root", argument);
        foreach (var value in template.GetComponentsInChildren<PllEffectZoneAuthoring>(true))
            RejectOverlap(source.transform, value.Root.transform, id, "effect-zone root", argument);
        foreach (var value in template.GetComponentsInChildren<PLLNativeResolver>(true))
            RejectOverlap(source.transform, value.Root.transform, id, "gameplay-resolver root", argument);
        foreach (var value in template.GetComponentsInChildren<PllItemSpawnerAuthoring>(true))
            RejectOverlap(source.transform, value.Root.transform, id, "item-spawner root", argument);
        foreach (var value in template.GetComponentsInChildren<PllVariationAuthoring>(true))
            foreach (var entry in value.Entries)
                if (entry is not null && entry.Root)
                    RejectOverlap(source.transform, entry.Root.transform, id, "variation root", argument);
        foreach (var value in template.GetComponentsInChildren<PllPropZoneAuthoring>(true))
            RejectOverlap(source.transform, value.Root.transform, id, "prop-zone root", argument);
        foreach (var value in template.GetComponentsInChildren<PllChildPropAuthoring>(true))
            RejectOverlap(source.transform, value.Root.transform, id, "child-prop root", argument);
        foreach (var value in template.GetComponentsInChildren<PllCampfireAuthoring>(true))
            RejectOverlap(source.transform, value.Root.transform, id, "campfire authoring root", argument);
        foreach (var value in template.GetComponentsInChildren<PllStatueAuthoring>(true))
            RejectOverlap(source.transform, value.transform, id, "statue authoring root", argument);
        foreach (var value in template.GetComponentsInChildren<PLLHookedProp>(true))
            RejectOverlap(source.transform, value.transform, id, "hooked-prop root", argument);
        foreach (var value in template.GetComponentsInChildren<PLLNoPropSpawn>(true))
            RejectOverlap(source.transform, value.transform, id, "no-prop-spawn root", argument);
        foreach (var value in template.GetComponentsInChildren<PLLFinalGeneration>(true))
            RejectOverlap(source.transform, value.transform, id, "final-generation root", argument);
        foreach (var value in template.GetComponentsInChildren<PLLForceSpawner>(true))
            RejectOverlap(source.transform, value.transform, id, "force-spawner root", argument);
    }

    static void RejectOverlap(Transform root, Transform? other, BiomeId id, string role, bool argument)
    {
        if (other && TransformOwnership.Overlaps(root, other)) throw Invalid(id, $"overlaps a {role}", argument);
    }

    static Exception Invalid(BiomeId id, string reason, bool argument) => argument
        ? new ArgumentException($"Biome '{id}' custom visual {reason}.", "definition")
        : new InvalidOperationException($"Biome '{id}' custom visual {reason}.");
}

internal sealed class NativeVisualContract
{
    readonly ComponentBinding<PLLNativeVisualResolver> component;
    readonly PllNativeVisualPreset preset;
    readonly bool SuppressNativeParticlesValue;
    readonly PllEffectParticleColorMode ParticleColorMode;
    readonly Color ParticleColor;
    readonly PllEffectMaterialColorChannel MaterialColorChannel;
    readonly Color MaterialColor;
    readonly float CoverDelaySeconds;

    NativeVisualContract(ComponentBinding<PLLNativeVisualResolver> component)
    {
        this.component = component;
        preset = component.Source.Preset;
        SuppressNativeParticlesValue = component.Source.SuppressNativeParticles;
        ParticleColorMode = component.Source.ParticleColorMode;
        ParticleColor = component.Source.ParticleColor;
        MaterialColorChannel = component.Source.MaterialColorChannel;
        MaterialColor = component.Source.MaterialColor;
        CoverDelaySeconds = component.Source.Transition.CoverDelaySeconds;
    }

    internal PllNativeVisualPreset Preset => preset;
    internal bool SuppressNativeParticles => SuppressNativeParticlesValue;
    internal float TransitionSeconds => CoverDelaySeconds;

    internal static NativeVisualContract? CreateOptional(
        Transform template,
        AuthoringCandidateGraph candidates,
        BiomeId id)
    {
        foreach (var candidate in candidates.Ordered)
            if (candidate.GetComponentsInChildren<PLLNativeVisualResolver>(true).Length != 0)
                throw new ArgumentException($"Biome '{id}' places a native visual resolver inside a generated candidate.", nameof(template));
        var values = template.GetComponentsInChildren<PLLNativeVisualResolver>(true);
        if (values.Length == 0) return null;
        if (values.Length != 1 || values[0].gameObject != template.gameObject
            || template.GetComponents<PLLNativeVisualResolver>().Length != 1)
            throw new ArgumentException($"Biome '{id}' must place at most one native visual resolver directly on its root.", nameof(template));
        ValidatePreset(values[0], id, true);
        PllEffectAppearanceValueContract.ValidateNativeVisual(values[0]);
        return new NativeVisualContract(ComponentBinding<PLLNativeVisualResolver>.Create(template, values[0]));
    }

    internal void Validate(Transform template, BiomeId id)
    {
        component.Validate(template, id, "native visual resolver");
        if (component.Source.gameObject != template.gameObject
            || template.GetComponentsInChildren<PLLNativeVisualResolver>(true).Length != 1
            || template.GetComponents<PLLNativeVisualResolver>().Length != 1)
            throw new InvalidOperationException($"Biome '{id}' changed its native visual resolver ownership before sealing.");
        ValidatePreset(component.Source, id, false);
        if (component.Source.Preset != preset
            || component.Source.SuppressNativeParticles != SuppressNativeParticlesValue
            || component.Source.ParticleColorMode != ParticleColorMode
            || !component.Source.ParticleColor.Equals(ParticleColor)
            || component.Source.MaterialColorChannel != MaterialColorChannel
            || !component.Source.MaterialColor.Equals(MaterialColor)
            || !component.Source.Transition.CoverDelaySeconds.Equals(CoverDelaySeconds))
            throw new InvalidOperationException($"Biome '{id}' changed its native visual resolver before sealing.");
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        component.WriteManifest(writer);
        writer.Write((byte)preset);
        writer.Write(SuppressNativeParticlesValue);
        writer.Write((byte)ParticleColorMode); ManifestEncoding.WriteColor(writer, ParticleColor);
        writer.Write((byte)MaterialColorChannel); ManifestEncoding.WriteColor(writer, MaterialColor);
        ManifestEncoding.WriteScalar(writer, CoverDelaySeconds);
    }

    internal void ValidateAppearance(NativeVisualSource Source)
    {
        if ((ParticleColorMode != PllEffectParticleColorMode.Original
                || MaterialColorChannel != PllEffectMaterialColorChannel.None) && Source.Particles.Count == 0)
            throw new InvalidOperationException("The selected native environment has no captured ambient particle graph.");
        if (CoverDelaySeconds > 0f && !SuppressNativeParticlesValue)
        {
            foreach (var ParticleSource in Source.Particles)
                foreach (var Component in ParticleSource.Fx.GetComponentsInChildren<Component>(true))
                {
                    if (Component is ParticleSystem Particle
                        && PllAuthoringValueContract.ParticleTransitionFailure(Particle) is { } Failure)
                        throw new ArgumentException(Failure, "source");
                    if (Component is Renderer and not ParticleSystemRenderer)
                        throw new ArgumentException("Cover Delay requires particle-only native visual renderers.", "source");
                }
        }
        if (MaterialColorChannel == PllEffectMaterialColorChannel.None) return;
        foreach (var ParticleSource in Source.Particles)
            foreach (var Particle in ParticleSource.Fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var Renderer = Particle.GetComponent<ParticleSystemRenderer>();
                if (!Renderer)
                    throw new InvalidOperationException("Native ambient particle appearance requires a particle renderer on every system.");
                PllEffectAppearanceValueContract.ValidateRenderer(Renderer, 0, MaterialColorChannel);
            }
    }

    internal void ApplyAppearance(ResolvedParticleGraph Graph)
    {
        if (ParticleColorMode == PllEffectParticleColorMode.Original
            && MaterialColorChannel == PllEffectMaterialColorChannel.None) return;
        foreach (var Particle in Graph.AllParticles)
        {
            if (ParticleColorMode == PllEffectParticleColorMode.Override)
            {
                var Main = Particle.main;
                Main.startColor = ParticleColor;
            }
            if (MaterialColorChannel == PllEffectMaterialColorChannel.None) continue;
            var Renderer = Particle.GetComponent<ParticleSystemRenderer>();
            var Block = new MaterialPropertyBlock();
            Renderer.GetPropertyBlock(Block, 0);
            if (Block.isEmpty) Renderer.GetPropertyBlock(Block);
            Block.SetColor(PllEffectAppearanceValueContract.MaterialProperty(MaterialColorChannel), MaterialColor);
            Renderer.SetPropertyBlock(Block, 0);
        }
    }

    static void ValidatePreset(PLLNativeVisualResolver source, BiomeId id, bool argument)
    {
        if (PllAuthoringValueContract.TransitionFailure(source.Transition.CoverDelaySeconds) is not null)
            throw new ArgumentException("The native visual resolver has an invalid Cover Delay.", "source");
        if (source && source.Preset != PllNativeVisualPreset.None
            && Enum.IsDefined(typeof(PllNativeVisualPreset), source.Preset)) return;
        if (argument)
            throw new ArgumentException($"Biome '{id}' native visual resolver must select one supported non-None preset.", "template");
        throw new InvalidOperationException($"Biome '{id}' changed its native visual preset before sealing.");
    }
}
internal sealed class ResolvedCustomVisual
{
    internal ResolvedCustomVisual(
        PllCustomVisualAuthoring authoring,
        GameObject root,
        IReadOnlyList<ParticleSystem> allParticles,
        IReadOnlyList<ParticleSystem> rootParticles,
        PllCustomVisualSpace space,
        Vector3 positionOffset,
        float repositionDistance)
    {
        Authoring = authoring;
        Root = root;
        AllParticles = allParticles;
        RootParticles = rootParticles;
        Space = space;
        PositionOffset = positionOffset;
        RepositionDistance = repositionDistance;
    }

    internal PllCustomVisualAuthoring Authoring { get; }
    internal GameObject Root { get; }
    internal IReadOnlyList<ParticleSystem> AllParticles { get; }
    internal IReadOnlyList<ParticleSystem> RootParticles { get; }
    internal PllCustomVisualSpace Space { get; }
    internal Vector3 PositionOffset { get; }
    internal float RepositionDistance { get; }
}
