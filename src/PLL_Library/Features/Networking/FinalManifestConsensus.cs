using System;
using System.Globalization;
using Photon.Pun;
using Photon.Realtime;
using IEnumerator = System.Collections.IEnumerator;

namespace PLL.Library;

internal sealed class FinalManifestConfirmation
{
    readonly int[] Actors;

    internal FinalManifestConfirmation(
        Room Room,
        ResolvedRunConfiguration Configuration,
        string Revision,
        int LocalActor,
        int[] Actors)
    {
        this.Room = Room;
        this.Configuration = Configuration;
        this.Revision = Revision;
        this.LocalActor = LocalActor;
        this.Actors = (int[])Actors.Clone();
    }

    internal Room Room { get; }
    internal ResolvedRunConfiguration Configuration { get; }
    internal string Revision { get; }
    internal int LocalActor { get; }
    internal int[] SnapshotActors() => (int[])Actors.Clone();
}

internal static class FinalManifestConsensus
{
    const string Protocol = "1";
    const string ReadyPrefix = PLLLibrary.PluginGuid + "/state/final-ready/";
    const string CommitKey = PLLLibrary.PluginGuid + "/state/final-commit";
    static Round? CurrentRound;

    abstract class Round
    {
        protected Round(ResolvedRunConfiguration Configuration, string Revision)
        {
            this.Configuration = Configuration;
            this.Revision = Revision;
        }

        internal ResolvedRunConfiguration Configuration { get; }
        internal string Revision { get; }
        internal string Epoch => Configuration.Protocol.GenerationEpoch;
    }

    sealed class OfflineRound : Round
    {
        internal OfflineRound(
            Room? Room,
            ResolvedRunConfiguration Configuration,
            string Revision) : base(Configuration, Revision) => this.Room = Room;

        internal Room? Room { get; }
    }

    sealed class OnlineRound : Round
    {
        internal OnlineRound(
            Room Room,
            ResolvedRunConfiguration Configuration,
            string Revision,
            int LocalActor,
            int MasterActor,
            int[] Actors) : base(Configuration, Revision)
        {
            this.Room = Room;
            this.LocalActor = LocalActor;
            LocalReadyKey = ReadyKey(LocalActor);
            this.Actors = Array.Empty<int>();
            ActorReadyKeys = Array.Empty<string>();
            ReplaceActors(Actors);
            ReplaceMaster(MasterActor);
            ReadyValue = CreateReadyValue(Epoch, Revision);
        }

        internal Room Room { get; }
        internal int LocalActor { get; }
        internal string LocalReadyKey { get; }
        internal object[] ReadyValue { get; }
        internal RoomPropertyPublication ReadyPublication { get; } = new();
        internal RoomPropertyPublication CommitPublication { get; } = new();
        internal int MasterActor { get; private set; }
        internal string? MasterReadyKey { get; private set; }
        internal int[] Actors { get; private set; }
        internal string[] ActorReadyKeys { get; private set; }
        internal object[]? DesiredCommit { get; set; }

        internal void ReplaceActors(int[] Actors)
        {
            this.Actors = Actors;
            ActorReadyKeys = new string[Actors.Length];
            for (var i = 0; i < Actors.Length; i++)
                ActorReadyKeys[i] = Actors[i] == LocalActor ? LocalReadyKey : ReadyKey(Actors[i]);
            RefreshMasterReadyKey();
            ResetCommit();
        }

        internal void ReplaceMaster(int Actor)
        {
            MasterActor = Actor;
            RefreshMasterReadyKey();
            ResetCommit();
        }

        internal void ResetCommit()
        {
            DesiredCommit = null;
            CommitPublication.Reset();
        }

        void RefreshMasterReadyKey()
        {
            MasterReadyKey = null;
            for (var i = 0; i < Actors.Length; i++)
            {
                if (Actors[i] != MasterActor) continue;
                MasterReadyKey = ActorReadyKeys[i];
                return;
            }
        }
    }

    internal static void Reset() => CurrentRound = null;

    internal static void ConfirmOffline(
        Room? Room,
        ResolvedRunConfiguration Configuration,
        string Revision) => CurrentRound = new OfflineRound(Room, Configuration, Revision);

    internal static IEnumerator Wait(ResolvedRunConfiguration Configuration)
    {
        var Room = PhotonNetwork.CurrentRoom;
        var Revision = SegmentRegistry.ManifestRevision;
        if (Room is null || Room.IsOffline)
        {
            ConfirmOffline(Room, Configuration, Revision);
            yield break;
        }
        if (!Room.BroadcastPropertiesChangeToAll)
            throw PhotonRoomRejection.Disconnect(
                "PLL Library cannot verify the final custom-content manifest without server property echoes.");
        if (PhotonNetwork.NetworkClientState != ClientState.Joined)
            throw PhotonRoomRejection.Disconnect(
                "PLL Library lost its Photon room before final custom-content validation.");
        var Master = PhotonNetwork.MasterClient;
        if (Master is null || Master.ActorNumber <= 0 || !Room.Players.ContainsKey(Master.ActorNumber))
            throw PhotonRoomRejection.Disconnect(
                "PLL Library could not identify the authoritative player for final custom-content validation.");
        var LocalActor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (LocalActor <= 0 || !Room.Players.ContainsKey(LocalActor))
            throw PhotonRoomRejection.Disconnect(
                "PLL Library could not identify the local player for final custom-content validation.");
        if (!TryCurrentActors(Room, out var Actors))
            throw PhotonRoomRejection.Disconnect(
                "PLL Library found an invalid player set during final custom-content validation.");

        var Consensus = CurrentRound is OnlineRound Existing
            && ReferenceEquals(Existing.Room, Room) && Existing.LocalActor == LocalActor
            && SameIdentity(Existing, Configuration, Revision)
                ? Existing
                : new OnlineRound(
                    Room, Configuration, Revision, LocalActor, Master.ActorNumber, Actors);
        CurrentRound = Consensus;
        while (true)
        {
            if (!IsCurrent(Consensus))
            {
                if (ReferenceEquals(CurrentRound, Consensus)) CurrentRound = null;
                throw PhotonRoomRejection.Disconnect(
                    "PLL Library lost its Photon room before final custom-content validation.");
            }
            if (!Consensus.Room.BroadcastPropertiesChangeToAll)
                throw PhotonRoomRejection.Disconnect(
                    "PLL Library cannot verify the final custom-content manifest without server property echoes.");
            if (TryMaintainAndConfirm(Consensus)) yield break;
            yield return null;
        }
    }

    internal static void RequireConfirmed(ResolvedRunConfiguration Configuration)
    {
        var Room = PhotonNetwork.CurrentRoom;
        var Revision = SegmentRegistry.ManifestRevision;
        if (Room is null || Room.IsOffline)
        {
            if (CurrentRound is OfflineRound Offline
                && ReferenceEquals(Offline.Room, Room)
                && SameIdentity(Offline, Configuration, Revision)) return;
            CurrentRound = null;
            throw new InvalidOperationException(
                "PLL Library's final custom-content manifest is not confirmed.");
        }
        if (CurrentRound is not OnlineRound Consensus
            || !ReferenceEquals(Consensus.Room, Room)
            || !SameIdentity(Consensus, Configuration, Revision)
            || !IsCurrent(Consensus))
        {
            CurrentRound = null;
            throw PhotonRoomRejection.Disconnect(
                "PLL Library's final custom-content manifest is not confirmed.");
        }
        if (!TryMaintainAndConfirm(Consensus))
            throw PhotonRoomRejection.Disconnect(
                "PLL Library's final custom-content manifest confirmation is no longer valid.");
    }

    internal static FinalManifestConfirmation? CaptureConfirmation(
        ResolvedRunConfiguration Configuration)
    {
        RequireConfirmed(Configuration);
        if (CurrentRound is not OnlineRound Consensus) return null;
        return new FinalManifestConfirmation(
            Consensus.Room,
            Configuration,
            Consensus.Revision,
            Consensus.LocalActor,
            Consensus.Actors);
    }

    internal static bool Owns(
        ResolvedRunConfiguration Configuration,
        Room Room) => CurrentRound is OnlineRound Consensus
        && ReferenceEquals(Consensus.Room, Room)
        && SameIdentity(Consensus, Configuration, SegmentRegistry.ManifestRevision)
        && IsCurrent(Consensus);

    internal static bool TryMaintainConfirmation(ResolvedRunConfiguration Configuration)
    {
        if (CurrentRound is not OnlineRound Consensus
            || !ReferenceEquals(Consensus.Configuration, Configuration)
            || !IsCurrent(Consensus))
            throw PhotonRoomRejection.Disconnect(
                "PLL Library's final custom-content manifest is not confirmed for initial generation.");
        return TryMaintainAndConfirm(Consensus);
    }

    internal static void ConnectionStateChanged(ClientState Current)
    {
        if (Current is ClientState.Disconnecting or ClientState.Disconnected) CurrentRound = null;
        else if (Current == ClientState.Joined) MaintainCurrent();
    }

    static string ReadyKey(int Actor) => ReadyPrefix + Actor.ToString(CultureInfo.InvariantCulture);

    static object[] CreateReadyValue(string Epoch, string Revision) =>
        new object[] { Protocol, Epoch, Revision };

    static object[] CreateCommitValue(
        string Epoch,
        string Revision,
        int MasterActor,
        int[] Actors) => new object[] { Protocol, Epoch, Revision, MasterActor, Actors };

    static bool SameIdentity(
        Round Consensus,
        ResolvedRunConfiguration Configuration,
        string Revision) => ReferenceEquals(Consensus.Configuration, Configuration)
        && string.Equals(Consensus.Revision, Revision, StringComparison.Ordinal);

    static bool IsCurrent(OnlineRound Consensus) => ReferenceEquals(CurrentRound, Consensus)
        && ReferenceEquals(PhotonNetwork.CurrentRoom, Consensus.Room)
        && PhotonNetwork.NetworkClientState == ClientState.Joined
        && string.Equals(
            Consensus.Revision, SegmentRegistry.ManifestRevision, StringComparison.Ordinal);

    static bool TryCurrentActors(Room Room, out int[] Actors)
    {
        Actors = new int[Room.Players.Count];
        var Index = 0;
        foreach (var Actor in Room.Players.Keys)
        {
            if (Actor <= 0 || Index == Actors.Length) return false;
            Actors[Index++] = Actor;
        }
        if (Index != Actors.Length || Index == 0) return false;
        Array.Sort(Actors);
        return true;
    }

    static bool CurrentActorsMatch(OnlineRound Consensus)
    {
        if (Consensus.Actors.Length != Consensus.Room.Players.Count) return false;
        foreach (var Actor in Consensus.Actors)
            if (!Consensus.Room.Players.ContainsKey(Actor)) return false;
        return true;
    }

    static bool RefreshTopology(OnlineRound Consensus)
    {
        if (!CurrentActorsMatch(Consensus))
        {
            if (!TryCurrentActors(Consensus.Room, out var Actors)) return false;
            Consensus.ReplaceActors(Actors);
        }
        var MasterActor = PhotonNetwork.MasterClient?.ActorNumber ?? 0;
        if (MasterActor <= 0 || !Consensus.Room.Players.ContainsKey(MasterActor))
        {
            if (Consensus.MasterActor != 0) Consensus.ReplaceMaster(0);
            return false;
        }
        if (Consensus.MasterActor != MasterActor) Consensus.ReplaceMaster(MasterActor);
        return Consensus.Room.Players.ContainsKey(Consensus.LocalActor);
    }

    static bool TryMaintainAndConfirm(OnlineRound Consensus)
    {
        var TopologyAvailable = MaintainLocal(Consensus, out var LocalReady);
        if (PublicationFailure(Consensus) is { } Failure)
            throw PhotonRoomRejection.Disconnect(Failure);
        if (!TopologyAvailable || !LocalReady) return false;
        if (Consensus.MasterActor == Consensus.LocalActor)
        {
            var AllReady = MaintainMaster(Consensus, true, out var CommitEchoed);
            if (PublicationFailure(Consensus) is { } CommitFailure)
                throw PhotonRoomRejection.Disconnect(CommitFailure);
            return AllReady && (CommitEchoed
                || CommitMatches(Consensus.Room.CustomProperties[CommitKey], Consensus));
        }
        if (Consensus.MasterReadyKey is null
            || !ReadyMatches(
                Consensus.Room.CustomProperties[Consensus.MasterReadyKey],
                Consensus,
                Consensus.MasterActor,
                true)) return false;
        if (!CommitMatches(Consensus.Room.CustomProperties[CommitKey], Consensus)) return false;
        return CurrentReadySetMatches(Consensus, true, Consensus.MasterActor);
    }

    static bool MaintainLocal(OnlineRound Consensus, out bool LocalReady)
    {
        LocalReady = false;
        if (PublicationFailure(Consensus) is not null
            || !IsCurrent(Consensus) || !RefreshTopology(Consensus)) return false;
        LocalReady = Consensus.ReadyPublication.AwaitEcho(
            Consensus.Room,
            Consensus.LocalReadyKey,
            Consensus.ReadyValue,
            "PLL Library could not publish its final custom-content manifest.");
        return true;
    }

    static bool MaintainMaster(
        OnlineRound Consensus, bool ValidateIdentity, out bool CommitEchoed)
    {
        var AllReady = CurrentReadySetMatches(Consensus, ValidateIdentity, 0);
        CommitEchoed = MaintainCommit(Consensus, AllReady);
        return AllReady;
    }

    static bool MaintainCommit(OnlineRound Consensus, bool AllReady)
    {
        if (!AllReady)
        {
            Consensus.ResetCommit();
            return false;
        }
        Consensus.DesiredCommit ??= CreateCommitValue(
            Consensus.Epoch, Consensus.Revision, Consensus.MasterActor, Consensus.Actors);
        return Consensus.CommitPublication.AwaitEcho(
            Consensus.Room,
            CommitKey,
            Consensus.DesiredCommit,
            "PLL Library could not publish the final custom-content manifest release.");
    }

    static string? PublicationFailure(OnlineRound Consensus) =>
        Consensus.ReadyPublication.Failure ?? Consensus.CommitPublication.Failure;

    internal static void MaintainCurrent()
    {
        var Room = PhotonNetwork.CurrentRoom;
        if (CurrentRound is OfflineRound Offline)
        {
            if (!ReferenceEquals(Offline.Room, Room) || Room is { IsOffline: false }
                || !string.Equals(
                    Offline.Revision, SegmentRegistry.ManifestRevision, StringComparison.Ordinal))
                CurrentRound = null;
            return;
        }
        if (CurrentRound is not OnlineRound Consensus) return;
        if (!IsCurrent(Consensus))
        {
            if (ReferenceEquals(CurrentRound, Consensus)) CurrentRound = null;
            return;
        }
        var TopologyAvailable = MaintainLocal(Consensus, out var LocalReady);
        if (!TopologyAvailable || !LocalReady || PublicationFailure(Consensus) is not null
            || Consensus.MasterActor != Consensus.LocalActor) return;
        MaintainMaster(Consensus, false, out _);
    }

    static bool CurrentReadySetMatches(
        OnlineRound Consensus, bool ValidateIdentity, int ExcludedActor)
    {
        for (var i = 0; i < Consensus.Actors.Length; i++)
        {
            var Actor = Consensus.Actors[i];
            if (Actor == Consensus.LocalActor || Actor == ExcludedActor) continue;
            if (!ReadyMatches(
                Consensus.Room.CustomProperties[Consensus.ActorReadyKeys[i]],
                Consensus,
                Actor,
                ValidateIdentity)) return false;
        }
        return true;
    }

    static bool ReadyMatches(
        object? Value,
        OnlineRound Consensus,
        int Actor,
        bool ValidateIdentity)
    {
        if (Value is not object[] Fields || Fields.Length < 2
            || Fields[1] is not string CandidateEpoch
            || !string.Equals(CandidateEpoch, Consensus.Epoch, StringComparison.Ordinal)) return false;
        if (Fields.Length != 3 || Fields[0] is not string CandidateProtocol
            || Fields[2] is not string CandidateRevision)
        {
            if (ValidateIdentity)
                throw PhotonRoomRejection.Disconnect(
                    $"Player {Actor} published a malformed final custom-content manifest.");
            return false;
        }
        if (!string.Equals(CandidateProtocol, Protocol, StringComparison.Ordinal))
        {
            if (ValidateIdentity)
                throw PhotonRoomRejection.Disconnect(
                    $"Player {Actor} uses an incompatible final custom-content protocol.");
            return false;
        }
        if (string.Equals(CandidateRevision, Consensus.Revision, StringComparison.Ordinal))
            return true;
        if (ValidateIdentity)
            throw PhotonRoomRejection.Disconnect(
                $"Player {Actor} has different surviving PLL content.");
        return false;
    }

    static bool CommitMatches(object? Value, OnlineRound Consensus)
    {
        if (Value is not object[] Fields || Fields.Length < 2
            || Fields[1] is not string CandidateEpoch
            || !string.Equals(CandidateEpoch, Consensus.Epoch, StringComparison.Ordinal)) return false;
        if (Fields.Length != 5 || Fields[0] is not string CandidateProtocol
            || Fields[2] is not string CandidateRevision || Fields[3] is not int CandidateMaster
            || Fields[4] is not int[] Actors)
            throw PhotonRoomRejection.Disconnect(
                "The host published a malformed final custom-content manifest release.");
        if (CandidateMaster != Consensus.MasterActor) return false;
        if (!string.Equals(CandidateProtocol, Protocol, StringComparison.Ordinal))
            throw PhotonRoomRejection.Disconnect(
                "The host uses an incompatible final custom-content protocol.");
        if (!string.Equals(CandidateRevision, Consensus.Revision, StringComparison.Ordinal))
            throw PhotonRoomRejection.Disconnect("The host has different surviving PLL content.");
        if (Actors.Length != Consensus.Actors.Length) return false;
        for (var i = 0; i < Actors.Length; i++)
            if (Actors[i] != Consensus.Actors[i]) return false;
        return true;
    }
}
