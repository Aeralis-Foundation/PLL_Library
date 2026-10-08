using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace PLL.Library;

internal sealed class DeferredMapNetworkOperations
{
    readonly MapRun Run;
    readonly Queue<DeferredNetworkOperation> DeferredOperations = new();
    bool ReplayingDeferredOperation;

    internal DeferredMapNetworkOperations(MapRun Run) =>
        this.Run = Run ?? throw new ArgumentNullException(nameof(Run));

    internal bool HasPending => DeferredOperations.Count != 0;

    internal void Clear()
    {
        DeferredOperations.Clear();
        ReplayingDeferredOperation = false;
    }
    internal abstract class DeferredNetworkOperation
    {
        DeferredNetworkOperation(Room Room, MapRun Run)
        {
            this.Room = Room;
            RunIdentity = Run.Identity;
            GenerationEpoch = Run.GenerationEpoch;
            LayoutRevision = Run.LayoutRevision;
        }

        internal Room Room { get; }
        internal long RunIdentity { get; }
        internal string GenerationEpoch { get; }
        internal int LayoutRevision { get; }

        internal sealed class IncomingRpc : DeferredNetworkOperation
        {
            internal IncomingRpc(PhotonHashtable Data, Photon.Realtime.Player? Sender, Room Room, int ViewId, MapRun Run)
                : base(Room, Run)
            {
                this.Data = Data;
                this.Sender = Sender;
                this.ViewId = ViewId;
            }

            internal PhotonHashtable Data { get; }
            internal Photon.Realtime.Player? Sender { get; }
            internal int ViewId { get; }
        }

        internal sealed class DirectAdvance : DeferredNetworkOperation
        {
            internal DirectAdvance(int Index, Room Room, MapRun Run) : base(Room, Run) => this.Index = Index;
            internal int Index { get; }
        }

        internal sealed class RunFinish : DeferredNetworkOperation
        {
            internal RunFinish(Room Room, MapRun Run) : base(Room, Run) { }
        }
    }

    const int MaximumDeferredNetworkOperations = 256;
    const int RejectedDirectAdvanceRequest = -1;
    const byte DirectAdvanceEventCode = 199;
    const string DirectAdvanceEventProtocol = PLLLibrary.PluginGuid + "/direct-advance/v1";
    const string PeakCampfireLightEventProtocol = PLLLibrary.PluginGuid + "/peak-campfire-light/v1";
    const string PeakStatueRewardEventProtocol = PLLLibrary.PluginGuid + "/peak-statue-reward/v1";
    const string PeakBestowEventProtocol = PLLLibrary.PluginGuid + "/peak-bestow/v2";
    const string PeakBestowRejectedProtocol = PeakBestowEventProtocol + "/rejected";
    static readonly object RpcViewIdKey = (byte)0;
    static readonly object RpcMethodNameKey = (byte)3;
    static readonly object RpcParametersKey = (byte)4;
    static readonly object RpcMethodIndexKey = (byte)5;
    internal static string? RpcMethod(PhotonHashtable Data)
    {
        if (Data[RpcMethodNameKey] is string Method) return Method;
        if (Data[RpcMethodIndexKey] is not byte Index
            || Index >= PhotonNetwork.PhotonServerSettings.RpcList.Count) return null;
        return PhotonNetwork.PhotonServerSettings.RpcList[Index];
    }

    bool IsCampfireRpc(PhotonHashtable Data)
    {
        var Method = RpcMethod(Data);
        return string.Equals(Method, "Extinguish_Rpc", StringComparison.Ordinal)
            || string.Equals(Method, "Light_Rpc", StringComparison.Ordinal);
    }

    bool IsProgressionLightRpc(PhotonHashtable Data) =>
        string.Equals(RpcMethod(Data), "Light_Rpc", StringComparison.Ordinal)
        && Data[RpcParametersKey] is object[] { Length: > 0 } Parameters
        && Parameters[0] is true;

    object SnapshotRpcValue(object Value)
    {
        if (Value is object[] Values)
        {
            var Result = new object[Values.Length];
            for (var i = 0; i < Result.Length; i++) Result[i] = SnapshotRpcValue(Values[i]);
            return Result;
        }
        if (Value is byte[] Bytes) return (byte[])Bytes.Clone();
        if (Value is int[] Integers) return (int[])Integers.Clone();
        if (Value is Array Array) return Array.Clone();
        return Value;
    }

    PhotonHashtable SnapshotRpc(PhotonHashtable Data)
    {
        var Result = new PhotonHashtable(Data.Count);
        foreach (var Field in Data) Result.Add(Field.Key, SnapshotRpcValue(Field.Value));
        return Result;
    }

    bool ReserveDeferredNetworkOperation()
    {
        if (DeferredOperations.Count < MaximumDeferredNetworkOperations) return true;
        LazySlotGenerator.AbortLazyGeneration(Map.Index, "Deferred synchronized network operation queue overflowed");
        return false;
    }

    bool QueueDeferredDirectAdvance(MapRun Run, Room Room, int Index)
    {
        foreach (var Operation in DeferredOperations)
            if (Operation is DeferredNetworkOperation.DirectAdvance Direct
                && Direct.Index == Index) return true;
        if (!ReserveDeferredNetworkOperation()) return false;
        DeferredOperations.Enqueue(
            new DeferredNetworkOperation.DirectAdvance(Index, Room, Run));
        return true;
    }

    bool QueueDeferredRunFinish(MapRun Run, Room Room)
    {
        foreach (var Operation in DeferredOperations)
            if (Operation is DeferredNetworkOperation.RunFinish) return true;
        if (!ReserveDeferredNetworkOperation()) return false;
        DeferredOperations.Enqueue(
            new DeferredNetworkOperation.RunFinish(Room, Run));
        return true;
    }

    bool IsLastDeferredProgressionLight(int ViewId)
    {
        DeferredNetworkOperation? Last = null;
        foreach (var Operation in DeferredOperations) Last = Operation;
        return Last is DeferredNetworkOperation.IncomingRpc Rpc
            && Rpc.ViewId == ViewId && IsProgressionLightRpc(Rpc.Data);
    }

    bool OwnsCampfireProgression(MapRun Run, int Index)
    {
        if (Index == Map.Index) return true;
        var Transition = Run.Transitions.Lease;
        return Transition.Current is MapTransitionCoordinator.TransitionLease.State.Reserved
                or MapTransitionCoordinator.TransitionLease.State.Active
            && Transition.DestinationIndex == Index;
    }

    bool DeferIncomingRpc(PhotonHashtable Data, Photon.Realtime.Player Sender)
    {
        var CurrentRun = Run;
        if (CurrentRun?.HasPublishedLayout != true || ReplayingDeferredOperation
            || PhotonNetwork.CurrentRoom is not { } Room
            || Data[RpcViewIdKey] is not int ViewId || ViewId <= 0 || ViewId >= PhotonNetwork.MAX_VIEW_IDS
            || !IsCampfireRpc(Data)) return false;
        var Queued = DeferredOperations;
        var View = PhotonNetwork.GetPhotonView(ViewId);
        var Progression = IsProgressionLightRpc(Data);
        GeneratedBatchKey Batch;
        try
        {
            if (View)
            {
                if (!Progression && Queued.Count == 0) return false;
                if (Progression)
                {
                    if (!TryGetCampfireSlot(View, out var OwnerIndex))
                        throw new InvalidOperationException(
                            "Synchronized campfire progression lost its owning map slot.");
                    if (OwnerIndex < Map.Index) return false;
                    if (!OwnsCampfireProgression(CurrentRun, OwnerIndex))
                        throw new InvalidOperationException(
                            "Synchronized campfire progression came from a map slot that does not own the active transition.");
                    if (CurrentRun.Active && OwnerIndex == Map.Index && Run.Slots[OwnerIndex].FeaturesReady
                        && Run.Slots[OwnerIndex].ViewBatchReady && Queued.Count == 0
                        && CurrentRun.Transitions.Lease.Current == MapTransitionCoordinator.TransitionLease.State.Idle
                        && CurrentRun.Transitions.QueuedTransitions.Count == 0) return false;
                }
                Batch = default;
            }
            else if (!Run.Network.ViewBatches.TryFindPendingBatch(ViewId, out Batch)) return false;
        }
        catch (InvalidOperationException Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Synchronized Campfire RPC validation failed", Exception);
            return true;
        }
        if (Progression && IsLastDeferredProgressionLight(ViewId)) return true;
        if (!ReserveDeferredNetworkOperation()) return true;
        Queued.Enqueue(new DeferredNetworkOperation.IncomingRpc(
            SnapshotRpc(Data), Sender, Room, ViewId, CurrentRun));
        if (!View && CurrentRun.Active && Batch.Kind == GeneratedBatchKey.Owner.Slot)
            LazySlotGenerator.GenerateLazySlot(Batch.RequestIndex);
        return true;
    }

    bool TryGetCampfireSlot(PhotonView? View, out int Index)
    {
        Index = -1;
        return View && View.TryGetComponent<global::Campfire>(out var CampfireComponent)
            && Campfire.TryGetSlot(CampfireComponent, out Index);
    }

    internal void Replay()
    {
        var CurrentRun = Run;
        if (CurrentRun?.HasPublishedLayout != true || ReplayingDeferredOperation) return;
        var Queued = DeferredOperations;
        if (Queued.Count == 0) return;
        Exception? Failure = null;
        ReplayingDeferredOperation = true;
        try
        {
            while (Queued.Count != 0)
            {
                var Call = Queued.Peek();
                if (!ReferenceEquals(Call.Room, PhotonNetwork.CurrentRoom)
                    || !CurrentRun.Matches(Call.RunIdentity, Call.GenerationEpoch, Call.LayoutRevision))
                    throw new InvalidOperationException(
                        "A deferred synchronized network operation lost its owning map run.");
                var IncomingView = Call is DeferredNetworkOperation.IncomingRpc Incoming
                    ? PhotonNetwork.GetPhotonView(Incoming.ViewId)
                    : null;
                if (!CurrentRun.Active) return;
                if (CurrentRun.Transitions.Lease.Current != MapTransitionCoordinator.TransitionLease.State.Idle
                    || CurrentRun.Transitions.QueuedTransitions.Count != 0) return;
                switch (Call)
                {
                    case DeferredNetworkOperation.IncomingRpc Rpc:
                        if (!IncomingView)
                        {
                            if (!Run.Network.ViewBatches.TryFindPendingBatch(Rpc.ViewId, out var Batch))
                                throw new InvalidOperationException(
                                    "A deferred synchronized RPC lost its published object batch.");
                            if (Batch.Kind == GeneratedBatchKey.Owner.Slot) LazySlotGenerator.GenerateLazySlot(Batch.RequestIndex);
                            return;
                        }
                        if (IsProgressionLightRpc(Rpc.Data))
                        {
                            if (!TryGetCampfireSlot(IncomingView, out var OwnerIndex))
                                throw new InvalidOperationException(
                                    "A deferred progression Campfire RPC lost its owning map slot.");
                            if (OwnerIndex < Map.Index)
                            {
                                Queued.Dequeue();
                                break;
                            }
                            if (!OwnsCampfireProgression(CurrentRun, OwnerIndex))
                                throw new InvalidOperationException(
                                    "A deferred progression Campfire RPC came from a map slot that does not own the active transition.");
                            if (OwnerIndex != Map.Index) return;
                            if (!Run.Slots[OwnerIndex].FeaturesReady || !Run.Slots[OwnerIndex].ViewBatchReady) return;
                        }
                        Queued.Dequeue();
                        PhotonNetwork.ExecuteRpc(Rpc.Data, Rpc.Sender!);
                        break;
                    case DeferredNetworkOperation.DirectAdvance Direct:
                        if (Direct.Index < Map.Index)
                        {
                            Queued.Dequeue();
                            if (Run.Transitions.RequestedDirectAdvance == Direct.Index)
                                Run.Transitions.RequestedDirectAdvance = -1;
                            break;
                        }
                        if (Run.Peak.Campfire.Owns(Direct.Index)
                            && !Run.Peak.CanApplyDirectAdvance(Direct.Index)) return;
                        Queued.Dequeue();
                        Run.Transitions.RequestedDirectAdvance = -1;
                        ApplyDirectAdvance(Direct.Index);
                        break;
                    case DeferredNetworkOperation.RunFinish:
                        if (!PhotonNetwork.IsMasterClient)
                        {
                            Queued.Dequeue();
                            break;
                        }
                        if (!LazySlotGenerator.CanFinishRun(false) || !Character.localCharacter) return;
                        Queued.Dequeue();
                        LazySlotGenerator.FinishRun();
                        break;
                    default:
                        throw new InvalidOperationException(
                            "A deferred synchronized network operation has an unknown kind.");
                }
            }
        }
        catch (Exception Exception)
        {
            Failure = Exception;
        }
        finally
        {
            ReplayingDeferredOperation = false;
        }
        if (Failure is not null)
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Deferred synchronized network operation replay failed", Failure);
            return;
        }
        if (!Run.Network.OwnsCurrentRun || !CurrentRun.Active || Queued.Count != 0) return;
        if (PhotonNetwork.CurrentRoom is { } Room)
        {
            ReleasedSlotRetirement.HandleReleasedProperties(Room.CustomProperties);
            if (!Run.Network.OwnsCurrentRun || !CurrentRun.Active) return;
            HandleRunProperties(Room.CustomProperties);
            if (!Run.Network.OwnsCurrentRun || !CurrentRun.Active) return;
        }
        ReplayPendingJumps();
    }

    [HarmonyPatch(typeof(PhotonNetwork), "ExecuteRpc", typeof(PhotonHashtable), typeof(Photon.Realtime.Player))]
    static class ExecuteRpcPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PhotonHashtable rpcData, Photon.Realtime.Player sender) =>
            Map.CurrentRun is not { } Run || !Run.Network.DeferredOperations.DeferIncomingRpc(rpcData, sender);
    }

    internal void ReplayJump(int Index)
    {
        if (Index < 0 || Index >= Run.Slots.Length || !MapTransitionCoordinator.JumpMutationOpen(Run)) return;
        ReplayQueuedJump(Run.Slots[Index].NavigationRole);
    }

    internal void ReplayPendingJumps()
    {
        var Pending = Run.Transitions.PendingJumps;
        if (Pending.Count == 0 || !MapTransitionCoordinator.JumpMutationOpen(Run)) return;
        var Roles = new Segment[Pending.Count];
        Pending.Keys.CopyTo(Roles, 0);
        foreach (var Role in Roles)
        {
            ReplayQueuedJump(Role);
            if (!MapTransitionCoordinator.JumpMutationOpen(Run)) break;
        }
    }

    bool ReplayQueuedJump(Segment Role)
    {
        if (!MapTransitionCoordinator.JumpMutationOpen(Run)) return false;
        if (!Run.Transitions.PendingJumps.TryGetValue(Role, out var Jump)) return false;
        var Index = MapJumpTransaction.JumpTargetIndex(Role);
        if (Index < Run.Slots.Length)
        {
            if (Run.Slots[Index].Released)
            {
                Run.Transitions.PendingJumps.Remove(Role);
                return false;
            }
            if (!Run.IsOccurrenceReady(Run.Slots[Index].Occurrence)
                || !MapJumpTransaction.PrepareJumpProjection(Run, Index)) return false;
        }
        Run.Transitions.PendingJumps.Remove(Role);
        Run.Transitions.ReplayingJump = true;
        try
        {
            MapHandler.JumpToSegmentLogic(Jump.Segment, Jump.Players, Jump.SendToEveryone, Jump.UpdateFog);
        }
        catch (Exception Exception)
        {
            if (ReferenceEquals(Map.CurrentRun, Run) && Run.Active)
                LazySlotGenerator.AbortLazyGeneration(Run, Index, "Deferred synchronized map jump failed", Exception);
            throw;
        }
        finally
        {
            Run.Transitions.ReplayingJump = false;
        }
        return true;
    }

    internal void Tick()
    {
        if (!Run.Network.OwnsCurrentRun || Run.Features.Aborted
            || !Run.Network.ViewBatches.HasPendingBatches && !Run.Network.RunProperties.HasPending
                && !HasPending && !Run.Peak.RewardOpening && !Run.Peak.HasPendingCompletion
                && !Run.Peak.Campfire.HasPendingPlayerOutputReconciliation
                && !Run.Peak.StableCampfireCompletionPending
                && Run.Transitions.PendingJumps.Count == 0
                && !Run.HookedGeneration.HasWork && !Run.WorldInteractions.HasPending && !Run.ClimbingFixtures.HasWork
                && !Run.PropInteractions.HasWork
                && Run.Retirement.PendingReleasedThrough <= Run.Retirement.ReleasedThrough) return;
        var Room = PhotonNetwork.CurrentRoom;
        if (Room is null)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "The synchronized map publication lost its Photon room");
            return;
        }
        try
        {
            Run.HookedGeneration.Tick();
            if (!Run.Network.OwnsCurrentRun) return;
            Run.WorldInteractions.Tick();
            if (Run.Active) Run.PropInteractions.Tick();
            if (Run.Active) Run.ClimbingFixtures.Tick(Time.deltaTime);
            if (!Run.Network.OwnsCurrentRun) return;
            Replay();
            if (!Run.Network.OwnsCurrentRun) return;
            Run.Peak.Campfire.ReconcilePendingPlayerOutputs();
            if (Run.Active && !HasPending
                && Run.Transitions.Lease.Current == MapTransitionCoordinator.TransitionLease.State.Idle)
            {
                ReleasedSlotRetirement.TryApplyReleasedPrefix();
                ReplayPendingJumps();
                if (!Run.Network.OwnsCurrentRun || !Run.Active) return;
            }
            Run.Network.RunProperties.Confirm(Room.CustomProperties);
            if (!Run.Peak.PublishPendingCompletion())
            {
                LazySlotGenerator.AbortLazyGeneration(
                    Map.Index, "Peak completion state could not be published");
                return;
            }
            if (Run.Peak.RewardOpening) TryCompletePeakReward();
            Run.Peak.CompleteStableCampfire();
            if (!Run.Network.OwnsCurrentRun || !Run.Network.ViewBatches.Tick()) return;
            if (Run.Network.RunProperties.Maintain(Room, out var Failure)) return;
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, Failure ?? "Synchronized map state could not be published");
        }
        catch (Exception Exception)
        {
            if (Run.Network.OwnsCurrentRun)
                LazySlotGenerator.AbortLazyGeneration(
                    Map.Index, "Synchronized map publication validation failed", Exception);
        }
    }

    internal void RoomPropertiesUpdated(PhotonHashtable Changed)
    {
        if (!Run.Network.OwnsCurrentRun) return;
        Run.Network.RunProperties.Confirm(Changed);
        Run.WorldInteractions.PropertiesChanged(Changed);
        ReleasedSlotRetirement.HandleReleasedProperties(Changed);
        if (!Run.Network.OwnsCurrentRun || Run.Features.Aborted) return;
        try
        {
            Run.Network.ViewBatches.RoomPropertiesUpdated(Changed);
        }
        catch (Exception Exception)
        {
            if (Run.Network.OwnsCurrentRun)
                LazySlotGenerator.AbortLazyGeneration(
                    Map.Index, "Synchronized object batch update failed", Exception);
            return;
        }
        if (!Run.Network.OwnsCurrentRun) return;
        HandleRunProperties(Changed);
        TryCompletePeakReward();
        if (!Run.Peak.PublishPendingCompletion())
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Peak completion state could not be published");
            return;
        }
        Run.Peak.CompleteStableCampfire();
        if (Run.Peak.TakeAuthorityRecovery() && RecoverPeakAuthority())
            ReleasedSlotRetirement.ReleaseOldZones();
    }

    internal void PlayerEnteredRoom()
    {
        if (!Run.Network.OwnsCurrentRun || !PhotonNetwork.IsMasterClient) return;
        try { Run.Peak.Campfire.RequestPlayerOutputReconciliation(); }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Peak campfire player-count output reconciliation failed", Exception);
        }
    }

    internal void MasterClientSwitched()
    {
        if (!Run.Network.OwnsCurrentRun || PhotonNetwork.CurrentRoom is not { } Room) return;
        Run.Network.RunProperties.Confirm(Room.CustomProperties);
        Run.WorldInteractions.MasterChanged();
        ReleasedSlotRetirement.HandleReleasedProperties(Room.CustomProperties);
        if (!Run.Network.OwnsCurrentRun || Run.Features.Aborted
            || !Run.Network.ViewBatches.RecoverAuthority()) return;
        Run.Peak.PrepareAuthoritySwitch();
        HandleRunProperties(Room.CustomProperties);
        if (!Run.Network.OwnsCurrentRun) return;
        Run.Retirement.RequestedReleasedThrough = -1;
        try
        {
            Run.Peak.Bestow.RepublishPendingPeakBestow();
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Pending Peak bestow authority recovery failed", Exception);
            return;
        }
        if (!PhotonNetwork.IsMasterClient) return;
        try
        {
            if (!Run.Peak.BeginAuthorityRecovery()) return;
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Peak completion authority recovery failed", Exception);
            return;
        }
        TryCompletePeakReward();
        if (RecoverPeakAuthority()) ReleasedSlotRetirement.ReleaseOldZones();
    }

    void TryCompletePeakReward()
    {
        try { _ = Run.Peak.TryCompleteRewardOpening(); }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Peak statue reward completion failed", Exception);
        }
    }

    bool RecoverPeakAuthority()
    {
        try
        {
            if (Run.Peak.HasFixtures)
            {
                var Inventory = PeakRoomItemOperations.CaptureInventory(Run);
                Run.Peak.Checkpoints.RecoverAuthority();
                Run.Peak.Campfire.RecoverAuthority(Inventory);
                Run.Peak.Reward.RecoverAuthority(Inventory);
                Run.Peak.CompleteStableCampfire();
            }
            return true;
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Peak authority recovery failed", Exception);
            return false;
        }
    }

    internal void RequestDirectAdvance(int Index)
    {
        if (!ReleasedSlotRetirement.CanDirectAdvance(Index) || Run.Transitions.RequestedDirectAdvance == Index) return;
        Run.Transitions.RequestedDirectAdvance = Index;
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false })
        {
            ApplyDirectAdvance(Index);
            return;
        }
        if (Run.Network.RunProperties.Publish(Map.AdvanceRequestKey, Index)) return;
        Run.Transitions.RequestedDirectAdvance = -1;
        Plugin.PLLLog.LogError("The direct map-advance request could not be published.");
    }

    internal void RequestPeakCampfireLight(int Index)
    {
        if (!Run.Peak.Campfire.Owns(Index)) return;
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false } || PhotonNetwork.IsMasterClient)
        {
            _ = Run.Peak.Campfire.TryAuthorizeLight(Index);
            return;
        }
        if (!PublishPeakCampfireLightRequest(Index))
            Plugin.PLLLog.LogError("The Peak campfire light request could not be sent.");
    }

    internal void RequestPeakStatueReward(int Index, Character Interactor)
    {
        if (!Interactor || !Run.Peak.Reward.CanUse(Index)) return;
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false } || PhotonNetwork.IsMasterClient)
        {
            var Actor = Interactor.photonView ? Interactor.photonView.OwnerActorNr : 0;
            _ = Run.Peak.Reward.TryAuthorizeOpening(Index, Actor);
            return;
        }
        if (!PublishPeakStatueRewardRequest(Index))
            Plugin.PLLLog.LogError("The Peak statue reward request could not be sent.");
    }

    internal void CompletePeakCampfire(int Index, bool HasSuccessor)
    {
        if (!PhotonNetwork.IsMasterClient || !Run.Peak.Campfire.Owns(Index)
            || !ReferenceEquals(Map.CurrentRun, Run) || !Run.Active || Map.Index != Index) return;
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false })
        {
            Run.Transitions.RequestedDirectAdvance = -1;
            if (HasSuccessor) ApplyDirectAdvance(Index);
            else if (Run.Configuration.Terminal.Winnable) LazySlotGenerator.RequestFinishRun();
            return;
        }
        if (HasSuccessor)
        {
            if (Run.Transitions.RequestedDirectAdvance == Index) return;
            Run.Transitions.RequestedDirectAdvance = Index;
            if (PublishDirectAdvance(Index)) return;
            Run.Transitions.RequestedDirectAdvance = -1;
            LazySlotGenerator.AbortLazyGeneration(
                Index, "The synchronized Peak campfire advance could not be published");
            return;
        }
        if (!Run.Network.RunProperties.Publish(Map.AdvanceRequestKey, RejectedDirectAdvanceRequest))
        {
            LazySlotGenerator.AbortLazyGeneration(
                Index, "The completed Peak campfire request could not be acknowledged");
            return;
        }
        if (Run.Configuration.Terminal.Winnable) LazySlotGenerator.RequestFinishRun();
    }

    bool PublishDirectAdvance(int Index) => PhotonNetwork.RaiseEvent(
        DirectAdvanceEventCode,
        new object[] { DirectAdvanceEventProtocol, Run.GenerationEpoch, Run.LayoutRevision, Index },
        new RaiseEventOptions { Receivers = ReceiverGroup.All },
        SendOptions.SendReliable);

    bool TryReadIndexEvent(
        object? Value,
        string ExpectedProtocol,
        string Epoch,
        int Revision,
        out int Index)
    {
        Index = -1;
        if (Value is not object[] { Length: 4 } Data
            || Data[0] is not string Protocol || Protocol != ExpectedProtocol
            || Data[1] is not string ActualEpoch || ActualEpoch != Epoch
            || Data[2] is not int ActualRevision || ActualRevision != Revision
            || Data[3] is not int Requested) return false;
        Index = Requested;
        return true;
    }

    bool PublishPeakCampfireLightRequest(int Index) => PhotonNetwork.RaiseEvent(
        DirectAdvanceEventCode,
        new object[]
        {
            PeakCampfireLightEventProtocol, Run.GenerationEpoch, Run.LayoutRevision, Index
        },
        new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
        SendOptions.SendReliable);

    bool PublishPeakStatueRewardRequest(int Index) => PhotonNetwork.RaiseEvent(
        DirectAdvanceEventCode,
        new object[]
        {
            PeakStatueRewardEventProtocol, Run.GenerationEpoch, Run.LayoutRevision, Index
        },
        new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
        SendOptions.SendReliable);

    internal bool PublishPeakBestowRequest(int Index, int ViewId, int Type) => PhotonNetwork.RaiseEvent(
        DirectAdvanceEventCode,
        new object[]
        {
            PeakBestowEventProtocol, Run.GenerationEpoch, Run.LayoutRevision, Index, ViewId, Type
        },
        new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
        SendOptions.SendReliable);

    bool PublishPeakBestowRejection(int Actor, int Index, int ViewId, int Type) =>
        PhotonNetwork.RaiseEvent(
            DirectAdvanceEventCode,
            new object[]
            {
                PeakBestowRejectedProtocol, Run.GenerationEpoch, Run.LayoutRevision, Index, ViewId, Type
            },
            new RaiseEventOptions { TargetActors = new[] { Actor } },
            SendOptions.SendReliable);

    bool TryReadPeakBestowEvent(
        object? Value,
        string Protocol,
        string Epoch,
        int Revision,
        out int Index,
        out int ViewId,
        out int Type)
    {
        Index = -1;
        ViewId = 0;
        Type = -1;
        if (Value is not object[] { Length: 6 } Data
            || Data[0] is not string ActualProtocol || ActualProtocol != Protocol
            || Data[1] is not string ActualEpoch || ActualEpoch != Epoch
            || Data[2] is not int ActualRevision || ActualRevision != Revision
            || Data[3] is not int RequestedIndex || Data[4] is not int RequestedView
            || Data[5] is not int RequestedType) return false;
        Index = RequestedIndex;
        ViewId = RequestedView;
        Type = RequestedType;
        return true;
    }

    internal void HandleRunProperties(PhotonHashtable Changed)
    {
        if (Run.Peak.HasFixtures && Run.Peak.OwnsCompletionProperty(Changed[Map.PeakCompletionKey])
            && !Run.Peak.HandleCompletionProperty(Changed[Map.PeakCompletionKey]))
            throw new InvalidOperationException("The room published malformed Peak completion state.");
        if (PhotonPropertyProtocol.TryReadRunValue<object[]>(
                Changed[Map.PeakBestowKey], Run.GenerationEpoch, Run.LayoutRevision, out var PeakBestows)
            && !Run.Peak.Bestow.HandlePeakBestowCommit(PeakBestows)) return;

        if (PhotonPropertyProtocol.TryReadRunValue(
                Changed[Map.AdvanceRequestKey], Run.GenerationEpoch, Run.LayoutRevision, out int Requested))
        {
            if (Requested == RejectedDirectAdvanceRequest)
            {
                Run.Network.RunProperties.Cancel(Map.AdvanceRequestKey);
                Run.Transitions.RequestedDirectAdvance = -1;
            }
            else if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom is not null)
            {
                if (HasPending) return;
                var Authorized = false;
                try
                {
                    Authorized = ProgressProjection.CanAuthorizeDirectAdvance(Requested);
                }
                catch (Exception Exception)
                {
                    LazySlotGenerator.AbortLazyGeneration(
                        Map.Index, "Peak campfire authorization failed", Exception);
                    return;
                }
                if (Authorized)
                {
                    if (!PublishDirectAdvance(Requested))
                        LazySlotGenerator.AbortLazyGeneration(
                            Map.Index, "The synchronized direct map advance could not be published");
                }
                else if (!Run.Network.RunProperties.Publish(Map.AdvanceRequestKey, RejectedDirectAdvanceRequest))
                    LazySlotGenerator.AbortLazyGeneration(
                        Map.Index, "The rejected direct map-advance request could not be acknowledged");
            }
        }

        if (!PhotonPropertyProtocol.TryReadRunValue(
                Changed[Map.WinRequestKey], Run.GenerationEpoch, Run.LayoutRevision, out bool Win) || !Win
            || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom is not { } Room) return;
        var CurrentRun = Run;
        if (DeferredOperations.Count != 0
            || CurrentRun.Transitions.Lease.Current != MapTransitionCoordinator.TransitionLease.State.Idle
            || CurrentRun.Transitions.QueuedTransitions.Count != 0)
        {
            _ = QueueDeferredRunFinish(CurrentRun, Room);
            return;
        }
        if (LazySlotGenerator.CanFinishRun(false)) LazySlotGenerator.FinishRun();
    }

    internal void NetworkEvent(EventData PhotonEvent)
    {
        if (PhotonEvent.Code != DirectAdvanceEventCode
            || !Run.Network.OwnsCurrentRun
            || PhotonNetwork.CurrentRoom is not { } Room) return;
        var Epoch = Run.GenerationEpoch;
        var Revision = Run.LayoutRevision;
        if (TryReadIndexEvent(
            PhotonEvent.CustomData, DirectAdvanceEventProtocol, Epoch, Revision, out var Direct))
        {
            if (PhotonEvent.Sender != Room.MasterClientId) return;
            Run.Network.RunProperties.Cancel(Map.AdvanceRequestKey);
            if (DeferredOperations.Count != 0
                || Run.Peak.Campfire.Owns(Direct)
                    && !Run.Peak.CanApplyDirectAdvance(Direct))
            {
                if (QueueDeferredDirectAdvance(Run, Room, Direct))
                    Run.Transitions.RequestedDirectAdvance = Direct;
                return;
            }
            Run.Transitions.RequestedDirectAdvance = -1;
            ApplyDirectAdvance(Direct);
            return;
        }
        if (TryReadIndexEvent(
            PhotonEvent.CustomData, PeakCampfireLightEventProtocol, Epoch, Revision, out var CampfireIndex))
        {
            if (!PhotonNetwork.IsMasterClient || !Room.Players.ContainsKey(PhotonEvent.Sender)) return;
            try
            {
                _ = Run.Peak.Campfire.TryAuthorizeLight(CampfireIndex);
            }
            catch (Exception Exception)
            {
                LazySlotGenerator.AbortLazyGeneration(
                    CampfireIndex, "Peak campfire light authorization failed", Exception);
            }
            return;
        }
        if (TryReadIndexEvent(
            PhotonEvent.CustomData, PeakStatueRewardEventProtocol, Epoch, Revision, out var RewardIndex))
        {
            if (!PhotonNetwork.IsMasterClient || !Room.Players.ContainsKey(PhotonEvent.Sender)) return;
            try { _ = Run.Peak.Reward.TryAuthorizeOpening(RewardIndex, PhotonEvent.Sender); }
            catch (Exception Exception)
            {
                LazySlotGenerator.AbortLazyGeneration(
                    RewardIndex, "Peak statue reward authorization failed", Exception);
            }
            return;
        }
        if (TryReadPeakBestowEvent(
            PhotonEvent.CustomData, PeakBestowEventProtocol, Epoch, Revision,
            out var Index, out var ViewId, out var Type))
        {
            if (!PhotonNetwork.IsMasterClient || !Room.Players.ContainsKey(PhotonEvent.Sender)) return;
            bool Accepted;
            try
            {
                Accepted = Run.Peak.Bestow.TryPublishAuthorizedPeakBestow(Index, ViewId, Type, PhotonEvent.Sender);
            }
            catch (Exception Exception)
            {
                _ = PublishPeakBestowRejection(PhotonEvent.Sender, Index, ViewId, Type);
                LazySlotGenerator.AbortLazyGeneration(Index, "Peak bestow authorization failed", Exception);
                return;
            }
            if (!Accepted && !PublishPeakBestowRejection(PhotonEvent.Sender, Index, ViewId, Type))
                LazySlotGenerator.AbortLazyGeneration(Index, "The rejected Peak bestow could not be acknowledged");
            return;
        }
        if (!TryReadPeakBestowEvent(
                PhotonEvent.CustomData, PeakBestowRejectedProtocol, Epoch, Revision,
                out Index, out ViewId, out Type)
            || PhotonEvent.Sender != Room.MasterClientId) return;
        try
        {
            Run.Peak.Bestow.RejectPendingPeakBestow(Index, ViewId, Type);
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(Index, "Peak bestow rejection failed", Exception);
        }
    }

    internal void ApplyDirectAdvance(int Index)
    {
        var CurrentRun = Run;
        var Handler = Map.Owner;
        if (!Run.Network.OwnsCurrentRun || !CurrentRun.Active || !Handler || Index < Map.Index) return;
        var Destination = Index + 1;
        var PeakCheckpointSave = false;
        try
        {
            if (Index == Map.Index && !ReleasedSlotRetirement.CanDirectAdvance(Index)) return;
            if (!LazySlotGenerator.TryReserveDirectTransition(CurrentRun, Index)) return;
            PeakCheckpointSave = CurrentRun.Peak.Checkpoints.HasCheckpoint(Index);
            if (PeakCheckpointSave) MapQuicksave.PrepareCampfireQuicksave(Index);
            LazySlotGenerator.GenerateLazySlot(Destination);
            var SourceIndex = Map.Index;
            var LeavingNadir = Run.Slots[SourceIndex].Source is NadirLayoutSegmentSource && !ReleasedSlotRetirement.IsRunTerminal(SourceIndex);
            Handler.GoToSegment(Run.Slots[Destination].NavigationRole);
            if (Map.Index != Destination)
                throw new InvalidOperationException(
                    "The authenticated direct map advance did not enter its destination slot.");
            if (LeavingNadir) DestroyOwnedScoutmasterSoulRoots();
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.CancelReservedTransition(CurrentRun, Destination);
            if (PeakCheckpointSave) MapQuicksave.CancelCampfireQuicksave(Index);
            LazySlotGenerator.AbortLazyGeneration(Index, "Direct map advance failed", Exception);
            throw;
        }
    }

    void DestroyOwnedScoutmasterSoulRoots()
    {
        foreach (var Orbiter in UnityEngine.Object.FindObjectsByType<Peak.ScoutmasterGhostOrbiter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!Orbiter) continue;
            var View = Orbiter.view;
            if (!View || !View.IsMine) continue;
            PhotonNetwork.Destroy(Orbiter.gameObject);
        }
    }
}
