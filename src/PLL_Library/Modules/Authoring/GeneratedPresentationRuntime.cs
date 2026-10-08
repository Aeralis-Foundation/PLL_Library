using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal static class GeneratedPresentationRuntime
{
    internal static ResolvedGeneratedPresentation Resolve(MapSlot Slot, BiomeAuthoringContract Contract)
    {
        var effects = new List<GeneratedEffect>();
        var visuals = new List<GeneratedVisual>();
        var zones = new List<GeneratedZone>();
        var emitters = new List<GeneratedEmitter>();
        var FogZones = new List<ResolvedGeneratedFogZone>();
        var NativeFlows = new List<NativeFlowRuntime>();
        var EffectPresentationRoots = new HashSet<Transform>();
        if (!Slot.Released && Slot.TerrainReady) Add(Slot.Segment.segmentParent, 0);
        if (!Slot.Released && Slot.CampfireReady) Add(Slot.Segment.segmentCampfire, 1);
        effects.Sort(Compare); visuals.Sort(Compare); zones.Sort(Compare); emitters.Sort(Compare);

        var StableEffects = StableEffectCount(Contract.EffectContracts);
        var resolvedEffects = new ResolvedEffect[effects.Count];
        var effectIndices = new Dictionary<(int Contract, int Root), int>();
        for (var i = 0; i < effects.Count; i++)
        {
            var entry = effects[i];
            resolvedEffects[i] = entry.Value;
            effectIndices.Add((entry.Contract, entry.Root.GetInstanceID()), StableEffects + i);
        }

        var resolvedVisuals = new ResolvedVisual[visuals.Count];
        for (var i = 0; i < visuals.Count; i++)
        {
            var entry = visuals[i];
            var effect = entry.Value.EffectIndex;
            if (effect >= 0 && !effectIndices.TryGetValue((effect, entry.Root.GetInstanceID()), out effect))
                throw new InvalidOperationException($"Biome '{Contract.Id}' generated visual lost its same-instance effect binding.");
            resolvedVisuals[i] = entry.Value.WithEffectIndex(effect);
        }

        var resolvedZones = new ResolvedEffectZone[zones.Count];
        var ZoneMemberships = new (EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)[zones.Count];
        for (var i = 0; i < zones.Count; i++)
        {
            var entry = zones[i];
            var indices = new int[entry.Value.EffectIndices.Length];
            for (var j = 0; j < indices.Length; j++)
                if (!effectIndices.TryGetValue((entry.Value.EffectIndices[j], entry.Root.GetInstanceID()), out indices[j]))
                    throw new InvalidOperationException($"Biome '{Contract.Id}' generated effect zone lost a same-instance effect binding.");
            resolvedZones[i] = new ResolvedEffectZone(
                entry.Value.Root, entry.Value.Volume, indices,
                string.Empty, entry.Value.EnvironmentFog, entry.Value.CoverDelaySeconds);
            ZoneMemberships[i] = (Contract.EffectZoneContracts[entry.Contract], resolvedZones[i]);
        }

        var resolvedEmitters = new ResolvedSoundEmitter[emitters.Count];
        for (var i = 0; i < emitters.Count; i++) resolvedEmitters[i] = emitters[i].Value;
        return new ResolvedGeneratedPresentation(resolvedVisuals, resolvedEffects, resolvedZones,
            resolvedEmitters, FogZones.ToArray(), ZoneMemberships, NativeFlows.ToArray());

        void Add(GameObject? featureRoot, int feature)
        {
            if (!featureRoot) return;
            var featureTransform = featureRoot.transform;
            var Inventory = new List<MonoBehaviour>();
            featureRoot.GetComponentsInChildren(true, Inventory);
            var GeneratedCount = 0;
            for (var Index = 0; Index < Inventory.Count; Index++)
            {
                var Component = Inventory[Index];
                if (Component is not PllEffectAuthoring and not PllVisualAuthoring
                    and not PllEffectZoneAuthoring and not PllSoundEmitterAuthoring
                    and not PllFogZoneAuthoring and not PllNativeFlowCondition) continue;
                Inventory[GeneratedCount++] = Component;
            }
            if (GeneratedCount != Inventory.Count)
                Inventory.RemoveRange(GeneratedCount, Inventory.Count - GeneratedCount);

            foreach (var Component in Inventory)
            {
                if (Component is not PllEffectAuthoring Source) continue;
                if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(featureTransform, Source.transform)) continue;
                var match = -1;
                Transform? GeneratedRoot = null;
                for (var i = 0; i < Contract.EffectContracts.Count; i++)
                {
                    var Definition = Contract.EffectContracts[i];
                    if (!Definition.TryMatchGenerated(Source, Contract.Id, out var CandidateRoot)) continue;
                    if (match >= 0)
                        throw new InvalidOperationException($"Biome '{Contract.Id}' generated effect matches more than one sealed source contract.");
                    match = i;
                    GeneratedRoot = CandidateRoot;
                }
                if (match < 0) throw Unsealed(Contract.Id, "effect", Source);
                if (Source.PresentationRoot) EffectPresentationRoots.Add(Source.PresentationRoot.transform);
                if (!ActiveForOccurrence(GeneratedRoot!, Source.transform, null)) continue;
                if (!Contract.EffectContracts[match].TryResolveGenerated(Source, Contract.Id, out var Resolved))
                    throw LostBinding(Contract.Id, "effect", Source);
                effects.Add(new GeneratedEffect(
                    feature, match, Key(featureTransform, Source, match), GeneratedRoot!, Resolved));
            }
            foreach (var Component in Inventory)
            {
                if (Component is not PllVisualAuthoring Source) continue;
                if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(featureTransform, Source.transform)) continue;
                var match = -1;
                Transform? GeneratedRoot = null;
                for (var i = 0; i < Contract.VisualContracts.Count; i++)
                {
                    var Definition = Contract.VisualContracts[i];
                    if (!Definition.TryMatchGenerated(Source, Contract.Id, out var CandidateRoot)) continue;
                    if (match >= 0)
                        throw new InvalidOperationException($"Biome '{Contract.Id}' generated visual matches more than one sealed source contract.");
                    match = i;
                    GeneratedRoot = CandidateRoot;
                }
                if (match < 0) throw Unsealed(Contract.Id, "visual", Source);
                if (!ActiveForOccurrence(
                    GeneratedRoot!, Source.transform,
                    EffectPresentationRoot(GeneratedRoot!, Source.transform, EffectPresentationRoots))) continue;
                if (!Contract.VisualContracts[match].TryResolveGenerated(Source, Contract.Id, out var Resolved))
                    throw LostBinding(Contract.Id, "visual", Source);
                visuals.Add(new GeneratedVisual(
                    feature, match, Key(featureTransform, Source, match), GeneratedRoot!, Resolved));
            }
            foreach (var Component in Inventory)
            {
                if (Component is not PllEffectZoneAuthoring Source) continue;
                if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(featureTransform, Source.transform)) continue;
                var match = -1;
                Transform? GeneratedRoot = null;
                for (var i = 0; i < Contract.EffectZoneContracts.Count; i++)
                {
                    var Definition = Contract.EffectZoneContracts[i];
                    if (!Definition.TryMatchGenerated(Source, Contract.Id, out var CandidateRoot)) continue;
                    if (match >= 0)
                        throw new InvalidOperationException($"Biome '{Contract.Id}' generated effect zone matches more than one sealed source contract.");
                    match = i;
                    GeneratedRoot = CandidateRoot;
                }
                if (match < 0) throw Unsealed(Contract.Id, "effect zone", Source);
                if (!ActiveForOccurrence(GeneratedRoot!, Source.transform, null)) continue;
                var Resolved = Contract.EffectZoneContracts[match].ResolveGenerated(GeneratedRoot!, Contract.Id);
                zones.Add(new GeneratedZone(
                    feature, match, Key(featureTransform, Source, match), GeneratedRoot!, Resolved));
            }
            foreach (var Component in Inventory)
            {
                if (Component is not PllSoundEmitterAuthoring Source) continue;
                if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(featureTransform, Source.transform)) continue;
                var match = -1;
                Transform? GeneratedRoot = null;
                for (var i = 0; i < Contract.SoundEmitterContracts.Count; i++)
                {
                    var Definition = Contract.SoundEmitterContracts[i];
                    if (!Definition.TryMatchGenerated(Source, Contract.Id, out var CandidateRoot)) continue;
                    if (match >= 0)
                        throw new InvalidOperationException($"Biome '{Contract.Id}' generated sound emitter matches more than one sealed source contract.");
                    match = i;
                    GeneratedRoot = CandidateRoot;
                }
                if (match < 0) throw Unsealed(Contract.Id, "sound emitter", Source);
                if (!ActiveForOccurrence(GeneratedRoot!, Source.transform, null)) continue;
                if (!Contract.SoundEmitterContracts[match].TryResolveGenerated(Source, Contract.Id, out var Resolved))
                    throw LostBinding(Contract.Id, "sound emitter", Source);
                emitters.Add(new GeneratedEmitter(
                    feature, match, Key(featureTransform, Source, match), GeneratedRoot!, Resolved));
            }
            foreach (var Component in Inventory)
            {
                if (Component is not PllNativeFlowCondition Bridge) continue;
                if (!Contract.ContainsGeneratedFlows(Bridge.OwnerPrefab))
                    throw Unsealed(Contract.Id, "native flow", Bridge);
                Transform CandidateRoot;
                if (!(Map.CurrentRun!.HookedGeneration.TryGetGeneratedRoot(
                        Bridge.transform, Bridge.OwnerPrefab, out CandidateRoot)
                    || GeneratedPropOutputCapture.TryGetNativeGeneratedRoot(
                        Bridge.transform, Bridge.OwnerPrefab, out CandidateRoot))
                    || CandidateRoot != Bridge.transform)
                    throw LostBinding(Contract.Id, "native flow", Bridge);
                foreach (var Flow in Bridge.Sources) NativeFlows.Add(Flow);
            }
            foreach (var Component in Inventory)
            {
                if (Component is not PllFogZoneAuthoring Source) continue;
                if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(featureTransform, Source.transform)) continue;
                var Matched = false;
                for (var i = 0; i < Contract.FogZoneContracts.Count; i++)
                {
                    var Definition = Contract.FogZoneContracts[i];
                    if (Definition.StableSourceId != Source.StableId || !Definition.Generated) continue;
                    if (!Definition.Owner.TryGetAdmittedGeneratedRoot(Source.transform, out var CandidateRoot))
                        throw LostBinding(Contract.Id, "fog zone", Source);
                    var Resolved = Definition.ResolveGenerated(CandidateRoot, Contract.Id);
                    if (Resolved.Root != Source.gameObject)
                        throw LostBinding(Contract.Id, "fog zone", Source);
                    if (ActiveForOccurrence(CandidateRoot, Source.transform, null))
                        FogZones.Add(new ResolvedGeneratedFogZone(Resolved, i,
                            feature.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"
                            + Key(featureTransform, Source, i)));
                    Matched = true;
                    break;
                }
                if (!Matched) throw Unsealed(Contract.Id, "fog zone", Source);
            }
        }
    }

    static int StableEffectCount(IReadOnlyList<EffectAuthoringContract> Values)
    {
        var result = 0;
        while (result < Values.Count && !Values[result].Generated) result++;
        return result;
    }

    static bool ActiveForOccurrence(Transform generatedRoot, Transform value, Transform? ignored)
    {
        for (var current = value; current && current != generatedRoot; current = current.parent)
            if (current != ignored && !current.gameObject.activeSelf) return false;
        return TransformOwnership.Contains(generatedRoot, value);
    }

    static Transform? EffectPresentationRoot(
        Transform GeneratedRoot,
        Transform Value,
        HashSet<Transform> PresentationRoots)
    {
        for (var Current = Value; Current && Current != GeneratedRoot; Current = Current.parent)
            if (PresentationRoots.Contains(Current)) return Current;
        return null;
    }

    static string Key(Transform featureRoot, Component value, int contract) =>
        contract.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "/"
        + FinalForceAuthoringContract.Key(featureRoot, value);

    static InvalidOperationException Unsealed(BiomeId id, string kind, Component value) => new(
        $"Biome '{id}' generated an unsealed {kind} '{value.name}'.");

    static InvalidOperationException LostBinding(BiomeId id, string kind, Component value) => new(
        $"Biome '{id}' generated {kind} '{value.name}' lost its sealed runtime binding.");

    static int Compare<T>(T left, T right) where T : IGeneratedPresentation
    {
        var feature = left.Feature.CompareTo(right.Feature);
        return feature != 0 ? feature : StringComparer.Ordinal.Compare(left.Key, right.Key);
    }

    interface IGeneratedPresentation
    {
        int Feature { get; }
        string Key { get; }
    }

    readonly struct GeneratedEffect : IGeneratedPresentation
    {
        internal GeneratedEffect(int feature, int contract, string key, Transform root, ResolvedEffect value)
        {
            Feature = feature; Contract = contract; Key = key; Root = root; Value = value;
        }

        public int Feature { get; }
        internal int Contract { get; }
        public string Key { get; }
        internal Transform Root { get; }
        internal ResolvedEffect Value { get; }
    }

    readonly struct GeneratedVisual : IGeneratedPresentation
    {
        internal GeneratedVisual(int feature, int contract, string key, Transform root, ResolvedVisual value)
        {
            Feature = feature; Contract = contract; Key = key; Root = root; Value = value;
        }

        public int Feature { get; }
        internal int Contract { get; }
        public string Key { get; }
        internal Transform Root { get; }
        internal ResolvedVisual Value { get; }
    }

    readonly struct GeneratedZone : IGeneratedPresentation
    {
        internal GeneratedZone(int feature, int contract, string key, Transform root, ResolvedEffectZone value)
        {
            Feature = feature; Contract = contract; Key = key; Root = root; Value = value;
        }

        public int Feature { get; }
        internal int Contract { get; }
        public string Key { get; }
        internal Transform Root { get; }
        internal ResolvedEffectZone Value { get; }
    }

    readonly struct GeneratedEmitter : IGeneratedPresentation
    {
        internal GeneratedEmitter(int feature, int contract, string key, Transform root, ResolvedSoundEmitter value)
        {
            Feature = feature; Contract = contract; Key = key; Root = root; Value = value;
        }

        public int Feature { get; }
        internal int Contract { get; }
        public string Key { get; }
        internal Transform Root { get; }
        internal ResolvedSoundEmitter Value { get; }
    }
}

internal readonly struct ResolvedGeneratedPresentation
{
    internal ResolvedGeneratedPresentation(
        ResolvedVisual[] visuals,
        ResolvedEffect[] effects,
        ResolvedEffectZone[] effectZones,
        ResolvedSoundEmitter[] soundEmitters,
        ResolvedGeneratedFogZone[] FogZones,
        (EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)[] ZoneMemberships,
        NativeFlowRuntime[] NativeFlows)
    {
        Visuals = visuals; Effects = effects; EffectZones = effectZones; SoundEmitters = soundEmitters;
        this.FogZones = FogZones; this.ZoneMemberships = ZoneMemberships;
        this.NativeFlows = NativeFlows;
    }

    internal ResolvedVisual[] Visuals { get; }
    internal ResolvedEffect[] Effects { get; }
    internal ResolvedEffectZone[] EffectZones { get; }
    internal ResolvedSoundEmitter[] SoundEmitters { get; }
    internal ResolvedGeneratedFogZone[] FogZones { get; }
    internal (EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)[] ZoneMemberships { get; }
    internal NativeFlowRuntime[] NativeFlows { get; }
}

internal readonly struct ResolvedGeneratedFogZone
{
    internal ResolvedGeneratedFogZone(ResolvedFogZone Source, int ContractIndex, string StableInstanceIdentity)
    {
        this.Source = Source; this.ContractIndex = ContractIndex;
        this.StableInstanceIdentity = StableInstanceIdentity;
    }
    internal ResolvedFogZone Source { get; }
    internal int ContractIndex { get; }
    internal string StableInstanceIdentity { get; }
}
