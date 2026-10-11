using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeSpawnerInstances
{
    readonly MapRun Run;
    readonly Dictionary<Component, NativeSpawnerContract> Entries = new();
    readonly Dictionary<GameObject, List<(NativeSpawnerContract Contract, BiomeId Id)>> Candidates = new();

    internal NativeSpawnerInstances(MapRun Run) => this.Run = Run;

    internal void Bind()
    {
        var Spawners = new List<Component>();
        var Occurrences = new HashSet<FeatureOccurrence>();
        var Indexed = new HashSet<PllNativeSpawnerAuthoring>();
        foreach (var Slot in Run.Slots)
        {
            if (Slot.CustomResolved is not { } Resolved || !Occurrences.Add(Slot.Occurrence)) continue;
            if (!BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract))
                throw new InvalidOperationException("Custom occurrence lost its native spawner contract.");
            foreach (var Definition in Contract.NativeSpawnerContracts)
            {
                if (Definition.Generated)
                {
                    if (!Indexed.Add(Definition.Source)) continue;
                    if (!Candidates.TryGetValue(Definition.Owner.SourceRoot, out var Values))
                        Candidates.Add(Definition.Owner.SourceRoot, Values = new());
                    Values.Add((Definition, Contract.Id));
                }
                else Bind(Definition, Definition.ResolveStable(Resolved.Root.Value, Contract.Id), Spawners);
            }
        }
        SpawnTrackerTransaction.PrepareGeneratedSpawnTrackers(Spawners);
    }

    internal void EnrollGenerated(GameObject Prefab, GameObject Instance)
    {
        if (!Candidates.TryGetValue(Prefab, out var Contracts)) return;
        var Spawners = new List<Component>();
        foreach (var (Contract, Id) in Contracts)
        {
            var Native = Contract.ResolveGenerated(Instance.transform, Id);
            if (!Entries.ContainsKey(Native)) Bind(Contract, Native, Spawners);
        }
        SpawnTrackerTransaction.PrepareGeneratedSpawnTrackers(Spawners);
    }

    void Bind(NativeSpawnerContract Contract, Component Native, List<Component> Spawners)
    {
        Entries.Add(Native, Contract);
        Contract.Bind(Native);
        if (Native is Spawner) Spawners.Add(Native);
    }

    internal void Retire(GameObject Root, bool GeneratedOnly = false)
    {
        List<Component>? Removed = null;
        foreach (var Entry in Entries)
        {
            if (GeneratedOnly && !Entry.Value.Generated) continue;
            if (!Entry.Key || TransformOwnership.Contains(Root.transform, Entry.Key.transform))
            {
                Entry.Value.Unbind(Entry.Key!); (Removed ??= new()).Add(Entry.Key!);
            }
        }
        if (Removed is not null) foreach (var Native in Removed) Entries.Remove(Native);
    }

    internal void Clear()
    {
        List<Exception>? Failures = null;
        foreach (var Entry in Entries)
            CleanupOperations.Capture(ref Failures, () => Entry.Value.Unbind(Entry.Key));
        Entries.Clear(); Candidates.Clear();
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Native spawner cleanup failed.", Failures);
    }
}
