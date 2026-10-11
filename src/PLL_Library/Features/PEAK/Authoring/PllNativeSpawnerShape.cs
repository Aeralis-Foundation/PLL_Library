using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

[AddComponentMenu("")]
public sealed class PllFoodBerrySpawner : BerryBush
{
#if !PLL_CREATOR_SDK
    public override List<PhotonView> SpawnItems(List<Transform> Points)
    {
        if (!PhotonNetwork.IsMasterClient) return new List<PhotonView>();
        if (spawnPool == SpawnPool.None)
            spawnedObjectPrefab = spawns.GetSingleSpawn();
        else
        {
            spawnedObjectPrefab = LootData.GetRandomItem(spawnPool, false);
            if (!spawnedObjectPrefab) return new List<PhotonView>();
        }
        spawnMode = SpawnMode.SingleItem;
        return base.SpawnItems(Points);
    }
#endif
}

[AddComponentMenu("")]
public sealed class PllFoodGroundSpawner : GroundPlaceSpawner
{
#if !PLL_CREATOR_SDK
    public override List<PhotonView> SpawnItems(List<Transform> Points)
    {
        if (!PhotonNetwork.IsMasterClient) return new List<PhotonView>();
        if (spawnPool != SpawnPool.None)
        {
            var Item = LootData.GetRandomItem(spawnPool, false);
            if (!Item) return new List<PhotonView>();
            spawns.items[0].prefab = Item;
        }
        return base.SpawnItems(Points);
    }
#endif
}

[AddComponentMenu("")]
public sealed class PllContentItemSpawner : Spawner
{
    public Vector3 ZoneSize;
    public int MinimumCount, MaximumCount;
#if !PLL_CREATOR_SDK
    public override List<PhotonView> SpawnItems(List<Transform> Points)
    {
        var Spawned = new List<PhotonView>();
        if (!PhotonNetwork.IsMasterClient) return Spawned;
        var Count = UnityEngine.Random.Range(MinimumCount, MaximumCount + 1);
        if (Count == 0) return Spawned;
        var Items = spawns.GetSpawns(Count, true);
        for (var i = 0; i < Count; i++)
        {
            var Position = transform.TransformPoint(new Vector3(
                UnityEngine.Random.Range(-ZoneSize.x * 0.5f, ZoneSize.x * 0.5f), 0f,
                UnityEngine.Random.Range(-ZoneSize.z * 0.5f, ZoneSize.z * 0.5f)));
            if (!Physics.Raycast(Position, -transform.up, out var Hit, ZoneSize.y,
                    HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical), QueryTriggerInteraction.Ignore)) continue;
            var Item = PhotonNetwork.InstantiateItemRoom(Items[i].name, Hit.point,
                HelperFunctions.GetRandomRotationWithUp(Hit.normal), true).GetComponent<Item>();
            Spawned.Add(Item.GetComponent<PhotonView>());
            Item.SetKinematicNetworked(true, Item.transform.position, Item.transform.rotation);
        }
        return Spawned;
    }
#endif
}

[AddComponentMenu("")]
public sealed class PllContentPropSpawner : PropSpawner
{
#if !PLL_CREATOR_SDK
    internal Func<GameObject>? SelectPrefab;
    public override System.Collections.IEnumerator Execute() => chanceToUseSpawner == 0f
        ? Array.Empty<object>().GetEnumerator() : base.Execute();
    public override void ExecuteImmediately()
    {
        if (chanceToUseSpawner != 0f) base.ExecuteImmediately();
    }
    public override GameObject GetPropToSpawn() =>
        (SelectPrefab ?? throw new InvalidOperationException("Content Spawner has no sealed native selection."))();
#endif
}

public static class PllNativeSpawnerShape
{
    public static Component Configure(MonoBehaviour Source, PllNativeSpawnerSelection Selection)
    {
        var Root = Source.gameObject;
        if (Selection.Kind == PllNativeSpawnerKind.Props)
        {
            var Native = new GameObject("NativeContent"); Native.transform.SetParent(Root.transform, false);
            Native.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var Prop = Native.AddComponent<PllContentPropSpawner>();
            Prop.area = new Vector2(Selection.ZoneSize.x, Selection.ZoneSize.z);
            Prop.rayLength = Selection.ZoneSize.y; Prop.rayNearCutoff = 0f; Prop.rayDirectionOffset = Vector3.zero;
            Prop.raycastPosition = true; Prop.nrOfSpawns = Selection.Maximum;
            Prop.randomSpawns = Selection.Minimum != Selection.Maximum; Prop.minSpawnCount = Selection.Minimum;
            Prop.chanceToUseSpawner = Selection.Chance; Prop.currentSpawns = 0;
            Prop.props = Array.Empty<GameObject>(); Prop.overrideProps = false; Prop.overridePropsList = null!;
            Prop.syncTransforms = true; Prop.layerType = HelperFunctions.LayerType.AllPhysical;
            Prop.modifiers = new List<PropSpawnerMod> { new PSM_SetUpRotationToNormal
                { minEffect = 1f, maxEffect = 1f, randomPow = 1f, flipNormal = false } };
            Prop.constraints = new List<PropSpawnerConstraint>();
            Prop.postConstraints = new List<PropSpawnerConstraintPost> { new PSCP_Custom() };
            Prop.postSpawnBehaviors = new List<PostSpawnBehavior>();
            return Prop;
        }
        Spawner Spawner = Selection.Kind switch
        {
            PllNativeSpawnerKind.AttachedFood => Root.AddComponent<PllFoodBerrySpawner>(),
            PllNativeSpawnerKind.GroundFood => Root.AddComponent<PllFoodGroundSpawner>(),
            PllNativeSpawnerKind.Items => Root.AddComponent<PllContentItemSpawner>(),
            _ => throw new InvalidOperationException("Native spawner kind is unsupported.")
        };
        Spawner.spawnMode = Spawner.SpawnMode.SpawnPool; Spawner.spawnPool = Selection.Pool;
        Spawner.spawnPointMode = Spawner.SpawnPointMode.SingleList;
        Spawner.spawnedObjectPrefab = null!; Spawner.spawns = Root.AddComponent<SpawnList>();
        Spawner.spawns.items = new List<SpawnEntry>();
        if (Selection.Kind == PllNativeSpawnerKind.GroundFood && Selection.Pool != SpawnPool.None)
            Spawner.spawns.items.Add(new SpawnEntry { weight = 1 });
        Spawner.spawnSpots = new List<Transform>(Selection.Points);
        Spawner.weightedSpawnSpots = new List<Spawner.WeightedSpawnPointEntry>();
        Spawner.spawnUpTowardsTarget = Selection.UpTowards;
        Spawner.spawnTransformIsSpawnerTransform = false; Spawner.spawnAwayFromUpTarget = false;
        Spawner.centerItemsVisually = false; Spawner.spawnOnStart = Selection.Chance != 0f; Spawner.isKinematic = true;
        Spawner.spawnWithForce = false; Spawner.spawnForce = 0f; Spawner.spawnTorque = 0f;
        Spawner.baseSpawnChance = Selection.Chance; Spawner.playersInRoomRequirement = 0;
        Spawner.belowAscentRequirement = -1; Spawner.normalRunsOnly = false; Spawner.canRepeatSpawns = false;
        Spawner.heightBasedSpawnPools = new List<Spawner.HeightBasedSpawnListEntry>(); Spawner.fallbackSpawn = null!;
        if (Spawner is PllFoodBerrySpawner Berry)
        {
            Berry.possibleBerries = new Vector2(Selection.Minimum, Selection.Maximum); Berry.randomPow = Selection.Bias;
        }
        else if (Spawner is PllFoodGroundSpawner Ground)
            Ground.possibleItems = new Vector2(Selection.Minimum, Selection.Maximum);
        else if (Spawner is PllContentItemSpawner Items)
        {
            Items.ZoneSize = Selection.ZoneSize; Items.MinimumCount = Selection.Minimum; Items.MaximumCount = Selection.Maximum;
        }
        Root.AddComponent<Peak.SpawnedItemTracker>();
        return Spawner;
    }

    public static Component Require(MonoBehaviour Source, PllNativeSpawnerSelection Selection)
    {
        if (Selection.Kind == PllNativeSpawnerKind.Props)
        {
            var Props = Source.GetComponentsInChildren<PllContentPropSpawner>(true);
            if (Props.Length != 1 || Props[0].transform.parent != Source.transform
                || Props[0].transform.localPosition != Vector3.zero || Props[0].transform.localScale != Vector3.one
                || Quaternion.Angle(Props[0].transform.localRotation, Quaternion.Euler(90f, 0f, 0f)) > 0.001f
                || Props[0].area != new Vector2(Selection.ZoneSize.x, Selection.ZoneSize.z)
                || Props[0].rayLength != Selection.ZoneSize.y || !Props[0].raycastPosition
                || Props[0].nrOfSpawns != Selection.Maximum || Props[0].minSpawnCount != Selection.Minimum
                || Props[0].chanceToUseSpawner != Selection.Chance || Props[0].overrideProps
                || Props[0].randomSpawns != (Selection.Minimum != Selection.Maximum)
                || Props[0].rayNearCutoff != 0f || Props[0].rayDirectionOffset != Vector3.zero
                || !Props[0].syncTransforms || Props[0].layerType != HelperFunctions.LayerType.AllPhysical
                || Props[0].modifiers is not { Count: 1 } || Props[0].modifiers[0] is not PSM_SetUpRotationToNormal
                    { mute: false, minEffect: 1f, maxEffect: 1f, randomPow: 1f, flipNormal: false }
                || Props[0].postConstraints is not { Count: 1 }
                || Props[0].postConstraints[0] is not PSCP_Custom { mute: false }
                || Props[0].postSpawnBehaviors is not { Count: 0 } || Props[0].overridePropsList)
                throw new InvalidOperationException("Content Spawner's native prop shape changed.");
            return Props[0];
        }
        var Spawners = Source.GetComponents<Spawner>();
        if (Spawners.Length != 1 || Source.GetComponents<SpawnList>().Length != 1
            || Source.GetComponents<Peak.SpawnedItemTracker>().Length != 1)
            throw new InvalidOperationException("Native item spawner shape is missing or duplicated.");
        var Spawner = Spawners[0];
        if (!Spawner.enabled || Spawner.spawnOnStart != (Selection.Chance != 0f) || !Spawner.isKinematic || Spawner.canRepeatSpawns
            || Spawner.spawnPool != Selection.Pool || Spawner.baseSpawnChance != Selection.Chance
            || Spawner.spawnPointMode != Spawner.SpawnPointMode.SingleList
            || Spawner.spawnSpots.Count != Selection.Points.Count || Spawner.spawnUpTowardsTarget != Selection.UpTowards
            || !Spawner.spawns || Spawner.spawns.gameObject != Source.gameObject
            || Spawner.playersInRoomRequirement != 0 || Spawner.belowAscentRequirement != -1
            || Spawner.normalRunsOnly || Spawner.spawnWithForce || Spawner.spawnForce != 0f || Spawner.spawnTorque != 0f
            || Spawner.spawnTransformIsSpawnerTransform || Spawner.spawnAwayFromUpTarget || Spawner.centerItemsVisually
            || Spawner.weightedSpawnSpots is not { Count: 0 } || Spawner.heightBasedSpawnPools is not { Count: 0 } || Spawner.fallbackSpawn)
            throw new InvalidOperationException("Native item spawner settings changed.");
        for (var i = 0; i < Selection.Points.Count; i++)
            if (Spawner.spawnSpots[i] != Selection.Points[i]) throw new InvalidOperationException("Native food spawn points changed.");
        var Counts = new Vector2(Selection.Minimum, Selection.Maximum);
        if (Selection.Kind switch
        {
            PllNativeSpawnerKind.AttachedFood => Spawner is PllFoodBerrySpawner Berry
                && Berry.possibleBerries == Counts && Berry.randomPow == Selection.Bias,
            PllNativeSpawnerKind.GroundFood => Spawner is PllFoodGroundSpawner Ground && Ground.possibleItems == Counts,
            PllNativeSpawnerKind.Items => Spawner is PllContentItemSpawner Items && Items.ZoneSize == Selection.ZoneSize
                && Items.MinimumCount == Selection.Minimum && Items.MaximumCount == Selection.Maximum,
            _ => throw new InvalidOperationException("Native spawner kind is unsupported.")
        }) return Spawner;
        throw new InvalidOperationException("Native item spawner type or count settings changed.");
    }
}
