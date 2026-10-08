using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class PeakCampfireRuntime
{
    sealed class PeakCampfireFixture
    {
        internal PeakCampfireFixture(
            int Index,
            Transform Root,
            global::Campfire Campfire,
            BerryBush[] Marshmallows,
            SingleItemSpawner Backpack)
        {
            this.Index = Index;
            this.Root = Root;
            this.Campfire = Campfire;
            this.Marshmallows = Marshmallows;
            this.Backpack = Backpack;
            foreach (var Marshmallow in Marshmallows) Marshmallow.enabled = false;
            Backpack.enabled = false;
        }

        internal int Index { get; }
        internal Transform Root { get; }
        internal global::Campfire Campfire { get; }
        internal BerryBush[] Marshmallows { get; }
        internal SingleItemSpawner Backpack { get; }
        internal ISpawner? ActiveSpawner { get; set; }

        internal ISpawner Output(int Index) => Index < Marshmallows.Length
            ? Marshmallows[Index]
            : Index == Marshmallows.Length
                ? Backpack
                : throw new ArgumentOutOfRangeException(nameof(Index));
    }

    readonly MapRun Owner;
    readonly PeakCheckpointRuntime Checkpoints;
    readonly Dictionary<global::Campfire, PeakCampfireFixture> FixtureIndex = new();
    readonly Dictionary<ISpawner, PeakCampfireFixture> OutputIndex = new();
    PeakCampfireFixture?[] Fixtures = Array.Empty<PeakCampfireFixture?>();
    bool PlayerOutputReconciliationRequested;

    internal PeakCampfireRuntime(MapRun Owner, PeakCheckpointRuntime Checkpoints)
    {
        this.Owner = Owner ?? throw new ArgumentNullException(nameof(Owner));
        this.Checkpoints = Checkpoints ?? throw new ArgumentNullException(nameof(Checkpoints));
    }

    internal bool Unconfigured => Fixtures.Length == 0
        && FixtureIndex.Count == 0 && OutputIndex.Count == 0;
    internal bool HasFixture(int Index) =>
        Index >= 0 && Index < Fixtures.Length && Fixtures[Index] is not null;
    internal bool Owns(int Index) => HasFixture(Index);
    internal bool HasPendingPlayerOutputReconciliation =>
        PlayerOutputReconciliationRequested;

    internal void Configure(
        PreparedMap Prepared,
        NativePeakCampfireSource? Source)
    {
        if (Fixtures.Length != 0 || FixtureIndex.Count != 0 || OutputIndex.Count != 0)
            throw new InvalidOperationException("A map run configured its Peak campfires more than once.");
        Fixtures = new PeakCampfireFixture?[Prepared.Slots.Length];
        if (Source is null)
        {
            foreach (var Slot in Prepared.Slots)
                if (Slot.BridgeTarget is not null)
                    throw new InvalidOperationException("A configured Peak bridge has no native campfire source fixture.");
            return;
        }

        foreach (var Slot in Prepared.Slots)
        {
            var Target = Slot.BridgeTarget;
            if (Target is null) continue;
            if (!Checkpoints.HasCheckpoint(Slot.Index))
                throw new InvalidOperationException("A configured Peak campfire has no checkpoint authority.");
            var CampfireObject = NativeFixtures.InstantiateInactivePrototype(
                Source.CampfireRoot.gameObject, Target.Root, Prepared.CreatedObjects);
            CampfireObject.name = $"PLL_PeakCampfire_{Slot.Index:D3}";
            CampfireObject.transform.SetPositionAndRotation(
                Target.CampfirePose.position, Target.CampfirePose.rotation);
            PhotonCloneIdentity.Reset(CampfireObject);
            var Campfire = Source.ResolveCampfire(CampfireObject.transform);
            Campfire.disableFogFakeMountain = false;
            var Marshmallows = Source.ResolveMarshmallows(CampfireObject.transform);

            var BackpackObject = new GameObject("Backpack_Spawner");
            Prepared.CreatedObjects.Add(BackpackObject);
            BackpackObject.transform.SetParent(CampfireObject.transform, false);
            BackpackObject.transform.localPosition = Source.BackpackPose.position;
            BackpackObject.transform.localRotation = Source.BackpackPose.rotation;
            var Backpack = BackpackObject.AddComponent<SingleItemSpawner>();
            Backpack.prefab = Source.Backpack.prefab;
            Backpack.isKinematic = Source.Backpack.isKinematic;
            Backpack.playersInRoomRequirement = Source.Backpack.playersInRoomRequirement;
            Backpack.belowAscentRequirement = Source.Backpack.belowAscentRequirement;
            Campfire.advanceToSegment = Slot.Index + 1 < Prepared.Slots.Length
                ? Prepared.Slots[Slot.Index + 1].NavigationRole
                : Slot.NavigationRole;

            var Fixture = new PeakCampfireFixture(
                Slot.Index, CampfireObject.transform, Campfire, Marshmallows, Backpack);
            Fixtures[Slot.Index] = Fixture;
            FixtureIndex.Add(Campfire, Fixture);
            foreach (var Marshmallow in Marshmallows) OutputIndex.Add(Marshmallow, Fixture);
            OutputIndex.Add(Backpack, Fixture);
            CampfireObject.SetActive(true);
        }
    }

    internal void Publish()
    {
        if (FixtureIndex.Count == 0) return;
        if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.Publishing)
            throw new InvalidOperationException("Peak campfires can only publish for their owning map run.");
        var Spawners = new List<Component>(OutputIndex.Count);
        foreach (var Current in Fixtures)
        {
            if (Current is null) continue;
            PeakSuccessorRuntime.RequirePublishedViews(Current.Root, "Peak campfire");
            foreach (var Marshmallow in Current.Marshmallows)
                Spawners.Add(Marshmallow);
            Spawners.Add(Current.Backpack);
        }
        SpawnTrackerTransaction.PrepareGeneratedSpawnTrackers(Spawners);
        Bind();
    }


    internal bool TryGetCheckpointSlotForTarget(int TargetSlot, out int CampfireSlot)
    {
        CampfireSlot = TargetSlot - 1;
        if (HasFixture(CampfireSlot)) return true;
        CampfireSlot = TargetSlot;
        return HasFixture(CampfireSlot);
    }

    internal bool TryGetSavedLitCheckpointForTarget(
        int TargetSlot,
        IReadOnlyList<byte> SavedStates,
        out int CampfireSlot,
        out Transform Root,
        out global::Campfire Campfire)
    {
        if (SavedStates.Count != Fixtures.Length)
            throw new InvalidOperationException("Saved Peak checkpoint state does not match the fixture slots.");
        var Previous = TargetSlot - 1;
        if (TryGetSavedLit(Previous, SavedStates, out var Current)
            || TryGetSavedLit(TargetSlot, SavedStates, out Current))
        {
            CampfireSlot = Current.Index;
            Root = Current.Root;
            Campfire = Current.Campfire;
            return true;
        }
        CampfireSlot = -1;
        Root = null!;
        Campfire = null!;
        return false;
    }

    internal bool TryGetLitCheckpointForTarget(
        int TargetSlot,
        out int CampfireSlot,
        out Transform Root,
        out global::Campfire Campfire)
    {
        if (TryGetLit(TargetSlot - 1, out var Current) || TryGetLit(TargetSlot, out Current))
        {
            CampfireSlot = Current.Index;
            Root = Current.Root;
            Campfire = Current.Campfire;
            return true;
        }
        CampfireSlot = -1;
        Root = null!;
        Campfire = null!;
        return false;
    }
    internal void Bind()
    {
        foreach (var Current in Fixtures)
            if (Current is not null && Current.Campfire && !Owner.Slots[Current.Index].Released)
                Campfire.Bind(Current.Campfire, Current.Index);
    }

    internal void Bind(int Index)
    {
        if (Index >= 0 && Index < Fixtures.Length && Fixtures[Index] is { } Current
            && Current.Campfire && !Owner.Slots[Index].Released)
            Campfire.Bind(Current.Campfire, Index);
    }

    internal bool EveryoneInRange(int Index) =>
        Index >= 0 && Index < Fixtures.Length && Fixtures[Index] is { } Current
        && Current.Campfire.EveryoneInRange();

    internal bool TryAuthorizeLight(int Index)
    {
        if (!PhotonNetwork.IsMasterClient || !CurrentSlot(Index)
            || !Owner.Peak.CompletionReady(Index) || Owner.Peak.CampfireLit(Index)
            || !Checkpoints.TryGet(Index, out var Checkpoint) || !Checkpoints.IsCurrent(Checkpoint)
            || !Checkpoint.CompletionInteractionReady || !EveryoneInRange(Index)) return false;
        return Owner.Peak.PublishCampfireCompletion(Index, Owner.Peak.CampfireOutputs(Index));
    }

    internal bool OnCampfireLight(global::Campfire Campfire)
    {
        if (!Campfire || !FixtureIndex.TryGetValue(Campfire, out var Current)) return false;
        if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.HasPublishedLayout)
            throw new InvalidOperationException("A Peak campfire light RPC has no owning published map run.");
        if (!Owner.Peak.ApplyingCompletion && !Owner.Peak.CampfireLit(Current.Index))
            throw new InvalidOperationException("A Peak campfire lit before its retained completion projection.");
        return true;
    }

    internal void HydrateLit(int Index)
    {
        if (Index < 0 || Index >= Fixtures.Length || Fixtures[Index] is not { } Current
            || Owner.Slots[Index].Released || Current.Campfire.Lit) return;
        if (!Owner.Peak.ApplyingCompletion)
            throw new InvalidOperationException("Peak campfire hydration requires its completion projection owner.");
        Current.Campfire.Light_Rpc(false, 0f);
        if (!Current.Campfire.Lit)
            throw new InvalidOperationException("Peak campfire completion hydration did not produce a lit campfire.");
    }

    internal void RequestPlayerOutputReconciliation()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PlayerOutputReconciliationRequested = true;
        ReconcilePendingPlayerOutputs();
    }

    internal void ReconcilePendingPlayerOutputs()
    {
        if (!PlayerOutputReconciliationRequested) return;
        if (!PhotonNetwork.IsMasterClient)
        {
            PlayerOutputReconciliationRequested = false;
            return;
        }
        if (Owner.Quicksave.PendingCampfireSaveTarget >= 0
            || Owner.Transitions.Lease.Current
                != MapTransitionCoordinator.TransitionLease.State.Idle) return;
        PlayerOutputReconciliationRequested = false;
        Dictionary<PeakRoomItemOperation, PhotonView>? Inventory = null;
        for (var i = 0; i < Fixtures.Length; i++)
        {
            if (!OutputsReady(i)) continue;
            var Eligible = EligibleOutputs(Fixtures[i]!);
            if ((Eligible & ~Owner.Peak.CampfireOutputs(i)) == 0) continue;
            Inventory ??= PeakRoomItemOperations.CaptureInventory(Owner);
            ReconcileOutputs(Fixtures[i]!, Eligible, RestoreCompleted: false, Inventory);
        }
    }

    internal void ReconcileOutputs(
        int Index,
        bool RestoreCompleted,
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory)
    {
        if (!PhotonNetwork.IsMasterClient || !OutputsReady(Index)) return;
        var Eligible = EligibleOutputs(Fixtures[Index]!);
        if (!RestoreCompleted && (Eligible & ~Owner.Peak.CampfireOutputs(Index)) == 0) return;
        ReconcileOutputs(Fixtures[Index]!, Eligible, RestoreCompleted, Inventory);
    }

    internal void RecoverAuthority(
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView> Inventory)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PlayerOutputReconciliationRequested = false;
        for (var i = 0; i < Fixtures.Length; i++)
            if (OutputsReady(i))
                ReconcileOutputs(Fixtures[i]!, EligibleOutputs(Fixtures[i]!), RestoreCompleted: true, Inventory);
    }

    internal void ReconcileCurrentOutputs()
    {
        if (!PhotonNetwork.IsMasterClient || Map.Index < 0 || Map.Index >= Owner.Slots.Length) return;
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory = null;
        foreach (var Slot in Owner.Slots[Map.Index].Occurrence.Slots)
        {
            if (!OutputsReady(Slot.Index)) continue;
            var Eligible = EligibleOutputs(Fixtures[Slot.Index]!);
            if ((Eligible & ~Owner.Peak.CampfireOutputs(Slot.Index)) == 0) continue;
            Inventory ??= PeakRoomItemOperations.CaptureInventory(Owner);
            ReconcileOutputs(Fixtures[Slot.Index]!, Eligible, RestoreCompleted: false, Inventory);
        }
    }

    internal void ReconcileActivatedCheckpoint(int Index)
    {
        if (!PhotonNetwork.IsMasterClient || !OutputsReady(Index)
            || Owner.Peak.CampfireOutputs(Index) == 0) return;
        ReconcileOutputs(Fixtures[Index]!, 0, RestoreCompleted: true,
            PeakRoomItemOperations.CaptureInventory(Owner));
    }

    internal void RetireSlot(int Index)
    {
        if (Index < 0 || Index >= Fixtures.Length || Fixtures[Index] is not { } Current) return;
        Campfire.Unbind(Current.Campfire);
        FixtureIndex.Remove(Current.Campfire);
        foreach (var Marshmallow in Current.Marshmallows) OutputIndex.Remove(Marshmallow);
        OutputIndex.Remove(Current.Backpack);
        Fixtures[Index] = null;
    }

    internal void ReleaseState()
    {
        foreach (var Current in Fixtures)
            if (Current is not null) Campfire.Unbind(Current.Campfire);
        FixtureIndex.Clear();
        OutputIndex.Clear();
        Fixtures = Array.Empty<PeakCampfireFixture?>();
        PlayerOutputReconciliationRequested = false;
    }

    internal byte EligibleOutputMask(int Index)
    {
        if (!OwnedSlot(Index) || Fixtures[Index] is not { } Current) return 0;
        return EligibleOutputs(Current);
    }

    static byte EligibleOutputs(PeakCampfireFixture Current)
    {
        byte Result = 0;
        for (byte Output = 0; Output <= Current.Marshmallows.Length; Output++)
            if (EligibleOutput(Current.Output(Output)))
                Result |= (byte)(1 << Output);
        return Result;
    }

    void ReconcileOutputs(
        PeakCampfireFixture Current,
        byte Eligible,
        bool RestoreCompleted,
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory)
    {
        var Completed = Owner.Peak.CampfireOutputs(Current.Index);
        var Result = Completed;
        for (byte Output = 0; Output <= Current.Marshmallows.Length; Output++)
        {
            var Bit = (byte)(1 << Output);
            var Spawner = Current.Output(Output);
            var Operation = PeakRoomItemOperations.Create(
                Owner, Output < Current.Marshmallows.Length
                    ? PeakRoomItemKind.Marshmallow : PeakRoomItemKind.Backpack,
                Current.Index, Output);
            if ((Result & Bit) != 0)
            {
                if (RestoreCompleted && !TrackLiveOutput(Spawner, Operation, Inventory))
                    RestoreSavedOutput(Current, Spawner, Operation);
                continue;
            }
            if ((Eligible & Bit) == 0) continue;
            SpawnOutput(Current, Spawner, Operation, Inventory);
            Result |= Bit;
        }
        if (Result != Completed && !Owner.Peak.PublishCampfireOutputs(Current.Index, Result))
            throw new InvalidOperationException("Peak campfire output completion could not be published.");
    }

    static bool TrackLiveOutput(
        ISpawner Spawner,
        PeakRoomItemOperation Operation,
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory)
    {
        var Found = Inventory is null
            ? PeakRoomItemOperations.TryAdopt(Operation, out var View)
            : PeakRoomItemOperations.TryAdopt(Operation, Inventory, out View);
        if (!Found) return false;
        PeakRoomItemOperations.Track(RequireTracker(Spawner), View);
        return true;
    }

    static void RestoreSavedOutput(
        PeakCampfireFixture Fixture,
        ISpawner Spawner,
        PeakRoomItemOperation Operation)
    {
        var Tracker = RequireTracker(Spawner);
        _ = PeakRoomItemOperations.RestoreLoadedHistory(
            Tracker, Operation, () => InvokeOutputSpawner(Fixture, Spawner));
    }

    static void SpawnOutput(
        PeakCampfireFixture Fixture,
        ISpawner Spawner,
        PeakRoomItemOperation Operation,
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory)
    {
        if (TrackLiveOutput(Spawner, Operation, Inventory)) return;
        var Spawned = PeakRoomItemOperations.Execute(
            Operation, () => InvokeOutputSpawner(Fixture, Spawner));
        if (Spawned.Count != 1 || !Operation.Matches(Spawned[0].InstantiationData))
            throw new InvalidOperationException("A Peak campfire output did not produce one marked room item.");
        PeakRoomItemOperations.Track(RequireTracker(Spawner), Spawned[0]);
    }

    static List<PhotonView> InvokeOutputSpawner(
        PeakCampfireFixture Fixture,
        ISpawner Spawner)
    {
        Fixture.ActiveSpawner = Spawner;
        try { return Spawner.TrySpawnItems(); }
        finally { Fixture.ActiveSpawner = null; }
    }

    static Peak.SpawnedItemTracker RequireTracker(ISpawner Spawner) =>
        ((Component)Spawner).GetComponent<Peak.SpawnedItemTracker>()
        ?? throw new InvalidOperationException("A Peak campfire output lost its spawn tracker.");

    bool TryGetSavedLit(
        int Index,
        IReadOnlyList<byte> SavedStates,
        out PeakCampfireFixture Fixture)
    {
        Fixture = null!;
        return Index >= 0 && Index < Fixtures.Length && Fixtures[Index] is { } Current
            && PeakSuccessorRuntime.QuicksaveCampfireLit(SavedStates[Index])
            && (Fixture = Current) is not null;
    }

    bool TryGetLit(int Index, out PeakCampfireFixture Fixture)
    {
        Fixture = null!;
        return Index >= 0 && Index < Fixtures.Length && Fixtures[Index] is { } Current
            && Owner.Peak.CampfireLit(Index) && (Fixture = Current) is not null;
    }

    bool CurrentSlot(int Index) =>
        ReferenceEquals(Map.CurrentRun, Owner) && Owner.Active && Map.Index == Index
        && Index >= 0 && Index < Owner.Slots.Length && !Owner.Slots[Index].Released
        && Fixtures[Index] is not null;

    bool OwnedSlot(int Index) =>
        ReferenceEquals(Map.CurrentRun, Owner) && Owner.HasPublishedLayout
        && Index >= 0 && Index < Owner.Slots.Length && !Owner.Slots[Index].Released
        && Fixtures[Index] is not null;

    bool OutputsReady(int Index)
    {
        if (!OwnedSlot(Index) || !Owner.Active || !Owner.Peak.CampfireOutputsReady
            || !Fixtures[Index]!.Root.gameObject.activeInHierarchy
            || Map.Index < 0 || Map.Index >= Owner.Slots.Length) return false;
        var Occurrence = Owner.Slots[Index].Occurrence;
        if (!ReferenceEquals(Occurrence, Owner.Slots[Map.Index].Occurrence)
            && Owner.Peak.CampfireOutputs(Index) == 0) return false;
        foreach (var Slot in Occurrence.Slots)
            if (!Slot.FeaturesReady || !Slot.ViewBatchReady) return false;
        return true;
    }

    static bool EligibleOutput(ISpawner Spawner) => Spawner switch
    {
        global::Spawner Native => Native.playersInRoomRequirement <= (PhotonNetwork.CurrentRoom?.PlayerCount ?? 0)
            && (Native.belowAscentRequirement == -1 || Ascents.currentAscent < Native.belowAscentRequirement),
        SingleItemSpawner Single => Single.playersInRoomRequirement <= (PhotonNetwork.CurrentRoom?.PlayerCount ?? 0)
            && (Single.belowAscentRequirement == -1 || Ascents.currentAscent < Single.belowAscentRequirement),
        _ => throw new InvalidOperationException("A Peak campfire output has an unsupported spawner type.")
    };

    bool TryHandleOutputSpawner(ISpawner Spawner, out List<PhotonView> Result)
    {
        Result = null!;
        if (Spawner is not Component Component || !Component || !OutputIndex.TryGetValue(Spawner, out var Current)) return false;
        if (ReferenceEquals(Current.ActiveSpawner, Spawner)) return false;
        Result = new List<PhotonView>();
        if (PhotonNetwork.IsMasterClient)
            ReconcileOutputs(Current.Index, RestoreCompleted: false, Inventory: null);
        return true;
    }

    bool BlocksInteraction(global::Campfire Campfire)
    {
        if (!Campfire || !FixtureIndex.TryGetValue(Campfire, out var Current)) return false;
        if (!Owner.Peak.CompletionReady(Current.Index) || Owner.Peak.CampfireLit(Current.Index))
            return !Owner.Peak.CampfireLit(Current.Index);
        return !Checkpoints.TryGet(Current.Index, out var Checkpoint)
            || !Checkpoints.IsCurrent(Checkpoint) || !Checkpoint.CompletionInteractionReady;
    }

    bool HandleInteraction(global::Campfire Campfire)
    {
        if (!Campfire || !FixtureIndex.TryGetValue(Campfire, out var Current)) return false;
        if (Owner.Peak.CampfireLit(Current.Index)) return false;
        if (!BlocksInteraction(Campfire))
            Owner.Network.DeferredOperations.RequestPeakCampfireLight(Current.Index);
        return true;
    }

    static PeakCampfireRuntime? Current => Map.CurrentRun?.Peak.Campfire;

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.TrySpawnItems))]
    static class NativeOutputSpawnGatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Spawner __instance, ref List<PhotonView> __result) =>
            Current?.TryHandleOutputSpawner(__instance, out __result) != true;
    }

    [HarmonyPatch(typeof(SingleItemSpawner), nameof(SingleItemSpawner.TrySpawnItems))]
    static class SingleOutputSpawnGatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(SingleItemSpawner __instance, ref List<PhotonView> __result) =>
            Current?.TryHandleOutputSpawner(__instance, out __result) != true;
    }

    [HarmonyPatch(typeof(global::Campfire), nameof(global::Campfire.IsConstantlyInteractable))]
    static class CampfireAvailabilityPatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::Campfire __instance, ref bool __result)
        {
            if (Current?.BlocksInteraction(__instance) != true) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(global::Campfire), nameof(global::Campfire.Interact_CastFinished))]
    static class CampfireInteractionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::Campfire __instance) =>
            Current?.HandleInteraction(__instance) != true;
    }
}
