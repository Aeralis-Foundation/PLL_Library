using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using UnityEngine;

namespace PLL.Library;

internal static class HookedPropRuntime
{
    static readonly SortedDictionary<int, PLLHookedProp[]> PropsBySlot = new();
    static readonly Dictionary<int, (MapSlot Slot, BiomeAuthoringContract Contract)> Slots = new();
    static readonly IReadOnlyList<PLLHookedProp> Empty = Array.AsReadOnly(Array.Empty<PLLHookedProp>());
    static IReadOnlyList<PLLHookedProp> hookedProps = Empty;
    static IReadOnlyDictionary<string, IReadOnlyList<PLLHookedProp>> propsByHook =
        new ReadOnlyDictionary<string, IReadOnlyList<PLLHookedProp>>(
            new Dictionary<string, IReadOnlyList<PLLHookedProp>>(StringComparer.Ordinal));
    static Queue<Action>? pending;
    static bool notifying;
    static int mainThreadId;

    internal static event Action<PLLHookedProp>? Added;
    internal static event Action<PLLHookedProp>? Removing;

    internal static IReadOnlyList<PLLHookedProp> HookedProps => Volatile.Read(ref hookedProps);

    internal static void Initialize() => mainThreadId = Thread.CurrentThread.ManagedThreadId;

    internal static IReadOnlyList<PLLHookedProp> Get(string hook)
    {
        ValidateHook(hook, nameof(hook));
        return Volatile.Read(ref propsByHook).TryGetValue(hook, out var result) ? result : Empty;
    }

    internal static void Bind(MapSlot[] Slots)
    {
        Mutate(() =>
        {
            var StagedSlots = new Dictionary<int, (MapSlot Slot, BiomeAuthoringContract Contract)>();
            var stagedProps = new SortedDictionary<int, PLLHookedProp[]>();
            foreach (var Slot in Slots)
                if (!Slot.Released && Slot.CustomResolved is not null
                    && BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract))
                    StagedSlots.Add(Slot.Index, (Slot, Contract));
            foreach (var Pair in StagedSlots)
                stagedProps.Add(Pair.Key, Discover(Pair.Value.Slot, Pair.Value.Contract));
            var previous = Snapshot();
            HookedPropRuntime.Slots.Clear();
            PropsBySlot.Clear();
            foreach (var Pair in StagedSlots) HookedPropRuntime.Slots.Add(Pair.Key, Pair.Value);
            foreach (var pair in stagedProps) PropsBySlot.Add(pair.Key, pair.Value);
            ReplacePublished(previous);
        });
    }

    internal static void RefreshSlot(int index)
    {
        if (!Slots.TryGetValue(index, out var entry)) return;
        Mutate(() =>
        {
            var previous = Snapshot();
            if (entry.Slot.Released)
            {
                Slots.Remove(index);
                PropsBySlot.Remove(index);
            }
            else PropsBySlot[index] = Discover(entry.Slot, entry.Contract);
            ReplacePublished(previous);
        });
    }

    internal static void NotifyDestroyed(PLLHookedProp value)
    {
        if (value is null) return;
        Mutate(() =>
        {
            var removed = false;
            var slotIndex = -1;
            PLLHookedProp[]? replacement = null;
            foreach (var pair in PropsBySlot)
            {
                var current = pair.Value;
                var index = IndexOf(current, value);
                if (index < 0) continue;
                replacement = new PLLHookedProp[current.Length - 1];
                if (index != 0) Array.Copy(current, 0, replacement, 0, index);
                if (index != replacement.Length) Array.Copy(current, index + 1, replacement, index, replacement.Length - index);
                slotIndex = pair.Key;
                removed = true;
                break;
            }
            if (!removed) return;
            var previous = Snapshot();
            PropsBySlot[slotIndex] = replacement!;
            ReplacePublished(previous);
        });
    }

    internal static void Clear() => Mutate(ClearCore);

    internal static void RequireMainThread(string Operation)
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            throw new InvalidOperationException(
                Operation + " can run only on Unity's main thread.");
    }

    internal static void ValidateHook(string value, string parameter)
    {
        if (value is null) throw new ArgumentNullException(parameter);
        if (PllAuthoringValueContract.HookFailure(value) is { } Failure)
            throw new ArgumentException(Failure, parameter);
    }

    static PLLHookedProp[] Discover(MapSlot Slot, BiomeAuthoringContract Contract)
    {
        var result = new List<(int Feature, int Contract, string Key, PLLHookedProp Value)>();
        var SeenIds = new HashSet<int>();
        for (var i = 0; i < Contract.HookedProps.Count; i++)
        {
            var Definition = Contract.HookedProps[i];
            if (Definition.Generated) continue;
            var Value = Definition.ResolveStable(Slot.CustomResolved!.Root.Value, Contract.Id);
            if (!ActiveForOccurrence(Slot.CustomResolved.Root.Value, Value.transform) || !SeenIds.Add(Value.GetInstanceID())) continue;
            result.Add((Feature(Slot, Value.transform), i, string.Empty, Value));
        }
        if (Slot.TerrainReady) AddGenerated(Slot.Segment.segmentParent, 0);
        if (Slot.CampfireReady) AddGenerated(Slot.Segment.segmentCampfire, 1);
        result.Sort((left, right) =>
        {
            var feature = left.Feature.CompareTo(right.Feature);
            if (feature != 0) return feature;
            var source = left.Contract.CompareTo(right.Contract);
            return source != 0 ? source : StringComparer.Ordinal.Compare(left.Key, right.Key);
        });
        var values = new PLLHookedProp[result.Count];
        for (var i = 0; i < values.Length; i++) values[i] = result[i].Value;
        return values;

        void AddGenerated(GameObject? featureRoot, int feature)
        {
            if (!featureRoot) return;
            var root = featureRoot.transform;
            foreach (var value in root.GetComponentsInChildren<PLLHookedProp>(true))
            {
                if (!value || SeenIds.Contains(value.GetInstanceID())) continue;
                var matched = -1;
                Transform? generatedRoot = null;
                for (var i = 0; i < Contract.HookedProps.Count; i++)
                {
                    var Definition = Contract.HookedProps[i];
                    if (!Definition.Generated || !Definition.TryResolveGenerated(value, Contract.Id, out var CandidateRoot)) continue;
                    if (matched >= 0)
                        throw new InvalidOperationException($"Biome '{Contract.Id}' generated hooked prop matches more than one sealed source contract.");
                    matched = i;
                    generatedRoot = CandidateRoot;
                }
                if (matched < 0)
                    throw new InvalidOperationException($"Biome '{Contract.Id}' generated an unsealed hooked prop '{value.name}'.");
                if (!ActiveForOccurrence(generatedRoot!, value.transform) || !SeenIds.Add(value.GetInstanceID())) continue;
                result.Add((feature, matched, FinalForceAuthoringContract.Key(root, value), value));
            }
        }
    }

    static bool ActiveForOccurrence(Transform featureRoot, Transform value)
    {
        if (FinalForceAuthoringContract.ActiveBelow(value, featureRoot, featureRoot)) return true;
        return NativePrefabContracts.HasPropSpawnerAncestor(featureRoot, value);
    }

    static int Feature(MapSlot Slot, Transform Value) => Slot.Segment.segmentParent
        && TransformOwnership.Contains(Slot.Segment.segmentParent.transform, Value) ? 0 : 1;

    static PLLHookedProp[] Snapshot()
    {
        var count = 0;
        foreach (var values in PropsBySlot.Values) count += values.Length;
        var result = new PLLHookedProp[count];
        var index = 0;
        foreach (var values in PropsBySlot.Values)
            for (var i = 0; i < values.Length; i++) result[index++] = values[i];
        return result;
    }

    static void ReplacePublished(PLLHookedProp[] previous)
    {
        var next = Snapshot();
        var PreviousIds = new HashSet<int>();
        foreach (var Value in previous) PreviousIds.Add(Value.GetInstanceID());
        var NextIds = new HashSet<int>();
        foreach (var Value in next) NextIds.Add(Value.GetInstanceID());
        var removed = new List<PLLHookedProp>();
        foreach (var value in previous)
            if (!NextIds.Contains(value.GetInstanceID())) removed.Add(value);
        var added = new List<PLLHookedProp>();
        foreach (var value in next)
            if (!PreviousIds.Contains(value.GetInstanceID())) added.Add(value);
        Publish(next);
        foreach (var Value in removed) Invoke(Removing, Value);
        foreach (var Value in added) Invoke(Added, Value);
    }

    static void Publish(PLLHookedProp[] values)
    {
        var all = Array.AsReadOnly(values);
        var builders = new Dictionary<string, List<PLLHookedProp>>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!builders.TryGetValue(value.Hook, out var matches)) builders.Add(value.Hook, matches = new List<PLLHookedProp>());
            matches.Add(value);
        }
        var index = new Dictionary<string, IReadOnlyList<PLLHookedProp>>(builders.Count, StringComparer.Ordinal);
        foreach (var pair in builders) index.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
        Volatile.Write(ref hookedProps, all);
        Volatile.Write(ref propsByHook, new ReadOnlyDictionary<string, IReadOnlyList<PLLHookedProp>>(index));
    }

    static void ClearCore()
    {
        var previous = Snapshot();
        PropsBySlot.Clear();
        Slots.Clear();
        ReplacePublished(previous);
    }

    static void Mutate(Action Mutation)
    {
        RequireMainThread("Hooked prop membership mutation");
        if (notifying)
        {
            (pending ??= new Queue<Action>()).Enqueue(Mutation);
            return;
        }
        try
        {
            Mutation();
            while (pending is { Count: > 0 }) pending.Dequeue()();
        }
        catch
        {
            pending = null;
            throw;
        }
    }

    static void Invoke(Action<PLLHookedProp>? Handlers, PLLHookedProp Value)
    {
        if (Handlers is null) return;
        notifying = true;
        try
        {
            Handlers(Value);
        }
        finally
        {
            notifying = false;
        }
    }

    static int IndexOf(PLLHookedProp[] values, PLLHookedProp target)
    {
        var TargetId = target.GetInstanceID();
        for (var i = 0; i < values.Length; i++)
            if (values[i].GetInstanceID() == TargetId) return i;
        return -1;
    }
}

internal sealed class HookedPropContract
{
    readonly AuthoringBindingOwner owner;
    readonly ComponentBinding<PLLHookedProp> component;
    readonly string hook;

    HookedPropContract(
        AuthoringBindingOwner owner,
        ComponentBinding<PLLHookedProp> component,
        string hook)
    {
        this.owner = owner;
        this.component = component;
        this.hook = hook;
    }

    internal bool Generated => owner.Generated;

    internal static HookedPropContract[] CreateAll(
        Transform template,
        BiomeId id,
        AuthoringCandidateGraph graph)
    {
        var values = new List<(AuthoringBindingOwner Owner, string Key, PLLHookedProp Value)>();
        Add(template.gameObject);
        foreach (var candidate in graph.Ordered) Add(candidate);
        values.Sort((left, right) =>
        {
            var ownerOrder = left.Owner.CandidateIndex.CompareTo(right.Owner.CandidateIndex);
            return ownerOrder != 0 ? ownerOrder : StringComparer.Ordinal.Compare(left.Key, right.Key);
        });
        var result = new HookedPropContract[values.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var entry = values[i];
            ValidateSource(entry.Owner.BindingRoot, entry.Value, id, true);
            result[i] = new HookedPropContract(
                entry.Owner,
                ComponentBinding<PLLHookedProp>.Create(entry.Owner.BindingRoot, entry.Value),
                entry.Value.Hook);
        }
        return result;

        void Add(GameObject graphRoot)
        {
            foreach (var value in graphRoot.GetComponentsInChildren<PLLHookedProp>(true))
            {
                if (!value) throw new ArgumentException($"Biome '{id}' contains a destroyed hooked-prop component.", nameof(template));
                var bindingOwner = AuthoringBindingOwner.Create(template, graph, value.transform, id, "hooked prop");
                if (bindingOwner.SourceRoot.GetInstanceID() != graphRoot.GetInstanceID()) continue;
                values.Add((bindingOwner,
                    DeterministicObjectIdentity.RelativeComponentPath(value, bindingOwner.BindingRoot, 0), value));
            }
        }
    }

    internal PLLHookedProp ResolveStable(Transform runtimeRoot, BiomeId id)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{id}' cannot resolve a generated hooked prop as stable authoring.");
        var resolved = component.Resolve(runtimeRoot, id, "hooked prop");
        ValidateResolved(resolved, id);
        return resolved;
    }

    internal bool TryResolveGenerated(PLLHookedProp Source, BiomeId Id, out Transform GeneratedRoot)
    {
        if (!component.TryMatchGenerated(
                owner, Source, Id, "generated hooked prop", out GeneratedRoot)) return false;
        ValidateResolved(Source, Id);
        return true;
    }

    internal void Validate(Transform template, BiomeId id, AuthoringCandidateGraph graph)
    {
        owner.Validate(template, graph, component.Source.transform, id, "hooked prop");
        component.Validate(owner.BindingRoot, id, "hooked prop");
        if (!string.Equals(component.Source.Hook, hook, StringComparison.Ordinal))
            throw new InvalidOperationException($"Biome '{id}' changed a hooked-prop hook before sealing.");
        ValidateSource(owner.BindingRoot, component.Source, id, false);
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        owner.WriteManifest(writer);
        component.WriteManifest(writer);
        writer.Write(hook);
    }

    void ValidateResolved(PLLHookedProp value, BiomeId id)
    {
        if (!value.enabled || value.GetComponents<PLLHookedProp>().Length != 1
            || !string.Equals(value.Hook, hook, StringComparison.Ordinal))
            throw new InvalidOperationException($"Biome '{id}' changed a hooked prop after sealing.");
        HookedPropRuntime.ValidateHook(value.Hook, nameof(PLLHookedProp.Hook));
    }

    static void ValidateSource(Transform bindingRoot, PLLHookedProp value, BiomeId id, bool argument)
    {
        if (!value.enabled || value.GetComponents<PLLHookedProp>().Length != 1
            || InsideManagedRoot(bindingRoot, value.transform))
            throw Failure(id, value, "is disabled, duplicated, or inside a managed authoring root", argument);
        try
        {
            HookedPropRuntime.ValidateHook(value.Hook, nameof(PLLHookedProp.Hook));
        }
        catch (ArgumentException exception)
        {
            if (!argument) throw new InvalidOperationException($"Biome '{id}' changed a hooked-prop hook before sealing.", exception);
            throw new ArgumentException($"Biome '{id}' hooked prop '{value.name}' has an invalid hook.", "source", exception);
        }
    }

    static bool InsideManagedRoot(Transform owner, Transform value)
    {
        for (var current = value; current; current = current.parent)
        {
            if (current.GetComponent<SegmentPort>() || current.GetComponent<CustomSegmentRuntime>()
                || current.GetComponent<PllBiomePrefabAnchors>() || current.GetComponent<PllBiomeRoot>()
                || current.GetComponent<PllEffectZoneAuthoring>() || current.GetComponent<PLLNativeResolver>()
                || current.GetComponent<PllItemSpawnerAuthoring>() || current.GetComponent<PllStatueAuthoring>()
                || current.GetComponent<PllCampfireAuthoring>() || current.GetComponent<PllLuggageAuthoring>()
                || current.GetComponent<PllBiomeStingerAuthoring>()) return true;
            if (current == owner) break;
        }
        foreach (var effect in owner.GetComponentsInChildren<PllEffectAuthoring>(true))
            if (effect && (effect.Root && TransformOwnership.Contains(effect.Root.transform, value)
                || effect.PresentationRoot && TransformOwnership.Contains(effect.PresentationRoot.transform, value))) return true;
        foreach (var visual in owner.GetComponentsInChildren<PllVisualAuthoring>(true))
            if (visual && visual.Root && TransformOwnership.Contains(visual.Root.transform, value)) return true;
        foreach (var emitter in owner.GetComponentsInChildren<PllSoundEmitterAuthoring>(true))
            if (emitter && emitter.Root && TransformOwnership.Contains(emitter.Root.transform, value)) return true;
        foreach (var stinger in owner.GetComponentsInChildren<PllBiomeStingerAuthoring>(true))
            if (stinger && TransformOwnership.Contains(stinger.transform, value)) return true;
        foreach (var resolver in owner.GetComponentsInChildren<PLLNativeResolver>(true))
            if (resolver && resolver.Root && TransformOwnership.Contains(resolver.Root.transform, value)) return true;
        return false;
    }

    static Exception Failure(BiomeId id, PLLHookedProp source, string reason, bool argument) => argument
        ? new ArgumentException($"Biome '{id}' hooked prop '{source.name}' {reason}.", "source")
        : new InvalidOperationException($"Biome '{id}' hooked prop '{source.name}' {reason}.");
}
