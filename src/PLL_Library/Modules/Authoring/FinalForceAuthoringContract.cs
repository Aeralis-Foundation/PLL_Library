using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class FinalForceAuthoringContract
{
    readonly Transform template;
    readonly string segmentId;
    readonly BiomeId biomeId;
    readonly FeatureContract[] features;
    readonly ForceMarkerContract[] forceMarkers;
    bool externalSealed;

    FinalForceAuthoringContract(
        Transform template,
        string segmentId,
        BiomeId biomeId,
        FeatureContract[] features,
        ForceMarkerContract[] forceMarkers)
    {
        this.template = template;
        this.segmentId = segmentId;
        this.biomeId = biomeId;
        this.features = features;
        this.forceMarkers = forceMarkers;
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marker in forceMarkers)
            if (seen.Add(marker.Name)) names.Add(marker.Name);
        names.Sort(StringComparer.Ordinal);
        ForceNames = names.ToArray();
        externalSealed = forceMarkers.Length == 0;
    }

    internal IReadOnlyList<string> ForceNames { get; }

    internal string DescribeForceName(string name)
    {
        var keys = new List<string>();
        foreach (var marker in forceMarkers)
            if (string.Equals(marker.Name, name, StringComparison.Ordinal)) keys.Add(marker.Key);
        if (keys.Count == 0)
            throw new InvalidOperationException(
                $"Registered segment '{segmentId}' has no force marker named '{name}'.");
        return $"Biome '{biomeId}' segment '{segmentId}' force marker paths [{string.Join(", ", keys)}] exact name '{name}'";
    }

    internal static FinalForceAuthoringContract Create(
        Transform template,
        Transform floorRoot,
        Transform campfireRoot,
        string segmentId,
        BiomeId biomeId)
    {
        if (!template || !floorRoot || !campfireRoot)
            throw new ArgumentException("Final/force authoring requires live template and feature roots.", nameof(template));
        var finalComponents = template.GetComponentsInChildren<PLLFinalGeneration>(true);
        var finals = new List<FinalMarkerContract>(finalComponents.Length);
        foreach (var marker in finalComponents)
        {
            if (!marker) throw Invalid(segmentId, "contains a destroyed final-generation marker");
            var feature = FeatureIndex(floorRoot, campfireRoot, marker.transform, segmentId, "final-generation marker");
            finals.Add(FinalMarkerContract.Create(
                template, feature == 0 ? floorRoot : campfireRoot, marker, feature, segmentId, biomeId));
        }
        finals.Sort((left, right) =>
        {
            var feature = left.Feature.CompareTo(right.Feature);
            return feature != 0 ? feature : StringComparer.Ordinal.Compare(left.Key, right.Key);
        });
        for (var i = 0; i < finals.Count; i++)
            for (var j = i + 1; j < finals.Count; j++)
                if (TransformOwnership.Overlaps(finals[i].Source.transform, finals[j].Source.transform))
                    throw Invalid(segmentId,
                        $"has overlapping final-generation roots '{finals[i].Key}' and '{finals[j].Key}'");

        var forceComponents = template.GetComponentsInChildren<PLLForceSpawner>(true);
        var forces = new List<ForceMarkerContract>(forceComponents.Length);
        foreach (var marker in forceComponents)
        {
            if (!marker) throw Invalid(segmentId, "contains a destroyed force-spawn marker");
            var feature = FeatureIndex(floorRoot, campfireRoot, marker.transform, segmentId, "force-spawn marker");
            FinalMarkerContract? final = null;
            for (var i = 0; i < finals.Count; i++)
            {
                if (!TransformOwnership.Contains(finals[i].Source.transform, marker.transform)) continue;
                if (final is not null) throw Invalid(segmentId, "has a force-spawn marker beneath overlapping final roots");
                final = finals[i];
            }
            forces.Add(ForceMarkerContract.Create(
                template, feature == 0 ? floorRoot : campfireRoot, marker, feature, final, segmentId, biomeId));
        }
        forces.Sort((left, right) =>
        {
            var feature = left.Feature.CompareTo(right.Feature);
            return feature != 0 ? feature : StringComparer.Ordinal.Compare(left.Key, right.Key);
        });

        var featureContracts = new[]
        {
            new FeatureContract(template, 0, floorRoot, SelectFinals(finals, 0), SelectForces(forces, 0)),
            new FeatureContract(template, 1, campfireRoot, SelectFinals(finals, 1), SelectForces(forces, 1))
        };
        return new FinalForceAuthoringContract(template, segmentId, biomeId, featureContracts, forces.ToArray());
    }

    internal void Validate(Transform currentTemplate, Transform floorRoot, Transform campfireRoot)
    {
        if (!template || currentTemplate != template)
            throw new InvalidOperationException($"Registered segment '{segmentId}' changed its final/force template owner.");
        var current = Create(currentTemplate, floorRoot, campfireRoot, segmentId, biomeId);
        using var expectedStream = new MemoryStream();
        using var currentStream = new MemoryStream();
        using (var writer = new BinaryWriter(expectedStream, System.Text.Encoding.UTF8, true)) WriteManifest(writer);
        using (var writer = new BinaryWriter(currentStream, System.Text.Encoding.UTF8, true)) current.WriteManifest(writer);
        if (!expectedStream.GetBuffer().AsSpan(0, checked((int)expectedStream.Length))
                .SequenceEqual(currentStream.GetBuffer().AsSpan(0, checked((int)currentStream.Length))))
            throw new InvalidOperationException($"Registered segment '{segmentId}' changed its final/force authoring topology.");
        foreach (var feature in features) feature.Validate(template, segmentId);
    }

    internal void SealExternal(IReadOnlyDictionary<string, ForceSpawnResolution> resolutions)
    {
        foreach (var marker in forceMarkers)
        {
            if (!resolutions.TryGetValue(marker.Name, out var resolution))
                throw new InvalidOperationException(
                    $"Registered segment '{segmentId}' force marker '{marker.Key}' was not resolved by the live catalog.");
            marker.Seal(resolution);
        }
        externalSealed = true;
    }

    internal void ClearExternal()
    {
        foreach (var marker in forceMarkers) marker.ClearExternal();
        externalSealed = forceMarkers.Length == 0;
    }

    internal FinalForceOccurrence Resolve(Transform root)
    {
        if (!root) throw new InvalidOperationException($"Registered segment '{segmentId}' clone was destroyed.");
        if (!externalSealed)
            throw new InvalidOperationException(
                $"Registered segment '{segmentId}' final/force plan was not sealed against the live catalog.");
        var runtimeFeatures = new FinalForceFeature[features.Length];
        for (var i = 0; i < features.Length; i++) runtimeFeatures[i] = features[i].Resolve(root, segmentId);
        return new FinalForceOccurrence(runtimeFeatures);
    }

    internal void ValidateResolved(Transform Root, FinalForceOccurrence Expected)
    {
        if (!Root) throw new InvalidOperationException($"Registered segment '{segmentId}' clone was destroyed.");
        if (!externalSealed)
            throw new InvalidOperationException(
                $"Registered segment '{segmentId}' final/force plan was not sealed against the live catalog.");
        if (Expected.FeatureCount != features.Length)
            throw new InvalidOperationException($"Registered segment '{segmentId}' changed its resolved final/force feature count.");
        for (var Index = 0; Index < features.Length; Index++)
            features[Index].ValidateResolved(Root, segmentId, Expected.FeatureAt(Index));
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(features.Length);
        foreach (var feature in features) feature.WriteManifest(writer);
    }

    internal void WriteExternalFingerprint(BinaryWriter writer)
    {
        if (!externalSealed)
            throw new InvalidOperationException(
                $"Registered segment '{segmentId}' final/force plan has no live compatibility seal.");
        writer.Write(forceMarkers.Length);
        foreach (var marker in forceMarkers) marker.WriteExternalFingerprint(writer);
    }

    static FinalMarkerContract[] SelectFinals(List<FinalMarkerContract> values, int feature)
    {
        var result = new List<FinalMarkerContract>();
        foreach (var value in values)
            if (value.Feature == feature) result.Add(value);
        return result.ToArray();
    }

    static ForceMarkerContract[] SelectForces(List<ForceMarkerContract> values, int feature)
    {
        var result = new List<ForceMarkerContract>();
        foreach (var value in values)
            if (value.Feature == feature) result.Add(value);
        return result.ToArray();
    }

    static int FeatureIndex(
        Transform floorRoot,
        Transform campfireRoot,
        Transform value,
        string id,
        string kind)
    {
        var Floor = value != floorRoot && TransformOwnership.Contains(floorRoot, value);
        var Campfire = value != campfireRoot && TransformOwnership.Contains(campfireRoot, value);
        if (Floor == Campfire)
            throw Invalid(id, $"{kind} '{value.name}' must belong to exactly one feature root and not be the feature root");
        return Floor ? 0 : 1;
    }

    internal static bool ActiveBelow(Transform value, Transform root, Transform? ignored = null)
    {
        for (var current = value; current && current != root; current = current.parent)
            if (current != ignored && !current.gameObject.activeSelf) return false;
        return TransformOwnership.Contains(root, value);
    }

    internal static string Key(Transform root, Component component)
    {
        var path = new List<int>();
        for (var current = component.transform; current != root; current = current.parent)
        {
            if (!current || !current.parent)
                throw new ArgumentException("An authoring component is outside its feature root.", nameof(component));
            path.Add(current.GetSiblingIndex());
        }
        var result = new System.Text.StringBuilder();
        for (var i = path.Count - 1; i >= 0; i--)
            result.Append(path[i].ToString("X8", CultureInfo.InvariantCulture)).Append('/');
        var components = component.GetComponents<Component>();
        var ordinal = Array.IndexOf(components, component);
        if (ordinal < 0) throw new ArgumentException("An authoring component is detached.", nameof(component));
        return result.Append('#').Append(ordinal.ToString("X8", CultureInfo.InvariantCulture)).ToString();
    }

    internal static InvalidOperationException Invalid(string id, string invariant) =>
        new($"Registered segment '{id}' {invariant}.");
}

internal sealed class FeatureContract
{
    readonly int index;
    readonly TransformBinding root;
    readonly FinalMarkerContract[] finals;
    readonly ForceMarkerContract[] forces;
    readonly int[] forceFinals;

    internal FeatureContract(
        Transform template,
        int index,
        Transform sourceRoot,
        FinalMarkerContract[] finals,
        ForceMarkerContract[] forces)
    {
        this.index = index;
        root = TransformBinding.Create(template, sourceRoot);
        SourceRoot = sourceRoot;
        this.finals = finals;
        this.forces = forces;
        forceFinals = new int[forces.Length];
        for (var i = 0; i < forces.Length; i++) forceFinals[i] = forces[i].FinalIndex(finals);
    }

    internal Transform SourceRoot { get; }

    internal void Validate(Transform template, string id)
    {
        if (!SourceRoot || !TransformOwnership.Contains(template, SourceRoot))
            throw new InvalidOperationException($"Registered segment '{id}' lost a final/force feature root.");
        root.Validate(template, id, "final/force feature root");
        foreach (var final in finals) final.Validate(template, SourceRoot, id);
        foreach (var force in forces) force.Validate(template, SourceRoot, id);
    }

    internal FinalForceFeature Resolve(Transform template, string id)
    {
        var runtimeRoot = root.ResolveValidated(template, id, "final/force feature root");
        var runtimeFinals = new FinalGenerationTarget[finals.Length];
        for (var i = 0; i < finals.Length; i++) runtimeFinals[i] = finals[i].Resolve(template, runtimeRoot, id);
        var runtimeForces = new ForceSpawnTarget[forces.Length];
        for (var i = 0; i < forces.Length; i++)
            runtimeForces[i] = forces[i].Resolve(template, runtimeRoot, id, forceFinals[i]);
        return new FinalForceFeature(index, runtimeRoot, runtimeFinals, runtimeForces);
    }

    internal void ValidateResolved(Transform Template, string Id, FinalForceFeature Expected)
    {
        var RuntimeRoot = root.ResolveValidated(Template, Id, "final/force feature root");
        if (Expected.Index != index || !ReferenceEquals(Expected.Root, RuntimeRoot)
            || Expected.FinalCount != finals.Length || Expected.ForceCount != forces.Length)
            throw new InvalidOperationException($"Registered segment '{Id}' changed a resolved final/force feature contract.");
        for (var Index = 0; Index < finals.Length; Index++)
            finals[Index].ValidateResolved(Template, RuntimeRoot, Id, Expected.FinalAt(Index));
        for (var Index = 0; Index < forces.Length; Index++)
            forces[Index].ValidateResolved(
                Template, RuntimeRoot, Id, forceFinals[Index], Expected.ForceAt(Index));
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(index);
        writer.Write(finals.Length);
        foreach (var final in finals) final.WriteManifest(writer);
        writer.Write(forces.Length);
        for (var i = 0; i < forces.Length; i++) forces[i].WriteManifest(writer, forceFinals[i]);
    }

}

internal sealed class FinalMarkerContract
{
    readonly ComponentBinding<PLLFinalGeneration> marker;
    readonly BiomeId biomeId;
    readonly StepBinding[] steps;
    readonly ComponentBinding<Peak.PropSpawner_AmuletStatues>[] Amulets;

    FinalMarkerContract(
        PLLFinalGeneration source,
        ComponentBinding<PLLFinalGeneration> marker,
        BiomeId biomeId,
        int feature,
        string key,
        StepBinding[] steps, ComponentBinding<Peak.PropSpawner_AmuletStatues>[] Amulets)
    {
        Source = source;
        this.marker = marker;
        this.biomeId = biomeId;
        Feature = feature;
        Key = key;
        this.steps = steps;
        this.Amulets = Amulets;
    }

    internal PLLFinalGeneration Source { get; }
    internal int Feature { get; }
    internal string Key { get; }

    internal static FinalMarkerContract Create(
        Transform template,
        Transform featureRoot,
        PLLFinalGeneration source,
        int feature,
        string id,
        BiomeId biomeId)
    {
        var root = source.transform;
        var key = FinalForceAuthoringContract.Key(featureRoot, source);
        if (root.gameObject.activeSelf)
            throw FinalForceAuthoringContract.Invalid(id, $"final root '{key}' must be inactive in the staged prefab");
        if (TransformOwnership.HasPropSpawnerInPath(featureRoot, root))
            throw FinalForceAuthoringContract.Invalid(id, $"final root '{key}' is beneath a PropSpawner");
        if (root.GetComponents<PLLFinalGeneration>().Length != 1
            || root.GetComponentsInChildren<PLLFinalGeneration>(true).Length != 1)
            throw FinalForceAuthoringContract.Invalid(id, $"final root '{key}' has an overlapping final marker");
        if (root.GetComponentInChildren<PropGrouper>(true))
            throw FinalForceAuthoringContract.Invalid(id, $"final root '{key}' contains a nested PropGrouper");
        var Amulets = new List<ComponentBinding<Peak.PropSpawner_AmuletStatues>>();
        foreach (var Finalizer in root.GetComponentsInChildren<ILevelGenFinalizer>(true))
            if (IsAmuletFinalizer(Finalizer))
                Amulets.Add(ComponentBinding<Peak.PropSpawner_AmuletStatues>.Create(
                    template, (Peak.PropSpawner_AmuletStatues)Finalizer));
            else throw FinalForceAuthoringContract.Invalid(id,
                $"final root '{key}' contains an unsupported authored ILevelGenFinalizer");
        foreach (var child in root.GetComponentsInChildren<PllChildPropAuthoring>(true))
            if (child && child.Timing != PllChildPropTiming.Immediate)
                throw FinalForceAuthoringContract.Invalid(id,
                    $"final root '{key}' contains a child-prop source whose timing is not Immediate");
        foreach (var swapper in featureRoot.GetComponentsInChildren<VariationSwapper>(true))
            if (swapper && swapper.Variations is { } variations)
                foreach (var variation in variations)
                    if (variation?.parent && variation.parent.transform == root)
                        throw FinalForceAuthoringContract.Invalid(id,
                            $"final root '{key}' must be nested beneath, not equal to, a VariationSwapper alternative");
        ValidateReserved(root, id, key);

        var sourceSteps = root.GetComponentsInChildren<LevelGenStep>(true);
        var bindings = new List<StepBinding>(sourceSteps.Length);
        foreach (var step in sourceSteps)
        {
            if (!step || !FinalForceAuthoringContract.ActiveBelow(step.transform, root))
                throw FinalForceAuthoringContract.Invalid(id, $"final root '{key}' contains a step beneath an inactive descendant");
            bindings.Add(StepBinding.Create(template, root, step, id));
        }
        bindings.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        return new FinalMarkerContract(
            source, ComponentBinding<PLLFinalGeneration>.Create(template, source),
            biomeId, feature, key, bindings.ToArray(), Amulets.ToArray());
    }

    internal void Validate(Transform template, Transform featureRoot, string id)
    {
        marker.Validate(template, biomeId, "final-generation marker");
        if (!Source || Key != FinalForceAuthoringContract.Key(featureRoot, Source))
            throw new InvalidOperationException($"Registered segment '{id}' changed a final marker path.");
        foreach (var step in steps) step.Validate(template, Source.transform, id);
        foreach (var Amulet in Amulets) Amulet.Validate(template, biomeId, "Final amulet callback");
    }

    internal FinalGenerationTarget Resolve(Transform template, Transform featureRoot, string id)
    {
        var resolved = marker.Resolve(template, biomeId, "final-generation marker");
        if (!TransformOwnership.Contains(featureRoot, resolved.transform))
            throw new InvalidOperationException($"Registered segment '{id}' final marker '{Key}' escaped its feature root.");
        if (resolved.gameObject.activeSelf)
            throw new InvalidOperationException($"Registered segment '{id}' final marker '{Key}' is active before final preparation.");
        var runtimeSteps = new LevelGenStep[steps.Length];
        for (var i = 0; i < steps.Length; i++) runtimeSteps[i] = steps[i].Resolve(template, resolved.transform, id);
        var Callbacks = new Peak.PropSpawner_AmuletStatues[Amulets.Length];
        for (var Index = 0; Index < Callbacks.Length; Index++)
            Callbacks[Index] = Amulets[Index].Resolve(template, biomeId, "Final amulet callback");
        return new FinalGenerationTarget(resolved, Key, runtimeSteps, Callbacks);
    }

    internal void ValidateResolved(
        Transform Template, Transform FeatureRoot, string Id, FinalGenerationTarget Expected)
    {
        var Resolved = marker.Resolve(Template, biomeId, "final-generation marker");
        if (!ReferenceEquals(Expected.Marker, Resolved)
            || !TransformOwnership.Contains(FeatureRoot, Resolved.transform)
            || Expected.SourceStepCount != steps.Length)
            throw new InvalidOperationException($"Registered segment '{Id}' changed a resolved final marker contract.");
        Expected.ValidateContract(Resolved.gameObject, Key);
        for (var Index = 0; Index < steps.Length; Index++)
            steps[Index].ValidateResolved(Template, Resolved.transform, Id, Expected.SourceStepAt(Index));
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(Key); marker.WriteManifest(writer); writer.Write(steps.Length);
        foreach (var step in steps) step.WriteManifest(writer);
        if (Amulets.Length != 0)
        {
            writer.Write("PLL/FinalAmulet/v1"); writer.Write(Amulets.Length);
            foreach (var Amulet in Amulets) Amulet.WriteManifest(writer);
        }
    }

    internal static bool IsAmuletFinalizer(ILevelGenFinalizer Value) =>
        Value is Peak.PropSpawner_AmuletStatues Spawner
        && Spawner.GetType() == typeof(Peak.PropSpawner_AmuletStatues)
        && Spawner.GetComponentInParent<PllAmuletSpawnerAuthoring>(true);

    static void ValidateReserved(Transform root, string id, string key)
    {
        var peakAssembly = typeof(LevelGenStep).Assembly;
        var pllAssembly = typeof(PLLFinalGeneration).Assembly;
        var photonAssembly = typeof(Photon.Pun.PhotonView).Assembly;
        foreach (var value in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (var component in value.GetComponents<Component>())
            {
                if (!component)
                    throw FinalForceAuthoringContract.Invalid(id,
                        $"final root '{key}' contains a missing script at '{value.name}'");
                if (component is ILevelGenFinalizer Finalizer && !IsAmuletFinalizer(Finalizer))
                    throw FinalForceAuthoringContract.Invalid(id,
                        $"final root '{key}' contains an authored ILevelGenFinalizer");
                var assembly = component.GetType().Assembly;
                var assemblyName = assembly.GetName().Name ?? "";
                var supported = assembly == peakAssembly || assembly == photonAssembly
                    || assemblyName.StartsWith("UnityEngine.", StringComparison.Ordinal)
                    || assemblyName.StartsWith("Unity.TextMeshPro", StringComparison.Ordinal)
                    || assembly == pllAssembly && component is PLLFinalGeneration or PLLForceSpawner
                        or PllItemSpawnerAuthoring or PllChildPropAuthoring or PLLNoPropSpawn
                        or PLLHookedProp or PllPropZoneAuthoring or PllAmuletSpawnerAuthoring;
                if (!supported)
                    throw FinalForceAuthoringContract.Invalid(id,
                        $"final root '{key}' contains foreign or unsupported component '{component.GetType().FullName}'");
            }
            if (value.GetComponent<global::Campfire>() || value.GetComponent<global::Luggage>()
                || value.GetComponent<RespawnChest>()
                || value.GetComponent<SegmentPort>() || value.GetComponent<CustomSegmentRuntime>()
                || value.GetComponent<PllBiomePrefabAnchors>() || value.GetComponent<PllBiomeRoot>()
                || value.GetComponent<PllLuggageAuthoring>() || value.GetComponent<PllStatueAuthoring>()
                || value.GetComponent<PllCampfireAuthoring>() || value.GetComponent<PLLNativeResolver>()
                || value.GetComponent<PllEffectZoneAuthoring>()
                || value.GetComponent<PllEffectAuthoring>() || value.GetComponent<PllVisualAuthoring>()
                || value.GetComponent<PllSoundEmitterAuthoring>()
                || value.GetComponent<PllBiomeStingerAuthoring>())
                throw FinalForceAuthoringContract.Invalid(id,
                    $"final root '{key}' overlaps a component with a separate runtime owner");
        }
    }

}

internal sealed class ForceMarkerContract
{
    readonly ComponentBinding<PLLForceSpawner> marker;
    readonly BiomeId biomeId;
    readonly FinalMarkerContract? final;
    ForceSpawnResolution? resolution;

    ForceMarkerContract(
        PLLForceSpawner source,
        ComponentBinding<PLLForceSpawner> marker,
        BiomeId biomeId,
        int feature,
        FinalMarkerContract? final,
        string key,
        string name)
    {
        Source = source;
        this.marker = marker;
        this.biomeId = biomeId;
        Feature = feature;
        this.final = final;
        Key = key;
        Name = name;
    }

    internal PLLForceSpawner Source { get; }
    internal int Feature { get; }
    internal string Key { get; }
    internal string Name { get; }

    internal static ForceMarkerContract Create(
        Transform template,
        Transform featureRoot,
        PLLForceSpawner source,
        int feature,
        FinalMarkerContract? final,
        string id,
        BiomeId biomeId)
    {
        var key = FinalForceAuthoringContract.Key(featureRoot, source);
        if (!ForceObjectNameContract.IsValid(source.ObjectName))
            throw FinalForceAuthoringContract.Invalid(id,
                $"force marker '{key}' ObjectName {ForceObjectNameContract.Requirement}");
        var root = source.transform;
        var components = root.GetComponents<Component>();
        if (!root.gameObject.activeSelf || root.childCount != 0 || components.Length != 2
            || components[0] is not Transform || !ReferenceEquals(components[1], source)
            || root.GetComponents<PLLForceSpawner>().Length != 1)
            throw FinalForceAuthoringContract.Invalid(id,
                $"force marker '{key}' must be an active empty leaf containing only Transform and PLLForceSpawner");
        if (TransformOwnership.HasPropSpawnerInPath(featureRoot, root))
            throw FinalForceAuthoringContract.Invalid(id, $"force marker '{key}' is beneath a PropSpawner");
        ValidateUnitFrame(root, id, key);
        return new ForceMarkerContract(
            source, ComponentBinding<PLLForceSpawner>.Create(template, source),
            biomeId, feature, final, key, source.ObjectName);
    }

    internal void Validate(Transform template, Transform featureRoot, string id)
    {
        marker.Validate(template, biomeId, "force-spawn marker");
        if (!Source || Key != FinalForceAuthoringContract.Key(featureRoot, Source)
            || !string.Equals(Source.ObjectName, Name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Registered segment '{id}' changed a force marker binding.");
        ValidateUnitFrame(Source.transform, id, Key);
    }

    internal void Seal(ForceSpawnResolution value) => resolution = value;
    internal void ClearExternal() => resolution = null;

    internal int FinalIndex(IReadOnlyList<FinalMarkerContract> featureFinals)
    {
        if (final is null) return -1;
        for (var i = 0; i < featureFinals.Count; i++)
            if (ReferenceEquals(featureFinals[i], final)) return i;
        throw new InvalidOperationException("A force marker lost its owning final-generation root.");
    }

    internal ForceSpawnTarget Resolve(
        Transform template,
        Transform featureRoot,
        string id,
        int finalIndex)
    {
        var resolved = marker.Resolve(template, biomeId, "force-spawn marker");
        if (resolution is null)
            throw new InvalidOperationException($"Registered segment '{id}' force marker '{Key}' has no live resolution.");
        if (!TransformOwnership.Contains(featureRoot, resolved.transform)
            || !string.Equals(resolved.ObjectName, Name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Registered segment '{id}' force marker '{Key}' changed in its occurrence.");
        return new ForceSpawnTarget(resolved, Key, finalIndex, resolution);
    }

    internal void ValidateResolved(
        Transform Template, Transform FeatureRoot, string Id,
        int FinalIndexValue, ForceSpawnTarget Expected)
    {
        var Resolved = marker.Resolve(Template, biomeId, "force-spawn marker");
        if (resolution is null)
            throw new InvalidOperationException($"Registered segment '{Id}' force marker '{Key}' has no live resolution.");
        if (!TransformOwnership.Contains(FeatureRoot, Resolved.transform)
            || !string.Equals(Resolved.ObjectName, Name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Registered segment '{Id}' force marker '{Key}' changed in its occurrence.");
        Expected.ValidateContract(Resolved, Key, FinalIndexValue);
    }

    internal void WriteManifest(BinaryWriter writer, int finalIndex)
    {
        writer.Write(Key); marker.WriteManifest(writer); writer.Write(Name); writer.Write(finalIndex);
    }

    internal void WriteExternalFingerprint(BinaryWriter writer)
    {
        writer.Write(Key);
        (resolution ?? throw new InvalidOperationException("A force marker has no live resolution."))
            .WriteFingerprint(writer);
    }

    static void ValidateUnitFrame(Transform value, string id, string key)
    {
        var scale = value.localScale;
        if (Math.Abs(scale.x - 1f) > 0.0001f || Math.Abs(scale.y - 1f) > 0.0001f
            || Math.Abs(scale.z - 1f) > 0.0001f)
            throw FinalForceAuthoringContract.Invalid(id, $"force marker '{key}' must have identity local scale");
        var matrix = value.localToWorldMatrix;
        var x = matrix.MultiplyVector(Vector3.right);
        var y = matrix.MultiplyVector(Vector3.up);
        var z = matrix.MultiplyVector(Vector3.forward);
        var rotation = value.rotation;
        var rotationX = rotation * Vector3.right;
        var rotationY = rotation * Vector3.up;
        var rotationZ = rotation * Vector3.forward;
        if (!Unit(x) || !Unit(y) || !Unit(z)
            || Math.Abs(Vector3.Dot(x.normalized, y.normalized)) > 0.0001f
            || Math.Abs(Vector3.Dot(x.normalized, z.normalized)) > 0.0001f
            || Math.Abs(Vector3.Dot(y.normalized, z.normalized)) > 0.0001f
            || Vector3.Dot(Vector3.Cross(x.normalized, y.normalized), z.normalized) < 0.9999f
            || !Aligned(x, rotationX) || !Aligned(y, rotationY) || !Aligned(z, rotationZ))
            throw FinalForceAuthoringContract.Invalid(id, $"force marker '{key}' inherits scale or shear");
    }

    static bool Aligned(Vector3 actual, Vector3 expected) => Unit(expected)
        && Vector3.Dot(actual.normalized, expected.normalized) >= 0.9999f;

    static bool Unit(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.z)
        && Math.Abs(value.magnitude - 1f) <= 0.0001f;
}

internal sealed class StepBinding
{
    readonly TransformBinding transform;
    readonly int componentIndex;
    readonly int componentCount;
    readonly string type;

    StepBinding(
        LevelGenStep source,
        TransformBinding transform,
        int componentIndex,
        int componentCount,
        string type,
        string key)
    {
        Source = source;
        this.transform = transform;
        this.componentIndex = componentIndex;
        this.componentCount = componentCount;
        this.type = type;
        Key = key;
    }

    internal LevelGenStep Source { get; }
    internal string Key { get; }

    internal static StepBinding Create(Transform template, Transform finalRoot, LevelGenStep source, string id)
    {
        var components = source.GetComponents<Component>();
        var index = Array.IndexOf(components, source);
        if (index < 0) throw new ArgumentException("A final LevelGenStep is detached.", nameof(source));
        var key = FinalForceAuthoringContract.Key(finalRoot, source);
        ValidateSupported(source, id, key);
        return new StepBinding(
            source, TransformBinding.Create(template, source.transform), index, components.Length,
            source.GetType().AssemblyQualifiedName ?? source.GetType().FullName ?? source.GetType().Name,
            key);
    }

    internal void Validate(Transform template, Transform finalRoot, string id)
    {
        if (!Source || Key != FinalForceAuthoringContract.Key(finalRoot, Source))
            throw new InvalidOperationException($"Registered segment '{id}' changed a final step path.");
        transform.Validate(template, id, "final LevelGenStep");
        ValidateComponents(Source.transform, Source, id);
        ValidateSupported(Source, id, Key);
    }

    internal LevelGenStep Resolve(Transform template, Transform finalRoot, string id)
    {
        var owner = transform.ResolveValidated(template, id, "final LevelGenStep");
        var components = owner.GetComponents<Component>();
        if (components.Length != componentCount || componentIndex >= components.Length
            || components[componentIndex] is not LevelGenStep step
            || !string.Equals(step.GetType().AssemblyQualifiedName ?? step.GetType().FullName ?? step.GetType().Name,
                type, StringComparison.Ordinal)
            || Key != FinalForceAuthoringContract.Key(finalRoot, step))
            throw new InvalidOperationException($"Registered segment '{id}' clone changed a final LevelGenStep binding.");
        ValidateSupported(step, id, Key);
        return step;
    }

    internal void ValidateResolved(
        Transform Template, Transform FinalRoot, string Id, LevelGenStep Expected)
    {
        if (!ReferenceEquals(Resolve(Template, FinalRoot, Id), Expected))
            throw new InvalidOperationException($"Registered segment '{Id}' clone replaced a final LevelGenStep.");
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(Key); transform.WriteManifest(writer); writer.Write(componentIndex);
        writer.Write(componentCount); writer.Write(type);
    }

    void ValidateComponents(Transform owner, LevelGenStep expected, string id)
    {
        var components = owner.GetComponents<Component>();
        if (components.Length != componentCount || componentIndex >= components.Length
            || !ReferenceEquals(components[componentIndex], expected)
            || !string.Equals(expected.GetType().AssemblyQualifiedName
                ?? expected.GetType().FullName ?? expected.GetType().Name, type, StringComparison.Ordinal))
            throw new InvalidOperationException($"Registered segment '{id}' changed a final LevelGenStep component order.");
    }

    internal static void ValidateRuntime(LevelGenStep step, string key) =>
        ValidateSupported(step, "runtime occurrence", key);

    static void ValidateSupported(LevelGenStep step, string id, string key)
    {
        var type = step.GetType();
        if (type.IsAbstract || type.Assembly != typeof(LevelGenStep).Assembly)
            throw FinalForceAuthoringContract.Invalid(id,
                $"final step '{key}' is not a concrete installed PEAK LevelGenStep");
        if (step is not PropSpawner spawner) return;
        ValidateManaged(spawner.constraints, id, key, "constraint");
        ValidateManaged(spawner.postConstraints, id, key, "post-constraint");
        ValidateManaged(spawner.postSpawnBehaviors, id, key, "post-spawn behavior");
        ValidateManaged(spawner.modifiers, id, key, "modifier");
        if (spawner is Peak.PropSpawner_AmuletStatues Amulet
            && FinalMarkerContract.IsAmuletFinalizer(Amulet)) return;
        var Candidates = NativePrefabContracts.EffectiveProps(spawner);
        if (Candidates is not { Length: > 0 })
            throw FinalForceAuthoringContract.Invalid(id, $"final PropSpawner '{key}' has no effective prefab candidates");
        foreach (var Candidate in Candidates)
        {
            if (!Candidate)
                throw FinalForceAuthoringContract.Invalid(id, $"final PropSpawner '{key}' has a missing prefab candidate");
            if (Candidate.GetComponentInChildren<PropGrouper>(true)
                || Candidate.GetComponentInChildren<ILevelGenFinalizer>(true) is not null
                || Candidate.GetComponentInChildren<PLLFinalGeneration>(true)
                || Candidate.GetComponentInChildren<PLLForceSpawner>(true)
                || Candidate.GetComponentInChildren<global::Campfire>(true)
                || Candidate.GetComponentInChildren<RespawnChest>(true))
                throw FinalForceAuthoringContract.Invalid(id,
                    $"final PropSpawner '{key}' has a candidate with a finalizer, nested grouper, force marker, or unique fixture role");
            foreach (var Component in Candidate.GetComponentsInChildren<Component>(true))
                if (!Component)
                    throw FinalForceAuthoringContract.Invalid(id,
                        $"final PropSpawner '{key}' has a candidate containing a missing script");
            var ChildSteps = Candidate.GetComponentsInChildren<LevelGenStep>(true);
            if (ChildSteps.Length != 0 && !HasChildExecution(spawner))
                throw FinalForceAuthoringContract.Invalid(id,
                    $"final PropSpawner '{key}' has child steps without one supported native child-execution edge");
        }
    }

    static void ValidateManaged(System.Collections.IEnumerable? values, string id, string key, string kind)
    {
        if (values is null) return;
        foreach (var value in values)
        {
            if (value is null)
                throw FinalForceAuthoringContract.Invalid(id,
                    $"final PropSpawner '{key}' contains a missing {kind}");
            var assembly = value.GetType().Assembly;
            if (assembly == typeof(LevelGenStep).Assembly) continue;
            if (assembly == typeof(PLLFinalGeneration).Assembly
                && value is PllNoPropSpawnConstraint or PllPropZoneConstraint or PllSocketTransformModifier
                    or PllAmuletVolumeConstraint) continue;
            throw FinalForceAuthoringContract.Invalid(id,
                $"final PropSpawner '{key}' contains foreign or unsupported {kind} '{value.GetType().FullName}'");
        }
    }

    static bool HasChildExecution(PropSpawner spawner)
    {
        var count = 0;
        if (spawner.modifiers is not null)
            foreach (var value in spawner.modifiers)
                if (value is PSM_ChildSpawners { mute: false }) count++;
        if (spawner.postSpawnBehaviors is not null)
            foreach (var value in spawner.postSpawnBehaviors)
                if (value is PSB_ChildSpawners { mute: false }) count++;
        return count == 1;
    }
}
