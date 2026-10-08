using System;
using System.Collections.Generic;
using System.Globalization;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PLL.Library;

internal sealed class GeneratedRpcReplaySynchronization
{
    sealed class PublishedCatalog
    {
        internal PublishedCatalog(object SourceVersion, byte[] Hash, GeneratedRpcDeferral.BatchCatalog Catalog,
            int[] Delivered, int MaximumCatalogBytes)
        {
            this.SourceVersion = SourceVersion; this.Hash = (byte[])Hash.Clone(); this.Catalog = Catalog;
            this.Delivered = (int[])Delivered.Clone(); this.MaximumCatalogBytes = MaximumCatalogBytes;
        }
        internal object SourceVersion { get; }
        internal byte[] Hash { get; }
        internal GeneratedRpcDeferral.BatchCatalog Catalog { get; }
        internal int[] Delivered { get; }
        internal int MaximumCatalogBytes { get; }
    }

    class CatalogReservation
    {
        internal CatalogReservation(int ReservedBytes, PublishedCatalog? Published = null)
        {
            this.ReservedBytes = ReservedBytes; this.Published = Published;
        }
        internal int ReservedBytes { get; }
        internal PublishedCatalog? Published { get; set; }
    }

    abstract class ReplayBatch : CatalogReservation
    {
        protected ReplayBatch(
            int Index, byte[] ViewPathHash, int[] RequiredActors, int[] ViewIds,
            GeneratedRpcDeferral.BatchCatalog Seed, int ReservedBytes) : base(ReservedBytes)
        {
            this.Index = Index;
            this.ViewPathHash = ViewPathHash;
            this.RequiredActors = RequiredActors;
            this.ViewIds = ViewIds;
            this.Seed = Seed;
        }

        internal int Index { get; }
        internal byte[] ViewPathHash { get; }
        internal int[] RequiredActors { get; }
        internal int[] ViewIds { get; }
        internal GeneratedRpcDeferral.BatchCatalog Seed { get; }
    }

    sealed class WaitingReplayBatch : ReplayBatch
    {
        internal WaitingReplayBatch(
            int Index, byte[] ViewPathHash, int[] RequiredActors, int[] ViewIds,
            GeneratedRpcDeferral.BatchCatalog Seed,
            GeneratedRpcDeferral.BatchCatalog LocalCatalog, int ReservedBytes)
            : base(Index, ViewPathHash, RequiredActors, ViewIds, Seed, ReservedBytes) =>
            this.LocalCatalog = LocalCatalog;

        internal GeneratedRpcDeferral.BatchCatalog LocalCatalog { get; }
    }

    sealed class RemoteReplayBatch : ReplayBatch
    {
        internal RemoteReplayBatch(ReplayBatch Batch)
            : base(
                Batch.Index, Batch.ViewPathHash, Batch.RequiredActors,
                Batch.ViewIds, Batch.Seed, Batch.ReservedBytes) => Published = Batch.Published;
        internal object? CatalogSource { get; set; }
        internal PropertyState CatalogState { get; set; }
        internal int CatalogRevision { get; set; } = -1;
        internal bool HasCalls { get; set; }
        internal int[] DeliveredActors { get; set; } = Array.Empty<int>();
        internal string? Failure { get; set; }
    }

    sealed class MasterReplayBatch : ReplayBatch
    {
        internal MasterReplayBatch(
            WaitingReplayBatch Waiting, GeneratedRpcDeferral.BatchCatalog Catalog)
            : base(
                Waiting.Index, Waiting.ViewPathHash,
                Waiting.RequiredActors, Waiting.ViewIds, Waiting.Seed, Waiting.ReservedBytes)
        {
            this.Catalog = Catalog; Published = Waiting.Published;
        }

        internal MasterReplayBatch(
            int Index, byte[] ViewPathHash, int[] ViewIds,
            GeneratedRpcDeferral.BatchCatalog Seed,
            GeneratedRpcDeferral.BatchCatalog Catalog,
            int[] RequiredActors, int[] DeliveredActors, object[] PropertyValue,
            int ReservedBytes)
            : base(Index, ViewPathHash, RequiredActors, ViewIds, Seed, ReservedBytes)
        {
            this.Catalog = Catalog;
            this.PropertyValue = PropertyValue;
            PropertyConfirmed = true;
            foreach (var Actor in DeliveredActors) this.DeliveredActors.Add(Actor);
        }

        internal GeneratedRpcDeferral.BatchCatalog Catalog { get; set; }
        internal HashSet<int> DeliveredActors { get; set; } = new();
        internal object[]? PropertyValue { get; set; }
        internal int ExpectedRevision { get; set; } = -1;
        internal int PendingRevision { get; set; } = -1;
        internal int CatalogMutationRevision { get; set; }
        internal int ConfirmedCatalogMutationRevision { get; set; }
        internal int PendingCatalogMutationRevision { get; set; }
        internal bool PropertyConfirmed { get; set; }
    }

    sealed class CatalogClear
    {
        internal CatalogClear(int RetainedBytes) => this.RetainedBytes = RetainedBytes;

        internal int RetainedBytes { get; }
        internal int ExpectedRevision { get; set; } = -1;
        internal int PendingRevision { get; set; } = -1;
    }

    sealed class FutureCatalog : CatalogReservation
    {
        internal FutureCatalog(
            int ReservedBytes, int InitializationActor, int BeehiveEffectOwnerActor, PublishedCatalog? Published = null)
            : base(ReservedBytes, Published)
        {
            this.InitializationActor = InitializationActor;
            this.BeehiveEffectOwnerActor = BeehiveEffectOwnerActor;
        }

        internal int InitializationActor { get; }
        internal int BeehiveEffectOwnerActor { get; }
    }

    sealed class ReadinessSnapshot
    {
        internal ReadinessSnapshot(
            object? Source, PropertyState State, int[] Indices, byte[][] Hashes, string? Failure)
        {
            this.Source = Source;
            this.State = State;
            this.Indices = Indices;
            this.Hashes = Hashes;
            this.Failure = Failure;
        }

        internal object? Source { get; }
        internal PropertyState State { get; }
        internal int[] Indices { get; }
        internal byte[][] Hashes { get; }
        internal string? Failure { get; }
    }

    internal enum PropertyState { Absent, Valid, Invalid }

    const string ReadinessProtocol = "2";
    const string CatalogProtocol = "6";
    const string ReadinessPrefix = PhotonPropertyProtocol.StatePrefix + "view-ready/";
    const string CatalogPrefix = PhotonPropertyProtocol.StatePrefix + "rpc/";
    const string CatalogRevisionPrefix = PhotonPropertyProtocol.StatePrefix + "rpc-revision/";
    readonly Dictionary<int, ReplayBatch> Batches = new();
    readonly Dictionary<int, ReadinessSnapshot> ReadinessSnapshots = new();
    readonly Dictionary<int, CatalogClear> CatalogClears = new();
    readonly Dictionary<int, FutureCatalog> FutureCatalogs = new();
    readonly Dictionary<int, CatalogReservation> PreparedCatalogReservations = new();
    readonly SortedDictionary<int, byte[]> ReadyBatches = new();
    readonly Dictionary<int, byte[]> RetiredBatches = new();
    readonly RoomPropertyPublication ReadinessPublication = new();
    readonly MapRun Run;
    Room? Room;
    object[]? ReadinessValue;
    int LocalActor;
    int AuthorityActor;
    int RetainedCatalogBytes
    {
        get => Run.ReplayBudget.Bytes(this);
        set => Run.ReplayBudget.Reserve(this, value);
    }
    bool ReadinessConfirmed = true;

    internal GeneratedRpcReplaySynchronization(MapRun Run) =>
        this.Run = Run ?? throw new ArgumentNullException(nameof(Run));

    internal bool HasPendingPublication => !ReadinessConfirmed
        || CatalogClears.Count != 0 || HasUnconfirmedCatalog();

    internal object[] ReserveInitialCatalogValue(
        int Index, byte[] ViewPathHash,
        GeneratedRpcDeferral.BatchCatalog Catalog)
    {
        var CurrentRoom = PhotonNetwork.CurrentRoom
            ?? throw new InvalidOperationException("Generated RPC replay requires a Photon room.");
        BindRoom(CurrentRoom);
        if (!PreparedCatalogReservations.TryGetValue(Index, out var Reservation))
            throw new InvalidOperationException("Generated replay initialization has no frozen mutation capacity.");
        var MaximumCatalogBytes = Reservation.ReservedBytes;
        var Value = EncodeCatalog(Index, ViewPathHash, Catalog, Array.Empty<int>(), MaximumCatalogBytes);
        if (GeneratedRpcDeferral.SerializePhotonValue(Value).Length > MaximumCatalogBytes)
            throw new InvalidOperationException("Generated replay initialization exceeds its preallocated budget.");
        return Value;
    }

    internal void ReserveMaximumCatalog(int Index, byte[] ViewPathHash, PhotonView[] Views)
    {
        var CurrentRoom = PhotonNetwork.CurrentRoom
            ?? throw new InvalidOperationException("Generated RPC replay requires a Photon room.");
        BindRoom(CurrentRoom);
        if (PreparedCatalogReservations.ContainsKey(Index))
            throw new InvalidOperationException("Generated replay initialization already owns its budget.");
        var MaximumCatalogBytes = CatalogReservationBytes(Index, ViewPathHash,
            GeneratedRpcDeferral.MaximumBatchCatalog(Views));
        RetainedCatalogBytes = checked(RetainedCatalogBytes + MaximumCatalogBytes);
        PreparedCatalogReservations.Add(Index, new CatalogReservation(MaximumCatalogBytes));
    }

    internal void RequirePublishedCatalogCapacity(int Index, byte[] ViewPathHash, PhotonView[] Views)
    {
        var CurrentRoom = PhotonNetwork.CurrentRoom
            ?? throw new InvalidOperationException("Generated RPC replay requires a Photon room.");
        BindRoom(CurrentRoom);
        var State = ReadCatalogProperty(CurrentRoom.CustomProperties[CatalogKey(Index)], Index, ViewPathHash,
            out var Published, out var Failure);
        var MaximumCatalogBytes = Published?.MaximumCatalogBytes ?? 0;
        var Catalog = Published?.Catalog;
        if (State != PropertyState.Valid || Catalog is null || MaximumCatalogBytes != CatalogReservationBytes(Index, ViewPathHash,
                GeneratedRpcDeferral.MaximumBatchCatalog(Views)))
            throw new InvalidOperationException(Failure ?? "Generated replay capacity differs from its frozen native inventory.");
        if (!PreparedCatalogReservations.ContainsKey(Index) && !FutureCatalogs.ContainsKey(Index))
        {
            RetainedCatalogBytes = checked(RetainedCatalogBytes + MaximumCatalogBytes);
            FutureCatalogs.Add(Index, new FutureCatalog(MaximumCatalogBytes, Catalog.InitializationActor,
                Catalog.BeehiveEffectOwnerActor, Published));
        }
    }

    internal void CancelPreparedCatalog(int Index)
    {
        if (!PreparedCatalogReservations.Remove(Index, out var Reservation)) return;
        if (PhotonNetwork.IsMasterClient
            && Room is { } CurrentRoom
            && ReferenceEquals(CurrentRoom, PhotonNetwork.CurrentRoom))
        {
            CatalogClears.Add(Index, new CatalogClear(Reservation.ReservedBytes));
            return;
        }
        RetainedCatalogBytes -= Reservation.ReservedBytes;
    }

    internal void RegisterBatch(
        int Index, byte[] ViewPathHash, PhotonView[] Views,
        byte[] ImmutableFingerprint,
        GeneratedRpcDeferral.BatchCatalog LocalCatalog)
    {
        Run.RequirePublished();
        var CurrentRoom = PhotonNetwork.CurrentRoom
            ?? throw new InvalidOperationException("Generated RPC replay requires a Photon room.");
        BindRoom(CurrentRoom);
        if (Batches.ContainsKey(Index) || ReadyBatches.ContainsKey(Index)
            || RetiredBatches.ContainsKey(Index))
            throw new InvalidOperationException(
                $"Generated RPC batch {Index} was registered more than once.");
        var Hash = (byte[])ViewPathHash.Clone();
        var ViewIds = RegisteredViewIds(Views);
        var ReservedBytes = ValidatePublishedCatalog(
            CurrentRoom, Index, Hash, ImmutableFingerprint, ViewIds,
            out var Seed);
        RequireSafeBeehiveTransfer(CurrentRoom, Index, Seed);
        var RequirePublishedFacts = Seed.InitializationActor == LocalActor;
        if (!Seed.IncludesLocalFacts(LocalCatalog, RequirePublishedFacts, ViewIds))
            throw new InvalidOperationException(
                $"Generated RPC replay seed {Index} differs from the locally registered batch.");
        ReservedBytes = ReconcilePreparedCatalogReservation(
            Index, ReservedBytes, Seed.InitializationActor,
            Seed.BeehiveEffectOwnerActor);
        var RequiredActors = CaptureRequiredActors(CurrentRoom, Index);
        ReadyBatches.Add(Index, Hash);
        InvalidateReadiness();
        Batches.Add(Index, new WaitingReplayBatch(
            Index, Hash, RequiredActors, ViewIds,
            Seed, LocalCatalog, ReservedBytes) { Published = PreparedCatalogReservations[Index].Published });
    }

    int ValidatePublishedCatalog(
        Room CurrentRoom, int Index, byte[] ViewPathHash,
        byte[] ImmutableFingerprint, int[] ViewIds,
        out GeneratedRpcDeferral.BatchCatalog Catalog)
    {
        var Value = CurrentRoom.CustomProperties[CatalogKey(Index)];
        var State = ReadCatalogProperty(
            Value, Index, ViewPathHash,
            out var Published, out var Failure);
        var ParsedCatalog = Published?.Catalog;
        _ = ReadCatalogRevision(CurrentRoom, Index);
        if (State != PropertyState.Valid || ParsedCatalog is null)
            throw new InvalidOperationException(
                Failure ?? $"Generated RPC catalog {Index} is missing from its published object batch.");
        Catalog = ParsedCatalog;
        if (!DeterministicObjectIdentity.SameHash(
                Catalog.ImmutableFingerprint, ImmutableFingerprint)
            || !Catalog.PreservesImmutableFacts(Catalog, ViewIds))
            throw new InvalidOperationException(
                $"Generated RPC catalog {Index} changed its authoritative replay seed.");
        return Published!.MaximumCatalogBytes;
    }

    static void RequireSafeBeehiveTransfer(
        Room CurrentRoom, int Index, GeneratedRpcDeferral.BatchCatalog Catalog)
    {
        var EffectOwner = Catalog.BeehiveEffectOwnerActor;
        if (!Catalog.HasBeehiveInitialization || EffectOwner <= 0
            || EffectOwner == CurrentRoom.MasterClientId
            || !CurrentRoom.Players.ContainsKey(EffectOwner) && CurrentRoom.AutoCleanUp) return;
        throw new InvalidOperationException(
            $"Generated Beehive batch {Index} still belongs to Photon actor {EffectOwner}.");
    }

    int ReconcilePreparedCatalogReservation(
        int Index, int ReservedBytes, int InitializationActor,
        int BeehiveEffectOwnerActor)
    {
        PreparedCatalogReservations.TryGetValue(Index, out var Prepared);
        FutureCatalogs.TryGetValue(Index, out var Future);
        if (Prepared is not null && Future is not null)
            throw new InvalidOperationException($"Generated RPC catalog {Index} has multiple retained budget owners.");
        if (Future is not null && (Future.InitializationActor != InitializationActor
            || Future.BeehiveEffectOwnerActor != BeehiveEffectOwnerActor))
            throw new InvalidOperationException($"Generated RPC catalog {Index} changed its retained owner state.");
        CatalogReservation? Previous = Prepared ?? (CatalogReservation?)Future;
        var PreviousBytes = Previous?.ReservedBytes ?? 0;
        var OtherBytes = RetainedCatalogBytes - PreviousBytes;
        if (PreviousBytes != 0 && ReservedBytes != PreviousBytes)
            throw new InvalidOperationException("Generated replay capacity changed during one source lifetime.");
        if (OtherBytes < 0 || ReservedBytes > GeneratedRpcDeferral.MaximumPropertyBytes
            || OtherBytes > GeneratedRpcDeferral.MaximumRetainedCatalogBytes - ReservedBytes)
            throw new InvalidOperationException(
                "Retained generated RPC catalogs exceed the Photon property budget.");
        RetainedCatalogBytes = OtherBytes + ReservedBytes;
        FutureCatalogs.Remove(Index);
        PreparedCatalogReservations[Index] = new CatalogReservation(ReservedBytes, Previous?.Published);
        return ReservedBytes;
    }

    internal bool AdvanceBatch(int Index)
    {
        if (!Batches.TryGetValue(Index, out var Batch))
            throw new InvalidOperationException($"Generated RPC batch {Index} is unavailable.");
        var CurrentRoom = RequireRoom();
        if (CurrentRoom.IsOffline)
            throw new InvalidOperationException(
                "Offline generated RPC replay cannot own a synchronized batch.");
        if (!MaintainReadiness(CurrentRoom) && !TrueSingleActorRoom(CurrentRoom)) return false;
        while (true)
        {
            switch (Batch)
            {
                case WaitingReplayBatch Waiting:
                    if (!PhotonNetwork.IsMasterClient)
                    {
                        var Remote = new RemoteReplayBatch(Waiting);
                        Batches[Index] = Remote; PreparedCatalogReservations.Remove(Index);
                        Batch = Remote;
                        continue;
                    }
                    if (!AllRequiredActorsReady(CurrentRoom, Waiting)) return false;
                    var MasterBatch = new MasterReplayBatch(
                        Waiting, Waiting.Seed.WithLocalMobFacts(Waiting.LocalCatalog));
                    MasterBatch.CatalogMutationRevision = 1;
                    Batches[Index] = MasterBatch;
                    ReserveCatalogProperty(MasterBatch);
                    Batch = MasterBatch;
                    continue;
                case RemoteReplayBatch Remote:
                    if (!RemoteCatalogDelivered(CurrentRoom, Remote, LocalActor)) return false;
                    return GeneratedRpcDeferral.CompletePeerInitialization(
                        Run, Remote.ViewIds, Remote.Seed);
                case MasterReplayBatch Master:
                    return AdvanceMasterCatalog(CurrentRoom, Master);
                default:
                    throw new InvalidOperationException(
                        "Generated RPC replay has an unsupported batch state.");
            }
        }
    }

    internal void RoomPropertiesUpdated(Hashtable Changed)
    {
        if (Room is not { } CurrentRoom || !ReferenceEquals(CurrentRoom, PhotonNetwork.CurrentRoom)) return;
        foreach (var Key in Changed.Keys)
        {
            if (Key is not string Name || !Name.StartsWith(ReadinessPrefix, StringComparison.Ordinal)
                || !int.TryParse(
                    Name.AsSpan(ReadinessPrefix.Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var Actor)
                || Actor <= 0 || !CurrentRoom.Players.ContainsKey(Actor)) continue;
            ReadinessSnapshots.Remove(Actor);
            foreach (var Batch in Batches.Values)
            {
                if (Batch is not MasterReplayBatch { PropertyConfirmed: true } Master
                    || !Master.Catalog.HasCalls || Master.DeliveredActors.Contains(Actor)
                    || !ActorReady(CurrentRoom, Actor, Master.Index, Master.ViewPathHash)) continue;
                Master.Catalog.ReplayActor(Run, CurrentRoom, Actor, Run.Retirement.RetiredViewIds);
                Master.DeliveredActors.Add(Actor);
                InvalidateCatalogProperty(Master);
            }
        }
        Tick();
    }

    internal void Tick()
    {
        if (Room is not { } CurrentRoom || !ReferenceEquals(CurrentRoom, PhotonNetwork.CurrentRoom)) return;
        if (!ReadinessConfirmed) _ = MaintainReadiness(CurrentRoom);
        for (var Index = 0; Index < Run.Slots.Length; Index++)
            if (Batches.TryGetValue(Index, out var Batch)
                && Batch is MasterReplayBatch { PropertyConfirmed: false } Master)
                _ = AdvanceMasterCatalog(CurrentRoom, Master);
        if (CatalogClears.Count != 0) MaintainCatalogClears(CurrentRoom);
    }

    internal void PlayerLeft(int Actor)
    {
        if (Room is not { } CurrentRoom || !ReferenceEquals(CurrentRoom, PhotonNetwork.CurrentRoom)) return;
        ReadinessSnapshots.Remove(Actor);
        foreach (var Batch in Batches.Values)
            if (Batch is MasterReplayBatch Master
                && Master.DeliveredActors.Remove(Actor))
                InvalidateCatalogProperty(Master);
        if (PhotonNetwork.IsMasterClient && !CurrentRoom.IsOffline
            && PhotonNetwork.NetworkClientState == ClientState.Joined)
            _ = CurrentRoom.SetCustomProperties(new Hashtable
            {
                [ReadinessKey(Actor)] = null
            });
    }

    internal bool TryRefreshMobState(
        int Index, int ViewId, int SenderActor, int TargetActor, int State)
    {
        if (!Batches.TryGetValue(Index, out var Batch)
            || Batch is not MasterReplayBatch Master) return false;
        var PreviousCatalog = Master.Catalog;
        var CandidateCatalog = PreviousCatalog.WithMobState(ViewId, SenderActor, State);
        var CurrentRoom = RequireRoom();
        if (ReferenceEquals(CandidateCatalog, PreviousCatalog)
            && !Master.DeliveredActors.Contains(TargetActor)) return true;
        var CandidateDeliveredActors = new HashSet<int>(Master.DeliveredActors);
        if (!PreviousCatalog.HasCalls)
            foreach (var Actor in CurrentRoom.Players.Keys)
                if (Actor != TargetActor
                    && ActorReadyOrLocalSingle(
                        CurrentRoom, Actor, Master.Index, Master.ViewPathHash))
                    CandidateDeliveredActors.Add(Actor);
        CandidateDeliveredActors.Remove(TargetActor);
        _ = CreateCatalogValue(
            Master, CandidateCatalog, CandidateDeliveredActors, out var CandidatePropertyBytes);
        if (CandidatePropertyBytes > Master.ReservedBytes)
            throw new InvalidOperationException(
                "Refreshed generated RPC catalog exceeds its reserved mutation capacity.");
        Master.Catalog = CandidateCatalog;
        Master.CatalogMutationRevision = checked(Master.CatalogMutationRevision + 1);
        Master.DeliveredActors = CandidateDeliveredActors;
        InvalidateCatalogProperty(Master);
        return true;
    }

    internal bool RecoverAuthority(out Exception? Failure)
    {
        Failure = null;
        if (PhotonNetwork.CurrentRoom is not { } CurrentRoom) return true;
        if (Room is null) BindRoom(CurrentRoom);
        else if (!ReferenceEquals(Room, CurrentRoom))
        {
            Failure = new InvalidOperationException(
                "Generated RPC recovery belongs to a different Photon room.");
            return false;
        }
        try
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                DemoteReplayOwnership(CurrentRoom);
                return true;
            }
            var Adopted = new Dictionary<int, MasterReplayBatch>();
            var AdoptedClears = new Dictionary<int, CatalogClear>();
            var AdoptedFuture = new Dictionary<int, FutureCatalog>();
            List<int>? ConfirmedRetirements = null;
            var CatalogBytes = 0;
            for (var Index = 0; Index < Run.Slots.Length; Index++)
            {
                var HasReadyBatch = ReadyBatches.TryGetValue(Index, out var ViewPathHash);
                var HasRetiredBatch = RetiredBatches.TryGetValue(Index, out var RetiredHash);
                var ExpectedHash = HasReadyBatch ? ViewPathHash : HasRetiredBatch ? RetiredHash : null;
                var Value = CurrentRoom.CustomProperties[CatalogKey(Index)];
                var State = ReadCatalogProperty(
                    Value, Index, ExpectedHash,
                    out var Published, out var CatalogFailure);
                if (State == PropertyState.Invalid)
                    throw new InvalidOperationException(
                        CatalogFailure ?? $"Generated RPC catalog {Index} is invalid after master migration.");

                ReplayBatch? Local = null;
                if (HasReadyBatch && !Batches.TryGetValue(Index, out Local))
                    throw new InvalidOperationException(
                        $"Generated RPC batch {Index} has no locally registered replay seed after master migration.");
                if (State == PropertyState.Absent)
                {
                    if (HasReadyBatch)
                        throw new InvalidOperationException(
                            $"Generated RPC catalog {Index} is missing from its published object batch.");
                    if (HasRetiredBatch)
                    {
                        ConfirmedRetirements ??= new List<int>();
                        ConfirmedRetirements.Add(Index);
                    }
                    continue;
                }
                var Catalog = Published!.Catalog;
                var Delivered = Published.Delivered;
                var MaximumCatalogBytes = Published.MaximumCatalogBytes;
                RequireSafeBeehiveTransfer(CurrentRoom, Index, Catalog);
                if (Local is not null
                    && !Catalog.PreservesImmutableFacts(Local.Seed, Local.ViewIds))
                    throw new InvalidOperationException(
                        $"Generated RPC catalog {Index} changed its authoritative replay seed.");
                _ = ReadCatalogRevision(CurrentRoom, Index);
                var ReservedBytes = MaximumCatalogBytes;
                if (ReservedBytes > GeneratedRpcDeferral.MaximumPropertyBytes
                    || CatalogBytes > GeneratedRpcDeferral.MaximumRetainedCatalogBytes - ReservedBytes)
                    throw new InvalidOperationException(
                        "Recovered generated RPC catalogs exceed the Photon property budget.");
                CatalogBytes += ReservedBytes;
                if (!HasReadyBatch && !HasRetiredBatch)
                {
                    if (Run.Slots[Index].Released)
                        AdoptedClears.Add(Index, new CatalogClear(ReservedBytes));
                    else
                        AdoptedFuture.Add(Index, new FutureCatalog(
                            ReservedBytes, Catalog.InitializationActor,
                            Catalog.BeehiveEffectOwnerActor, Published));
                    continue;
                }
                if (HasRetiredBatch)
                {
                    AdoptedClears.Add(Index, new CatalogClear(ReservedBytes));
                    continue;
                }
                var CurrentDelivered = CurrentActors(CurrentRoom, Delivered);
                Adopted.Add(Index, new MasterReplayBatch(
                    Index, ViewPathHash!, Local!.ViewIds, Local.Seed, Catalog,
                    Local.RequiredActors, CurrentDelivered,
                    (object[])Value!, ReservedBytes) { Published = Published });
            }
            Batches.Clear();
            CatalogClears.Clear();
            FutureCatalogs.Clear();
            PreparedCatalogReservations.Clear();
            if (ConfirmedRetirements is not null)
                foreach (var Index in ConfirmedRetirements) RetiredBatches.Remove(Index);
            foreach (var Pair in Adopted) Batches.Add(Pair.Key, Pair.Value);
            foreach (var Pair in AdoptedClears) CatalogClears.Add(Pair.Key, Pair.Value);
            foreach (var Pair in AdoptedFuture) FutureCatalogs.Add(Pair.Key, Pair.Value);
            RetainedCatalogBytes = CatalogBytes;
            AuthorityActor = CurrentRoom.MasterClientId;
            foreach (var Batch in Adopted.Values) _ = AdvanceMasterCatalog(CurrentRoom, Batch);
            if (CatalogClears.Count != 0) MaintainCatalogClears(CurrentRoom);
            return true;
        }
        catch (Exception Exception)
        {
            Failure = Exception;
            return false;
        }
    }

    internal void RetireBatch(int Index)
    {
        if (!ReadyBatches.TryGetValue(Index, out var ViewPathHash))
        {
            if (FutureCatalogs.Remove(Index, out var Future))
                BeginCatalogClear(Index, Future.ReservedBytes);
            else if (PreparedCatalogReservations.Remove(Index, out var PreparedBytes))
                BeginCatalogClear(Index, PreparedBytes.ReservedBytes);
            return;
        }
        ReadyBatches.Remove(Index);
        RetiredBatches.Add(Index, ViewPathHash);
        InvalidateReadiness();
        if (!Batches.Remove(Index, out var Batch)) return;
        PreparedCatalogReservations.Remove(Index);
        BeginCatalogClear(Index, Batch.ReservedBytes);
    }

    internal void CancelBatch(int Index)
    {
        if (!Batches.Remove(Index, out var Batch)) return;
        PreparedCatalogReservations.Remove(Index);
        BeginCatalogClear(Index, Batch.ReservedBytes);
    }

    internal void Release()
    {
        ClearRoomProperties();
        Batches.Clear();
        CatalogClears.Clear();
        FutureCatalogs.Clear();
        PreparedCatalogReservations.Clear();
        ReadyBatches.Clear();
        RetiredBatches.Clear();
        ReadinessSnapshots.Clear();
        ReadinessPublication.Reset();
        Run.ReplayBudget.Reserve(ReadinessPublication, 0);
        Room = null;
        ReadinessValue = null;
        LocalActor = 0;
        AuthorityActor = 0;
        RetainedCatalogBytes = 0;
        ReadinessConfirmed = true;
    }

    internal static string ReadinessKey(int Actor) => ReadinessPrefix
        + Actor.ToString(CultureInfo.InvariantCulture);

    internal static string CatalogKey(int Index) => CatalogPrefix
        + Index.ToString("D3", CultureInfo.InvariantCulture);

    internal static string CatalogRevisionKey(int Index) => CatalogRevisionPrefix
        + Index.ToString("D3", CultureInfo.InvariantCulture);

    void BindRoom(Room CurrentRoom)
    {
        if (CurrentRoom.Players.Count > GeneratedRpcDeferral.MaximumReplayActors)
            throw new InvalidOperationException("Generated replay exceeds its supported Photon actor count.");
        var Actor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (Actor <= 0 || !CurrentRoom.Players.ContainsKey(Actor))
            throw new InvalidOperationException("Generated RPC replay requires the local Photon actor.");
        if (Room is not null
            && (!ReferenceEquals(Room, CurrentRoom) || LocalActor != Actor))
            throw new InvalidOperationException("Generated RPC replay changed Photon ownership during one map run.");
        if (Room is null)
        {
            var Indices = new HashSet<int> { 0 };
            foreach (var Slot in Run.Slots) Indices.Add(Slot.Occurrence.RequestIndex);
            var Entries = new object[Indices.Count]; var Offset = 0;
            foreach (var Index in Indices) Entries[Offset++] = new object[] { Index, new byte[32] };
            var Bytes = GeneratedRpcDeferral.SerializePhotonValue(new object[]
                { ReadinessProtocol, Run.GenerationEpoch, Run.LayoutRevision, Entries }).Length;
            if (Bytes > GeneratedRpcDeferral.MaximumPropertyBytes)
                throw new InvalidOperationException("Generated readiness exceeds its Photon property budget.");
            Run.ReplayBudget.Reserve(ReadinessPublication,
                checked(Bytes * GeneratedRpcDeferral.MaximumReplayActors));
        }
        Room = CurrentRoom;
        LocalActor = Actor;
        AuthorityActor = CurrentRoom.MasterClientId;
        if (AuthorityActor <= 0)
            throw new InvalidOperationException("Generated RPC replay requires a Photon master actor.");
    }

    Room RequireRoom()
    {
        Run.RequirePublished();
        if (!Run.Network.OwnsCurrentRun || Room is not { } CurrentRoom
            || !ReferenceEquals(CurrentRoom, PhotonNetwork.CurrentRoom))
            throw new InvalidOperationException("Generated RPC replay lost its owning Photon room.");
        if (CurrentRoom.MasterClientId != AuthorityActor)
            throw new InvalidOperationException("Photon authority changed during generated RPC replay.");
        return CurrentRoom;
    }

    bool MaintainReadiness(Room CurrentRoom)
    {
        if (CurrentRoom.IsOffline) return true;
        ReadinessValue ??= CreateReadinessValue();
        var Confirmed = ReadinessPublication.AwaitEcho(
            CurrentRoom, ReadinessKey(LocalActor), ReadinessValue,
            "Generated RPC readiness could not be published.");
        if (ReadinessPublication.Failure is { } Failure)
            throw new InvalidOperationException(Failure);
        ReadinessConfirmed = Confirmed;
        return Confirmed;
    }

    object[] CreateReadinessValue()
    {
        if (ReadyBatches.Count == 0) return Array.Empty<object>();
        var Entries = new object[ReadyBatches.Count];
        var Offset = 0;
        foreach (var Pair in ReadyBatches)
            Entries[Offset++] = new object[] { Pair.Key, Pair.Value };
        var Value = new object[] { ReadinessProtocol, Run.GenerationEpoch, Run.LayoutRevision, Entries };
        if (GeneratedRpcDeferral.SerializePhotonValue(Value).Length > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("Generated RPC readiness exceeds the Photon property budget.");
        return Value;
    }

    void InvalidateReadiness()
    {
        ReadinessValue = null;
        ReadinessConfirmed = Room is null || Room.IsOffline;
        ReadinessPublication.Reset();
    }

    bool AllRequiredActorsReady(Room CurrentRoom, WaitingReplayBatch Batch)
    {
        foreach (var Actor in Batch.RequiredActors)
            if (CurrentRoom.Players.ContainsKey(Actor)
                && !ActorReadyOrLocalSingle(
                    CurrentRoom, Actor, Batch.Index, Batch.ViewPathHash)) return false;
        return true;
    }

    bool ActorReadyOrLocalSingle(Room CurrentRoom, int Actor, int Index, byte[] Hash) =>
        Actor == LocalActor && TrueSingleActorRoom(CurrentRoom)
        || ActorReady(CurrentRoom, Actor, Index, Hash);

    bool ActorReady(Room CurrentRoom, int Actor, int Index, byte[] Hash)
    {
        var Value = CurrentRoom.CustomProperties[ReadinessKey(Actor)];
        if (!ReadinessSnapshots.TryGetValue(Actor, out var Snapshot)
            || !ReferenceEquals(Snapshot.Source, Value))
        {
            Snapshot = ParseReadiness(Value);
            ReadinessSnapshots[Actor] = Snapshot;
        }
        if (Snapshot.State == PropertyState.Invalid)
            throw new InvalidOperationException(
                Snapshot.Failure ?? $"Client {Actor} published malformed generated RPC readiness.");
        if (Snapshot.State == PropertyState.Absent) return false;
        var Offset = Array.BinarySearch(Snapshot.Indices, Index);
        if (Offset < 0) return false;
        if (!DeterministicObjectIdentity.SameHash(Snapshot.Hashes[Offset], Hash))
            throw new InvalidOperationException(
                $"Client {Actor} published a different generated RPC batch {Index}.");
        return true;
    }

    bool ClaimBeehiveEffectOwner(Room CurrentRoom, MasterReplayBatch Master)
    {
        if (!Master.Catalog.HasBeehiveInitialization
            || Master.Catalog.BeehiveEffectOwnerActor == LocalActor) return true;
        RequireSafeBeehiveTransfer(CurrentRoom, Master.Index, Master.Catalog);
        Master.Catalog = Master.Catalog.WithBeehiveEffectOwner(LocalActor);
        Master.CatalogMutationRevision = checked(Master.CatalogMutationRevision + 1);
        InvalidateCatalogProperty(Master);
        return false;
    }

    bool AdvanceMasterCatalog(Room CurrentRoom, MasterReplayBatch Master)
    {
        if (!MaintainCatalog(CurrentRoom, Master)) return false;
        if (!Master.Catalog.HasCalls)
        {
            if (!ClaimBeehiveEffectOwner(CurrentRoom, Master)) return false;
            return GeneratedRpcDeferral.CompletePeerInitialization(
                    Run, Master.ViewIds, Master.Catalog)
                && GeneratedRpcDeferral.CompleteMasterInitialization(
                    Run, Master.ViewIds, Master.Catalog);
        }
        foreach (var Actor in CurrentRoom.Players.Keys)
        {
            if (Master.DeliveredActors.Contains(Actor)
                || !ActorReadyOrLocalSingle(CurrentRoom, Actor, Master.Index, Master.ViewPathHash)) continue;
            Master.Catalog.ReplayActor(Run, CurrentRoom, Actor, Run.Retirement.RetiredViewIds);
            Master.DeliveredActors.Add(Actor);
            InvalidateCatalogProperty(Master);
        }
        if (!Master.PropertyConfirmed
            && !MaintainCatalog(CurrentRoom, Master)) return false;
        if (!RequiredActorsDelivered(CurrentRoom, Master)
            || !ClaimBeehiveEffectOwner(CurrentRoom, Master)) return false;
        return GeneratedRpcDeferral.CompletePeerInitialization(
                Run, Master.ViewIds, Master.Catalog)
            && GeneratedRpcDeferral.CompleteMasterInitialization(
                Run, Master.ViewIds, Master.Catalog);
    }

    bool MaintainCatalog(Room CurrentRoom, MasterReplayBatch Master)
    {
        var CurrentRevision = ReadCatalogRevision(CurrentRoom, Master.Index);
        var CurrentValue = CurrentRoom.CustomProperties[CatalogKey(Master.Index)];
        var State = ReadCatalogProperty(
            CurrentValue, Master.Index, Master.ViewPathHash,
            out var Published, out var Failure);
        var CurrentCatalog = Published?.Catalog;
        if (State != PropertyState.Valid || CurrentCatalog is null
            || !CurrentCatalog.PreservesImmutableFacts(Master.Seed, Master.ViewIds))
            throw new InvalidOperationException(
                Failure ?? $"Generated RPC catalog {Master.Index} changed its authoritative replay seed.");

        if (Master.PendingRevision >= 0)
        {
            if (CurrentRevision == Master.PendingRevision)
            {
                if (Master.PropertyValue is null
                    || !PhotonPropertyProtocol.SameValue(CurrentValue, Master.PropertyValue))
                    throw new InvalidOperationException(
                        $"Generated RPC catalog {Master.Index} changed at one publication revision.");
                Master.ConfirmedCatalogMutationRevision = Math.Max(
                    Master.ConfirmedCatalogMutationRevision,
                    Master.PendingCatalogMutationRevision);
                Master.PendingRevision = -1;
                Master.ExpectedRevision = -1;
                Master.PropertyValue = null;
            }
            else if (CurrentRevision == Master.ExpectedRevision) return false;
            else if (CurrentRevision < Master.ExpectedRevision)
                throw new InvalidOperationException(
                    $"Generated RPC catalog {Master.Index} publication revision moved backwards.");
            else
            {
                Master.PendingRevision = -1;
                Master.ExpectedRevision = -1;
                Master.PropertyValue = null;
            }
        }

        foreach (var Actor in Published!.Delivered)
            if (CurrentRoom.Players.ContainsKey(Actor)) Master.DeliveredActors.Add(Actor);
        if (Master.CatalogMutationRevision == Master.ConfirmedCatalogMutationRevision)
            Master.Catalog = CurrentCatalog;
        var PropertyValue = CreateCatalogValue(
            Master, Master.Catalog, Master.DeliveredActors, out var PropertyBytes);
        if (Master.ReservedBytes != 0 && PropertyBytes > Master.ReservedBytes)
            throw new InvalidOperationException(
                "Generated RPC catalog exceeds its Photon property budget.");
        if (PhotonPropertyProtocol.SameValue(CurrentValue, PropertyValue))
        {
            Master.PropertyConfirmed = true;
            Master.ConfirmedCatalogMutationRevision = Master.CatalogMutationRevision;
            return true;
        }

        var NextRevision = checked(CurrentRevision + 1);
        if (!CurrentRoom.SetCustomProperties(
                new Hashtable
                {
                    [CatalogKey(Master.Index)] = PropertyValue,
                    [CatalogRevisionKey(Master.Index)] = NextRevision
                },
                new Hashtable { [CatalogRevisionKey(Master.Index)] = CurrentRevision }))
            throw new InvalidOperationException(
                "Generated RPC catalog could not be published.");
        Master.PropertyValue = PropertyValue;
        Master.ExpectedRevision = CurrentRevision;
        Master.PendingRevision = NextRevision;
        Master.PendingCatalogMutationRevision = Master.CatalogMutationRevision;
        Master.PropertyConfirmed = false;
        return false;
    }

    object[] CreateCatalogValue(
        MasterReplayBatch Master, GeneratedRpcDeferral.BatchCatalog Catalog,
        HashSet<int> DeliveredActors, out int Bytes)
    {
        var Delivered = new int[DeliveredActors.Count];
        DeliveredActors.CopyTo(Delivered);
        Array.Sort(Delivered);
        var Value = EncodeCatalog(Master.Index, Master.ViewPathHash, Catalog, Delivered, Master.ReservedBytes);
        Bytes = GeneratedRpcDeferral.SerializePhotonValue(Value).Length;
        if (Bytes > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException(
                "Generated RPC catalog exceeds its Photon property budget.");
        return Value;
    }

    object[] EncodeCatalog(int Index, byte[] ViewPathHash,
        GeneratedRpcDeferral.BatchCatalog Catalog, int[] Delivered, int MaximumCatalogBytes) => new object[]
        {
            CatalogProtocol, Run.GenerationEpoch, Run.LayoutRevision, Index,
            ViewPathHash, Catalog.InitializationActor,
            Catalog.HasBeehiveInitialization, Catalog.BeehiveEffectOwnerActor,
            Catalog.Fingerprint, Catalog.PropertyValue, Delivered, MaximumCatalogBytes
        };

    int CatalogReservationBytes(int Index, byte[] ViewPathHash, GeneratedRpcDeferral.BatchCatalog MaximumCatalog)
    {
        var Actors = new int[MaximumCatalog.HasCalls ? GeneratedRpcDeferral.MaximumReplayActors : 0];
        for (var i = 0; i < Actors.Length; i++) Actors[i] = int.MaxValue - Actors.Length + i + 1;
        var Bytes = GeneratedRpcDeferral.SerializePhotonValue(EncodeCatalog(Index, ViewPathHash,
            MaximumCatalog, Actors, GeneratedRpcDeferral.MaximumPropertyBytes)).Length;
        if (Bytes > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("Generated replay capacity exceeds its Photon property budget.");
        return Bytes;
    }
    static int ReadCatalogRevision(Room CurrentRoom, int Index)
    {
        if (CurrentRoom.CustomProperties[CatalogRevisionKey(Index)] is int Revision
            && Revision >= 0) return Revision;
        throw new InvalidOperationException(
            $"Generated RPC catalog {Index} has no valid publication revision.");
    }

    void ReserveCatalogProperty(MasterReplayBatch Master)
    {
        if (!PreparedCatalogReservations.Remove(Master.Index, out var Reservation))
        {
            RetainedCatalogBytes = PrepareCatalogPublication(
                Master, RetainedCatalogBytes);
            return;
        }
        var OtherBytes = RetainedCatalogBytes - Reservation.ReservedBytes;
        if (OtherBytes < 0)
            throw new InvalidOperationException(
                $"Generated RPC catalog {Master.Index} lost its retained property reservation.");
        RetainedCatalogBytes = PrepareCatalogPublication(
            Master, OtherBytes);
    }

    int PrepareCatalogPublication(
        MasterReplayBatch Master, int CurrentCatalogBytes)
    {
        var PropertyValue = CreateCatalogValue(
            Master, Master.Catalog, Master.DeliveredActors, out var PropertyBytes);
        var ReservedBytes = Master.ReservedBytes;
        if (PropertyBytes > ReservedBytes)
            throw new InvalidOperationException("Generated RPC publication exceeds its reserved mutation capacity.");
        if (ReservedBytes > GeneratedRpcDeferral.MaximumPropertyBytes
            || CurrentCatalogBytes > GeneratedRpcDeferral.MaximumRetainedCatalogBytes - ReservedBytes)
            throw new InvalidOperationException(
                "Retained generated RPC catalogs exceed the Photon property budget.");
        Master.PropertyValue = PropertyValue;
        Master.PropertyConfirmed = false;
        return CurrentCatalogBytes + ReservedBytes;
    }

    void InvalidateCatalogProperty(MasterReplayBatch Master)
    {
        if (Master.PendingRevision < 0) Master.PropertyValue = null;
        Master.PropertyConfirmed = false;
    }

    bool RemoteCatalogDelivered(Room CurrentRoom, RemoteReplayBatch Remote, int Actor)
    {
        var Value = CurrentRoom.CustomProperties[CatalogKey(Remote.Index)];
        var Revision = ReadCatalogRevision(CurrentRoom, Remote.Index);
        if (!ReferenceEquals(Remote.CatalogSource, Value)
            || Remote.CatalogRevision != Revision)
        {
            if (Remote.CatalogRevision > Revision)
                throw new InvalidOperationException(
                    $"Generated RPC catalog {Remote.Index} publication revision moved backwards.");
            if (Remote.CatalogRevision == Revision && Remote.CatalogSource is not null
                && !PhotonPropertyProtocol.SameValue(Remote.CatalogSource, Value))
                throw new InvalidOperationException(
                    $"Generated RPC catalog {Remote.Index} changed at one publication revision.");
            Remote.CatalogSource = Value;
            Remote.CatalogRevision = Revision;
            Remote.CatalogState = ReadCatalogProperty(
                Value, Remote.Index, Remote.ViewPathHash,
                out var Published, out var Failure);
            var Catalog = Published?.Catalog;
            if (Remote.CatalogState == PropertyState.Valid
                && (Catalog is null || !Catalog.PreservesImmutableFacts(Remote.Seed, Remote.ViewIds)))
            {
                Remote.CatalogState = PropertyState.Invalid;
                Failure = "Generated RPC catalog changed its authoritative replay seed.";
            }
            Remote.HasCalls = Catalog?.HasCalls ?? false;
            Remote.DeliveredActors = Published?.Delivered ?? Array.Empty<int>();
            Remote.Failure = Failure;
        }
        if (Remote.CatalogState == PropertyState.Invalid)
            throw new InvalidOperationException(Remote.Failure ?? "Generated RPC catalog is malformed.");
        if (Remote.CatalogState == PropertyState.Absent) return false;
        return !Remote.HasCalls || Array.BinarySearch(Remote.DeliveredActors, Actor) >= 0;
    }

    bool RequiredActorsDelivered(Room CurrentRoom, MasterReplayBatch Master)
    {
        if (!Master.Catalog.HasCalls) return true;
        foreach (var Actor in Master.RequiredActors)
            if (CurrentRoom.Players.ContainsKey(Actor)
                && !Master.DeliveredActors.Contains(Actor)) return false;
        return true;
    }

    void MaintainCatalogClears(Room CurrentRoom)
    {
        for (var Index = 0; Index < Run.Slots.Length; Index++)
        {
            if (!CatalogClears.TryGetValue(Index, out var Clear)) continue;
            var Value = CurrentRoom.CustomProperties[CatalogKey(Index)];
            if (CurrentRoom.CustomProperties[CatalogRevisionKey(Index)]
                is not int Revision) continue;
            if (Revision < 0)
                throw new InvalidOperationException(
                    $"Generated RPC catalog {Index} has an invalid publication revision.");
            if (Value is null || Value is object[] { Length: 0 })
            {
                RetainedCatalogBytes -= Clear.RetainedBytes;
                CatalogClears.Remove(Index);
                RetiredBatches.Remove(Index);
                continue;
            }
            if (!PhotonNetwork.IsMasterClient) continue;
            if (Clear.PendingRevision >= 0)
            {
                if (Revision == Clear.PendingRevision)
                    throw new InvalidOperationException(
                        $"Generated RPC catalog {Index} retained content after its clear revision.");
                if (Revision == Clear.ExpectedRevision) return;
                if (Revision < Clear.ExpectedRevision)
                    throw new InvalidOperationException(
                        $"Generated RPC catalog {Index} publication revision moved backwards.");
                Clear.PendingRevision = -1;
                Clear.ExpectedRevision = -1;
            }
            var NextRevision = checked(Revision + 1);
            if (!CurrentRoom.SetCustomProperties(
                    new Hashtable
                    {
                        [CatalogKey(Index)] = Array.Empty<object>(),
                        [CatalogRevisionKey(Index)] = NextRevision
                    },
                    new Hashtable { [CatalogRevisionKey(Index)] = Revision }))
                throw new InvalidOperationException(
                    "Generated RPC catalog could not be retired.");
            Clear.ExpectedRevision = Revision;
            Clear.PendingRevision = NextRevision;
        }
    }

    void BeginCatalogClear(int Index, int RetainedBytes)
    {
        if (Room is null || Room.IsOffline) return;
        if (CatalogClears.ContainsKey(Index)) return;
        CatalogClears.Add(Index, new CatalogClear(RetainedBytes));
    }

    void ClearRoomProperties()
    {
        if (Room is not { IsOffline: false } CurrentRoom
            || !ReferenceEquals(CurrentRoom, PhotonNetwork.CurrentRoom)
            || PhotonNetwork.NetworkClientState != ClientState.Joined) return;
        var Values = new Hashtable { [ReadinessKey(LocalActor)] = Array.Empty<object>() };
        if (PhotonNetwork.IsMasterClient)
        {
            foreach (var Index in ReadyBatches.Keys) Clear(Index);
            foreach (var Index in RetiredBatches.Keys) Clear(Index);
            foreach (var Index in FutureCatalogs.Keys) Clear(Index);
            foreach (var Index in PreparedCatalogReservations.Keys) Clear(Index);
            foreach (var Index in CatalogClears.Keys) Clear(Index);

            void Clear(int Index)
            {
                Values[CatalogKey(Index)] = Array.Empty<object>();
                if (CurrentRoom.CustomProperties[CatalogRevisionKey(Index)] is int Revision
                    && Revision >= 0)
                    Values[CatalogRevisionKey(Index)] = checked(Revision + 1);
            }
        }
        if (!CurrentRoom.SetCustomProperties(Values))
            throw new InvalidOperationException("Generated RPC room state could not be cleared.");
    }

    bool HasUnconfirmedCatalog()
    {
        foreach (var Batch in Batches.Values)
            if (Batch is MasterReplayBatch { PropertyConfirmed: false }) return true;
        return false;
    }

    void DemoteReplayOwnership(Room CurrentRoom)
    {
        foreach (var Pair in ReadyBatches)
        {
            if (!Batches.TryGetValue(Pair.Key, out var Batch))
                throw new InvalidOperationException(
                    $"Generated RPC batch {Pair.Key} has no locally registered replay seed after master migration.");
            if (Batch is not RemoteReplayBatch)
                Batches[Pair.Key] = new RemoteReplayBatch(Batch);
            PreparedCatalogReservations.Remove(Pair.Key);
        }
        foreach (var Clear in CatalogClears.Values)
        {
            Clear.ExpectedRevision = -1; Clear.PendingRevision = -1;
        }
        AuthorityActor = CurrentRoom.MasterClientId;
    }

    static int[] RegisteredViewIds(PhotonView[] Views)
    {
        var Result = new int[Views.Length];
        var Seen = new HashSet<int>();
        for (var Offset = 0; Offset < Views.Length; Offset++)
        {
            var View = Views[Offset];
            if (!View || View.ViewID <= 0
                || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
                throw new InvalidOperationException(
                    "Generated RPC replay requires every synchronized PhotonView registration.");
            if (!Seen.Add(View.ViewID))
                throw new InvalidOperationException(
                    $"Generated RPC replay contains duplicate PhotonView {View.ViewID}.");
            Result[Offset] = View.ViewID;
        }
        return Result;
    }

    static int[] CurrentActors(Room CurrentRoom, int[] Actors)
    {
        var Result = new List<int>(Actors.Length);
        foreach (var Actor in Actors)
            if (CurrentRoom.Players.ContainsKey(Actor)) Result.Add(Actor);
        return Result.ToArray();
    }

    int[] CaptureRequiredActors(Room CurrentRoom, int Index)
    {
        if (Index == 0 && Run.InitialGenerationCohort is { } Cohort)
        {
            var Admission = new List<int>(Cohort.Count);
            for (var Offset = 0; Offset < Cohort.Count; Offset++)
            {
                var Actor = Cohort.ActorAt(Offset);
                if (CurrentRoom.Players.ContainsKey(Actor)) Admission.Add(Actor);
            }
            return Admission.ToArray();
        }
        var Actors = new List<int>(CurrentRoom.Players.Count);
        foreach (var Actor in CurrentRoom.Players.Keys)
            if (Actor > 0) Actors.Add(Actor);
        Actors.Sort();
        if (Actors.Count == 0)
            throw new InvalidOperationException(
                "Generated RPC replay requires at least one Photon actor.");
        return Actors.ToArray();
    }

    static bool TrueSingleActorRoom(Room CurrentRoom) => CurrentRoom.Players.Count == 1;

    ReadinessSnapshot ParseReadiness(object? Value)
    {
        if (Value is null || Value is object[] { Length: 0 })
            return new ReadinessSnapshot(Value, PropertyState.Absent, Array.Empty<int>(), Array.Empty<byte[]>(), null);
        if (Value is object[] { Length: > 1 } Stale
            && Stale[1] is string StaleEpoch && StaleEpoch != Run.GenerationEpoch)
            return new ReadinessSnapshot(Value, PropertyState.Absent, Array.Empty<int>(), Array.Empty<byte[]>(), null);
        if (Value is not object[] { Length: 4 } Fields
            || Fields[0] is not string ProtocolValue || ProtocolValue != ReadinessProtocol
            || Fields[1] is not string Epoch || Fields[2] is not int Revision
            || Fields[3] is not object[] Entries || Entries.Length > NavigationRoles.MaximumOrdinarySegments)
            return Invalid("Generated RPC readiness is malformed.");
        if (Epoch != Run.GenerationEpoch)
            return new ReadinessSnapshot(Value, PropertyState.Absent, Array.Empty<int>(), Array.Empty<byte[]>(), null);
        if (Revision != Run.LayoutRevision)
            return Invalid("Generated RPC readiness has a different layout revision.");
        var Indices = new int[Entries.Length];
        var Hashes = new byte[Entries.Length][];
        var Previous = -1;
        for (var i = 0; i < Entries.Length; i++)
        {
            if (Entries[i] is not object[] { Length: 2 } Entry
                || Entry[0] is not int Index || Index <= Previous || Index < 0 || Index >= Run.Slots.Length
                || Entry[1] is not byte[] Hash || Hash.Length != 32)
                return Invalid("Generated RPC readiness entries are malformed.");
            Previous = Index;
            Indices[i] = Index;
            Hashes[i] = (byte[])Hash.Clone();
        }
        return new ReadinessSnapshot(Value, PropertyState.Valid, Indices, Hashes, null);

        ReadinessSnapshot Invalid(string Failure) =>
            new(Value, PropertyState.Invalid, Array.Empty<int>(), Array.Empty<byte[]>(), Failure);
    }

    PropertyState ReadCatalogProperty(
        object? Value, int RequiredIndex, byte[]? RequiredHash,
        out PublishedCatalog? Published, out string? Failure)
    {
        Published = null;
        Failure = null;
        if (Value is null || Value is object[] { Length: 0 }) return PropertyState.Absent;
        if (Value is object[] { Length: > 1 } Stale
            && Stale[1] is string StaleEpoch && StaleEpoch != Run.GenerationEpoch)
            return PropertyState.Absent;
        CatalogReservation? Owner = Batches.TryGetValue(RequiredIndex, out var Batch) ? Batch
            : FutureCatalogs.TryGetValue(RequiredIndex, out var Future) ? Future
            : PreparedCatalogReservations.TryGetValue(RequiredIndex, out var Prepared) ? Prepared : null;
        if (Owner?.Published is { } Observed && ReferenceEquals(Value, Observed.SourceVersion))
        {
            if (RequiredHash is not null && !DeterministicObjectIdentity.SameHash(Observed.Hash, RequiredHash))
            {
                Failure = "Generated RPC catalog belongs to a different map batch.";
                return PropertyState.Invalid;
            }
            Published = Observed;
            return PropertyState.Valid;
        }
        if (Value is not object[] { Length: 12 } Fields
            || Fields[0] is not string ProtocolValue || ProtocolValue != CatalogProtocol
            || Fields[1] is not string Epoch || Fields[2] is not int Revision
            || Fields[3] is not int Index || Fields[4] is not byte[] Hash || Hash.Length != 32
            || Fields[5] is not int InitializationActor || InitializationActor <= 0
            || Fields[6] is not bool HasBeehiveInitialization
            || Fields[7] is not int BeehiveEffectOwnerActor || BeehiveEffectOwnerActor < 0
            || !HasBeehiveInitialization && BeehiveEffectOwnerActor != 0
            || Fields[11] is not int Capacity || Capacity <= 0 || Capacity > GeneratedRpcDeferral.MaximumPropertyBytes
            || GeneratedRpcDeferral.SerializePhotonValue(Fields).Length > Capacity
            || Fields[8] is not byte[] CatalogFingerprint
            || !GeneratedRpcDeferral.BatchCatalog.TryParsePropertyCalls(
                Fields[9], CatalogFingerprint, InitializationActor,
                HasBeehiveInitialization, BeehiveEffectOwnerActor, out var ParsedCatalog)
            || ParsedCatalog.HasKind(GeneratedRpcDeferral.ReplayKind.Beehive)
                != HasBeehiveInitialization
            || Fields[10] is not int[] Actors || Actors.Length > GeneratedRpcDeferral.MaximumReplayActors)
        {
            Failure = "Generated RPC catalog is malformed.";
            return PropertyState.Invalid;
        }
        if (Revision != Run.LayoutRevision || Index != RequiredIndex
            || RequiredHash is not null && !DeterministicObjectIdentity.SameHash(Hash, RequiredHash))
        {
            Failure = "Generated RPC catalog belongs to a different map batch.";
            return PropertyState.Invalid;
        }
        for (var i = 0; i < Actors.Length; i++)
            if (Actors[i] <= 0 || i != 0 && Actors[i - 1] >= Actors[i])
            {
                Failure = "Generated RPC catalog delivery actors are malformed.";
                return PropertyState.Invalid;
            }
        var ExpectedCapacity = Owner?.ReservedBytes
            ?? (CatalogClears.TryGetValue(RequiredIndex, out var Clear) ? Clear.RetainedBytes : 0);
        if (ExpectedCapacity != 0 && Capacity != ExpectedCapacity)
        {
            Failure = "Generated replay capacity changed during one source lifetime.";
            return PropertyState.Invalid;
        }
        Published = new PublishedCatalog(Value, Hash, ParsedCatalog, Actors, Capacity);
        if (Owner is not null) Owner.Published = Published;
        return PropertyState.Valid;
    }
}
