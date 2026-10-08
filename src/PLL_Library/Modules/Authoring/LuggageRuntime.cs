using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class LuggageRuntime
{
    static readonly Dictionary<global::Luggage, ResolvedLuggageItem[]> ItemCatalogs = new();
    static readonly Dictionary<int, LuggageState[]> StatesBySlot = new();
    static readonly Dictionary<int, RuntimeBiomeAuthoring> GeneratedBiomesBySlot = new();
    static readonly Dictionary<int, List<LuggageState>> GeneratedStatesBySlot = new();
    static readonly Dictionary<FeatureOccurrence, NativeReplacementOwner> NativeReplacementOwners = new();
    static MapSlot[] RunSlots = Array.Empty<MapSlot>();

    internal static void Bind(
        IReadOnlyDictionary<int, RuntimeBiomeAuthoring> Biomes,
        MapSlot[] Slots)
    {
        Clear();
        var HasDefinitions = false;
        foreach (var Biome in Biomes.Values)
            HasDefinitions |= Biome.Resolved.Luggage.Length != 0
                || Biome.Resolved.Statue is not null || Biome.Contract.HasGeneratedLuggage;
        if (!HasDefinitions) return;
        RunSlots = Slots ?? throw new ArgumentNullException(nameof(Slots));
        try
        {
            foreach (var Biome in Biomes.Values)
            {
                if (Biome.Resolved.Statue is { } Statue)
                {
                    var Items = new ResolvedLuggageItem[Statue.Items.Length];
                    for (var i = 0; i < Items.Length; i++)
                    {
                        if (!Statue.Items[i])
                            throw new InvalidOperationException(
                                $"Biome '{Biome.Id}' custom statue lost a sealed PEAK ItemDatabase entry.");
                        Items[i] = new ResolvedLuggageItem(Statue.Items[i], 1);
                    }
                    ItemCatalogs.Add(Statue.Chest, Items);
                }
                if (Biome.Resolved.Luggage.Length != 0)
                {
                    var ByAuthoring = new Dictionary<PllLuggageAuthoring, LuggageState>();
                    var Declared = new LuggageState[Biome.Resolved.Luggage.Length];
                    for (var i = 0; i < Declared.Length; i++)
                    {
                        var State = new LuggageState(Biome, Biome.Resolved.Luggage[i]);
                        Declared[i] = State;
                        ByAuthoring.Add(State.Resolved.Authoring, State);
                    }

                    StatesBySlot.Add(Biome.Slot.Index, Declared);
                    foreach (var State in Declared) State.Configure();
                    foreach (var State in Declared)
                    {
                        if (!State.Selected
                            || State.Resolved.ReplacesAuthored is not { } Authored) continue;
                        if (!ByAuthoring.TryGetValue(Authored, out var Target))
                            throw new InvalidOperationException(
                                $"Biome '{Biome.Id}' lost a sealed authored luggage replacement target.");
                        Target.Disable();
                    }
                }
                if (Biome.Contract.HasGeneratedLuggage)
                {
                    GeneratedBiomesBySlot.Add(Biome.Slot.Index, Biome);
                    GeneratedStatesBySlot.Add(Biome.Slot.Index, new List<LuggageState>());
                }
                RefreshSlot(Biome.Slot.Index);
            }
        }
        catch (Exception Failure)
        {
            try { Clear(); }
            catch (Exception Cleanup)
            {
                throw new AggregateException(
                    "Luggage binding and rollback both failed.", Failure, Cleanup);
            }
            throw;
        }
    }

    internal static void RefreshSlot(int Index)
    {
        if (StatesBySlot.TryGetValue(Index, out var States))
        {
            var Slot = States[0].Biome.Slot;
            if (Slot.Released) RestoreNativeReplacements(Slot);
            else
            {
                var Requested = false;
                foreach (var State in States)
                    if (State.Selected
                        && State.Resolved.NativeReplacementFingerprint
                            is not null)
                    {
                        Requested = true;
                        break;
                    }
                if (Requested)
                    foreach (var Candidate in EnumerateLuggage(Slot))
                    {
                        if (!Candidate
                            || NativeLuggageCatalog.HasAuthoring(Candidate)
                            || !MatchesRequested(Candidate)
                            || !TryResolveOwningSlot(
                                Candidate, RunSlots, out var Owner)
                            || !ReferenceEquals(Owner, Slot)) continue;
                        NativeReplacementOwnerFor(Slot.Occurrence)
                            .Disable(Slot, Candidate);
                    }

                bool MatchesRequested(global::Luggage Candidate)
                {
                    foreach (var State in States)
                        if (State.Selected
                            && State.Resolved.NativeReplacementFingerprint
                                is { } Fingerprint
                            && NativeLuggageCatalog.Matches(
                                Candidate, Fingerprint)) return true;
                    return false;
                }
            }
        }
        if (!GeneratedBiomesBySlot.TryGetValue(Index, out var Biome)
            || !GeneratedStatesBySlot.TryGetValue(Index, out var Generated)) return;
        RefreshGenerated(Biome, Generated);
    }

    static NativeReplacementOwner NativeReplacementOwnerFor(
        FeatureOccurrence Occurrence)
    {
        if (!NativeReplacementOwners.TryGetValue(
                Occurrence, out var Owner))
            NativeReplacementOwners.Add(
                Occurrence, Owner = new NativeReplacementOwner(Occurrence));
        return Owner;
    }

    static void RestoreNativeReplacements(MapSlot Slot)
    {
        if (!NativeReplacementOwners.TryGetValue(
                Slot.Occurrence, out var Owner)) return;
        Owner.RestoreSlot(Slot.Index);
        if (Owner.Empty) NativeReplacementOwners.Remove(Slot.Occurrence);
    }

    static void RefreshGenerated(
        RuntimeBiomeAuthoring Biome,
        List<LuggageState> States)
    {
        var Live = new List<PllLuggageAuthoring>();
        var Seen = new HashSet<PllLuggageAuthoring>();
        if (!Biome.Slot.Released)
        {
            if (Biome.Slot.TerrainReady) Add(Biome.Slot.Segment.segmentParent);
            if (Biome.Slot.CampfireReady) Add(Biome.Slot.Segment.segmentCampfire);
        }
        List<Exception>? CleanupFailures = null;
        for (var i = States.Count - 1; i >= 0; i--)
        {
            var State = States[i];
            if (State.Resolved.Authoring) continue;
            States.RemoveAt(i);
            AuthoringCleanup.Capture(ref CleanupFailures, State.Restore);
        }
        if (CleanupFailures is { Count: 1 }) ExceptionPropagation.Throw(CleanupFailures[0]);
        if (CleanupFailures is { Count: > 1 })
            throw new AggregateException("Generated luggage cleanup failed.", CleanupFailures);
        foreach (var State in States)
        {
            if (Seen.Contains(State.Resolved.Authoring)) State.Resume();
            else State.Suspend();
        }
        foreach (var Authoring in Live)
        {
            var Known = false;
            foreach (var State in States)
                if (ReferenceEquals(State.Resolved.Authoring, Authoring))
                {
                    Known = true;
                    break;
                }
            if (Known) continue;
            if (!Biome.Contract.TryResolveGeneratedLuggage(Authoring, out var Resolved))
                throw new InvalidOperationException(
                    $"Biome '{Biome.Id}' generated an unsealed luggage type '{Authoring.Id}'.");
            var Added = new LuggageState(Biome, Resolved);
            try { Added.Configure(); }
            catch (Exception Failure)
            {
                try { Added.Restore(); }
                catch (Exception Cleanup)
                {
                    throw new AggregateException(
                        $"Biome '{Biome.Id}' generated luggage binding and rollback both failed.",
                        Failure, Cleanup);
                }
                throw;
            }
            States.Add(Added);
        }

        void Add(GameObject? Root)
        {
            if (!Root) return;
            foreach (var Value in Root.GetComponentsInChildren<PllLuggageAuthoring>(true))
                if (Value && NativeCollision.IsGeneratedOutput(Root.transform, Value.transform)
                    && Seen.Add(Value)) Live.Add(Value);
        }
    }

    internal static bool TrySelectItems(
        global::Luggage Luggage,
        int Count,
        bool CanRepeat,
        out List<GameObject> Selected)
    {
        Selected = null!;
        if (!ItemCatalogs.TryGetValue(Luggage, out var Catalog)) return false;
        Selected = new List<GameObject>(Math.Max(0, Count));
        if (Count <= 0 || Catalog.Length == 0) return true;
        var Eligible = new List<int>(Catalog.Length);
        var TotalWeight = 0;
        for (var i = 0; i < Catalog.Length; i++)
            if (Catalog[i].Item
                && Catalog[i].Item.TryGetComponent<Item>(out var Item)
                && Item.IsValidToSpawn())
            {
                Eligible.Add(i);
                TotalWeight = checked(TotalWeight + Catalog[i].Weight);
            }
        if (Eligible.Count == 0) return true;
        if (CanRepeat)
        {
            for (var i = 0; i < Count; i++)
                Selected.Add(Catalog[Eligible[Pick(Eligible, Catalog, TotalWeight)]].Item);
            return true;
        }

        var Pool = new List<int>(Eligible);
        var PoolWeight = TotalWeight;
        while (Selected.Count < Count)
        {
            if (Pool.Count == 0)
            {
                Pool.AddRange(Eligible);
                PoolWeight = TotalWeight;
            }
            var PoolIndex = Pick(Pool, Catalog, PoolWeight);
            var ItemIndex = Pool[PoolIndex];
            Selected.Add(Catalog[ItemIndex].Item);
            PoolWeight -= Catalog[ItemIndex].Weight;
            Pool.RemoveAt(PoolIndex);
        }
        return true;
    }

    static int Pick(
        IReadOnlyList<int> Pool,
        IReadOnlyList<ResolvedLuggageItem> Catalog,
        int TotalWeight)
    {
        var Roll = UnityEngine.Random.Range(0, TotalWeight);
        for (var i = 0; i < Pool.Count; i++)
        {
            var Weight = Catalog[Pool[i]].Weight;
            if (Roll < Weight) return i;
            Roll -= Weight;
        }
        throw new InvalidOperationException("Weighted luggage selection exceeded its sealed weight range.");
    }

    internal static Spawner.HeightBasedSpawnListEntry SelectHeightPool(
        IReadOnlyList<Spawner.HeightBasedSpawnListEntry> Pools,
        Biome.BiomeType NativeBiome)
    {
        Spawner.HeightBasedSpawnListEntry? Selected = null;
        for (var i = 0; i < Pools.Count; i++)
        {
            var Pool = Pools[i] ?? throw new InvalidOperationException(
                "A height-based luggage pool entry is null.");
            if (Pool.hasBiomeRequirement
                    ? Pool.biomeRequirement != NativeBiome
                    : NativeBiome != Biome.BiomeType.Shore) continue;
            if (Selected is not null)
                throw new InvalidOperationException(
                    $"Height-based luggage has multiple spawn pools for biome '{NativeBiome}'.");
            Selected = Pool;
        }
        return Selected ?? throw new InvalidOperationException(
            $"Height-based luggage has no spawn pool for biome '{NativeBiome}'.");
    }

    static bool TryResolveOwningSlot(
        global::Luggage Luggage,
        IReadOnlyList<MapSlot> Slots,
        out MapSlot Owner)
    {
        Owner = null!;
        var Deepest = -1;
        for (var i = 0; i < Slots.Count; i++)
        {
            var Slot = Slots[i];
            if (Slot.Released) continue;
            var Depth = OwnershipDepth(Slot, Luggage.transform);
            if (Depth < 0 || Depth < Deepest) continue;
            if (Depth == Deepest)
                throw new InvalidOperationException(
                    $"Height-based luggage '{Luggage.name}' has equal-depth ownership in map slots "
                    + $"{Owner.Index + 1} and {Slot.Index + 1}.");
            Owner = Slot;
            Deepest = Depth;
        }
        return Deepest >= 0;
    }

    static int OwnershipDepth(MapSlot Slot, Transform Value)
    {
        var Deepest = -1;
        var Roots = new SlotRootSet(Slot);
        for (var i = 0; i < Roots.Count; i++)
        {
            var Root = Roots[i];
            if (!Root || !TransformOwnership.Contains(Root, Value)) continue;
            var Depth = 0;
            for (var Current = Root; Current; Current = Current.parent) Depth++;
            if (Depth > Deepest) Deepest = Depth;
        }
        return Deepest;
    }

    readonly struct SlotRootSet
    {
        readonly Transform? SegmentParent;
        readonly Transform? SegmentCampfire;
        readonly Transform? WallNext;
        readonly Transform? WallPrevious;
        readonly Transform? PeakAttachment;

        internal SlotRootSet(MapSlot Slot)
        {
            SegmentParent = TransformOf(Slot.Segment._segmentParent);
            SegmentCampfire = TransformOf(Slot.Segment._segmentCampfire);
            WallNext = TransformOf(Slot.Segment.wallNext);
            WallPrevious = TransformOf(Slot.Segment.wallPrevious);
            PeakAttachment = Slot.PeakAttachment;
        }

        static Transform? TransformOf(GameObject? Value) =>
            Value ? Value.transform : null;

        internal int Count => 5;

        internal Transform? this[int Index] => Index switch
        {
            0 => SegmentParent,
            1 => SegmentCampfire,
            2 => WallNext,
            3 => WallPrevious,
            4 => PeakAttachment,
            _ => throw new ArgumentOutOfRangeException(nameof(Index))
        };
    }
    internal static void Clear()
    {
        var States = new List<LuggageState>();
        var NativeOwners = new List<NativeReplacementOwner>(
            NativeReplacementOwners.Values);
        foreach (var SlotStates in StatesBySlot.Values)
            for (var i = SlotStates.Length - 1; i >= 0; i--) States.Add(SlotStates[i]);
        foreach (var SlotStates in GeneratedStatesBySlot.Values)
            for (var i = SlotStates.Count - 1; i >= 0; i--) States.Add(SlotStates[i]);
        StatesBySlot.Clear();
        GeneratedBiomesBySlot.Clear();
        GeneratedStatesBySlot.Clear();
        NativeReplacementOwners.Clear();
        RunSlots = Array.Empty<MapSlot>();
        ItemCatalogs.Clear();

        List<Exception>? Failures = null;
        foreach (var Owner in NativeOwners)
            AuthoringCleanup.Capture(ref Failures, Owner.Restore);
        foreach (var State in States) AuthoringCleanup.Capture(ref Failures, State.Restore);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Luggage cleanup failed.", Failures);
    }

    static int ListCount(global::Luggage Luggage)
    {
        var Count = 0;
        foreach (var Value in global::Luggage.ALL_LUGGAGE)
            if (Value == Luggage) Count++;
        return Count;
    }

    static void SetListCount(global::Luggage Luggage, int Count)
    {
        for (var i = global::Luggage.ALL_LUGGAGE.Count - 1; i >= 0; i--)
            if (global::Luggage.ALL_LUGGAGE[i] == Luggage)
                global::Luggage.ALL_LUGGAGE.RemoveAt(i);
        for (var i = 0; i < Count; i++) global::Luggage.ALL_LUGGAGE.Add(Luggage);
    }

    static global::Luggage[] EnumerateLuggage(MapSlot Slot)
    {
        var Result = new List<global::Luggage>();
        var Seen = new HashSet<int>();
        var Roots = new SlotRootSet(Slot);
        for (var i = 0; i < Roots.Count; i++)
        {
            var Root = Roots[i];
            if (!Root) continue;
            foreach (var Luggage in Root.GetComponentsInChildren<global::Luggage>(true))
                if (Luggage && Seen.Add(Luggage.GetInstanceID())) Result.Add(Luggage);
        }
        return Result.ToArray();
    }
    sealed class NativeReplacementOwner
    {
        readonly FeatureOccurrence Occurrence;
        readonly List<DisabledNativeState> States = new();
        readonly Dictionary<int, DisabledNativeState> StatesByInstanceId = new();

        internal NativeReplacementOwner(FeatureOccurrence Occurrence)
        {
            this.Occurrence = Occurrence;
        }

        internal bool Empty => States.Count == 0;

        internal void Disable(MapSlot Slot, global::Luggage Luggage)
        {
            if (!ReferenceEquals(Slot.Occurrence, Occurrence))
                throw new InvalidOperationException(
                    "Native luggage replacement received a foreign occurrence slot.");
            var InstanceId = Luggage.GetInstanceID();
            if (StatesByInstanceId.TryGetValue(InstanceId, out var Existing))
            {
                if (!ReferenceEquals(Existing.Luggage, Luggage)
                    || Existing.SlotIndex != Slot.Index)
                    throw new InvalidOperationException(
                        "Native luggage replacement has conflicting slot ownership.");
                return;
            }
            var Root = Luggage.gameObject;
            var State = new DisabledNativeState(
                InstanceId, Slot.Index, Root, Root.activeSelf,
                Luggage, ListCount(Luggage));
            States.Add(State);
            StatesByInstanceId.Add(InstanceId, State);
            SetListCount(Luggage, 0);
            Root.SetActive(false);
        }

        internal void RestoreSlot(int SlotIndex) =>
            Restore(State => State.SlotIndex == SlotIndex);

        internal void Restore() => Restore(static State => true);

        void Restore(Func<DisabledNativeState, bool> Selected)
        {
            List<Exception>? Failures = null;
            for (var i = States.Count - 1; i >= 0; i--)
            {
                var State = States[i];
                if (!Selected(State)) continue;
                try
                {
                    State.Restore();
                    States.RemoveAt(i);
                    StatesByInstanceId.Remove(State.InstanceId);
                }
                catch (Exception Failure)
                {
                    (Failures ??= new List<Exception>()).Add(Failure);
                }
            }
            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is { Count: > 1 })
                throw new AggregateException(
                    "Native luggage replacement restoration failed.", Failures);
        }
    }

    sealed class DisabledNativeState
    {
        internal DisabledNativeState(
            int InstanceId, int SlotIndex, GameObject Root,
            bool Active, global::Luggage Luggage, int ListCount)
        {
            this.InstanceId = InstanceId;
            this.SlotIndex = SlotIndex;
            this.Root = Root;
            this.Active = Active;
            this.Luggage = Luggage;
            this.ListCount = ListCount;
        }

        internal int InstanceId { get; }
        internal int SlotIndex { get; }
        internal GameObject Root { get; }
        internal bool Active { get; }
        internal global::Luggage Luggage { get; }
        internal int ListCount { get; }

        internal void Restore()
        {
            if (!Root) return;
            Root.SetActive(Active);
            if (Luggage) SetListCount(Luggage, ListCount);
        }
    }

    sealed class LuggageState
    {
        readonly bool InitialActive;
        readonly int InitialListCount;
        readonly ContentState InitialContents;
        ResolvedLuggageItem[]? ItemCatalog;
        bool ListManaged;

        internal LuggageState(
            RuntimeBiomeAuthoring Biome,
            ResolvedLuggage Resolved)
        {
            this.Biome = Biome;
            this.Resolved = Resolved;
            InitialActive = Resolved.Root.activeSelf;
            InitialListCount = ListCount(Resolved.Luggage);
            InitialContents = new ContentState(Resolved.Luggage);
        }

        internal RuntimeBiomeAuthoring Biome { get; }
        internal ResolvedLuggage Resolved { get; }
        internal bool Selected { get; private set; }

        internal void Configure()
        {
            Selected = Resolved.Generated
                || Select(Resolved.SpawnChance);
            if (!Selected)
            {
                if (ListCount(Resolved.Luggage) != 0)
                {
                    SetListCount(Resolved.Luggage, 0);
                    ListManaged = true;
                }
                Resolved.Root.SetActive(false);
                return;
            }

            Resolved.Contents?.ApplyTo(Resolved.Luggage);
            if (Resolved.CustomItems.Length != 0)
            {
                foreach (var Item in Resolved.CustomItems)
                    if (!Item.Item)
                        throw new InvalidOperationException(
                            $"Biome '{Biome.Id}' luggage '{Resolved.Authoring.Id}' lost a sealed PEAK ItemDatabase entry.");
                ItemCatalog = Resolved.CustomItems;
                Resume();
            }
            var Before = ListCount(Resolved.Luggage);
            Resolved.Root.SetActive(true);
            ListManaged |= ListCount(Resolved.Luggage) != Before;
        }

        internal void Disable()
        {
            Suspend();
            if (ListCount(Resolved.Luggage) != 0)
            {
                SetListCount(Resolved.Luggage, 0);
                ListManaged = true;
            }
            if (Resolved.Root) Resolved.Root.SetActive(false);
        }


        internal void Restore()
        {
            List<Exception>? Failures = null;
            AuthoringCleanup.Capture(ref Failures, Suspend);
            if (Resolved.Luggage)
                AuthoringCleanup.Capture(
                    ref Failures, () => InitialContents.Restore(Resolved.Luggage));
            if (Resolved.Root)
                AuthoringCleanup.Capture(
                    ref Failures, () => Resolved.Root.SetActive(InitialActive));
            if (ListManaged && Resolved.Luggage)
                AuthoringCleanup.Capture(
                    ref Failures,
                    () => SetListCount(Resolved.Luggage, InitialListCount));
            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is { Count: > 1 })
                throw new AggregateException("Luggage restoration failed.", Failures);
        }

        internal void Resume()
        {
            if (ItemCatalog is null || !Resolved.Luggage) return;
            if (ItemCatalogs.TryGetValue(Resolved.Luggage, out var Current))
            {
                if (!ReferenceEquals(Current, ItemCatalog))
                    throw new InvalidOperationException(
                        $"Biome '{Biome.Id}' luggage '{Resolved.Authoring.Id}' has a conflicting item catalog.");
                return;
            }
            ItemCatalogs.Add(Resolved.Luggage, ItemCatalog);
        }

        internal void Suspend() => ItemCatalogs.Remove(Resolved.Luggage);

        bool Select(float Chance)
        {
            if (Chance <= 0f) return false;
            if (Chance >= 1f) return true;
            var Seed = FeatureGeneration.FeatureSeed(
                FeatureGeneration.MapSeed(), Biome.Slot.Index,
                $"{Biome.Id}:luggage:{Resolved.Authoring.Id}");
            return (uint)Seed / 4294967296d < Chance;
        }
    }

    readonly struct ContentState
    {
        readonly Spawner.SpawnMode SpawnMode;
        readonly GameObject SpawnedObjectPrefab;
        readonly SpawnList Spawns;
        readonly SpawnPool SpawnPool;
        readonly List<Spawner.HeightBasedSpawnListEntry> HeightBasedSpawnPools;
        readonly GameObject FallbackSpawn;

        internal ContentState(global::Luggage Value)
        {
            SpawnMode = Value.spawnMode;
            SpawnedObjectPrefab = Value.spawnedObjectPrefab;
            Spawns = Value.spawns;
            SpawnPool = Value.spawnPool;
            HeightBasedSpawnPools = Value.heightBasedSpawnPools;
            FallbackSpawn = Value.fallbackSpawn;
        }

        internal void Restore(global::Luggage Value)
        {
            Value.spawnMode = SpawnMode;
            Value.spawnedObjectPrefab = SpawnedObjectPrefab;
            Value.spawns = Spawns;
            Value.spawnPool = SpawnPool;
            Value.heightBasedSpawnPools = HeightBasedSpawnPools;
            Value.fallbackSpawn = FallbackSpawn;
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
    static class ItemSelectionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            Spawner __instance,
            int __0,
            bool __1,
            ref List<GameObject> __result)
        {
            if (__instance is not global::Luggage Luggage) return true;
            if (TrySelectItems(Luggage, __0, __1, out var Selected))
            {
                __result = Selected;
                return false;
            }
            if (Luggage.spawnMode != Spawner.SpawnMode.HeightBasedSpawnPools
                || !Map.TryGetPublishedRun(out var Run)
                || !TryResolveOwningSlot(Luggage, Run.Slots, out var Slot)) return true;
            var Pools = Luggage.heightBasedSpawnPools
                ?? throw new InvalidOperationException(
                    "Height-based luggage has no spawn-pool table.");
            if (LayoutSegmentBehavior.Resolve(Slot.Source) is not { } Compatibility)
                throw new InvalidOperationException(
                    $"Authored biome '{Slot.Source.BiomeId}' contains unmanaged height-based luggage.");
            var Pool = SelectHeightPool(Pools, Compatibility.Biome);
            __result = LootData.GetRandomItems(
                Pool.spawnPool, __0, __1, Luggage.fallbackSpawn);
            return false;
        }
    }
}
