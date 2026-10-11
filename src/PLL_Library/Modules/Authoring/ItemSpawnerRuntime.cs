using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class ItemSpawnerRuntime
{
    static readonly Dictionary<int, ItemSpawnerBinding[]> BindingsBySlot = new();
    static readonly Dictionary<int, MapSlot> Slots = new();

    internal static void Bind(MapSlot[] Slots)
    {
        Clear();
        try
        {
            foreach (var Slot in Slots)
            {
                if (Slot.Released || Slot.CustomResolved is null
                    || !BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract)) continue;
                ItemSpawnerRuntime.Slots.Add(Slot.Index, Slot);
                BindingsBySlot.Add(Slot.Index, Resolve(Slot, Contract, Array.Empty<ItemSpawnerBinding>()));
            }
        }
        catch (Exception failure)
        {
            try { Clear(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("Item-spawner binding and rollback both failed.", failure, cleanup);
            }
            throw;
        }
    }

    internal static void RefreshSlot(int index)
    {
        if (!Slots.TryGetValue(index, out var slot)) return;
        if (slot.Released)
        {
            Release(index);
            return;
        }
        if (!BiomeAuthoringCatalog.TryGet(slot.Source.Id, out var contract))
            throw new InvalidOperationException($"Slot {index} lost its registered item-spawner contract.");
        BindingsBySlot.TryGetValue(index, out var previous);
        previous ??= Array.Empty<ItemSpawnerBinding>();
        var replacement = Resolve(slot, contract, previous);
        BindingsBySlot.Remove(index);
        List<Exception>? failures = null;
        foreach (var binding in previous)
            CleanupOperations.Capture(ref failures, () => binding.RestoreUnless(replacement));
        if (failures is { } Failures)
        {
            var CleanupFailureCount = Failures.Count;
            foreach (var binding in replacement)
                try { binding.RestoreUnless(previous); }
                catch (Exception Failure) { Failures.Add(Failure); }
            if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
            throw new AggregateException(
                Failures.Count == CleanupFailureCount
                    ? "Item-spawner refresh cleanup failed."
                    : "Item-spawner refresh cleanup and rollback failed.",
                Failures);
        }
        BindingsBySlot.Add(index, replacement);
    }

    internal static void Clear()
    {
        var Bindings = new List<ItemSpawnerBinding>();
        foreach (var SlotBindings in BindingsBySlot.Values)
            for (var i = SlotBindings.Length - 1; i >= 0; i--) Bindings.Add(SlotBindings[i]);
        BindingsBySlot.Clear();
        Slots.Clear();

        List<Exception>? Failures = null;
        foreach (var Binding in Bindings) CleanupOperations.Capture(ref Failures, Binding.Restore);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Item-spawner cleanup failed.", Failures);
    }

    static void Release(int index)
    {
        Slots.Remove(index);
        if (!BindingsBySlot.TryGetValue(index, out var bindings)) return;
        BindingsBySlot.Remove(index);
        List<Exception>? failures = null;
        for (var i = bindings.Length - 1; i >= 0; i--)
            CleanupOperations.Capture(ref failures, bindings[i].Restore);
        if (failures is { Count: 1 }) ExceptionPropagation.Throw(failures[0]);
        if (failures is { Count: > 1 }) throw new AggregateException("Item-spawner release failed.", failures);
    }

    static ItemSpawnerBinding[] Resolve(
        MapSlot Slot,
        BiomeAuthoringContract contract,
        IReadOnlyList<ItemSpawnerBinding> previous)
    {
        var resolved = new List<ResolvedItemSpawner>();
        var seen = new HashSet<PllItemSpawnerAuthoring>();
        foreach (var definition in contract.ItemSpawners)
        {
            if (definition.Generated) continue;
            var Value = definition.ResolveStable(Slot.CustomResolved!.Root.Value, contract.Id);
            if (!seen.Add(Value.Authoring))
                throw new InvalidOperationException($"Biome '{contract.Id}' resolved one stable item spawner more than once.");
            resolved.Add(Value);
        }
        if (Slot.TerrainReady) AddGenerated(Slot.Segment.segmentParent);
        if (Slot.CampfireReady) AddGenerated(Slot.Segment.segmentCampfire);
        resolved.Sort((left, right) => StringComparer.Ordinal.Compare(left.OrderKey, right.OrderKey));

        var bindings = new ItemSpawnerBinding[resolved.Count];
        var bound = 0;
        var created = new List<ItemSpawnerBinding>();
        try
        {
            for (; bound < bindings.Length; bound++)
            {
                ItemSpawnerBinding? retained = null;
                foreach (var candidate in previous)
                    if (candidate.Matches(resolved[bound]))
                    {
                        retained = candidate;
                        break;
                    }
                if (retained is not null)
                {
                    retained.Validate(resolved[bound]);
                    bindings[bound] = retained;
                    continue;
                }
                bindings[bound] = new ItemSpawnerBinding(resolved[bound]);
                created.Add(bindings[bound]);
                bindings[bound].Bind();
            }
            return bindings;
        }
        catch (Exception failure)
        {
            List<Exception>? cleanup = null;
            for (var i = created.Count - 1; i >= 0; i--)
                try { created[i].Restore(); }
                catch (Exception exception)
                {
                    (cleanup ??= new List<Exception>()).Add(exception);
                }
            if (cleanup is not null)
            {
                cleanup.Insert(0, failure);
                throw new AggregateException("Item-spawner resolution and rollback both failed.", cleanup);
            }
            throw;
        }

        void AddGenerated(GameObject? featureRoot)
        {
            if (!featureRoot) return;
            foreach (var source in featureRoot.GetComponentsInChildren<PllItemSpawnerAuthoring>(true))
            {
                if (!source || seen.Contains(source)) continue;
                ResolvedItemSpawner match = default;
                var matched = false;
                foreach (var definition in contract.ItemSpawners)
                {
                    if (!definition.Generated || !definition.TryResolveGenerated(source, contract.Id, out var candidate)) continue;
                    if (matched)
                        throw new InvalidOperationException($"Biome '{contract.Id}' generated item spawner matches more than one sealed source contract.");
                    match = candidate;
                    matched = true;
                }
                if (!matched)
                    throw new InvalidOperationException($"Biome '{contract.Id}' generated an unsealed item spawner '{source.name}'.");
                seen.Add(source);
                var Feature = ReferenceEquals(featureRoot, Slot.Segment.segmentParent) ? "0/" : "1/";
                resolved.Add(match.WithOrderPrefix(
                    Feature + FinalForceAuthoringContract.Key(featureRoot.transform, source) + "/"));
            }
        }
    }

}

internal sealed class ItemSpawnerBinding
{
    readonly Spawner spawner;
    readonly Peak.SpawnedItemTracker tracker;
    readonly GameObject item;
    readonly List<Peak.SpawnedItemTracker.SpawnRecord>? initialSavedHistory;
    readonly List<PhotonView>? initialSpawnedItems;
    readonly Hash128 initialSpawnerId;
    readonly bool initialHasSpawnHistory;

    internal ItemSpawnerBinding(ResolvedItemSpawner resolved)
    {
        spawner = resolved.Spawner;
        tracker = resolved.Tracker;
        item = resolved.Item;
        if (!spawner || !resolved.Tracker || !item || spawner.spawnedObjectPrefab)
            throw new InvalidOperationException("An item spawner was not in its sealed unbound state.");
        initialSavedHistory = tracker._historyFromSave;
        initialSpawnedItems = tracker._spawnedItems;
        initialSpawnerId = tracker.SpawnerId;
        initialHasSpawnHistory = tracker.HasSpawnHistory;
    }

    internal void Bind()
    {
        if (!spawner || !tracker || !item || spawner.spawnedObjectPrefab)
            throw new InvalidOperationException("An item spawner was not in its sealed unbound state.");
        spawner.spawnedObjectPrefab = item;
        SpawnTrackerTransaction.PrepareGeneratedSpawnTracker(spawner.gameObject);
    }

    internal bool Matches(ResolvedItemSpawner resolved) => ReferenceEquals(spawner, resolved.Spawner);

    internal void Validate(ResolvedItemSpawner resolved)
    {
        if (!ReferenceEquals(spawner, resolved.Spawner) || item != resolved.Item
            || !ReferenceEquals(tracker, resolved.Tracker) || !spawner || !tracker
            || spawner.spawnedObjectPrefab != item)
            throw new InvalidOperationException("An existing item-spawner binding changed before refresh.");
    }

    internal void RestoreUnless(IReadOnlyList<ItemSpawnerBinding> replacements)
    {
        foreach (var replacement in replacements)
            if (ReferenceEquals(replacement.spawner, spawner)) return;
        Restore();
    }

    internal void Restore()
    {
        List<Exception>? failures = null;
        if (spawner)
        {
            var current = spawner.spawnedObjectPrefab;
            if (current is not null && !ReferenceEquals(current, item))
                (failures ??= new List<Exception>()).Add(
                    new InvalidOperationException($"Item spawner '{spawner.name}' changed its live ItemDatabase binding."));
        }
        CleanupOperations.Capture(ref failures, () =>
        {
            if (tracker) SpawnTrackerTransaction.RemoveSpawnTracker(tracker);
        });
        CleanupOperations.Capture(ref failures, () =>
        {
            if (tracker)
            {
                tracker._historyFromSave = initialSavedHistory;
                tracker._spawnedItems = initialSpawnedItems;
                tracker.SpawnerId = initialSpawnerId;
                tracker.HasSpawnHistory = initialHasSpawnHistory;
            }
        });
        CleanupOperations.Capture(ref failures, () =>
        {
            if (spawner && ReferenceEquals(spawner.spawnedObjectPrefab, item)) spawner.spawnedObjectPrefab = null;
        });
        if (failures is { Count: 1 }) ExceptionPropagation.Throw(failures[0]);
        if (failures is { Count: > 1 }) throw new AggregateException("Item-spawner restoration failed.", failures);
    }
}
