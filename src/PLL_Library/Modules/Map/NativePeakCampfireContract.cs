using System;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativePeakCampfireSource
{
    readonly BerryBush[] Marshmallows;
    readonly RespawnChest Chest;

    internal NativePeakCampfireSource(
        Transform CampfireRoot,
        BerryBush[] Marshmallows,
        Transform RespawnRoot,
        RespawnChest Chest,
        SingleItemSpawner Backpack,
        Pose BackpackPose)
    {
        this.CampfireRoot = CampfireRoot;
        this.Marshmallows = Marshmallows;
        this.RespawnRoot = RespawnRoot;
        this.Chest = Chest;
        this.Backpack = Backpack;
        this.BackpackPose = BackpackPose;
    }

    internal Transform CampfireRoot { get; }
    internal Transform RespawnRoot { get; }
    internal SingleItemSpawner Backpack { get; }
    internal Pose BackpackPose { get; }

    internal global::Campfire ResolveCampfire(Transform CloneRoot) =>
        CloneRoot.GetComponent<global::Campfire>()
        ?? throw new InvalidOperationException("The cloned Peak campfire lost its native Campfire component.");

    internal BerryBush[] ResolveMarshmallows(Transform CloneRoot)
    {
        var Result = new BerryBush[Marshmallows.Length];
        for (var Index = 0; Index < Result.Length; Index++)
            Result[Index] = CloneRoot.GetChild(
                    Marshmallows[Index].transform.GetSiblingIndex()).GetComponent<BerryBush>()
                ?? throw new InvalidOperationException(
                    "The cloned Peak campfire lost a native player-gated marshmallow spawner.");
        return Result;
    }

    internal RespawnChest ResolveChest(Transform CloneRoot) =>
        CloneRoot.GetChild(Chest.transform.GetSiblingIndex()).GetComponent<RespawnChest>()
        ?? throw new InvalidOperationException("The cloned Peak statue reward lost its native RespawnChest.");
}

internal static class NativePeakCampfireContract
{
    const int OutputCount = 4;

    internal static NativePeakCampfireSource Capture(
        NativeOrdinarySegmentSource Gloom,
        NativeOrdinarySegmentSource Beach)
    {
        var CampfireRoot = Gloom.SegmentCampfire
            ? Gloom.SegmentCampfire.transform
            : throw new InvalidOperationException("PEAK's native Gloom campfire root is unavailable.");
        ValidateCampfire(
            CampfireRoot, "PEAK's native Gloom campfire root", false, out var Marshmallows);

        var Branch = CampfireRoot.parent;
        var RespawnSibling = CampfireRoot.GetSiblingIndex() + 1;
        if (!Branch || !TransformOwnership.Contains(Gloom.Container, Branch)
            || RespawnSibling >= Branch.childCount)
            throw new InvalidOperationException("PEAK's native Gloom campfire branch has no reward wrapper.");
        var Candidate = Branch.GetChild(RespawnSibling);
        var Chests = Candidate.GetComponentsInChildren<RespawnChest>(true);
        if (!string.Equals(Candidate.name, "RespawnChest", StringComparison.Ordinal)
            || Chests.Length != 1 || !Chests[0] || Chests[0].transform.parent != Candidate)
            throw new InvalidOperationException(
                "PEAK's native Gloom campfire branch has an invalid adjacent RespawnChest wrapper.");
        var Chest = Chests[0];
        var RespawnRoot = Chest.transform.parent;
        ValidateRespawn(RespawnRoot, Chest, "PEAK's native Gloom RespawnChest wrapper");

        var BeachCampfireRoot = Beach.SegmentCampfire
            ? Beach.SegmentCampfire.transform
            : throw new InvalidOperationException("PEAK's native Beach campfire root is unavailable.");
        var BeachCampfires = BeachCampfireRoot.GetComponentsInChildren<global::Campfire>(true);
        if (BeachCampfires.Length != 1 || !BeachCampfires[0])
            throw new InvalidOperationException("PEAK's native Beach campfire binding is ambiguous.");
        SingleItemSpawner? Backpack = null;
        foreach (var Spawner in BeachCampfireRoot.GetComponentsInChildren<SingleItemSpawner>(true))
        {
            var Prefab = Spawner.prefab;
            if (!Prefab || !Prefab.TryGetComponent<Backpack>(out _)) continue;
            if (Backpack)
                throw new InvalidOperationException("PEAK's native Beach backpack spawner is ambiguous.");
            Backpack = Spawner;
        }
        if (!Backpack)
            throw new InvalidOperationException("PEAK's native Beach backpack spawner is unavailable.");

        return new NativePeakCampfireSource(
            CampfireRoot,
            Marshmallows,
            RespawnRoot,
            Chest,
            Backpack,
            NativeSegmentPorts.LocalPose(BeachCampfires[0].transform, Backpack.transform));
    }

    internal static void ValidatePrebuilt(Transform Root, string Context)
    {
        if (!Root || !Root.gameObject.activeSelf)
            throw new InvalidOperationException($"{Context} must be active.");
        ValidateCampfire(Root, Context, true, out _);
    }

    static void ValidateCampfire(
        Transform Root,
        string Context,
        bool RequireUnassignedViews,
        out BerryBush[] Marshmallows)
    {
        var Campfires = Root.GetComponentsInChildren<global::Campfire>(true);
        var SafeZones = Root.GetComponentsInChildren<Peak.GloomSafeZone>(true);
        if (Campfires.Length != 1 || !Campfires[0] || !Campfires[0].enabled
            || !NativeFixtures.SameObject(Campfires[0].transform, Root)
            || SafeZones.Length != 1 || !SafeZones[0] || !SafeZones[0].enabled
            || !NativeFixtures.SameObject(SafeZones[0].transform, Root))
            throw new InvalidOperationException(
                $"{Context} must own one direct enabled Campfire and GloomSafeZone.");
        if (Root.GetComponentsInChildren<PropSpawner>(true).Length != 0)
            throw new InvalidOperationException($"{Context} must not contain a PropSpawner.");

        var RootViews = Root.GetComponents<PhotonView>();
        Marshmallows = OrderedMarshmallows(Root, Context);
        if (RootViews.Length != 1 || !ValidView(RootViews[0], RequireUnassignedViews)
            || Root.GetComponentsInChildren<PhotonView>(true).Length != OutputCount + 1)
            throw new InvalidOperationException(
                $"{Context} must own {OutputCount + 1} enabled PhotonViews: one on the root and one per marshmallow output.");
        foreach (var Marshmallow in Marshmallows)
            if (!NativeGloomOutput(Root, Marshmallow, RequireUnassignedViews))
                throw new InvalidOperationException(
                    $"{Context} must own exactly {OutputCount} direct operational one-item Campfire-marshmallow outputs.");
    }

    static BerryBush[] OrderedMarshmallows(Transform Root, string Context)
    {
        var Marshmallows = Root.GetComponentsInChildren<BerryBush>(true);
        if (Marshmallows.Length != OutputCount)
            throw new InvalidOperationException(
                $"{Context} must own exactly {OutputCount} direct operational one-item Campfire-marshmallow outputs.");
        var Result = new BerryBush[OutputCount];
        foreach (var Marshmallow in Marshmallows)
        {
            var Index = Marshmallow.playersInRoomRequirement - 1;
            if (Index < 0 || Index >= Result.Length || Result[Index])
                throw new InvalidOperationException(
                    $"{Context} must own Campfire-marshmallow player requirements 1 through {OutputCount} exactly once.");
            Result[Index] = Marshmallow;
        }
        return Result;
    }

    static void ValidateRespawn(
        Transform Root,
        RespawnChest Chest,
        string Context)
    {
        var RootViews = Root.GetComponents<PhotonView>();
        var ChestViews = Chest.GetComponents<PhotonView>();
        if (!Root.gameObject.activeSelf || !Chest.enabled || !Chest.gameObject.activeSelf
            || RootViews.Length != 1 || !ValidView(RootViews[0], false)
            || ChestViews.Length != 1 || !ValidView(ChestViews[0], false)
            || Root.GetComponentsInChildren<PhotonView>(true).Length != 2
            || Chest.spawnPool != SpawnPool.RespawnCoffin
            || Chest.spawnMode != Spawner.SpawnMode.SpawnPool
            || Chest.spawnPointMode != Spawner.SpawnPointMode.SingleList
            || !Chest.baseSpawnChance.Equals(1f) || Chest.spawnOnStart
            || Chest.playersInRoomRequirement != 0 || Chest.belowAscentRequirement != -1
            || Chest.normalRunsOnly || !Chest.centerItemsVisually
            || !Chest.skeleton || !TransformOwnership.Contains(Root, Chest.skeleton.transform))
            throw new InvalidOperationException($"{Context} does not match PEAK's normal RespawnChest contract.");
    }

    static bool NativeGloomOutput(
        Transform Root,
        BerryBush Marshmallow,
        bool RequireUnassignedView) =>
        Marshmallow && Marshmallow.enabled
        && NativeFixtures.SameObject(Marshmallow.transform.parent, Root)
        && Marshmallow.GetComponents<BerryBush>().Length == 1
        && Marshmallow.spawnPool == SpawnPool.Campfire
        && Marshmallow.spawnMode == Spawner.SpawnMode.SpawnPool && Marshmallow.spawnOnStart
        && Marshmallow.spawnPointMode == Spawner.SpawnPointMode.SingleList
        && Marshmallow.baseSpawnChance.Equals(1f) && Marshmallow.possibleBerries.Equals(Vector2.one)
        && Marshmallow.belowAscentRequirement == -1 && !Marshmallow.normalRunsOnly
        && Marshmallow.spawnSpots is { Count: 1 } Spots
        && NativeFixtures.SameObject(Spots[0], Marshmallow.transform)
        && TransformOwnership.DiscoverableWhenActivated(Root, Marshmallow.transform)
        && Marshmallow.GetComponents<PhotonView>() is { Length: 1 } Views
        && ValidView(Views[0], RequireUnassignedView);

    static bool ValidView(PhotonView View, bool RequireUnassigned) =>
        View && View.enabled && (RequireUnassigned
            ? View.ViewID == 0 && PhotonCloneIdentity.IsBatchOrigin(View)
            : View.sceneViewId > 0 && View.InstantiationId == 0);
}
