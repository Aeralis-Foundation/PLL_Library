#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library
{
    internal static class PllStagedLuggageShapeContract
    {
        internal static void Configure(
            global::Luggage Value,
            PllLuggageAuthoring Source,
            GameObject? ActivateOnOpen,
            Func<Transform, Transform> MapPoint)
        {
            Value.displayName = Source.DisplayName;
            Value.timeToOpen = Source.OpenTime;
            Value.activateOnOpen = ActivateOnOpen;
            ConfigureNeutral(
                Value, Source.RepeatMode == PllLuggageRepeatMode.AllowRepeats);

            if (Source.SpawnGroups.Count == 1)
            {
                Value.spawnPointMode = Spawner.SpawnPointMode.SingleList;
                Value.spawnSpots = MapPoints(Source.SpawnGroups[0], MapPoint);
                Value.weightedSpawnSpots = new List<Spawner.WeightedSpawnPointEntry>();
                return;
            }

            Value.spawnPointMode = Spawner.SpawnPointMode.WeightedLists;
            Value.spawnSpots = new List<Transform>();
            Value.weightedSpawnSpots = new List<Spawner.WeightedSpawnPointEntry>(
                Source.SpawnGroups.Count);
            foreach (var Group in Source.SpawnGroups)
                Value.weightedSpawnSpots.Add(new Spawner.WeightedSpawnPointEntry
                {
                    spawnSpots = MapPoints(Group, MapPoint),
                    weight = Group.Weight
                });
        }

        internal static void ConfigureNeutral(Spawner Value, bool CanRepeat)
        {
            Value.playersInRoomRequirement = 0;
            Value.belowAscentRequirement = -1;
            Value.normalRunsOnly = false;
            Value.spawnMode = Spawner.SpawnMode.SingleItem;
            Value.baseSpawnChance = 1f;
            Value.spawnedObjectPrefab = null;
            Value.spawns = null;
            Value.spawnPool = default(SpawnPool);
            Value.canRepeatSpawns = CanRepeat;
            Value.spawnUpTowardsTarget = null;
            Value.spawnTransformIsSpawnerTransform = false;
            Value.spawnAwayFromUpTarget = false;
            Value.centerItemsVisually = true;
            Value.spawnOnStart = false;
            Value.isKinematic = true;
            Value.spawnWithForce = false;
            Value.spawnForce = 0f;
            Value.spawnTorque = 0f;
            Value.heightBasedSpawnPools = new List<Spawner.HeightBasedSpawnListEntry>();
            Value.fallbackSpawn = null;
        }
        internal static bool Matches(
            global::Luggage Value,
            PllLuggageAuthoring Source,
            Func<Spawner.WeightedSpawnPointEntry, float> ReadPercentageOdds)
        {
            if (!Value || Value.GetType() != typeof(global::Luggage) || Value.IsOpen
                || Value.displayName != Source.DisplayName || !Value.timeToOpen.Equals(Source.OpenTime)
                || Value.activateOnOpen != Source.ActivateOnOpen
                || !MatchesNeutral(
                    Value, Source.RepeatMode == PllLuggageRepeatMode.AllowRepeats)) return false;
            var SpawnSpots = Value.spawnSpots;
            var WeightedSpawnSpots = Value.weightedSpawnSpots;
            if (SpawnSpots is null || WeightedSpawnSpots is null) return false;

            if (Source.SpawnGroups.Count == 1)
                return Value.spawnPointMode == Spawner.SpawnPointMode.SingleList
                    && SamePoints(SpawnSpots, Source.SpawnGroups[0].Points)
                    && WeightedSpawnSpots.Count == 0;
            if (Source.SpawnGroups.Count <= 1
                || Value.spawnPointMode != Spawner.SpawnPointMode.WeightedLists
                || SpawnSpots.Count != 0
                || WeightedSpawnSpots.Count != Source.SpawnGroups.Count) return false;
            for (var Index = 0; Index < Source.SpawnGroups.Count; Index++)
            {
                var Actual = WeightedSpawnSpots[Index];
                var Expected = Source.SpawnGroups[Index];
                if (Actual is null || !ReadPercentageOdds(Actual).Equals(0f)
                    || Actual.weight != Expected.Weight
                    || Actual.spawnSpots is null
                    || !SamePoints(Actual.spawnSpots, Expected.Points)) return false;
            }
            return true;
        }

        internal static bool MatchesNeutral(Spawner Value, bool CanRepeat)
        {
            if (!Value) return false;
            return Value.playersInRoomRequirement == 0
                && Value.belowAscentRequirement == -1 && !Value.normalRunsOnly
                && Value.spawnMode == Spawner.SpawnMode.SingleItem
                && Value.baseSpawnChance.Equals(1f) && !Value.spawnedObjectPrefab
                && !Value.spawns && (int)Value.spawnPool == 0
                && Value.canRepeatSpawns == CanRepeat && !Value.spawnUpTowardsTarget
                && !Value.spawnTransformIsSpawnerTransform
                && !Value.spawnAwayFromUpTarget && Value.centerItemsVisually
                && !Value.spawnOnStart && Value.isKinematic && !Value.spawnWithForce
                && Value.spawnForce.Equals(0f) && Value.spawnTorque.Equals(0f)
                && Value.heightBasedSpawnPools is { Count: 0 } && !Value.fallbackSpawn;
        }
        static List<Transform> MapPoints(
            PllLuggageSpawnGroup Group,
            Func<Transform, Transform> MapPoint)
        {
            var Result = new List<Transform>(Group.Points.Count);
            foreach (var Point in Group.Points) Result.Add(MapPoint(Point));
            return Result;
        }

        static bool SamePoints(
            IReadOnlyList<Transform> Actual,
            IReadOnlyList<Transform> Expected)
        {
            if (Actual.Count != Expected.Count) return false;
            for (var Index = 0; Index < Actual.Count; Index++)
                if (Actual[Index] != Expected[Index]) return false;
            return true;
        }
    }
}
