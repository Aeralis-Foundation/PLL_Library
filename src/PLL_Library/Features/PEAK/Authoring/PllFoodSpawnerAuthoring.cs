using System;
using UnityEngine;

namespace PLL.Library;

public enum PllFoodPool
{
    BeachBerries = (int)SpawnPool.BerryBushBeach,
    JungleBerries = (int)SpawnPool.BerryBushJungle,
    Mushrooms = (int)SpawnPool.MushroomCluster,
    SpikyVine = (int)SpawnPool.SpikyVine,
    Coconuts = (int)SpawnPool.CoconutTree,
    JungleWillow = (int)SpawnPool.WillowTreeJungle,
    JungleVine = (int)SpawnPool.JungleVine,
    Winterberries = (int)SpawnPool.WinterberryTree,
    Cactus = (int)SpawnPool.Cactus,
    Redwood = (int)SpawnPool.Redwood,
    Nest = (int)SpawnPool.Nest
}

public enum PllFoodPlacement : byte { Attached, Ground }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Food Spawner")]
public sealed class PllFoodSpawnerAuthoring : PllNativeSpawnerAuthoring
{
    public PllFoodPlacement Placement;
    public bool UseCustomList;
    public PllFoodPool NativePool = PllFoodPool.BeachBerries;
    public PllNativeSpawnEntry[] CustomItems = Array.Empty<PllNativeSpawnEntry>();
    public Transform[] SpawnPoints = Array.Empty<Transform>();
    public Transform? UpTowards;
    [Min(0)] public int MinimumCount = 1;
    [Min(1)] public int MaximumCount = 5;
    [Min(0.0001f)] public float CountBias = 2f;
    [Range(0f, 1f)] public float Chance = 1f;

    public override PllNativeSpawnerSelection Capture() => PllNativeSpawnerSelection.Capture(this);
}
