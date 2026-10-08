using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal sealed class PeakBestowLedger
{
    readonly Entry[] Entries;

    PeakBestowLedger(int CheckpointIndex, Entry[] Entries)
    {
        this.CheckpointIndex = CheckpointIndex;
        this.Entries = Entries;
    }

    internal static PeakBestowLedger Empty { get; } = new(-1, Array.Empty<Entry>());
    internal int CheckpointIndex { get; }
    internal int Count => Entries.Length;

    internal int CanonicalTypeAt(int Index)
    {
        if (Index < 0 || Index >= Entries.Length)
            throw new ArgumentOutOfRangeException(nameof(Index));
        var UsedTypes = 0;
        for (var i = 0; i <= Index; i++)
        {
            var Type = Entries[i].RawType;
            if ((UsedTypes & 1 << Type) != 0)
            {
                Type = 0;
                while ((UsedTypes & 1 << Type) != 0) Type++;
            }
            UsedTypes |= 1 << Type;
            if (i == Index) return Type;
        }
        throw new InvalidOperationException("A Peak bestow canonical type is unavailable.");
    }

    internal bool CanAppend(int CheckpointIndex, int ViewId, int RawType)
    {
        if (CheckpointIndex < 0 || ViewId <= 0 || RawType is < 0 or > 3 || Entries.Length >= 4
            || this.CheckpointIndex != -1 && this.CheckpointIndex != CheckpointIndex) return false;
        for (var i = 0; i < Entries.Length; i++)
            if (Entries[i].ViewId == ViewId) return false;
        return true;
    }

    internal PeakBestowLedger Append(int CheckpointIndex, int ViewId, int RawType)
    {
        if (!CanAppend(CheckpointIndex, ViewId, RawType))
            throw new InvalidOperationException("A Peak bestow ledger append is invalid.");
        var Result = new Entry[Entries.Length + 1];
        Array.Copy(Entries, Result, Entries.Length);
        Result[Result.Length - 1] = new Entry(ViewId, RawType);
        return new PeakBestowLedger(CheckpointIndex, Result);
    }

    internal PeakBestowLedger Extend(int CheckpointIndex, int[] ViewIds, int[] RawTypes)
    {
        if (CheckpointIndex < 0 || ViewIds.Length is < 1 or > 4 || ViewIds.Length != RawTypes.Length
            || this.CheckpointIndex != -1 && this.CheckpointIndex != CheckpointIndex
            || Entries.Length > ViewIds.Length)
            throw new InvalidOperationException("A Peak bestow ledger extension is invalid.");
        var ExtendedEntries = Entries.Length == ViewIds.Length ? null : new Entry[ViewIds.Length];
        for (var i = 0; i < ViewIds.Length; i++)
        {
            var RawType = RawTypes[i];
            if (ViewIds[i] <= 0 || RawType is < 0 or > 3)
                throw new InvalidOperationException("A Peak bestow ledger contains an invalid entry.");
            for (var j = 0; j < i; j++)
                if (ViewIds[j] == ViewIds[i])
                    throw new InvalidOperationException("A Peak bestow ledger repeats a submitted PhotonView.");
            if (i < Entries.Length
                && (Entries[i].ViewId != ViewIds[i] || Entries[i].RawType != RawType))
                throw new InvalidOperationException("A Peak bestow ledger changed its accepted prefix.");
            if (ExtendedEntries is not null) ExtendedEntries[i] = new Entry(ViewIds[i], RawType);
        }
        return ExtendedEntries is null ? this : new PeakBestowLedger(CheckpointIndex, ExtendedEntries);
    }

    internal bool TryGetRawType(int ViewId, out int RawType)
    {
        for (var i = 0; i < Entries.Length; i++)
        {
            if (Entries[i].ViewId != ViewId) continue;
            RawType = Entries[i].RawType;
            return true;
        }
        RawType = -1;
        return false;
    }

    internal object[] ToPayload()
    {
        var ViewIds = new int[Entries.Length];
        var RawTypes = new int[Entries.Length];
        for (var i = 0; i < Entries.Length; i++)
        {
            ViewIds[i] = Entries[i].ViewId;
            RawTypes[i] = Entries[i].RawType;
        }
        return new object[] { CheckpointIndex, ViewIds, RawTypes };
    }

    readonly struct Entry
    {
        internal Entry(int ViewId, int RawType)
        {
            this.ViewId = ViewId;
            this.RawType = RawType;
        }

        internal int ViewId { get; }
        internal int RawType { get; }
    }
}

internal sealed class PeakBestowRuntime
{
    internal sealed class PendingPeakBestowDrop
    {
        internal PendingPeakBestowDrop(
            float Charge,
            byte Slot,
            Vector3 Position,
            Vector3 Velocity,
            Quaternion Rotation,
            ItemInstanceData Data)
        {
            this.Charge = Charge;
            this.Slot = Slot;
            this.Position = Position;
            this.Velocity = Velocity;
            this.Rotation = Rotation;
            this.Data = Data;
        }

        internal float Charge { get; }
        internal byte Slot { get; }
        internal Vector3 Position { get; }
        internal Vector3 Velocity { get; }
        internal Quaternion Rotation { get; }
        internal ItemInstanceData Data { get; }
    }

    internal sealed class PendingPeakBestow
    {
        internal PendingPeakBestow(
            int Index,
            int ViewId,
            int RawType,
            Item Item,
            Character Holder,
            CharacterItems Items,
            float OriginalProgress)
        {
            this.Index = Index;
            this.ViewId = ViewId;
            this.RawType = RawType;
            this.Item = Item;
            this.Holder = Holder;
            this.Items = Items;
            this.OriginalProgress = OriginalProgress;
        }

        internal int Index { get; }
        internal int ViewId { get; }
        internal int RawType { get; }
        internal Item Item { get; }
        internal Character Holder { get; }
        internal CharacterItems Items { get; }
        internal float OriginalProgress { get; }
        internal PendingPeakBestowDrop? Drop { get; private set; }
        internal bool HasDeferredEquip { get; private set; }
        internal Optionable<byte> DeferredEquip { get; private set; }
        internal bool Resolving { get; private set; }
        internal bool ApplyingConsumeMoveNext { get; private set; }

        internal void CaptureDrop(PendingPeakBestowDrop Drop) => this.Drop ??= Drop;

        internal void DeferEquip(Optionable<byte> Slot)
        {
            if (HasDeferredEquip && (DeferredEquip.IsSome != Slot.IsSome
                || Slot.IsSome && DeferredEquip.Value != Slot.Value))
                throw new InvalidOperationException("A pending Peak bestow received overlapping forced equip requests.");
            DeferredEquip = Slot;
            HasDeferredEquip = true;
        }

        internal void BeginResolving()
        {
            if (Resolving)
                throw new InvalidOperationException("A pending Peak bestow began consumption more than once.");
            Resolving = true;
        }

        internal void BeginConsumeMoveNext()
        {
            if (!Resolving || ApplyingConsumeMoveNext)
                throw new InvalidOperationException("A pending Peak bestow entered an invalid consume step.");
            ApplyingConsumeMoveNext = true;
        }

        internal void EndConsumeMoveNext()
        {
            if (!ApplyingConsumeMoveNext)
                throw new InvalidOperationException("A pending Peak bestow exited an unowned consume step.");
            ApplyingConsumeMoveNext = false;
        }
    }

    readonly MapRun Owner;
    readonly PeakCheckpointRuntime Checkpoints;
    PeakBestowLedger[] Ledgers = Array.Empty<PeakBestowLedger>();

    static readonly MethodInfo NativeEquipSlotMethod =
        AccessTools.DeclaredMethod(typeof(CharacterItems), "EquipSlot", new[] { typeof(Optionable<byte>) })
        ?? throw new MissingMethodException(typeof(CharacterItems).FullName, "EquipSlot");
    static readonly MethodInfo NativeDropAllItemsMethod =
        AccessTools.DeclaredMethod(typeof(CharacterItems), "DropAllItems", new[] { typeof(bool) })
        ?? throw new MissingMethodException(typeof(CharacterItems).FullName, "DropAllItems");

    internal PeakBestowRuntime(MapRun Owner, PeakCheckpointRuntime Checkpoints)
    {
        this.Owner = Owner ?? throw new ArgumentNullException(nameof(Owner));
        this.Checkpoints = Checkpoints ?? throw new ArgumentNullException(nameof(Checkpoints));
    }

    internal bool Unconfigured => Pending is null && Ledgers.Length == 0;
    internal PendingPeakBestow? Pending { get; private set; }

    internal void Configure(PreparedMap Prepared)
    {
        if (Ledgers.Length != 0)
            throw new InvalidOperationException("A map run configured Peak bestow state more than once.");
        Ledgers = new PeakBestowLedger[Prepared.Slots.Length];
        for (var i = 0; i < Ledgers.Length; i++)
        {
            Ledgers[i] = PeakBestowLedger.Empty;
        }
    }


    internal bool CanBestowPeakAmulet(
        PeakCheckpointRuntime.PeakCheckpoint Checkpoint,
        Character Interactor)
    {
        if (!Checkpoints.IsCurrent(Checkpoint)
            || Checkpoint.Mode != PeakCheckpointRuntime.PeakCheckpointMode.ScoutsHonor
            || Checkpoint.Ready || Pending is not null || !Interactor) return false;
        var Item = Interactor.data.currentItem;
        if (!Item || !Item.TryGetComponent<Peak.AmuletBase>(out var Amulet)
            || Amulet.amuletIndex < 0 || Amulet.amuletIndex > 3) return false;
        var View = Item.photonView;
        return View && LedgerAt(Checkpoint.Index).CanAppend(Checkpoint.Index, View.ViewID, Amulet.amuletIndex);
    }

    internal void RequestPeakBestow(
        PeakCheckpointRuntime.PeakCheckpoint Checkpoint,
        Character Interactor)
    {
        if (!CanBestowPeakAmulet(Checkpoint, Interactor)) return;
        var Item = Interactor.data.currentItem;
        var Amulet = Item.GetComponent<Peak.AmuletBase>();
        var View = Item.photonView;
        if (!View || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
            throw new InvalidOperationException("A submitted Peak amulet has no published PhotonView.");
        var PendingBestow = BeginPendingPeakBestow(Checkpoint, Item, Amulet.amuletIndex, Interactor);
        try
        {
            if (PhotonNetwork.CurrentRoom is { IsOffline: false })
            {
                if (Owner.Network.DeferredOperations.PublishPeakBestowRequest(Checkpoint.Index, View.ViewID, Amulet.amuletIndex))
                    return;
                ReleasePendingPeakBestow(PendingBestow, true, true);
                Plugin.PLLLog.LogError("The Peak bestow request could not be published.");
                return;
            }
            if (TryPublishAuthorizedPeakBestow(Checkpoint, Item, Amulet.amuletIndex, Interactor, null)) return;
            ReleasePendingPeakBestow(PendingBestow, true, true);
        }
        catch
        {
            if (ReferenceEquals(Pending, PendingBestow))
                ReleasePendingPeakBestow(PendingBestow, true, false);
            throw;
        }
    }

    internal void ReleasePendingPeakBestow(
        PendingPeakBestow PendingBestow,
        bool ReplayDeferred,
        bool Strict)
    {
        if (!ReferenceEquals(Pending, PendingBestow))
        {
            if (Strict) throw new InvalidOperationException("A Peak bestow released another pending item.");
            return;
        }
        var Item = PendingBestow.Item;
        var View = Item ? Item.photonView : null;
        if (Strict && (!Item || !PendingBestow.Holder || !View || View.ViewID != PendingBestow.ViewId
            || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View)
            || Item.overrideProgress <= 0f
            || !PendingBestow.Resolving && !ReferenceEquals(PendingBestow.Holder.data.currentItem, Item)))
            throw new InvalidOperationException("The pending Peak bestow item escaped its interaction lock.");
        Pending = null;
        if (Item) Item.overrideProgress = PendingBestow.OriginalProgress;
        if (!ReplayDeferred || !PendingBestow.Items) return;
        Exception? ReplayFailure = null;
        var PendingDropCompleted = true;
        if (!PendingBestow.Resolving && PendingBestow.Drop is { } Drop)
            try
            {
                PendingBestow.Items.photonView.RPC(
                    nameof(CharacterItems.DropItemRpc),
                    RpcTarget.All,
                    Drop.Charge,
                    Drop.Slot,
                    Drop.Position,
                    Drop.Velocity,
                    Drop.Rotation,
                    Drop.Data,
                    true);
            }
            catch (Exception Exception)
            {
                PendingDropCompleted = false;
                ReplayFailure = Exception;
            }
        if (PendingBestow.HasDeferredEquip && PendingDropCompleted)
            try { PendingBestow.Items.EquipSlot(PendingBestow.DeferredEquip); }
            catch (Exception Exception) { ReplayFailure = Exception; }
        if (ReplayFailure is not null) ExceptionPropagation.Throw(ReplayFailure);
    }

    internal void RejectPendingPeakBestow(int Index, int ViewId, int RawType)
    {
        var PendingBestow = Pending;
        if (PendingBestow is null) return;
        if (PendingBestow.Index != Index || PendingBestow.ViewId != ViewId
            || PendingBestow.RawType != RawType)
            throw new InvalidOperationException("A Peak bestow rejection changed the pending item fingerprint.");
        if (PendingBestow.Resolving) return;
        if (PhotonNetwork.CurrentRoom is { } Room
            && PhotonPropertyProtocol.TryReadRunValue<object[]>(
                Room.CustomProperties[Map.PeakBestowKey], Owner.GenerationEpoch, Owner.LayoutRevision, out var Committed))
        {
            ApplyPeakBestowCommit(Committed);
            if (PendingBestow.Resolving) return;
        }
        ReleasePendingPeakBestow(PendingBestow, true, true);
    }

    internal void RepublishPendingPeakBestow()
    {
        var PendingBestow = Pending;
        if (!Owner.Active || PendingBestow is null || PendingBestow.Resolving) return;
        var Item = PendingBestow.Item;
        var View = Item ? Item.photonView : null;
        if (!Item || !PendingBestow.Holder
            || !ReferenceEquals(PendingBestow.Holder.data.currentItem, Item)
            || !View || View.ViewID != PendingBestow.ViewId
            || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View)
            || !Item.TryGetComponent<Peak.AmuletBase>(out var Amulet)
            || Amulet.amuletIndex != PendingBestow.RawType || Item.overrideProgress <= 0f)
            throw new InvalidOperationException("The pending Peak bestow lock was bypassed before authority recovery.");
        if (Owner.Network.DeferredOperations.PublishPeakBestowRequest(
            PendingBestow.Index, PendingBestow.ViewId, PendingBestow.RawType)) return;
        ReleasePendingPeakBestow(PendingBestow, true, true);
        Plugin.PLLLog.LogError("The pending Peak bestow request could not be republished.");
    }

    internal bool TryPublishAuthorizedPeakBestow(int Index, int ViewId, int RawType, int Actor)
    {
        if (!PhotonNetwork.IsMasterClient || !Checkpoints.TryGet(Index, out var Checkpoint)) return false;
        var View = PhotonNetwork.GetPhotonView(ViewId);
        var Item = View ? View.GetComponent<Item>() : null;
        if (!View || !Item || View.ViewID != ViewId || !ReferenceEquals(Item.photonView, View)
            || !ReferenceEquals(PhotonNetwork.GetPhotonView(ViewId), View)) return false;
        Character? Holder = null;
        foreach (var Character in PlayerHandler.GetAllPlayerCharacters())
        {
            if (!Character || !Character.photonView || Character.photonView.OwnerActorNr != Actor) continue;
            if (Holder)
                throw new InvalidOperationException("A Photon actor owns multiple live Peak bestow characters.");
            Holder = Character;
        }
        return Holder && TryPublishAuthorizedPeakBestow(Checkpoint, Item, RawType, Holder, Actor);
    }

    internal bool HandlePeakBestowCommit(object[] Data)
    {
        try
        {
            ApplyPeakBestowCommit(Data);
            return true;
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Synchronized Peak bestow commit failed", Exception);
            return false;
        }
    }

    internal void RetireSlot(int Index)
    {
        if (Pending is { } PendingBestow && PendingBestow.Index == Index)
            ReleasePendingPeakBestow(PendingBestow, true, false);
    }

    internal void ReleaseState()
    {
        if (Pending is { } PendingBestow)
            ReleasePendingPeakBestow(PendingBestow, true, false);
        Ledgers = Array.Empty<PeakBestowLedger>();
    }

    PendingPeakBestow BeginPendingPeakBestow(
        PeakCheckpointRuntime.PeakCheckpoint Checkpoint,
        Item Item,
        int RawType,
        Character Holder)
    {
        if (!Owner.Active || Pending is not null || !ReferenceEquals(Holder, Character.localCharacter)
            || !ReferenceEquals(Holder.data.currentItem, Item))
            throw new InvalidOperationException("A Peak bestow lock was requested outside its local item owner.");
        var View = Item.photonView;
        if (!View || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
            throw new InvalidOperationException("A pending Peak bestow item has no published PhotonView.");
        var Progress = Item.overrideProgress;
        var PendingBestow = new PendingPeakBestow(
            Checkpoint.Index, View.ViewID, RawType, Item, Holder, Holder.refs.items, Progress);
        Pending = PendingBestow;
        Item.overrideProgress = Math.Max(Progress, 1f);
        return PendingBestow;
    }


    bool TryPublishAuthorizedPeakBestow(
        PeakCheckpointRuntime.PeakCheckpoint Checkpoint,
        Item Item,
        int RawType,
        Character Holder,
        int? Actor)
    {
        if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.Active || !Checkpoints.IsCurrent(Checkpoint)
            || Checkpoint.Mode != PeakCheckpointRuntime.PeakCheckpointMode.ScoutsHonor || Checkpoint.Ready
            || RawType is < 0 or > 3 || !Holder || Holder.data.dead
            || !ReferenceEquals(Holder.data.currentItem, Item)) return false;
        if (Vector3.Distance(Holder.Center, Checkpoint.Statue.transform.position)
            > global::Interaction.instance.distance) return false;
        var View = Item.photonView;
        if (!View || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View)
            || Actor.HasValue && (View.OwnerActorNr != Actor.Value
                || !Holder.photonView || Holder.photonView.OwnerActorNr != Actor.Value)
            || !Item.TryGetComponent<Peak.AmuletBase>(out var Amulet) || Amulet.amuletIndex != RawType)
            return false;
        var Ledger = LedgerAt(Checkpoint.Index);
        if (!Ledger.CanAppend(Checkpoint.Index, View.ViewID, RawType)
            || PhotonNetwork.CurrentRoom is { IsOffline: false }
                && Owner.Network.RunProperties.IsPending(Map.PeakBestowKey)) return false;
        var Candidate = (PeakBestowLedger[])Ledgers.Clone();
        Candidate[Checkpoint.Index] = Ledger.Append(Checkpoint.Index, View.ViewID, RawType);
        var Payload = Snapshot(Candidate);
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false })
        {
            ApplyPeakBestowCommit(Payload);
            return true;
        }
        if (Owner.Network.RunProperties.Publish(Map.PeakBestowKey, Payload)) return true;
        Plugin.PLLLog.LogError("The Peak bestow commit could not be published.");
        return false;
    }

    void ApplyPeakBestowCommit(object[] Data)
    {
        if (!ReferenceEquals(Map.CurrentRun, Owner))
            throw new InvalidOperationException("Peak bestow commit has no owning map run.");
        var Candidate = (PeakBestowLedger[])Ledgers.Clone();
        var Seen = new bool[Ledgers.Length];
        var PreviousIndex = -1;
        foreach (var RawEntry in Data)
        {
            if (RawEntry is not object[] { Length: 3 } Entry || Entry[0] is not int Index
                || Entry[1] is not int[] ViewIds || Entry[2] is not int[] RawTypes
                || Index < 0 || Index >= Ledgers.Length || !Checkpoints.HonorRequired(Index)
                || Index <= PreviousIndex)
                throw new InvalidOperationException("The synchronized Peak bestow commit payload is invalid.");
            PreviousIndex = Index;
            Seen[Index] = true;
            Candidate[Index] = Ledgers[Index].Extend(Index, ViewIds, RawTypes);
        }
        for (var i = 0; i < Ledgers.Length; i++)
            if (Ledgers[i].Count != 0 && !Seen[i])
                throw new InvalidOperationException("The synchronized Peak bestow commit regressed a checkpoint ledger.");
        Ledgers = Candidate;
        for (var i = 0; i < Candidate.Length; i++)
        {
            if (!Seen[i]) continue;
            var Accepted = Candidate[i];
            if (Checkpoints.TryGet(i, out var Checkpoint)) Checkpoint.ApplyBestowProjection(Accepted);
        }
        if (Pending is { } PendingBestow && PendingBestow.Index >= 0
            && PendingBestow.Index < Candidate.Length && Seen[PendingBestow.Index])
            ResolveCommittedPendingPeakBestow(Candidate[PendingBestow.Index]);
        if (PhotonNetwork.IsMasterClient && Owner.Active) Checkpoints.RecoverAuthority();
    }

    PeakBestowLedger LedgerAt(int Index)
    {
        if (Index < 0 || Index >= Ledgers.Length)
            throw new ArgumentOutOfRangeException(nameof(Index));
        return Ledgers[Index];
    }

    static object[] Snapshot(PeakBestowLedger[] Values)
    {
        var Count = 0;
        foreach (var Value in Values)
            if (Value.Count != 0) Count++;
        var Result = new object[Count];
        var Offset = 0;
        foreach (var Value in Values)
            if (Value.Count != 0) Result[Offset++] = Value.ToPayload();
        return Result;
    }

    void ResolveCommittedPendingPeakBestow(PeakBestowLedger Accepted)
    {
        var PendingBestow = Pending;
        if (PendingBestow is null
            || !Accepted.TryGetRawType(PendingBestow.ViewId, out var RawType)) return;
        if (PendingBestow.Index != Accepted.CheckpointIndex || RawType != PendingBestow.RawType)
            throw new InvalidOperationException("A Peak bestow commit changed the pending item fingerprint.");
        if (PendingBestow.Resolving) return;
        var Item = PendingBestow.Item;
        var View = Item ? Item.photonView : null;
        if (!Item || !PendingBestow.Holder
            || !ReferenceEquals(PendingBestow.Holder.data.currentItem, Item)
            || !View || View.ViewID != PendingBestow.ViewId
            || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View)
            || !ReferenceEquals(View.GetComponent<Item>(), Item) || Item.overrideProgress <= 0f
            || PhotonNetwork.CurrentRoom is { IsOffline: false }
                && (PhotonNetwork.LocalPlayer is not { } Local || View.OwnerActorNr != Local.ActorNumber))
            throw new InvalidOperationException("The pending Peak bestow item escaped its interaction lock.");
        PendingBestow.BeginResolving();
        try
        {
            Plugin.StartRoutine(ConsumePendingPeakBestow(PendingBestow));
        }
        catch
        {
            ReleasePendingPeakBestow(PendingBestow, true, false);
            throw;
        }
    }

    IEnumerator ConsumePendingPeakBestow(PendingPeakBestow PendingBestow)
    {
        IEnumerator? Routine = null;
        Exception? Failure = null;
        try
        {
            Routine = PendingBestow.Item.ConsumeDelayed(false);
            while (ReferenceEquals(Map.CurrentRun, Owner) && ReferenceEquals(Pending, PendingBestow))
            {
                object? Current = null;
                bool Moved;
                try
                {
                    PendingBestow.BeginConsumeMoveNext();
                    try
                    {
                        Moved = Routine.MoveNext();
                        if (Moved) Current = Routine.Current;
                    }
                    finally
                    {
                        PendingBestow.EndConsumeMoveNext();
                    }
                }
                catch (Exception Exception)
                {
                    Failure = Exception;
                    break;
                }
                if (!Moved) break;
                yield return Current;
            }
        }
        finally
        {
            try { (Routine as IDisposable)?.Dispose(); }
            catch (Exception Exception)
            {
                Failure = Failure is null ? Exception : new AggregateException(Failure, Exception);
            }
            try { ReleasePendingPeakBestow(PendingBestow, true, false); }
            catch (Exception Exception)
            {
                Failure = Failure is null ? Exception : new AggregateException(Failure, Exception);
            }
        }
        if (Failure is not null && ReferenceEquals(Map.CurrentRun, Owner) && Owner.HasPublishedLayout)
            LazySlotGenerator.AbortLazyGeneration(
                PendingBestow.Index, "Committed Peak bestow consumption failed", Failure);
    }


    static PeakBestowRuntime? Current => Map.CurrentRun?.Peak.Bestow;

    static bool AllowPendingPeakBestowStash(Character Character)
    {
        var PendingBestow = Current?.Pending;
        return PendingBestow is null || !ReferenceEquals(PendingBestow.Holder, Character)
            || !ReferenceEquals(Character.data.currentItem, PendingBestow.Item);
    }

    [HarmonyPatch(typeof(Backpack), nameof(Backpack.Stash), typeof(Character), typeof(byte))]
    static class PendingPeakBestowBackpackStashPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Character __0) => AllowPendingPeakBestowStash(__0);
    }

    [HarmonyPatch(
        typeof(CharacterBackpackHandler),
        nameof(CharacterBackpackHandler.StashInBackpack),
        typeof(Character),
        typeof(byte))]
    static class PendingPeakBestowCharacterStashPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Character __0) => AllowPendingPeakBestowStash(__0);
    }

    [HarmonyPatch]
    static class PendingPeakBestowEquipSlotPatch
    {
        static MethodBase TargetMethod() => NativeEquipSlotMethod;

        [HarmonyPrefix]
        static bool Prefix(CharacterItems __instance, Optionable<byte> __0)
        {
            var PendingBestow = Current?.Pending;
            if (PendingBestow is null || PendingBestow.ApplyingConsumeMoveNext
                || !ReferenceEquals(PendingBestow.Items, __instance)) return true;
            PendingBestow.DeferEquip(__0);
            return false;
        }
    }

    [HarmonyPatch]
    static class PendingPeakBestowDropAllPatch
    {
        sealed class DropMask
        {
            readonly ItemSlot Slot;
            readonly Item Prefab;
            readonly Item.ItemUIData Ui;
            readonly bool CanDrop;
            bool Restored;

            internal DropMask(ItemSlot Slot, Item Item)
            {
                this.Slot = Slot;
                Prefab = Slot.prefab;
                Ui = Item.UIData;
                CanDrop = Ui.canDrop;
                Slot.prefab = Item;
                Ui.canDrop = false;
            }

            internal void Restore()
            {
                if (Restored) return;
                Restored = true;
                Ui.canDrop = CanDrop;
                Slot.prefab = Prefab;
            }
        }

        static MethodBase TargetMethod() => NativeDropAllItemsMethod;

        [HarmonyPrefix]
        static void Prefix(CharacterItems __instance, out DropMask? __state)
        {
            __state = null;
            var PendingBestow = Current?.Pending;
            if (PendingBestow is null || !ReferenceEquals(PendingBestow.Items, __instance)) return;
            var Item = PendingBestow.Item;
            if (!Item || !PendingBestow.Holder
                || !ReferenceEquals(PendingBestow.Holder.data.currentItem, Item))
                throw new InvalidOperationException("The pending Peak bestow item escaped before DropAllItems.");
            var Selected = __instance.currentSelectedSlot;
            if (!Selected.IsSome)
                throw new InvalidOperationException("The pending Peak bestow has no selected inventory slot.");
            var Slot = PendingBestow.Holder.player.GetItemSlot(Selected.Value)
                ?? throw new InvalidOperationException("The pending Peak bestow inventory slot is unavailable.");
            var Prefab = Slot.prefab
                ?? throw new InvalidOperationException("The pending Peak bestow inventory prefab is unavailable.");
            if (!Prefab.UIData.canDrop) return;
            var Position = Item.transform.position;
            var Velocity = Item.rig.linearVelocity;
            if (Item is Backpack)
            {
                Velocity = Vector3.zero;
                Position += 0.25f * Vector3.up;
            }
            PendingBestow.CaptureDrop(new PendingPeakBestowDrop(
                __instance.throwChargeLevel, Selected.Value, Position, Velocity,
                Item.transform.rotation, Slot.data));
            __state = new DropMask(Slot, Item);
        }

        [HarmonyPostfix]
        static void Postfix(DropMask? __state) => __state?.Restore();

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception, DropMask? __state)
        {
            __state?.Restore();
            return __exception;
        }
    }
}
