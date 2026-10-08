using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

[HarmonyPatch]
internal sealed class PeakStatueRewardRuntime
{

    sealed class PeakStatueReward
    {
        internal PeakStatueReward(
            int Index,
            Peak.ScoutStatue Statue,
            Transform Root,
            RespawnChest Chest,
            Peak.SpawnedItemTracker Tracker)
        {
            this.Index = Index;
            this.Statue = Statue;
            this.Root = Root;
            this.Chest = Chest;
            this.Tracker = Tracker;
        }

        internal int Index { get; }
        internal Peak.ScoutStatue Statue { get; }
        internal Transform Root { get; }
        internal RespawnChest Chest { get; }
        internal Peak.SpawnedItemTracker Tracker { get; }
        internal PeakRoomItemOperation? ItemOperation { get; set; }
        internal bool Published => ItemOperation.HasValue;
        internal bool NativeEventsRaised { get; set; }
    }

    readonly MapRun Owner;
    PeakStatueReward?[] Rewards = Array.Empty<PeakStatueReward?>();

    internal PeakStatueRewardRuntime(MapRun Owner) =>
        this.Owner = Owner ?? throw new ArgumentNullException(nameof(Owner));

    internal bool Unconfigured => Rewards.Length == 0;
    internal bool HasFixture(int Index) =>
        Index >= 0 && Index < Rewards.Length && Rewards[Index] is not null;

    internal void Configure(
        PreparedMap Prepared,
        NativePeakCampfireSource? Source)
    {
        if (Rewards.Length != 0)
            throw new InvalidOperationException("A map run configured its Peak statue rewards more than once.");
        Rewards = new PeakStatueReward?[Prepared.Slots.Length];
        if (Source is null)
        {
            foreach (var Slot in Prepared.Slots)
                if (Slot.BridgeTarget is not null)
                    throw new InvalidOperationException("A configured Peak bridge has no native reward source fixture.");
            return;
        }

        foreach (var Slot in Prepared.Slots)
        {
            var Target = Slot.BridgeTarget;
            if (Target is null) continue;
            var RewardObject = NativeFixtures.InstantiateInactivePrototype(
                Source.RespawnRoot.gameObject, Target.Root, Prepared.CreatedObjects);
            RewardObject.name = $"PLL_PeakStatueReward_{Slot.Index:D3}";
            RewardObject.transform.SetPositionAndRotation(
                Target.Statue.transform.position, Target.Statue.transform.rotation);
            PhotonCloneIdentity.Reset(RewardObject);
            var Chest = Source.ResolveChest(RewardObject.transform);
            Chest.spawnSpots.Clear();
            Chest.spawnSpots.Add(Target.GemSpot);
            Chest.SegmentNumber = Slot.NavigationRole;
            if (Chest.skeleton) Chest.skeleton.gameObject.SetActive(false);
            foreach (var Renderer in RewardObject.GetComponentsInChildren<Renderer>(true)) Renderer.enabled = false;
            foreach (var Collider in RewardObject.GetComponentsInChildren<Collider>(true)) Collider.enabled = false;
            foreach (var Audio in RewardObject.GetComponentsInChildren<AudioSource>(true)) Audio.enabled = false;
            var Tracker = Chest.GetComponent<Peak.SpawnedItemTracker>()
                ?? Chest.gameObject.AddComponent<Peak.SpawnedItemTracker>();
            Rewards[Slot.Index] = new PeakStatueReward(
                Slot.Index, Target.Statue, RewardObject.transform, Chest, Tracker);
            RewardObject.SetActive(true);
        }
    }

    internal void Publish()
    {
        if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.Publishing)
            throw new InvalidOperationException("Peak statue rewards can only publish for their owning map run.");
        var Spawners = new List<Component>();
        foreach (var Current in Rewards)
        {
            if (Current is null) continue;
            PeakSuccessorRuntime.RequirePublishedViews(Current.Root, "Peak statue reward");
            Spawners.Add(Current.Chest);
        }
        SpawnTrackerTransaction.PrepareGeneratedSpawnTrackers(Spawners);
        foreach (var Current in Rewards)
            if (Current is not null) Current.ItemOperation = PeakRoomItemOperations.Create(
                Owner, PeakRoomItemKind.StatueReward, Current.Index, 0);
    }

    internal bool CanUse(int Index) =>
        Owner.Peak.CheckpointMutationOpen(Index)
        && Owner.Peak.CompletionReady(Index) && Owner.Peak.RewardAvailable(Index)
        && Index >= 0 && Index < Rewards.Length && Rewards[Index] is { Published: true }
        && Owner.Peak.Checkpoints.TryGet(Index, out var Checkpoint)
        && Owner.Peak.Checkpoints.IsCurrent(Checkpoint);

    internal bool RetainsStatueGem(int Index) =>
        Index >= 0 && Index < Rewards.Length && Rewards[Index] is { Published: true } Current
        && (Owner.Peak.HonorRequired(Index) && !Owner.Peak.HonorComplete(Index)
            || !Owner.Peak.RewardSpent(Index) && !Current.Tracker.HasSpawnHistory);

    internal string InteractionText(int Index) => RequireAvailable(Index).Chest.GetInteractionText();
    internal string Name(int Index) => RequireAvailable(Index).Chest.GetName();
    internal float InteractTime(int Index, Character Interactor) =>
        RequireAvailable(Index).Chest.GetInteractTime(Interactor);

    internal void Interact(int Index, Character Interactor)
    {
        if (!Interactor || !CanUse(Index)) return;
        Owner.Network.DeferredOperations.RequestPeakStatueReward(Index, Interactor);
    }

    internal bool TryAuthorizeOpening(int Index, int Actor)
    {
        if (!PhotonNetwork.IsMasterClient || !CanUse(Index)
            || !Owner.Peak.Checkpoints.TryGet(Index, out var Checkpoint)) return false;
        if (PhotonNetwork.CurrentRoom is { IsOffline: false })
        {
            Character? Interactor = null;
            foreach (var Character in PlayerHandler.GetAllPlayerCharacters())
            {
                if (!Character || !Character.photonView
                    || Character.photonView.OwnerActorNr != Actor) continue;
                if (Interactor)
                    throw new InvalidOperationException("A Photon actor owns multiple Peak reward characters.");
                Interactor = Character;
            }
            var View = Interactor ? Interactor.photonView : null;
            if (!Interactor || Interactor.data.dead || Interactor.data.fullyPassedOut
                || !View || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View)
                || Vector3.Distance(Interactor.Center, Checkpoint.Statue.transform.position)
                    > global::Interaction.instance.distance) return false;
        }
        return Owner.Peak.PublishRewardOpening(Index, Actor);
    }

    internal void ApplyHonorCompletion(int Index)
    {
        if (Index < 0 || Index >= Rewards.Length || Rewards[Index] is not { } Current
            || Owner.Slots[Index].Released || !Current.Chest.IsSpent) return;
        RemoveStatueGem(Current);
    }

    internal void ApplyCompletion(
        int Index,
        PeakRewardState State,
        int Actor)
    {
        if (Index < 0 || Index >= Rewards.Length || Rewards[Index] is not { } Current
            || Owner.Slots[Index].Released
            || State is PeakRewardState.Available or PeakRewardState.Opening) return;
        if (!Owner.Peak.ApplyingCompletion || !Current.Published)
            throw new InvalidOperationException("Peak statue projection requires its published completion owner.");
        if (!Current.Chest.IsSpent) Current.Chest.OpenLuggageRPC(false);
        if (!Current.Chest.IsSpent)
            throw new InvalidOperationException("Peak statue projection did not produce an open statue reward.");
        if (State is PeakRewardState.OpeningReward or PeakRewardState.OpeningRevive)
            RaiseNativeEvents(Current, Actor);
    }

    internal PeakRewardState SelectResolution() =>
        Ascents.canReviveDead && Character.PlayerIsDeadOrDown()
            ? PeakRewardState.OpeningRevive
            : PeakRewardState.OpeningReward;

    internal bool TryCompleteOpening(
        int Index,
        PeakRewardState State)
    {
        if (Index < 0 || Index >= Rewards.Length
            || Rewards[Index] is not { ItemOperation: { } Operation } Current
            || Owner.Slots[Index].Released || !PhotonNetwork.IsMasterClient) return false;
        if (State is not (PeakRewardState.OpeningReward or PeakRewardState.OpeningRevive)) return false;
        if (State == PeakRewardState.OpeningRevive)
        {
            var Handler = Owner.Handler;
            if (!Handler)
                throw new InvalidOperationException("The Peak statue reward lost its owning map handler.");
            Handler.LastRevivedSegment = (int)Current.Chest.SegmentNumber;
            Current.Chest.RespawnAllPlayersHere();
            RemoveStatueGemWhenComplete(Current);
            return true;
        }
        if (TryAdoptRewardItem(Current, Operation)) return true;
        SpawnRewardItem(Current, Operation);
        return true;
    }

    internal void ReconcileSpent(
        int Index,
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory)
    {
        if (!PhotonNetwork.IsMasterClient || Index < 0 || Index >= Rewards.Length
            || Rewards[Index] is not { ItemOperation: { } Operation } Current
            || Owner.Slots[Index].Released || !Owner.Peak.RewardSpent(Index)) return;
        var Adopted = Inventory is null
            ? PeakRoomItemOperations.TryAdopt(Operation, out var RewardView)
            : PeakRoomItemOperations.TryAdopt(Operation, Inventory, out RewardView);
        if (Adopted) PeakRoomItemOperations.Track(Current.Tracker, RewardView);
        else
            _ = PeakRoomItemOperations.RestoreLoadedHistory(
                Current.Tracker, Operation, Current.Chest.TrySpawnItems);
        RemoveStatueGemWhenComplete(Current);
    }

    internal void RecoverAuthority(
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView> Inventory)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        for (var i = 0; i < Rewards.Length; i++) ReconcileSpent(i, Inventory);
    }

    internal void RetireSlot(int Index)
    {
        if (Index >= 0 && Index < Rewards.Length) Rewards[Index] = null;
    }

    internal void ReleaseState()
    {
        Rewards = Array.Empty<PeakStatueReward?>();
    }

    PeakStatueReward RequireAvailable(int Index)
    {
        if (Index < 0 || Index >= Rewards.Length || Rewards[Index] is not { Published: true } Current
            || !Owner.Peak.RewardAvailable(Index))
            throw new InvalidOperationException("The Peak statue reward is unavailable.");
        return Current;
    }

    bool TryAdoptRewardItem(PeakStatueReward Current, PeakRoomItemOperation Operation)
    {
        if (!PeakRoomItemOperations.TryAdopt(Operation, out var RewardView)) return false;
        PeakRoomItemOperations.Track(Current.Tracker, RewardView);
        RemoveStatueGemWhenComplete(Current);
        return true;
    }

    void SpawnRewardItem(PeakStatueReward Current, PeakRoomItemOperation Operation)
    {
        var Spawned = PeakRoomItemOperations.Execute(
            Operation, () => SpawnRewardItems(Current.Chest, Current.Chest.spawnSpots));
        if (Spawned.Count != 1 || !Operation.Matches(Spawned[0].InstantiationData))
            throw new InvalidOperationException(
                $"A Peak statue reward spawned {Spawned.Count} marked room items instead of one.");
        PeakRoomItemOperations.Track(Current.Tracker, Spawned[0]);
        RemoveStatueGemWhenComplete(Current);
    }

    void RemoveStatueGemWhenComplete(PeakStatueReward Current)
    {
        if (!Owner.Peak.HonorRequired(Current.Index) || Owner.Peak.HonorComplete(Current.Index))
            RemoveStatueGem(Current);
    }

    static void RaiseNativeEvents(PeakStatueReward Current, int Actor)
    {
        if (Current.NativeEventsRaised || !Character.localCharacter) return;
        var LocalActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
        if (Actor != 0 && Actor != LocalActor) return;
        Current.NativeEventsRaised = true;
        GlobalEvents.TriggerLuggageOpened(Current.Chest, Character.localCharacter);
        GlobalEvents.TriggerRespawnChestOpened(Current.Chest, Character.localCharacter);
    }

    static void RemoveStatueGem(PeakStatueReward Current)
    {
        var Gem = Current.Statue.spawnedGem_master;
        if (!Gem) return;
        PhotonNetwork.Destroy(Gem);
        Current.Statue.spawnedGem_master = null;
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnItems))]
    static List<PhotonView> SpawnRewardItems(Spawner Instance, List<Transform> SpawnSpots) =>
        throw new InvalidOperationException("The native Peak reward spawner is unavailable.");

}
