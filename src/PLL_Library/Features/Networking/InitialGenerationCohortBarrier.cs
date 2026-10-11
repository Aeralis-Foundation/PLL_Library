using System;
using System.Globalization;
using Photon.Pun;
using Photon.Realtime;
using IEnumerator = System.Collections.IEnumerator;

namespace PLL.Library;

internal static class InitialGenerationCohortBarrier
{
    enum PropertyState { Absent, Valid, Invalid }

    const string Protocol = "1";
    const string CohortKey = PLLLibrary.PluginGuid + "/state/generation/cohort";
    const string ReadyPrefix = PLLLibrary.PluginGuid + "/state/generation/cohort-ready/";

    internal static IEnumerator Wait(
        ResolvedRunConfiguration Configuration,
        Action<MapRun.GenerationCohort?> Capture)
    {
        if (Configuration is null) throw new ArgumentNullException(nameof(Configuration));
        if (Capture is null) throw new ArgumentNullException(nameof(Capture));
        var Confirmation = FinalManifestConsensus.CaptureConfirmation(Configuration);
        if (Confirmation is null)
        {
            Capture(null);
            yield break;
        }
        var Room = Confirmation.Room;
        var CohortPublication = new RoomPropertyPublication();
        var ReadyPublication = new RoomPropertyPublication();
        object[]? ProposedValue = null;
        MapRun.GenerationCohort? Cohort = null;
        object[]? CohortValue = null;
        string[]? ReadyKeys = null;
        string? LocalReadyKey = null;
        object? CachedProperty = null;
        MapRun.GenerationCohort CachedCohort = null!;
        string? CachedFailure = null;
        var CachedState = PropertyState.Absent;
        var PropertyCached = false;
        while (true)
        {
            if (!ReferenceEquals(PhotonNetwork.CurrentRoom, Room)
                || PhotonNetwork.NetworkClientState != ClientState.Joined
                || !FinalManifestConsensus.Owns(Configuration, Room))
                throw PhotonRoomRejection.Disconnect(
                    "The Photon room changed before initial map generation was confirmed.");
            if (!Room.BroadcastPropertiesChangeToAll)
                throw PhotonRoomRejection.Disconnect(
                    "The server does not confirm room settings. Initial map generation cannot start.");
            var FinalManifestConfirmed = FinalManifestConsensus.TryMaintainConfirmation(Configuration);
            var Property = Room.CustomProperties[CohortKey];
            PropertyState State;
            MapRun.GenerationCohort CandidateCohort;
            string? Failure;
            if (PropertyCached && ReferenceEquals(Property, CachedProperty))
            {
                State = CachedState;
                CandidateCohort = CachedCohort;
                Failure = CachedFailure;
            }
            else
            {
                if (CohortValue is not null
                    && PhotonPropertyProtocol.SameValue(Property, CohortValue))
                {
                    State = PropertyState.Valid;
                    CandidateCohort = Cohort!;
                    Failure = null;
                }
                else
                    State = Read(
                        Property,
                        Configuration.Protocol.GenerationEpoch,
                        Confirmation.Revision,
                        out CandidateCohort,
                        out Failure);
                CachedProperty = Property;
                CachedState = State;
                CachedCohort = CandidateCohort;
                CachedFailure = Failure;
                PropertyCached = true;
            }
            if (State == PropertyState.Invalid) throw PhotonRoomRejection.Disconnect(Failure!);
            if (State == PropertyState.Absent)
            {
                if (Cohort is not null)
                    throw PhotonRoomRejection.Disconnect(
                        "The initial generation cohort disappeared before release.");
                if (PhotonNetwork.IsMasterClient)
                {
                    if (ProposedValue is null)
                    {
                        if (!FinalManifestConfirmed)
                        {
                            yield return null;
                            continue;
                        }
                        var PublicationConfirmation =
                            FinalManifestConsensus.CaptureConfirmation(Configuration)!;
                        ProposedValue = Encode(new MapRun.GenerationCohort(
                            Configuration.Protocol.GenerationEpoch,
                            PublicationConfirmation.Revision,
                            PublicationConfirmation.SnapshotActors()));
                    }
                    CohortPublication.AwaitEcho(
                        Room,
                        CohortKey,
                        ProposedValue,
                        "The initial generation player list could not be sent.");
                    if (CohortPublication.Failure is { } CohortPublicationFailure)
                        throw PhotonRoomRejection.Disconnect(CohortPublicationFailure);
                }
                yield return null;
                continue;
            }

            if (Cohort is null)
            {
                Cohort = CandidateCohort;
                CohortValue = Encode(Cohort);
                ReadyKeys = new string[Cohort.Count];
                for (var i = 0; i < ReadyKeys.Length; i++)
                {
                    var Actor = Cohort.ActorAt(i);
                    var Key = ReadyKey(Actor);
                    ReadyKeys[i] = Key;
                    if (Actor == Confirmation.LocalActor) LocalReadyKey = Key;
                }
            }
            else if (!Cohort.Matches(CandidateCohort))
                throw PhotonRoomRejection.Disconnect(
                    "The initial generation cohort changed before release.");

            if (LocalReadyKey is null)
            {
                if (!FinalManifestConfirmed)
                {
                    yield return null;
                    continue;
                }
                Capture(Cohort);
                yield break;
            }
            var Ready = ReadyPublication.AwaitEcho(
                Room,
                LocalReadyKey,
                CohortValue!,
                "The initial generation player list could not be confirmed.");
            if (ReadyPublication.Failure is { } ReadyPublicationFailure)
                throw PhotonRoomRejection.Disconnect(ReadyPublicationFailure);
            if (Ready && FinalManifestConfirmed
                && AllActorsReady(Room, Cohort, CohortValue!, ReadyKeys!))
            {
                Capture(Cohort);
                yield break;
            }
            yield return null;
        }
    }

    internal static void RequireCurrent(
        MapRun.GenerationCohort Cohort,
        object[] Expected,
        Room Room)
    {
        if (!ReferenceEquals(PhotonNetwork.CurrentRoom, Room))
            throw new InvalidOperationException(
                "Initial generation synchronization lost its Photon room.");
        var Value = Room.CustomProperties[CohortKey];
        if (PhotonPropertyProtocol.SameValue(Value, Expected)) return;
        var State = Read(
            Value,
            Cohort.GenerationEpoch,
            Cohort.FinalManifestRevision,
            out var Candidate,
            out var Failure);
        if (State != PropertyState.Valid)
            throw new InvalidOperationException(
                Failure ?? "The initial generation cohort is unavailable.");
        if (!Cohort.Matches(Candidate))
            throw new InvalidOperationException(
                "The initial generation cohort changed after map preparation.");
    }

    static string ReadyKey(int Actor) =>
        ReadyPrefix + Actor.ToString(CultureInfo.InvariantCulture);

    internal static object[] Encode(MapRun.GenerationCohort Cohort) => new object[]
    {
        Protocol,
        Cohort.GenerationEpoch,
        Cohort.FinalManifestRevision,
        Cohort.SnapshotActors()
    };

    static PropertyState Read(
        object? Value,
        string Epoch,
        string Revision,
        out MapRun.GenerationCohort Cohort,
        out string? Failure)
    {
        Cohort = null!;
        Failure = null;
        if (Value is null || Value is object[] { Length: 0 }) return PropertyState.Absent;
        if (Value is object[] { Length: > 1 } Stale
            && Stale[1] is string StaleEpoch
            && !string.Equals(StaleEpoch, Epoch, StringComparison.Ordinal)) return PropertyState.Absent;
        if (Value is not object[] { Length: 4 } Fields
            || Fields[0] is not string CandidateProtocol
            || Fields[1] is not string CandidateEpoch
            || Fields[2] is not string CandidateRevision
            || Fields[3] is not int[] CandidateActors)
        {
            Failure = "The initial generation cohort property is malformed.";
            return PropertyState.Invalid;
        }
        if (!string.Equals(CandidateProtocol, Protocol, StringComparison.Ordinal))
        {
            Failure = "The initial generation cohort protocol is incompatible.";
            return PropertyState.Invalid;
        }
        if (!string.Equals(CandidateEpoch, Epoch, StringComparison.Ordinal))
            return PropertyState.Absent;
        if (!string.Equals(CandidateRevision, Revision, StringComparison.Ordinal))
        {
            Failure = "The initial generation cohort has a different final manifest revision.";
            return PropertyState.Invalid;
        }
        try
        {
            Cohort = new MapRun.GenerationCohort(
                CandidateEpoch, CandidateRevision, CandidateActors);
            return PropertyState.Valid;
        }
        catch (ArgumentException)
        {
            Failure = CandidateActors.Length == 0
                ? "The initial generation cohort has no actors."
                : "The initial generation cohort actor list is invalid.";
            return PropertyState.Invalid;
        }
    }

    static bool AllActorsReady(
        Room Room,
        MapRun.GenerationCohort Cohort,
        object[] Expected,
        string[] ReadyKeys)
    {
        for (var i = 0; i < Cohort.Count; i++)
        {
            var Actor = Cohort.ActorAt(i);
            if (!Room.Players.ContainsKey(Actor)) continue;
            var Value = Room.CustomProperties[ReadyKeys[i]];
            if (PhotonPropertyProtocol.SameValue(Value, Expected)) continue;
            var State = Read(
                Value,
                Cohort.GenerationEpoch,
                Cohort.FinalManifestRevision,
                out var Candidate,
                out _);
            if (State == PropertyState.Absent) return false;
            if (State == PropertyState.Invalid)
                throw PhotonRoomRejection.Disconnect(
                    $"Player {Actor} published an invalid initial generation cohort echo.");
            if (!Cohort.Matches(Candidate))
                throw PhotonRoomRejection.Disconnect(
                    $"Player {Actor} published a different initial generation cohort echo.");
        }
        return true;
    }
}
