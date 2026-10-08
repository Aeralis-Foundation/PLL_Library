using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using PhotonPlayer = Photon.Realtime.Player;
using UnityEngine;

namespace PLL.Library;

internal static class GeneratedRpcDeferral
{
    internal enum RecipientKind : byte { All, Others, MasterClient, AllViaServer, Player }
    internal enum ReplayKind : byte { Beehive, Trackable, BreakableBridge, MobState }
    static readonly ReplayKind[] ReplayKinds = (ReplayKind[])Enum.GetValues(typeof(ReplayKind));
    enum ReplayDisposition : byte { Catalog, Suppress }

    sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? Left, T? Right) => ReferenceEquals(Left, Right);
        public int GetHashCode(T Value) => RuntimeHelpers.GetHashCode(Value);
    }

    sealed class RootReservation : IDisposable
    {
        readonly Func<bool> CancellationRequested;
        bool CancellationForced;

        internal RootReservation(NativeFeatureGeneration.FeatureRoots ReservedRoots, Func<bool> IsCancellationRequested)
        {
            Roots = ReservedRoots;
            CancellationRequested = IsCancellationRequested;
        }

        internal NativeFeatureGeneration.FeatureRoots Roots { get; }
        internal bool Cancelled => CancellationForced || CancellationRequested();
        internal void Cancel() => CancellationForced = true;

        public void Dispose()
        {
            if (ReferenceEquals(ActiveRoots, this)) ActiveRoots = null;
        }
    }

    sealed class DeferredRpc
    {
        internal DeferredRpc(
            PhotonView View, Transform Root, string Method, RecipientKind Recipient,
            int SenderActor, int TargetActor, ReplayKind Kind,
            byte[] ParameterPayload, int CatalogBytes, Room? Room, MapRun Run)
        {
            this.View = View;
            this.Root = Root;
            this.Method = Method;
            this.Recipient = Recipient;
            this.SenderActor = SenderActor;
            this.TargetActor = TargetActor;
            this.Kind = Kind;
            this.ParameterPayload = ParameterPayload;
            this.CatalogBytes = CatalogBytes;
            this.Room = Room;
            this.Run = Run;
            RunIdentity = Run.Identity;
            GenerationEpoch = Run.GenerationEpoch;
            LayoutRevision = Run.LayoutRevision;
        }

        internal PhotonView View { get; }
        internal Transform Root { get; }
        internal string Method { get; }
        internal RecipientKind Recipient { get; }
        internal int SenderActor { get; }
        internal int TargetActor { get; }
        internal ReplayKind Kind { get; }
        internal byte[] ParameterPayload { get; }
        internal int CatalogBytes { get; }
        internal Room? Room { get; }
        internal MapRun Run { get; }
        internal long RunIdentity { get; }
        internal string GenerationEpoch { get; }
        internal int LayoutRevision { get; }

        internal CatalogCall ToCatalogCall() => new(
            View.ViewID, Method, Recipient, SenderActor, TargetActor, Kind, ParameterPayload);

        internal void ReplayOriginal()
        {
            var Parameters = DeserializeParameters(ParameterPayload);
            switch (Recipient)
            {
                case RecipientKind.All:
                    View.RPC(Method, RpcTarget.All, Parameters);
                    return;
                case RecipientKind.Others:
                    View.RPC(Method, RpcTarget.Others, Parameters);
                    return;
                case RecipientKind.MasterClient:
                    View.RPC(Method, RpcTarget.MasterClient, Parameters);
                    return;
                case RecipientKind.AllViaServer:
                    View.RPC(Method, RpcTarget.AllViaServer, Parameters);
                    return;
                case RecipientKind.Player:
                    if (PhotonNetwork.CurrentRoom is { } CurrentRoom
                        && CurrentRoom.Players.TryGetValue(TargetActor, out var Target))
                        View.RPC(Method, Target, Parameters);
                    return;
                default:
                    throw new InvalidOperationException("A generated RPC has an unsupported recipient kind.");
            }
        }
    }

    sealed class GeneratedReplayApplication
    {
        internal GeneratedReplayApplication(GeneratedBatchKey BatchIndex, PhotonView View)
        {
            this.BatchIndex = BatchIndex;
            this.View = View;
        }

        internal GeneratedBatchKey BatchIndex { get; }
        internal PhotonView View { get; }
        int InstanceId;
        int MaximumInstanceId;
        bool SpawnTracker;
        bool Applied;
        bool BeehiveCompleted;
        bool BeehiveProjected;
        internal bool ReplayApplied => Applied;
        bool TrackableCreated;

        internal bool TryTakeBeehiveProjected()
        {
            if (BeehiveProjected) return false;
            BeehiveProjected = true;
            return true;
        }

        internal bool TryTakeBeehiveCompleted()
        {
            if (BeehiveCompleted) return false;
            BeehiveCompleted = true;
            return true;
        }

        internal bool TryTakeTrackableCreated()
        {
            if (TrackableCreated) return false;
            TrackableCreated = true;
            return true;
        }

        internal bool IsRepeated(int InstanceId, int MaximumInstanceId, bool SpawnTracker)
        {
            if (!Applied)
            {
                this.InstanceId = InstanceId;
                this.MaximumInstanceId = MaximumInstanceId;
                this.SpawnTracker = SpawnTracker;
                Applied = true;
                return false;
            }
            return this.InstanceId == InstanceId
                && this.MaximumInstanceId == MaximumInstanceId
                && this.SpawnTracker == SpawnTracker;
        }
    }

    internal sealed class CatalogCall
    {
        internal CatalogCall(
            int ViewId, string Method, RecipientKind Recipient,
            int SenderActor, int TargetActor, ReplayKind Kind, byte[] ParameterPayload)
        {
            this.ViewId = ViewId;
            this.Method = Method;
            this.Recipient = Recipient;
            this.SenderActor = SenderActor;
            this.TargetActor = TargetActor;
            this.Kind = Kind;
            this.ParameterPayload = ParameterPayload;
            PropertyValue = new object[]
            {
                ViewId, Method, (byte)Recipient, SenderActor, TargetActor, ParameterPayload
            };
        }

        internal int ViewId { get; }
        internal string Method { get; }
        internal RecipientKind Recipient { get; }
        internal int SenderActor { get; }
        internal int TargetActor { get; }
        internal ReplayKind Kind { get; }
        internal byte[] ParameterPayload { get; }
        internal object[] PropertyValue { get; }

        internal bool IncludesActor(int Actor) => Recipient switch
        {
            RecipientKind.All or RecipientKind.AllViaServer => true,
            RecipientKind.Others => Actor != SenderActor,
            RecipientKind.MasterClient or RecipientKind.Player => Actor == TargetActor,
            _ => throw new InvalidOperationException("A generated RPC has an unsupported recipient kind.")
        };

        internal void Replay(PhotonPlayer Target, ISet<int> RetiredViewIds)
        {
            var View = PhotonNetwork.GetPhotonView(ViewId);
            if (!View)
            {
                if (RetiredViewIds.Contains(ViewId)) return;
                throw new InvalidOperationException(
                    $"Generated PhotonView {ViewId} was unavailable for RPC '{Method}'.");
            }
            var Parameters = DeserializeParameters(ParameterPayload);
            if (!MatchesReplayComponent(View, Kind))
                throw new InvalidOperationException(
                    $"Generated PhotonView {ViewId} no longer matches RPC '{Method}'.");
            View.RPC(Method, Target, Parameters);
        }
    }

    internal sealed class BatchCatalog
    {
        readonly CatalogCall[] Calls;
        readonly object[] PropertyCalls;

        BatchCatalog(
            CatalogCall[] Calls, int InitializationActor = 0,
            bool HasBeehiveInitialization = false,
            int BeehiveEffectOwnerActor = 0)
        {
            this.Calls = Calls;
            this.InitializationActor = InitializationActor;
            this.HasBeehiveInitialization = HasBeehiveInitialization
                || HasKind(ReplayKind.Beehive);
            if (!this.HasBeehiveInitialization && BeehiveEffectOwnerActor != 0)
                throw new InvalidOperationException(
                    "A catalog without Beehive initialization has a Beehive effect owner.");
            this.BeehiveEffectOwnerActor = BeehiveEffectOwnerActor;
            PropertyCalls = new object[Calls.Length];
            for (var i = 0; i < Calls.Length; i++) PropertyCalls[i] = Calls[i].PropertyValue;
            var Serialized = SerializePhotonValue(new object[]
            {
                InitializationActor, this.HasBeehiveInitialization,
                BeehiveEffectOwnerActor, PropertyCalls
            });
            var SerializedBytes = Serialized.Length;
            if (SerializedBytes > MaximumPropertyBytes)
                throw new InvalidOperationException(
                    $"Generated RPC catalog contains {SerializedBytes} bytes; the maximum is {MaximumPropertyBytes}.");
            using var Hasher = SHA256.Create();
            Fingerprint = Hasher.ComputeHash(Serialized);
            var ImmutableCalls = new List<object>(Calls.Length);
            foreach (var Call in Calls)
                if (Call.Kind != ReplayKind.MobState) ImmutableCalls.Add(Call.PropertyValue);
            ImmutableFingerprint = Hasher.ComputeHash(
                SerializePhotonValue(new object[]
                {
                    InitializationActor, this.HasBeehiveInitialization,
                    ImmutableCalls.ToArray()
                }));
        }

        internal static BatchCatalog Create(
            CatalogCall[] Calls, int InitializationActor = 0,
            bool HasBeehiveInitialization = false,
            int BeehiveEffectOwnerActor = 0)
        {
            if (!HasUniqueReplayFacts(Calls))
                throw new InvalidOperationException(
                    "A generated RPC catalog contains multiple values for one replay fact.");
            return new BatchCatalog(
                Calls, InitializationActor, HasBeehiveInitialization,
                BeehiveEffectOwnerActor);
        }

        internal bool HasCalls => Calls.Length != 0;
        internal object[] PropertyValue => PropertyCalls;
        internal int InitializationActor { get; }
        internal bool HasBeehiveInitialization { get; }
        internal int BeehiveEffectOwnerActor { get; }

        internal BatchCatalog WithBeehiveEffectOwner(int Actor) =>
            new(Calls, InitializationActor, HasBeehiveInitialization, Actor);

        internal bool HasKind(ReplayKind Kind)
        {
            foreach (var Call in Calls)
                if (Call.Kind == Kind) return true;
            return false;
        }

        internal bool HasFact(int ViewId, ReplayKind Kind)
        {
            foreach (var Call in Calls)
                if (Call.ViewId == ViewId && Call.Kind == Kind) return true;
            return false;
        }
        internal byte[] Fingerprint { get; }
        internal byte[] ImmutableFingerprint { get; }

        internal bool IncludesLocalFacts(
            BatchCatalog Candidate, bool RequireMasterInitializationFacts,
            int[] RegisteredViewIds)
        {
            foreach (var CandidateCall in Candidate.Calls)
            {
                if (Array.IndexOf(RegisteredViewIds, CandidateCall.ViewId) < 0) return false;
                if (CandidateCall.Kind == ReplayKind.MobState
                    || !RequireMasterInitializationFacts
                        && CandidateCall.Kind is ReplayKind.Beehive
                            or ReplayKind.Trackable or ReplayKind.BreakableBridge) continue;
                var Found = false;
                foreach (var Call in Calls)
                {
                    if (!SameLocalFact(Call, CandidateCall)) continue;
                    Found = true;
                    break;
                }
                if (!Found) return false;
            }
            return true;
        }

        internal bool PreservesImmutableFacts(BatchCatalog Seed, int[] RegisteredViewIds)
        {
            foreach (var Call in Calls)
                if (Array.IndexOf(RegisteredViewIds, Call.ViewId) < 0) return false;
            foreach (var Call in Seed.Calls)
                if (Array.IndexOf(RegisteredViewIds, Call.ViewId) < 0) return false;
            return DeterministicObjectIdentity.SameHash(
                ImmutableFingerprint, Seed.ImmutableFingerprint);
        }

        internal BatchCatalog WithLocalMobFacts(BatchCatalog Local)
        {
            var Result = this;
            foreach (var Call in Local.Calls)
            {
                if (Call.Kind != ReplayKind.MobState) continue;
                var Parameters = DeserializeParameters(Call.ParameterPayload);
                if (Parameters is not { Length: 1 } || Parameters[0] is not int State)
                    throw new InvalidOperationException(
                        "A local generated Mob replay fact is malformed.");
                Result = Result.WithMobState(Call.ViewId, Call.SenderActor, State);
            }
            return Result;
        }

        static bool SameLocalFact(CatalogCall Left, CatalogCall Right) =>
            Left.ViewId == Right.ViewId
            && Left.Kind == Right.Kind
            && string.Equals(Left.Method, Right.Method, StringComparison.Ordinal)
            && Left.Recipient == Right.Recipient
            && (Left.Recipient is RecipientKind.All or RecipientKind.AllViaServer
                || Left.SenderActor == Right.SenderActor)
            && Left.TargetActor == Right.TargetActor
            && Left.ParameterPayload.AsSpan().SequenceEqual(Right.ParameterPayload);

        internal BatchCatalog WithMobState(int ViewId, int SenderActor, int State)
        {
            var Payload = SerializePhotonValue(new object[] { State });
            for (var i = 0; i < Calls.Length; i++)
            {
                var Call = Calls[i];
                if (Call.ViewId != ViewId || Call.Kind != ReplayKind.MobState) continue;
                if (Call.SenderActor == SenderActor
                    && Call.ParameterPayload.AsSpan().SequenceEqual(Payload)) return this;
                var UpdatedCalls = (CatalogCall[])Calls.Clone();
                UpdatedCalls[i] = new CatalogCall(
                    ViewId, MobStateMethod, RecipientKind.Others,
                    SenderActor, 0, ReplayKind.MobState, Payload);
                return new BatchCatalog(
                    UpdatedCalls, InitializationActor, HasBeehiveInitialization,
                    BeehiveEffectOwnerActor);
            }
            if (Calls.Length >= MaximumDeferredRpcs)
                throw new InvalidOperationException(
                    $"Generated PhotonView RPC count exceeds {MaximumDeferredRpcs}.");
            var ExpandedCalls = new CatalogCall[Calls.Length + 1];
            Array.Copy(Calls, ExpandedCalls, Calls.Length);
            ExpandedCalls[Calls.Length] = new CatalogCall(
                ViewId, MobStateMethod, RecipientKind.Others,
                SenderActor, 0, ReplayKind.MobState, Payload);
            return new BatchCatalog(
                ExpandedCalls, InitializationActor, HasBeehiveInitialization,
                BeehiveEffectOwnerActor);
        }

        internal void ReplayActor(MapRun Run, Room Room, int Actor, ISet<int> RetiredViewIds)
        {
            if (!Room.Players.TryGetValue(Actor, out var Target)) return;
            var Local = Actor == PhotonNetwork.LocalPlayer.ActorNumber;
            Replaying = true;
            try
            {
                foreach (var Call in Calls)
                    if (Call.IncludesActor(Actor))
                    {
                        Call.Replay(Target, RetiredViewIds);
                        if (Local) RequireCurrentRun(Run);
                    }
            }
            finally
            {
                Replaying = false;
            }
        }

        internal static bool TryParsePropertyCalls(
            object? Value, byte[] Fingerprint, int InitializationActor,
            bool HasBeehiveInitialization, int BeehiveEffectOwnerActor,
            out BatchCatalog Catalog)
        {
            Catalog = null!;
            if (Fingerprint is not { Length: 32 }
                || Value is not object[] Calls || Calls.Length > MaximumDeferredRpcs) return false;
            var Parsed = new CatalogCall[Calls.Length];
            for (var i = 0; i < Calls.Length; i++)
            {
                if (Calls[i] is not object[] { Length: 6 } Fields
                    || Fields[0] is not int ViewId || ViewId <= 0 || ViewId >= PhotonNetwork.MAX_VIEW_IDS
                    || Fields[1] is not string Method
                    || Fields[2] is not byte RecipientValue
                    || Fields[3] is not int SenderActor || SenderActor <= 0
                    || Fields[4] is not int TargetActor || TargetActor != 0
                    || Fields[5] is not byte[] ParameterPayload
                    || ParameterPayload.Length > MaximumPropertyBytes) return false;
                ReplayKind Kind;
                try
                {
                    if (!TryParseReplay(Method, DeserializeParameters(ParameterPayload), out Kind)) return false;
                }
                catch
                {
                    return false;
                }
                var Recipient = (RecipientKind)RecipientValue;
                var ExpectedRecipient = Kind == ReplayKind.MobState
                    ? RecipientKind.Others : RecipientKind.All;
                if (Recipient != ExpectedRecipient) return false;
                Parsed[i] = new CatalogCall(
                    ViewId, Method, Recipient, SenderActor, TargetActor, Kind,
                    (byte[])ParameterPayload.Clone());
            }
            if (!HasUniqueReplayFacts(Parsed)) return false;
            var Candidate = new BatchCatalog(
                Parsed, InitializationActor, HasBeehiveInitialization,
                BeehiveEffectOwnerActor);
            if (!DeterministicObjectIdentity.SameHash(Candidate.Fingerprint, Fingerprint)) return false;
            Catalog = Candidate;
            return true;
        }
        static bool HasUniqueReplayFacts(CatalogCall[] Calls)
        {
            var Seen = new HashSet<(int ViewId, ReplayKind Kind)>();
            foreach (var Call in Calls)
                if (!Seen.Add((Call.ViewId, Call.Kind))) return false;
            return true;
        }
    }

    internal const int MaximumPropertyBytes = short.MaxValue;
    internal const int MaximumRetainedCatalogBytes = 1 << 18;
    internal const int MaximumReplayActors = byte.MaxValue;
    const int MaximumDeferredRpcs = 256;
    const int CatalogEnvelopeReserveBytes = 1024;
    const string InstanceIdMethod = nameof(Beehive.SetInstanceIDRPC);
    const string BridgeCapacityMethod = nameof(BreakableBridge.SyncHoldsPeopleRPC);
    const string MobStateMethod = "RPC_SyncMobState";
    static readonly IProtocol Photon16 = new Protocol16();
    static readonly IProtocol Photon18 = new Protocol18();
    static NativeFeatureGeneration.FeatureRoots PendingRoots = NativeFeatureGeneration.FeatureRoots.Empty;
    static readonly List<DeferredRpc> DeferredRpcs = new();
    static readonly Dictionary<MonoBehaviour, GeneratedReplayApplication> ReplayApplications =
        new(ReferenceComparer<MonoBehaviour>.Instance);
    static readonly HashSet<PhotonView> RegisteredPendingViews = new(ReferenceComparer<PhotonView>.Instance);
    static RootReservation? ActiveRoots;
    static int DeferredCatalogBytes;
    static bool Replaying;
    static bool RunningMasterInitialization;

    internal static IDisposable ReserveRoots(
        NativeFeatureGeneration.FeatureRoots Roots, Func<bool> IsCancellationRequested)
    {
        if (ActiveRoots is not null)
            throw new InvalidOperationException("Generated RPC roots were reserved more than once.");
        var Reservation = new RootReservation(Roots, IsCancellationRequested);
        ActiveRoots = Reservation;
        AddPendingRoots(Roots);
        return Reservation;
    }

    internal static void ReserveRoots(MapRun Run, IReadOnlyList<GameObject> Roots)
    {
        RequireCurrentRun(Run);
        var Additions = new List<Transform>(Roots.Count);
        foreach (var RootObject in Roots)
            if (RootObject) Additions.Add(RootObject.transform);
        AddPendingRoots(Additions);
    }

    internal static void HoldRegisteredBatch(MapRun Run, int BatchIndex, PhotonView[] Views)
        => HoldRegisteredBatch(Run, GeneratedBatchKey.Slot(BatchIndex), Views);

    internal static void HoldRegisteredBatch(MapRun Run, GeneratedBatchKey BatchIndex, PhotonView[] Views)
    {
        RequireCurrentRun(Run);
        foreach (var View in Views)
        {
            if (!View || View.ViewID <= 0
                || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
                throw new InvalidOperationException(
                    "A registered generated PhotonView was unavailable before RPC readiness.");
            RegisteredPendingViews.Add(View);
            if (View.TryGetComponent<Beehive>(out var Beehive))
                RegisterReplayApplication(BatchIndex, View, Beehive);
            if (View.TryGetComponent<TrackableNetworkObject>(out var Trackable))
                RegisterReplayApplication(BatchIndex, View, Trackable);
        }
    }

    internal static void ReleaseRegisteredBatch(PhotonView[] Views)
    {
        var ReleasedViews = new HashSet<PhotonView>(ReferenceComparer<PhotonView>.Instance);
        foreach (var View in Views)
        {
            if (View is null) continue;
            RegisteredPendingViews.Remove(View);
            ReleasedViews.Add(View);
        }
        if (ReleasedViews.Count == 0) return;
        List<MonoBehaviour>? Released = null;
        foreach (var Pair in ReplayApplications)
        {
            if (!ReleasedViews.Contains(Pair.Value.View)) continue;
            Released ??= new List<MonoBehaviour>();
            Released.Add(Pair.Key);
        }
        if (Released is null) return;
        foreach (var Target in Released) ReplayApplications.Remove(Target);
    }

    internal static void RunMasterInitialization(
        MapRun Run, PhotonView[] Views)
    {
        RequireCurrentRun(Run);
        RequireMasterAuthority();
        RunningMasterInitialization = true;
        try
        {
            foreach (var View in Views)
            {
                if (!View) continue;
                if (View.TryGetComponent<Beehive>(out var Beehive)
                    && !HasDeferredFact(Run, View, ReplayKind.Beehive))
                {
                    var SpawnBees = Beehive.spawnBees;
                    Beehive.spawnBees = false;
                    try { Beehive.Init(); }
                    finally { Beehive.spawnBees = SpawnBees; }
                    if (!HasDeferredFact(Run, View, ReplayKind.Beehive))
                    {
                        Beehive.OnInstanceDataSet();
                        View.RPC(
                            InstanceIdMethod, RpcTarget.All,
                            new object[] { Beehive.instanceID, Beehive.currentMaxInstanceID });
                    }
                }
                if (View.TryGetComponent<TrackableNetworkObject>(out var Trackable)
                    && !HasDeferredFact(Run, View, ReplayKind.Trackable))
                    Trackable.Init();
            }
        }
        finally
        {
            RunningMasterInitialization = false;
        }
    }

    static bool HasDeferredFact(MapRun Run, PhotonView View, ReplayKind Kind)
    {
        foreach (var Call in DeferredRpcs)
            if (ReferenceEquals(Call.Run, Run)
                && ReferenceEquals(Call.View, View) && Call.Kind == Kind) return true;
        return false;
    }

    internal static bool CompletePeerInitialization(
        MapRun Run, int[] ViewIds, BatchCatalog Catalog)
    {
        RequireCurrentRun(Run);
        foreach (var ViewId in ViewIds)
        {
            var View = PhotonNetwork.GetPhotonView(ViewId);
            if (!View) continue;
            if (View.TryGetComponent<Beehive>(out var Beehive))
            {
                if (Catalog.HasFact(ViewId, ReplayKind.Beehive)
                    && !HasAppliedReplay(Beehive)) return false;
                if (TryTakeBeehiveProjected(Beehive)) Beehive.OnInstanceDataSet();
            }
            if (!View.TryGetComponent<TrackableNetworkObject>(out var Trackable)) continue;
            if (Catalog.HasFact(ViewId, ReplayKind.Trackable))
            {
                if (!HasAppliedReplay(Trackable)) return false;
            }
            else Trackable.OnInstanceDataSet();
            if (TryTakeTrackableCreated(Trackable))
            {
                TrackableNetworkObject.OnTrackableObjectCreated?.Invoke(Trackable.instanceID);
                RequireCurrentRun(Run);
            }
        }
        return true;
    }
    internal static bool CompleteMasterInitialization(
        MapRun Run, int[] ViewIds, BatchCatalog Catalog)
    {
        RequireCurrentRun(Run);
        RequireMasterAuthority();
        foreach (var ViewId in ViewIds)
        {
            var View = PhotonNetwork.GetPhotonView(ViewId);
            if (!View) continue;
            if (Catalog.HasFact(ViewId, ReplayKind.Beehive)
                && !HasAppliedReplay(View.GetComponent<Beehive>())) return false;
        }
        RunningMasterInitialization = true;
        try
        {
            foreach (var ViewId in ViewIds)
            {
                var View = PhotonNetwork.GetPhotonView(ViewId);
                if (!View || !View.TryGetComponent<Beehive>(out var Beehive)
                    || !TryTakeBeehiveCompleted(Beehive)) continue;
                Beehive.initialized = false;
                Beehive.Init();
            }
        }
        finally
        {
            RunningMasterInitialization = false;
        }
        return true;
    }

    static void RequireMasterAuthority()
    {
        if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom is not { } CurrentRoom
            || PhotonNetwork.LocalPlayer.ActorNumber != CurrentRoom.MasterClientId)
            throw new InvalidOperationException(
                "Generated master initialization requires current Photon authority.");
    }

    internal static BatchCatalog SnapshotBatch(
        MapRun Run, IReadOnlyList<GameObject> Roots, PhotonView[] Views,
        int InitializationActor, bool HasBeehiveInitialization,
        int BeehiveEffectOwnerActor)
    {
        RequireCurrentRun(Run);
        var CompletedRoots = BatchRoots(Roots);
        var Registered = BatchViews(Views);
        var CatalogCalls = new List<CatalogCall>();
        foreach (var Call in DeferredRpcs)
        {
            if (!ReferenceEquals(Call.Run, Run)
                || Call.Root && CompletedRoots.DeepestRoot(Call.Root, out _) is null) continue;
            RequireReplayable(Call, Registered);
            CatalogCalls.Add(Call.ToCatalogCall());
        }
        return BatchCatalog.Create(
            CatalogCalls.ToArray(), InitializationActor,
            HasBeehiveInitialization, BeehiveEffectOwnerActor);
    }

    internal static BatchCatalog MaximumBatchCatalog(PhotonView[] Views)
    {
        var Calls = new List<CatalogCall>();
        var Beehive = false;
        var ReplayView = 0;
        for (var i = 0; i < Views.Length; i++)
        {
            var Id = 0;
            foreach (var Kind in ReplayKinds)
            {
                if (!MatchesReplayComponent(Views[i], Kind)) continue;
                if (Id == 0) Id = PhotonNetwork.MAX_VIEW_IDS - MaximumDeferredRpcs + ReplayView++;
                Beehive |= Kind == ReplayKind.Beehive;
                var Method = Kind is ReplayKind.Beehive or ReplayKind.Trackable ? InstanceIdMethod
                    : Kind == ReplayKind.BreakableBridge ? BridgeCapacityMethod : MobStateMethod;
                var Parameters = Kind == ReplayKind.Beehive ? new object[] { int.MaxValue, int.MaxValue }
                    : Kind == ReplayKind.Trackable ? new object[] { int.MaxValue, int.MaxValue, true }
                    : new object[] { int.MaxValue };
                Calls.Add(new CatalogCall(Id, Method,
                    Kind == ReplayKind.MobState ? RecipientKind.Others : RecipientKind.All,
                    int.MaxValue, 0, Kind, SerializePhotonValue(Parameters)));
            }
        }
        if (Calls.Count > MaximumDeferredRpcs)
            throw new InvalidOperationException("Generated output exceeds the supported replay fact capacity.");
        return BatchCatalog.Create(Calls.ToArray(), int.MaxValue, Beehive, int.MaxValue * (Beehive ? 1 : 0));
    }

    internal static BatchCatalog SealBatch(
        MapRun Run, IReadOnlyList<GameObject> Roots, PhotonView[] Views)
    {
        RequireCurrentRun(Run);
        var (Registered, Calls) = ReleaseBatch(Run, Roots, Views);
        var CatalogCalls = new CatalogCall[Calls.Count];
        for (var i = 0; i < Calls.Count; i++)
        {
            var Call = Calls[i];
            RequireReplayable(Call, Registered);
            CatalogCalls[i] = Call.ToCatalogCall();
        }
        return BatchCatalog.Create(CatalogCalls);
    }

    internal static void CompleteBatch(
        MapRun Run, IReadOnlyList<GameObject> Roots, PhotonView[] Views)
    {
        RequireCurrentRun(Run);
        var (Registered, Calls) = ReleaseBatch(Run, Roots, Views);
        Replaying = true;
        try
        {
            foreach (var Call in Calls)
            {
                RequireReplayable(Call, Registered);
                Call.ReplayOriginal();
            }
        }
        finally
        {
            Replaying = false;
        }
    }

    internal static BatchCatalog InitializeLocalBatch(
        MapRun Run, IReadOnlyList<GameObject> Roots, PhotonView[] Views)
    {
        RunMasterInitialization(Run, Views);
        var Beehive = false; var Ids = new int[Views.Length];
        for (var i = 0; i < Views.Length; i++)
        {
            Beehive |= Views[i].GetComponent<Beehive>(); Ids[i] = Views[i].ViewID;
        }
        var Catalog = SnapshotBatch(Run, Roots, Views, PhotonNetwork.LocalPlayer.ActorNumber, Beehive, 0);
        CompleteBatch(Run, Roots, Views);
        if (!CompletePeerInitialization(Run, Ids, Catalog) || !CompleteMasterInitialization(Run, Ids, Catalog))
            throw new InvalidOperationException("Local generated replay initialization did not complete synchronously.");
        return Catalog;
    }

    internal static void DiscardBatch(
        MapRun Run, IReadOnlyList<GameObject> Roots, PhotonView[] Views) =>
        _ = ReleaseBatch(Run, Roots, Views);

    internal static void Reset()
    {
        ActiveRoots?.Cancel();
        Replaying = false;
        RunningMasterInitialization = false;
        PendingRoots = NativeFeatureGeneration.FeatureRoots.Empty;
        DeferredRpcs.Clear();
        ReplayApplications.Clear();
        RegisteredPendingViews.Clear();
        DeferredCatalogBytes = 0;
    }

    static void RegisterReplayApplication(
        GeneratedBatchKey BatchIndex, PhotonView View, ItemComponent Target)
    {
        var Trackable = Target as TrackableNetworkObject;
        var TargetName = Trackable is null ? nameof(Beehive) : nameof(TrackableNetworkObject);
        var Registered = ReplayApplications.TryGetValue(Target, out var Existing);
        if (Registered && (Existing.BatchIndex != BatchIndex
            || !ReferenceEquals(Existing.View, View)))
            throw new InvalidOperationException(
                $"Generated {TargetName} on PhotonView {View.ViewID} belongs to object batch {Existing.BatchIndex}, not batch {BatchIndex}.");
        if (!ReferenceEquals(Target.gameObject, View.gameObject))
            throw new InvalidOperationException(
                $"Generated {TargetName} on PhotonView {View.ViewID} in object batch {BatchIndex} belongs to a different PhotonView object.");
        var Item = Target.GetComponent<Item>();
        if (!Item)
            throw new InvalidOperationException(
                $"Generated {TargetName} on PhotonView {View.ViewID} in object batch {BatchIndex} has no Item component.");
        if (Target.item && !ReferenceEquals(Target.item, Item))
            throw new InvalidOperationException(
                $"Generated {TargetName} on PhotonView {View.ViewID} in object batch {BatchIndex} belongs to a different Item component.");
        if (Trackable && Trackable.photonView
            && !ReferenceEquals(Trackable.photonView, View))
            throw new InvalidOperationException(
                $"Generated {TargetName} on PhotonView {View.ViewID} in object batch {BatchIndex} belongs to a different PhotonView.");
        Target.item = Item;
        if (Trackable) Trackable.photonView = View;
        if (!Registered)
            ReplayApplications.Add(Target, new GeneratedReplayApplication(BatchIndex, View));
    }

    internal static void ReleaseReplayBatch(int BatchIndex)
        => ReleaseReplayBatch(GeneratedBatchKey.Slot(BatchIndex));

    internal static void ReleaseReplayBatch(GeneratedBatchKey BatchIndex)
    {
        List<MonoBehaviour>? Released = null;
        foreach (var Pair in ReplayApplications)
        {
            if (Pair.Value.BatchIndex != BatchIndex) continue;
            Released ??= new List<MonoBehaviour>();
            Released.Add(Pair.Key);
        }
        if (Released is null) return;
        foreach (var Target in Released) ReplayApplications.Remove(Target);
    }

    static bool IsRepeatedReplay(
        MonoBehaviour Target, int InstanceId, int MaximumInstanceId, bool SpawnTracker) =>
        ReplayApplications.TryGetValue(Target, out var Application)
        && Application.IsRepeated(InstanceId, MaximumInstanceId, SpawnTracker);

    static bool HasAppliedReplay(MonoBehaviour? Target) =>
        Target && ReplayApplications.TryGetValue(Target, out var Application)
        && Application.ReplayApplied;

    static bool TryTakeBeehiveProjected(Beehive Target) =>
        ReplayApplications.TryGetValue(Target, out var Application)
        && Application.TryTakeBeehiveProjected();

    static bool TryTakeBeehiveCompleted(Beehive Target) =>
        ReplayApplications.TryGetValue(Target, out var Application)
        && Application.TryTakeBeehiveCompleted();

    static bool TryTakeTrackableCreated(TrackableNetworkObject Target) =>
        ReplayApplications.TryGetValue(Target, out var Application)
        && Application.TryTakeTrackableCreated();

    static void AddPendingRoots(IReadOnlyList<Transform> Roots)
    {
        var Combined = new List<Transform>(PendingRoots.Count + Roots.Count);
        foreach (var Root in PendingRoots)
            if (Root) Combined.Add(Root);
        foreach (var Root in Roots)
            if (Root && !Combined.Contains(Root)) Combined.Add(Root);
        PendingRoots = Combined.Count == 0
            ? NativeFeatureGeneration.FeatureRoots.Empty
            : new NativeFeatureGeneration.FeatureRoots(Combined);
    }

    static NativeFeatureGeneration.FeatureRoots BatchRoots(IReadOnlyList<GameObject> Roots)
    {
        var Values = new List<Transform>(Roots.Count);
        foreach (var RootObject in Roots)
        {
            if (!RootObject) continue;
            var Root = RootObject.transform;
            if (!Values.Contains(Root)) Values.Add(Root);
        }
        return Values.Count == 0
            ? NativeFeatureGeneration.FeatureRoots.Empty
            : new NativeFeatureGeneration.FeatureRoots(Values);
    }

    static HashSet<PhotonView> BatchViews(PhotonView[] Views)
    {
        var Registered = new HashSet<PhotonView>(ReferenceComparer<PhotonView>.Instance);
        foreach (var View in Views)
            if (View) Registered.Add(View);
        return Registered;
    }

    static (HashSet<PhotonView> Registered, List<DeferredRpc> Calls) ReleaseBatch(
        MapRun Run, IReadOnlyList<GameObject> Roots, PhotonView[] Views)
    {
        var CompletedRoots = BatchRoots(Roots);
        var Registered = BatchViews(Views);
        foreach (var View in Registered) RegisteredPendingViews.Remove(View);
        var Calls = new List<DeferredRpc>();
        for (var i = DeferredRpcs.Count - 1; i >= 0; i--)
        {
            var Call = DeferredRpcs[i];
            if (!ReferenceEquals(Call.Run, Run)
                || Call.Root && CompletedRoots.DeepestRoot(Call.Root, out _) is null) continue;
            DeferredRpcs.RemoveAt(i);
            DeferredCatalogBytes -= Call.CatalogBytes;
            if (DeferredCatalogBytes < 0)
                throw new InvalidOperationException("Generated RPC catalog byte ownership became negative.");
            Calls.Add(Call);
        }
        Calls.Reverse();
        var RetainedRoots = new List<Transform>(PendingRoots.Count);
        foreach (var Root in PendingRoots)
            if (Root && CompletedRoots.DeepestRoot(Root, out _) is null)
                RetainedRoots.Add(Root);
        PendingRoots = RetainedRoots.Count == 0
            ? NativeFeatureGeneration.FeatureRoots.Empty
            : new NativeFeatureGeneration.FeatureRoots(RetainedRoots);
        return (Registered, Calls);
    }

    static void RequireReplayable(DeferredRpc Call, HashSet<PhotonView> Registered)
    {
        if (!Call.View || !Call.Root
            || !ReferenceEquals(Call.Room, PhotonNetwork.CurrentRoom)
            || !Call.Run.Network.OwnsCallback(
                Call.RunIdentity, Call.GenerationEpoch, Call.LayoutRevision))
            throw new InvalidOperationException(
                $"Generated PhotonView RPC '{Call.Method}' lost its owning map run.");
        if (!Registered.Contains(Call.View)
            || Call.View.ViewID <= 0
            || !ReferenceEquals(PhotonNetwork.GetPhotonView(Call.View.ViewID), Call.View))
            throw new InvalidOperationException(
                $"Generated PhotonView RPC '{Call.Method}' was not registered by its synchronized batch.");
    }

    static Transform? PendingRoot(Transform Target)
    {
        var Owner = PendingRoots.DeepestRoot(Target, out _);
        if (Owner) return Owner;
        return ActiveRoots?.Roots.DeepestRoot(Target, out _);
    }

    internal static bool HasPendingRoot(Transform Target) => PendingRoot(Target) is not null;

    static bool Defer(
        PhotonView View, string Method, RpcTarget Target,
        PhotonPlayer? Player, object[] Parameters)
    {
        if (Replaying) return false;
        var RegisteredPending = RegisteredPendingViews.Contains(View);
        if (!RegisteredPending && (View.ViewID != 0 || !PhotonCloneIdentity.IsBatchOrigin(View))
            || PendingRoot(View.transform) is not { } Root)
            return TryRefreshRetainedMobState(View, Method, Player, Parameters);
        if (ActiveRoots?.Cancelled == true) return true;
        if (!Map.TryGetPublishedRun(out var Run)) return true;
        try
        {
            var Room = PhotonNetwork.CurrentRoom;
            var SenderActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
            var Recipient = ResolveRecipient(Target, Player, Room, SenderActor, out var TargetActor);
            var Policy = RequireReplayPolicy(
                View, Method, Recipient, SenderActor, TargetActor, Parameters, Room);
            if (Policy.Disposition == ReplayDisposition.Suppress) return true;
            var ReplacedIndex = -1;
            if (Policy.Kind == ReplayKind.MobState)
                for (var i = DeferredRpcs.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(DeferredRpcs[i].View, View)
                        && DeferredRpcs[i].Kind == ReplayKind.MobState)
                    {
                        ReplacedIndex = i;
                        break;
                    }
            if (ReplacedIndex < 0 && DeferredRpcs.Count >= MaximumDeferredRpcs)
                throw new InvalidOperationException(
                    $"Generated PhotonView RPC count exceeds {MaximumDeferredRpcs}.");
            var ParameterPayload = SerializePhotonValue(Parameters);
            var CatalogBytes = SerializePhotonValue(new object[]
            {
                0, Method, (byte)Recipient, SenderActor, TargetActor, ParameterPayload
            }).Length;
            var ReplacedBytes = ReplacedIndex < 0 ? 0 : DeferredRpcs[ReplacedIndex].CatalogBytes;
            var RetainedBytes = DeferredCatalogBytes - ReplacedBytes;
            if (CatalogBytes > MaximumPropertyBytes - CatalogEnvelopeReserveBytes
                || RetainedBytes > MaximumPropertyBytes - CatalogEnvelopeReserveBytes - CatalogBytes)
                throw new InvalidOperationException(
                    $"Generated PhotonView RPC catalog exceeds {MaximumPropertyBytes} bytes.");
            var Call = new DeferredRpc(
                View, Root, Method, Recipient, SenderActor, TargetActor,
                Policy.Kind, ParameterPayload, CatalogBytes, Room, Run);
            if (ReplacedIndex >= 0)
            {
                DeferredRpcs.RemoveAt(ReplacedIndex);
                DeferredCatalogBytes = RetainedBytes;
            }
            DeferredRpcs.Add(Call);
            DeferredCatalogBytes += CatalogBytes;
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Generated PhotonView RPC capture failed", Exception);
        }
        return true;
    }

    static bool TryRefreshRetainedMobState(
        PhotonView View, string Method, PhotonPlayer? Player, object[] Parameters)
    {
        if (Player is null || Method != MobStateMethod
            || Parameters is not { Length: 1 } || Parameters[0] is not int State
            || View.ViewID <= 0 || View.ViewID >= PhotonNetwork.MAX_VIEW_IDS
            || PhotonNetwork.CurrentRoom is not { IsOffline: false } CurrentRoom
            || PhotonNetwork.NetworkClientState != ClientState.Joined
            || PhotonNetwork.LocalPlayer is not { } Sender
            || Sender.ActorNumber <= 0 || Sender.ActorNumber != CurrentRoom.MasterClientId
            || Player.ActorNumber <= 0 || Player.ActorNumber == Sender.ActorNumber
            || !CurrentRoom.Players.TryGetValue(Player.ActorNumber, out var CurrentTarget)
            || !ReferenceEquals(CurrentTarget, Player)
            || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View)
            || !View.TryGetComponent<Mob>(out _)
            || !Map.TryGetPublishedRun(out var Run)) return false;
        try
        {
            return Run.Network.ViewBatches.TryRefreshRetainedMobState(
                View, Sender.ActorNumber, Player.ActorNumber, State);
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Map.Index, "Generated Mob state replay refresh failed", Exception);
            return true;
        }
    }

    static RecipientKind ResolveRecipient(
        RpcTarget Target, PhotonPlayer? Player, Room? Room, int SenderActor, out int TargetActor)
    {
        if (Player is not null)
        {
            TargetActor = Player.ActorNumber;
            if (TargetActor <= 0)
                throw new InvalidOperationException("Generated PhotonView RPC targets an invalid player actor.");
            return RecipientKind.Player;
        }
        TargetActor = 0;
        switch (Target)
        {
            case RpcTarget.All:
                return RecipientKind.All;
            case RpcTarget.Others:
                if (SenderActor <= 0)
                    throw new InvalidOperationException("Generated PhotonView Others RPC has no sender actor.");
                return RecipientKind.Others;
            case RpcTarget.MasterClient:
                TargetActor = Room?.MasterClientId ?? 0;
                if (TargetActor <= 0)
                    throw new InvalidOperationException("Generated PhotonView master RPC has no master actor.");
                return RecipientKind.MasterClient;
            case RpcTarget.AllViaServer:
                return RecipientKind.AllViaServer;
            default:
                throw new InvalidOperationException(
                    $"Generated PhotonView RPC target {Target} is buffered or unsupported.");
        }
    }

    static (ReplayKind Kind, ReplayDisposition Disposition) RequireReplayPolicy(
        PhotonView View, string Method, RecipientKind Recipient,
        int SenderActor, int TargetActor, object[] Parameters, Room? Room)
    {
        if (Room is not null && SenderActor > 0
            && TryParseReplay(Method, Parameters, out var Kind)
            && MatchesReplayComponent(View, Kind))
        {
            if (Kind == ReplayKind.MobState && SenderActor == Room.MasterClientId)
            {
                if (Recipient == RecipientKind.Others && TargetActor == 0)
                    return (Kind, ReplayDisposition.Catalog);
                if (Recipient == RecipientKind.Player && TargetActor > 0
                    && TargetActor != SenderActor)
                    return (Kind, ReplayDisposition.Suppress);
            }
            else if (Kind == ReplayKind.BreakableBridge)
            {
                if (Recipient == RecipientKind.All && TargetActor == 0)
                    return (Kind, ReplayDisposition.Catalog);
                if (Recipient == RecipientKind.Player && TargetActor > 0
                    && SenderActor != Room.MasterClientId && TargetActor != SenderActor)
                    return (Kind, ReplayDisposition.Suppress);
            }
            else if (Recipient == RecipientKind.All
                && SenderActor == Room.MasterClientId && TargetActor == 0)
                return (Kind, ReplayDisposition.Catalog);
        }
        throw new InvalidOperationException(
            $"Generated PhotonView RPC '{Method}' is not a supported replay call.");
    }

    static bool TryParseReplay(
        string Method, object[] Parameters, out ReplayKind Kind)
    {
        if (Method == InstanceIdMethod)
        {
            if (Parameters is { Length: 2 } && Parameters[0] is int && Parameters[1] is int)
            {
                Kind = ReplayKind.Beehive;
                return true;
            }
            if (Parameters is { Length: 3 }
                && Parameters[0] is int && Parameters[1] is int && Parameters[2] is bool)
            {
                Kind = ReplayKind.Trackable;
                return true;
            }
        }
        else if (Method == BridgeCapacityMethod
            && Parameters is { Length: 1 } && Parameters[0] is int)
        {
            Kind = ReplayKind.BreakableBridge;
            return true;
        }
        else if (Method == MobStateMethod
            && Parameters is { Length: 1 } && Parameters[0] is int)
        {
            Kind = ReplayKind.MobState;
            return true;
        }
        Kind = default;
        return false;
    }

    static bool MatchesReplayComponent(PhotonView View, ReplayKind Kind) => Kind switch
    {
        ReplayKind.Beehive => View.TryGetComponent<Beehive>(out _),
        ReplayKind.Trackable => View.TryGetComponent<TrackableNetworkObject>(out _),
        ReplayKind.BreakableBridge => View.TryGetComponent<BreakableBridge>(out _),
        ReplayKind.MobState => View.TryGetComponent<Mob>(out _),
        _ => throw new InvalidOperationException("A generated replay kind is unsupported.")
    };

    internal static byte[] SerializePhotonValue(object Value) =>
        PhotonNetwork.NetworkingClient.SerializationProtocol == SerializationProtocol.GpBinaryV16
            ? Photon16.Serialize(Value)
            : Photon18.Serialize(Value);

    internal static object DeserializePhotonValue(byte[] Value) =>
        PhotonNetwork.NetworkingClient.SerializationProtocol == SerializationProtocol.GpBinaryV16
            ? Photon16.Deserialize(Value)
            : Photon18.Deserialize(Value);

    static object[] DeserializeParameters(byte[] Payload) =>
        DeserializePhotonValue(Payload) as object[]
        ?? throw new InvalidOperationException("Generated PhotonView RPC parameters are malformed.");

    static void RequireCurrentRun(MapRun Run)
    {
        Run.RequirePublished();
        if (!Map.TryGetPublishedRun(out var Current) || !ReferenceEquals(Current, Run))
            throw MapRun.FeatureWriter.Cancelled();
    }

    [HarmonyPatch(typeof(FakeItemSticky), nameof(FakeItemSticky.OnCollide))]
    static class FakeItemStickyCollisionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FakeItemSticky __instance) =>
            !HasPendingRoot(__instance.transform);
    }

    static bool InitializationAllowed(PhotonView View) =>
        RunningMasterInitialization || !HasPendingRoot(View.transform)
            && (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode
                || ViewBatchSynchronization.GeneratedBatchOriginReady(View));

    static bool TrackableInitializationAllowed(TrackableNetworkObject Target) =>
        RunningMasterInitialization || !HasPendingRoot(Target.transform)
            && (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode
                || !ReplayApplications.ContainsKey(Target)
                    && ViewBatchSynchronization.GeneratedBatchOriginReady(Target.photonView));

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.Init))]
    static class BeehiveInitializationPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Beehive __instance) =>
            InitializationAllowed(__instance.photonView);
    }

    [HarmonyPatch(typeof(TrackableNetworkObject), nameof(TrackableNetworkObject.TryInitIfMaster))]
    static class TrackableInitializationPatch
    {
        [HarmonyPrefix]
        static bool Prefix(TrackableNetworkObject __instance) =>
            TrackableInitializationAllowed(__instance);
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.SetInstanceIDRPC))]
    static class BeehiveInstanceIdPatch
    {
        [HarmonyPrefix]
        static void Prefix(Beehive __instance, int instanceID, ref int maxInstanceID)
        {
            _ = IsRepeatedReplay(__instance, instanceID, maxInstanceID, false);
            if (HasAppliedReplay(__instance))
                maxInstanceID = Math.Max(maxInstanceID, Beehive.currentMaxInstanceID);
        }
    }

    [HarmonyPatch(typeof(TrackableNetworkObject), nameof(TrackableNetworkObject.SetInstanceIDRPC))]
    static class TrackableInstanceIdPatch
    {
        [HarmonyPrefix]
        static void Prefix(
            TrackableNetworkObject __instance, int instanceID, ref int maxInstanceID,
            ref bool autoSpawnTracker)
        {
            var Repeated = IsRepeatedReplay(__instance, instanceID, maxInstanceID, autoSpawnTracker);
            if (HasAppliedReplay(__instance))
                maxInstanceID = Math.Max(maxInstanceID, TrackableNetworkObject.currentMaxInstanceID);
            if (Repeated) autoSpawnTracker = false;
        }

        [HarmonyPostfix]
        static void Postfix(TrackableNetworkObject __instance)
        {
            if (TryTakeTrackableCreated(__instance))
                TrackableNetworkObject.OnTrackableObjectCreated?.Invoke(__instance.instanceID);
        }
    }

    [HarmonyPatch(typeof(PhotonView), nameof(PhotonView.RPC),
        new Type[] { typeof(string), typeof(RpcTarget), typeof(object[]) })]
    static class GroupRpcPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PhotonView __instance, string __0, RpcTarget __1, object[] __2) =>
            !Defer(__instance, __0, __1, null, __2);
    }

    [HarmonyPatch(typeof(PhotonView), nameof(PhotonView.RPC),
        new Type[] { typeof(string), typeof(PhotonPlayer), typeof(object[]) })]
    static class PlayerRpcPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PhotonView __instance, string __0, PhotonPlayer __1, object[] __2) =>
            !Defer(__instance, __0, default, __1, __2);
    }
}
