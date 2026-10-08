using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal enum PeakRoomItemKind : byte { StatueReward = 1, Marshmallow = 2, Backpack = 3 }

internal readonly struct PeakRoomItemOperation : IEquatable<PeakRoomItemOperation>
{
    const string Protocol = PLLLibrary.PluginGuid + "/peak-room-item/v1";

    internal PeakRoomItemOperation(
        string GenerationEpoch,
        int LayoutRevision,
        PeakRoomItemKind Kind,
        int Slot,
        byte Output)
    {
        if (string.IsNullOrEmpty(GenerationEpoch)) throw new ArgumentException(null, nameof(GenerationEpoch));
        if (LayoutRevision == 0) throw new ArgumentOutOfRangeException(nameof(LayoutRevision));
        if (Kind == 0 || Slot < 0) throw new ArgumentOutOfRangeException(nameof(Slot));
        this.GenerationEpoch = GenerationEpoch;
        this.LayoutRevision = LayoutRevision;
        this.Kind = Kind;
        this.Slot = Slot;
        this.Output = Output;
    }

    internal string GenerationEpoch { get; }
    internal int LayoutRevision { get; }
    internal PeakRoomItemKind Kind { get; }
    internal int Slot { get; }
    internal byte Output { get; }

    internal object[] Append(object[]? Data)
    {
        var Offset = Data?.Length ?? 0;
        var Result = new object[Offset + 6];
        if (Offset != 0) Array.Copy(Data!, Result, Offset);
        Result[Offset] = Protocol;
        Result[Offset + 1] = GenerationEpoch;
        Result[Offset + 2] = LayoutRevision;
        Result[Offset + 3] = (byte)Kind;
        Result[Offset + 4] = Slot;
        Result[Offset + 5] = Output;
        return Result;
    }

    internal bool Matches(object[]? Data)
    {
        if (Data is null || Data.Length < 6) return false;
        var Offset = Data.Length - 6;
        return Data[Offset] is string ActualProtocol && ActualProtocol == Protocol
            && Data[Offset + 1] is string Epoch && Epoch == GenerationEpoch
            && Data[Offset + 2] is int Revision && Revision == LayoutRevision
            && Data[Offset + 3] is byte RawKind && RawKind == (byte)Kind
            && Data[Offset + 4] is int Index && Index == Slot
            && Data[Offset + 5] is byte OutputIndex && OutputIndex == Output;
    }

    internal static bool TryRead(object[]? Data, out PeakRoomItemOperation Operation)
    {
        Operation = default;
        if (Data is null || Data.Length < 6) return false;
        var Offset = Data.Length - 6;
        if (Data[Offset] is not string ActualProtocol || ActualProtocol != Protocol
            || Data[Offset + 1] is not string Epoch || string.IsNullOrEmpty(Epoch)
            || Data[Offset + 2] is not int Revision || Revision == 0
            || Data[Offset + 3] is not byte RawKind
            || RawKind is < (byte)PeakRoomItemKind.StatueReward or > (byte)PeakRoomItemKind.Backpack
            || Data[Offset + 4] is not int Slot || Slot < 0
            || Data[Offset + 5] is not byte Output) return false;
        Operation = new PeakRoomItemOperation(
            Epoch, Revision, (PeakRoomItemKind)RawKind, Slot, Output);
        return true;
    }

    public bool Equals(PeakRoomItemOperation Other) =>
        GenerationEpoch == Other.GenerationEpoch && LayoutRevision == Other.LayoutRevision
        && Kind == Other.Kind && Slot == Other.Slot && Output == Other.Output;

    public override bool Equals(object? Value) =>
        Value is PeakRoomItemOperation Other && Equals(Other);

    public override int GetHashCode() =>
        HashCode.Combine(GenerationEpoch, LayoutRevision, Kind, Slot, Output);
}

internal static class PeakRoomItemOperations
{
    static PeakRoomItemOperation? ActiveOperation;

    internal static PeakRoomItemOperation Create(
        MapRun Owner,
        PeakRoomItemKind Kind,
        int Slot,
        byte Output) => new(Owner.GenerationEpoch, Owner.LayoutRevision, Kind, Slot, Output);

    internal static bool TryAdopt(PeakRoomItemOperation Operation, out PhotonView View)
    {
        View = null!;
        foreach (var Candidate in PhotonNetwork.PhotonViewCollection)
        {
            if (!Candidate || !Operation.Matches(Candidate.InstantiationData)
                || !Candidate.TryGetComponent<Item>(out _)) continue;
            if (View)
                throw new InvalidOperationException("A Peak room-item operation produced multiple room items.");
            View = Candidate;
        }
        return View;
    }

    internal static bool TryAdopt(
        PeakRoomItemOperation Operation,
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView> Inventory,
        out PhotonView View)
    {
        if (!Inventory.TryGetValue(Operation, out var Candidate) || !Candidate)
        {
            View = null!;
            return false;
        }
        View = Candidate;
        return true;
    }

    internal static Dictionary<PeakRoomItemOperation, PhotonView> CaptureInventory(
        MapRun Owner)
    {
        var Result = new Dictionary<PeakRoomItemOperation, PhotonView>();
        foreach (var Candidate in PhotonNetwork.PhotonViewCollection)
        {
            if (!Candidate || !Candidate.TryGetComponent<Item>(out _)
                || !PeakRoomItemOperation.TryRead(Candidate.InstantiationData, out var Operation)
                || Operation.GenerationEpoch != Owner.GenerationEpoch
                || Operation.LayoutRevision != Owner.LayoutRevision) continue;
            if (!Result.TryAdd(Operation, Candidate))
                throw new InvalidOperationException("A Peak room-item operation produced multiple room items.");
        }
        return Result;
    }

    internal static void Track(Peak.SpawnedItemTracker Tracker, PhotonView View)
    {
        Tracker._historyFromSave = null;
        foreach (var Item in Tracker.SpawnedItems)
            if (Item && Item.photonView && ReferenceEquals(Item.photonView, View)) return;
        Tracker.TrackSpawnedItems(new List<PhotonView>(1) { View });
    }

    internal static bool RestoreLoadedHistory(
        Peak.SpawnedItemTracker Tracker,
        PeakRoomItemOperation Operation,
        Func<List<PhotonView>> Restore)
    {
        if (Tracker._historyFromSave is null) return false;
        List<PhotonView> Spawned;
        try { Spawned = Execute(Operation, Restore); }
        finally { Tracker._historyFromSave = null; }
        if (Spawned.Count > 1
            || Spawned.Count == 1 && !Operation.Matches(Spawned[0].InstantiationData))
            throw new InvalidOperationException(
                "A saved Peak room-item operation restored multiple or unmarked items.");
        if (Spawned.Count == 1) Track(Tracker, Spawned[0]);
        return true;
    }

    internal static List<PhotonView> Execute(
        PeakRoomItemOperation Operation,
        Func<List<PhotonView>> Spawn)
    {
        if (ActiveOperation.HasValue)
            throw new InvalidOperationException("A Peak room-item operation is already active.");
        ActiveOperation = Operation;
        try { return Spawn(); }
        finally { ActiveOperation = null; }
    }

    [HarmonyPatch(
        typeof(PhotonNetwork), nameof(PhotonNetwork.InstantiateRoomObject),
        new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(byte), typeof(object[]) })]
    static class InstantiationDataPatch
    {
        [HarmonyPrefix]
        static void Prefix(ref object[] __4)
        {
            if (ActiveOperation is { } Operation) __4 = Operation.Append(__4);
        }
    }
}
