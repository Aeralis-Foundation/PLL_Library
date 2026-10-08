using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class PreparedMap
{
    internal PreparedMap(
        MapHandler Handler,
        LevelGeneration Level,
        MapSlot[] Slots,
        int InitialGenerationSlotCount,
        OriginalMapSources Originals,
        Dictionary<Transform, Vector3> Positions,
        HashSet<Transform> Reused,
        GameObject[] WaterObjects,
        bool[] WaterStates,
        Vector3[] WaterPositions,
        MapEnvironmentProjection.EnvironmentState Environment,
        Transform? TerminalRespawn,
        HashSet<MapHandler.MapSegment> NativeCatalogSegments,
        NativeFixtures.StartKit StartKit,
        List<GameObject> CreatedObjects,
        List<GloomVoiceRuntime.GloomProfileClone> CreatedGloomProfiles)
    {
        if (InitialGenerationSlotCount < 1 || InitialGenerationSlotCount > Slots.Length
            || Slots[InitialGenerationSlotCount - 1].Occurrence.BatchIndex != InitialGenerationSlotCount - 1)
            throw new InvalidOperationException("Initial generation must end at an ordinary occurrence boundary.");
        this.Handler = Handler;
        this.Level = Level;
        this.Slots = Slots;
        this.InitialGenerationSlotCount = InitialGenerationSlotCount;
        this.Originals = Originals;
        this.Positions = Positions;
        this.Reused = Reused;
        this.WaterObjects = WaterObjects;
        this.WaterStates = WaterStates;
        this.WaterPositions = WaterPositions;
        this.Environment = Environment;
        this.TerminalRespawn = TerminalRespawn;
        this.NativeCatalogSegments = NativeCatalogSegments;
        this.StartKit = StartKit;
        this.CreatedObjects = CreatedObjects;
        this.CreatedGloomProfiles = CreatedGloomProfiles;
        Originals.ConfigureVoidController(Slots, Reused);
    }

    internal MapHandler Handler { get; }
    internal LevelGeneration Level { get; }
    internal MapSlot[] Slots { get; }
    internal int InitialGenerationSlotCount { get; }
    internal OriginalMapSources Originals { get; }
    internal Dictionary<Transform, Vector3> Positions { get; }
    internal HashSet<Transform> Reused { get; }
    internal GameObject[] WaterObjects { get; }
    internal bool[] WaterStates { get; }
    internal Vector3[] WaterPositions { get; }
    internal MapEnvironmentProjection.EnvironmentState Environment { get; }
    internal Transform? TerminalRespawn { get; }
    internal HashSet<MapHandler.MapSegment> NativeCatalogSegments { get; }
    internal NativeFixtures.StartKit StartKit { get; }
    internal List<GameObject> CreatedObjects { get; }
    internal List<GloomVoiceRuntime.GloomProfileClone> CreatedGloomProfiles { get; }
    internal int? OriginalLevelSeed { get; set; }
    internal MapHandler.MapSegment[]? OriginalSegments { get; set; }
    internal List<Biome.BiomeType>? OriginalBiomeTypes { get; set; }
    internal Transform? OriginalPeakRespawn { get; set; }
    internal PeakHandler? OriginalPeakHandler { get; set; }
    internal PeakHandler? EndCutscenePeakHandler { get; set; }
    internal bool? OriginalVoidInitialized { get; set; }
    internal List<PropGrouper> GeneratedGroupers { get; } = new();
    internal List<PropGrouper> AddedGroupers { get; } = new();
    internal List<Peak.SpawnedItemTracker>? OriginalSpawnTrackers { get; set; }
    internal HashSet<int>? OriginalTrackerIds { get; set; }
    internal List<Peak.SpawnedItemTracker> DeferredTrackerDestruction { get; } = new();
    internal FakeItemTransaction? FakeItems { get; set; }
    internal bool Committed { get; set; }

    internal void RestoreHandler(Action<Action> ApplyRestore, bool ResumeNativeSources = true)
    {
        if (!Handler) return;
        var CurrentHandler = Handler;
        ApplyRestore(() =>
        {
            if (OriginalLevelSeed is { } OriginalSeed && Level) Level.seed = OriginalSeed;
            if (OriginalSegments is not null) CurrentHandler.segments = OriginalSegments;
            if (OriginalVoidInitialized is { } OriginalVoid) CurrentHandler.voidInitialized = OriginalVoid;
            Originals.RestoreVoidController(ResumeNativeSources);
            CurrentHandler.respawnThePeak = OriginalPeakRespawn;
            Zorro.Core.Singleton<PeakHandler>._instance = OriginalPeakHandler!;
        });
        ApplyRestore(() =>
        {
            if (OriginalBiomeTypes is null) return;
            CurrentHandler.biomes.Clear();
            CurrentHandler.biomes.AddRange(OriginalBiomeTypes);
        });
        ApplyRestore(() => SpawnTrackerTransaction.RestoreSpawnTrackers(this));
    }
}
