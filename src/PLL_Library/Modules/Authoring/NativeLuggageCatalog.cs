using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeLuggageCatalog
{
    static NativeLuggageCatalog? Published;

    internal static NativeLuggageCatalog Empty { get; } = CreateEmpty();

    readonly Dictionary<string, List<NativeLuggageCandidate>> Tokens = new(StringComparer.Ordinal);
    readonly Dictionary<int, NativeLuggageCandidate> CandidatesBySourceId = new();
    readonly List<NativeLuggageCandidate> Candidates = new();
    bool Sealed;

    internal bool IsSealed => Sealed;
    internal static NativeLuggageCatalog Runtime => Published ?? Empty;

    internal static void Publish(NativeLuggageCatalog? Catalog)
    {
        if (Catalog is not null && !Catalog.IsSealed)
            throw new InvalidOperationException("The native luggage catalog must be sealed before publication.");
        Published = Catalog;
    }

    internal static void Clear() => Published = null;

    internal static NativeLuggageCatalog CreateCanonicalCatalog(MapHandler Handler)
    {
        if (!Handler) throw new InvalidOperationException("PEAK's canonical MapHandler luggage catalog is unavailable.");
        if (Handler.segments is null || Handler.variantSegments is null)
            throw new InvalidOperationException("PEAK's canonical MapHandler segment catalog is unavailable.");
        var Catalog = new NativeLuggageCatalog();
        var Pending = new Queue<GameObject>();
        var Seen = new HashSet<int>();
        foreach (var Segment in Handler.segments)
        {
            if (Segment is null) continue;
            Enqueue(Segment._segmentParent); Enqueue(Segment._segmentCampfire);
            Enqueue(Segment.wallNext); Enqueue(Segment.wallPrevious);
        }
        foreach (var Segment in Handler.variantSegments)
        {
            if (Segment is null) continue;
            Enqueue(Segment._segmentParent); Enqueue(Segment._segmentCampfire);
            Enqueue(Segment.wallNext); Enqueue(Segment.wallPrevious);
        }
        while (Pending.Count != 0)
        {
            var Root = Pending.Dequeue();
            foreach (var Luggage in Root.GetComponentsInChildren<global::Luggage>(true))
                if (Luggage && !HasAuthoring(Luggage)) Catalog.Add(Root, Luggage);
            foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
            {
                if (!Spawner || NativePrefabContracts.EffectiveProps(Spawner) is not { } Prefabs) continue;
                foreach (var Prefab in Prefabs) Enqueue(Prefab);
            }
        }
        Catalog.Seal();
        return Catalog;

        void Enqueue(GameObject? Value)
        {
            if (Value && Seen.Add(Value.GetInstanceID())) Pending.Enqueue(Value);
        }
    }

    internal static bool HasAuthoring(global::Luggage Value) =>
        Value.GetComponent<PllLuggageAuthoring>() || Value.GetComponentInParent<PllLuggageAuthoring>(true);

    internal static bool Matches(global::Luggage Candidate, NativeLuggageFingerprint Expected)
    {
        if (Candidate.GetType() != typeof(global::Luggage) || Candidate.spawnUpTowardsTarget
            || !NativeLuggageSelection.TryCapture(Candidate, out var Selection, out _)) return false;
        return NativeLuggageFingerprint.Create(Selection).Equals(Expected);
    }

    internal NativeLuggageCandidate Resolve(string Token, string Context)
    {
        if (!Sealed) throw new InvalidOperationException("The native luggage catalog is not sealed.");
        Token = Normalize(Token);
        if (!Tokens.TryGetValue(Token, out var Values) || Values.Count == 0)
            throw new InvalidOperationException($"{Context} cannot resolve canonical PEAK luggage ID '{Token}'.");
        if (Values.Count != 1)
            throw new InvalidOperationException($"{Context} resolved canonical PEAK luggage ID '{Token}' to distinct sources.");
        var Result = Values[0];
        if (Result.UnsupportedReason is { } Reason) throw new NotSupportedException($"{Context} is unsupported: {Reason}");
        foreach (var Candidate in Candidates)
            if (!ReferenceEquals(Candidate, Result) && Candidate.Fingerprint is { } Fingerprint
                && Fingerprint.Equals(Result.Fingerprint))
                throw new InvalidOperationException($"{Context} has a fingerprint shared by distinct canonical PEAK luggage sources.");
        return Result;
    }

    internal NativeLuggageContents ResolveContents(string Token, string Context)
    {
        if (!Sealed) throw new InvalidOperationException("The native luggage catalog is not sealed.");
        Token = Normalize(Token);
        if (!Tokens.TryGetValue(Token, out var Values) || Values.Count == 0)
            throw new InvalidOperationException($"{Context} cannot resolve canonical PEAK luggage ID '{Token}'.");
        return ResolveContents(Values, Context, false);
    }

    internal NativeLuggageContents ResolveProgression(string Context)
    {
        if (!Sealed) throw new InvalidOperationException("The native luggage catalog is not sealed.");
        return ResolveContents(Candidates, Context, true);
    }

    static NativeLuggageContents ResolveContents(
        IReadOnlyList<NativeLuggageCandidate> Candidates,
        string Context,
        bool Progression)
    {
        NativeLuggageContents? Result = null;
        foreach (var Candidate in Candidates)
        {
            if (Progression && !Candidate.ProgressionSource) continue;
            if (Candidate.Contents is not { } Contents)
                throw new NotSupportedException(
                    $"{Context} is unsupported: {Candidate.ContentsUnsupportedReason}.");
            if (Result is null)
            {
                Result = Contents;
                continue;
            }
            if (!Result.Fingerprint.Equals(Contents.Fingerprint))
                throw new InvalidOperationException(
                    $"{Context} resolves to distinct native luggage contents.");
        }
        return Result ?? throw new InvalidOperationException(
            $"{Context} cannot resolve native luggage contents.");
    }
    void Add(GameObject Prefab, global::Luggage Source)
    {
        if (Sealed) throw new InvalidOperationException("The native luggage catalog is sealed.");
        var SourceId = Source.GetInstanceID();
        if (!CandidatesBySourceId.TryGetValue(SourceId, out var Candidate))
        {
            Candidate = new NativeLuggageCandidate(Source);
            CandidatesBySourceId.Add(SourceId, Candidate);
            Candidates.Add(Candidate);
        }
        AddToken(Prefab.name, Candidate); AddToken(Source.gameObject.name, Candidate); AddToken(Source.displayName, Candidate);
    }

    void AddToken(string? Token, NativeLuggageCandidate Candidate)
    {
        Token = Normalize(Token);
        if (Token.Length == 0) return;
        if (!Tokens.TryGetValue(Token, out var Values)) Tokens.Add(Token, Values = new List<NativeLuggageCandidate>());
        if (!Values.Contains(Candidate)) Values.Add(Candidate);
    }

    void Seal()
    {
        Sealed = true;
        CandidatesBySourceId.Clear();
    }

    static string Normalize(string? Value)
    {
        Value ??= "";
        const string Clone = "(Clone)";
        return Value.EndsWith(Clone, StringComparison.Ordinal)
            ? Value.Substring(0, Value.Length - Clone.Length).TrimEnd()
            : Value;
    }

    static NativeLuggageCatalog CreateEmpty()
    {
        var Result = new NativeLuggageCatalog();
        Result.Seal();
        return Result;
    }
}

internal sealed class NativeLuggageCandidate
{
    internal NativeLuggageCandidate(global::Luggage Source)
    {
        var SourceType = Source.GetType();
        var ExactBase = SourceType == typeof(global::Luggage);
        ProgressionSource = ExactBase
            && Source.spawnMode == Spawner.SpawnMode.HeightBasedSpawnPools
            && Source.heightBasedSpawnPools is { Count: > 0 };
        if (ExactBase)
        {
            if (NativeLuggageContents.TryCreate(Source, out var Contents, out var ContentsReason))
                this.Contents = Contents;
            else ContentsUnsupportedReason = ContentsReason;
        }
        else ContentsUnsupportedReason = $"native subtype '{SourceType.FullName}' has no contents contract";

        if (!ExactBase)
        {
            UnsupportedReason = $"native subtype '{SourceType.FullName}' has no complete copy contract";
            return;
        }
        if (Source.spawnUpTowardsTarget)
        {
            UnsupportedReason = "spawnUpTowardsTarget cannot be remapped into an authored luggage root";
            return;
        }
        if (!NativeLuggageSelection.TryCapture(Source, out var Selection, out var Reason))
        {
            UnsupportedReason = Reason;
            return;
        }
        Fingerprint = NativeLuggageFingerprint.Create(Selection);
    }

    internal NativeLuggageFingerprint Fingerprint { get; } = null!;
    internal string? UnsupportedReason { get; }
    internal NativeLuggageContents? Contents { get; }
    internal string? ContentsUnsupportedReason { get; }
    internal bool ProgressionSource { get; }
}

internal sealed class NativeLuggageSelection
{
    readonly NativeHeightSelection[]? Heights;

    NativeLuggageSelection(
        global::Luggage Source,
        NativePrefabSelection SpawnedObjectPrefab,
        NativeSpawnList? Spawns,
        NativeHeightSelection[]? Heights,
        NativePrefabSelection FallbackSpawn)
    {
        DisplayName = Source.displayName ?? "";
        TimeToOpen = Source.timeToOpen;
        PlayersInRoomRequirement = Source.playersInRoomRequirement;
        BelowAscentRequirement = Source.belowAscentRequirement;
        NormalRunsOnly = Source.normalRunsOnly;
        SpawnMode = Source.spawnMode;
        BaseSpawnChance = Source.baseSpawnChance;
        this.SpawnedObjectPrefab = SpawnedObjectPrefab;
        this.Spawns = Spawns;
        SpawnPool = Source.spawnPool;
        CanRepeatSpawns = Source.canRepeatSpawns;
        SpawnTransformIsSpawnerTransform = Source.spawnTransformIsSpawnerTransform;
        SpawnAwayFromUpTarget = Source.spawnAwayFromUpTarget;
        CenterItemsVisually = Source.centerItemsVisually;
        SpawnOnStart = Source.spawnOnStart;
        IsKinematic = Source.isKinematic;
        this.Heights = Heights;
        this.FallbackSpawn = FallbackSpawn;
    }

    internal string DisplayName { get; }
    internal float TimeToOpen { get; }
    internal int PlayersInRoomRequirement { get; }
    internal int BelowAscentRequirement { get; }
    internal bool NormalRunsOnly { get; }
    internal Spawner.SpawnMode SpawnMode { get; }
    internal float BaseSpawnChance { get; }
    internal NativePrefabSelection SpawnedObjectPrefab { get; }
    internal NativeSpawnList? Spawns { get; }
    internal SpawnPool SpawnPool { get; }
    internal bool CanRepeatSpawns { get; }
    internal bool SpawnTransformIsSpawnerTransform { get; }
    internal bool SpawnAwayFromUpTarget { get; }
    internal bool CenterItemsVisually { get; }
    internal bool SpawnOnStart { get; }
    internal bool IsKinematic { get; }
    internal bool HasHeights => Heights is not null;
    internal int HeightCount => Heights?.Length ?? 0;
    internal NativePrefabSelection FallbackSpawn { get; }

    internal NativeHeightSelection HeightAt(int Index) => Heights![Index];

    internal static bool TryCapture(
        global::Luggage Source,
        out NativeLuggageSelection Selection,
        out string Reason)
    {
        Selection = null!;
        if (!NativePrefabSelection.TryCapture(
                Source.spawnedObjectPrefab, "spawnedObjectPrefab", out var SpawnedObjectPrefab, out Reason)
            || !NativePrefabSelection.TryCapture(
                Source.fallbackSpawn, "fallbackSpawn", out var FallbackSpawn, out Reason)) return false;
        NativeSpawnList? Spawns = null;
        var SourceSpawns = Source.spawns;
        if (SourceSpawns && !NativeSpawnList.TryCapture(SourceSpawns, out Spawns, out Reason)) return false;
        NativeHeightSelection[]? Heights = null;
        if (Source.heightBasedSpawnPools is { } SourceHeights)
        {
            Heights = SourceHeights.Count == 0 ? Array.Empty<NativeHeightSelection>() : new NativeHeightSelection[SourceHeights.Count];
            for (var Index = 0; Index < Heights.Length; Index++)
            {
                var Entry = SourceHeights[Index];
                if (Entry is null)
                {
                    Reason = "heightBasedSpawnPools contains a null entry";
                    return false;
                }
                Heights[Index] = new NativeHeightSelection(Entry);
            }
        }
        Selection = new NativeLuggageSelection(
            Source, SpawnedObjectPrefab, Spawns, Heights, FallbackSpawn);

        Reason = "";
        return true;
    }

    internal static bool MatchesStagedSpawner(
        global::Luggage Value,
        bool CanRepeatSpawns,
        int SpawnSpotCount)
    {
        return PllStagedLuggageShapeContract.MatchesNeutral(Value, CanRepeatSpawns)
            && Value.spawnPointMode == Spawner.SpawnPointMode.SingleList
            && Value.spawnSpots is { } SpawnSpots && SpawnSpots.Count == SpawnSpotCount
            && Value.weightedSpawnSpots is { Count: 0 };
    }

}

internal readonly struct NativeHeightSelection
{
    internal NativeHeightSelection(Spawner.HeightBasedSpawnListEntry Source)
    {
        SpawnPool = Source.spawnPool;
        MinimumHeight = Source.minimumHeight;
        MinimumZ = Source.minimumZ;
        HasBiomeRequirement = Source.hasBiomeRequirement;
        BiomeRequirement = Source.biomeRequirement;
    }

    internal SpawnPool SpawnPool { get; }
    internal float MinimumHeight { get; }
    internal float MinimumZ { get; }
    internal bool HasBiomeRequirement { get; }
    internal Biome.BiomeType BiomeRequirement { get; }

    internal Spawner.HeightBasedSpawnListEntry Create() => new()
    {
        spawnPool = SpawnPool,
        minimumHeight = MinimumHeight,
        minimumZ = MinimumZ,
        hasBiomeRequirement = HasBiomeRequirement,
        biomeRequirement = BiomeRequirement
    };
}

internal sealed class NativeSpawnList
{
    readonly NativeSpawnEntry?[]? Entries;

    NativeSpawnList(string Name, NativeSpawnEntry?[]? Entries)
    {
        this.Name = Name;
        this.Entries = Entries;
    }

    internal string Name { get; }
    internal bool HasEntries => Entries is not null;
    internal int EntryCount => Entries?.Length ?? 0;

    internal NativeSpawnEntry? EntryAt(int Index) => Entries![Index];

    internal static bool TryCapture(SpawnList Source, out NativeSpawnList Result, out string Reason)
    {
        var SourceEntries = Source.items;
        if (SourceEntries is null)
        {
            Result = new NativeSpawnList(Source.gameObject.name, null);
            Reason = "";
            return true;
        }
        var Entries = SourceEntries.Count == 0 ? Array.Empty<NativeSpawnEntry?>() : new NativeSpawnEntry?[SourceEntries.Count];
        for (var Index = 0; Index < Entries.Length; Index++)
        {
            var Entry = SourceEntries[Index];
            if (Entry is null) continue;
            if (!NativePrefabSelection.TryCapture(
                    Entry.prefab, "spawns item prefab", out var Prefab, out Reason))
            {
                Result = null!;
                return false;
            }
            Entries[Index] = new NativeSpawnEntry(Prefab, Entry.weight, Entry.percentageOdds);
        }
        Result = new NativeSpawnList(Source.gameObject.name, Entries);
        Reason = "";
        return true;
    }

}

internal sealed class NativeSpawnEntry
{
    internal NativeSpawnEntry(NativePrefabSelection Prefab, int Weight, float PercentageOdds)
    {
        this.Prefab = Prefab;
        this.Weight = Weight;
        this.PercentageOdds = PercentageOdds;
    }

    internal NativePrefabSelection Prefab { get; }
    internal int Weight { get; }
    internal float PercentageOdds { get; }

}

internal sealed class NativePrefabSelection
{
    static readonly NativePrefabSelection Missing = new(false, "", Array.Empty<NativeItemSelection>());
    readonly NativeItemSelection[] Items;

    NativePrefabSelection(bool Present, string Name, NativeItemSelection[] Items)
    {
        this.Present = Present;
        this.Name = Name;
        this.Items = Items;
    }

    internal bool Present { get; }
    internal string Name { get; }
    internal int ItemCount => Items.Length;

    internal NativeItemSelection ItemAt(int Index) => Items[Index];

    internal static bool TryCapture(
        GameObject? Source,
        string Field,
        out NativePrefabSelection Selection,
        out string Reason)
    {
        Selection = Missing;
        if (!Source)
        {
            Reason = "";
            return true;
        }
        if (Source.scene.IsValid())
        {
            Reason = $"{Field} references scene GameObject '{Source.name}'";
            return false;
        }
        var SourceItems = Source.GetComponentsInChildren<Item>(true);
        var Items = SourceItems.Length == 0 ? Array.Empty<NativeItemSelection>() : new NativeItemSelection[SourceItems.Length];
        for (var Index = 0; Index < Items.Length; Index++) Items[Index] = new NativeItemSelection(SourceItems[Index]);
        Selection = new NativePrefabSelection(true, Source.name, Items);
        Reason = "";
        return true;
    }
}

internal readonly struct NativeItemSelection
{
    internal NativeItemSelection(Item? Source)
    {
        if (!Source)
        {
            Name = ""; ItemId = ushort.MaxValue; TypeName = "";
            return;
        }
        var Type = Source.GetType();
        Name = Source.gameObject.name; ItemId = Source.itemID; TypeName = Type.FullName ?? Type.Name;
    }

    internal string Name { get; }
    internal ushort ItemId { get; }
    internal string TypeName { get; }
}
