using System;
using System.Collections;
using System.Globalization;
using Photon.Pun;
using Photon.Realtime;

namespace PLL.Library;

internal sealed class InitialGenerationBarrier
{
    readonly MapRun Run;
    byte[] GeneratedNetworkContractHash = Array.Empty<byte>();
    Exception? GenerationFailure;
    object[]? SealedCohortValue;
    string[] GenerationReadyKeys = Array.Empty<string>();

    internal InitialGenerationBarrier(MapRun Run) => this.Run = Run ?? throw new ArgumentNullException(nameof(Run));

    internal bool GenerationRunning { get; private set; }
    internal void BeginGeneration()
    {
        if (GenerationRunning)
            throw new InvalidOperationException("Initial map generation is already running.");
        var Cohort = Run.InitialGenerationCohort;
        if (Cohort is null)
        {
            SealedCohortValue = null;
            GenerationReadyKeys = Array.Empty<string>();
        }
        else
        {
            SealedCohortValue = InitialGenerationCohortBarrier.Encode(Cohort);
            GenerationReadyKeys = new string[Cohort.Count];
            for (var i = 0; i < GenerationReadyKeys.Length; i++)
                GenerationReadyKeys[i] = GenerationReadyKey(Cohort.ActorAt(i));
        }
        GenerationFailure = null;
        GenerationRunning = true;
    }

    internal void CompleteGeneration(Exception? Failure)
    {
        GenerationFailure = Failure;
        GenerationRunning = false;
    }

    internal Exception? TakeGenerationFailure()
    {
        var Failure = GenerationFailure;
        GenerationFailure = null;
        return Failure;
    }

    internal void RecordGeneratedNetworkContractHash(byte[] Hash)
    {
        if (Hash is not { Length: 32 })
            throw new ArgumentException("The initial generated network contract hash must contain 32 bytes.", nameof(Hash));
        GeneratedNetworkContractHash = (byte[])Hash.Clone();
    }

    internal void ClearGeneratedNetworkContractHash() => GeneratedNetworkContractHash = Array.Empty<byte>();

    internal void ConnectionStateChanged(ClientState State)
    {
        if (State == ClientState.Joined || Run.InitialGenerationCohort is null) return;
        if (Run.Lifecycle == MapRun.Phase.Active && !Run.FeatureMutationActive) return;
        LazySlotGenerator.AbortLazyGeneration(
            Run.Active ? Map.Index : 0, "Map generation lost its Photon room");
    }

    internal void ReleaseState()
    {
        GeneratedNetworkContractHash = Array.Empty<byte>();
        GenerationFailure = null;
        SealedCohortValue = null;
        GenerationReadyKeys = Array.Empty<string>();
        GenerationRunning = false;
    }

    static string GenerationReadyKey(int Actor) => Map.GenerationReadyPrefix
        + Actor.ToString(CultureInfo.InvariantCulture);

    bool AllActorsReady(MapRun.GenerationCohort Cohort, Room Room)
    {
        for (var i = 0; i < Cohort.Count; i++)
        {
            var Actor = Cohort.ActorAt(i);
            if (!Room.Players.ContainsKey(Actor)) continue;
            if (!MatchingReady(Room.CustomProperties[GenerationReadyKeys[i]], Actor)) return false;
        }
        return true;
    }

    bool MatchingReady(object? Value, int Actor)
    {
        if (Value is null || Value is object[] { Length: 0 }) return false;
        if (Value is object[] { Length: > 0 } Stale
            && Stale[0] is string StaleEpoch && StaleEpoch != Run.GenerationEpoch) return false;
        if (Value is not object[] { Length: 3 } Data
            || Data[0] is not string Epoch || Data[1] is not int Revision
            || Data[2] is not byte[] Candidate || Candidate.Length != 32)
            throw new InvalidOperationException(
                $"Client {Actor} published malformed initial generation readiness.");
        if (Epoch != Run.GenerationEpoch) return false;
        if (Revision != Run.LayoutRevision)
            throw new InvalidOperationException(
                $"Client {Actor} published a different initial generation revision.");
        if (!DeterministicObjectIdentity.SameHash(Candidate, GeneratedNetworkContractHash))
            throw new InvalidOperationException(
                $"Client {Actor} produced a different initial generated network contract hash.");
        return true;
    }

    bool MatchingCertificate(MapRun.GenerationCohort Cohort, Room Room, object? Value)
    {
        if (Value is null || Value is object[] { Length: 0 }) return false;
        if (Value is object[] { Length: > 0 } Stale
            && Stale[0] is string StaleEpoch && StaleEpoch != Run.GenerationEpoch) return false;
        if (Value is not object[] { Length: 3 } Data
            || Data[0] is not string Epoch || Data[1] is not int Revision
            || Data[2] is not object[] { Length: 2 } Certificate
            || Certificate[0] is not byte[] Hash || Hash.Length != 32
            || Certificate[1] is not int[] Actors)
            throw new InvalidOperationException("Initial generation certificate is malformed.");
        if (Epoch != Run.GenerationEpoch) return false;
        if (Revision != Run.LayoutRevision)
            throw new InvalidOperationException(
                "Initial generation certificate has a different layout revision.");
        if (!DeterministicObjectIdentity.SameHash(Hash, GeneratedNetworkContractHash))
            throw new InvalidOperationException(
                "Initial generation certificate hash differs from the local generated network contract.");
        if (!Cohort.Matches(Actors))
            throw new InvalidOperationException(
                "Initial generation certificate has a different actor cohort.");
        return AllActorsReady(Cohort, Room);
    }

    void Validate(
        MapRun.GenerationCohort Cohort,
        object[] ExpectedCohort,
        Room Room)
    {
        Run.RequirePublished();
        if (Run.Features.Aborted)
            throw new InvalidOperationException("Initial generated-object synchronization was aborted.");
        if (!ReferenceEquals(Room, PhotonNetwork.CurrentRoom)
            || PhotonNetwork.NetworkClientState != ClientState.Joined)
            throw new InvalidOperationException("Initial generation synchronization lost its Photon room.");
        InitialGenerationCohortBarrier.RequireCurrent(Cohort, ExpectedCohort, Room);
    }

    internal IEnumerator Wait()
    {
        Run.RequirePublished();
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false } Room) yield break;
        if (GeneratedNetworkContractHash.Length != 32)
            throw new InvalidOperationException("The initial generated network contract hash is unavailable.");

        var Cohort = Run.InitialGenerationCohort
            ?? throw new InvalidOperationException("The initial generation cohort is unavailable.");
        var CohortValue = SealedCohortValue
            ?? throw new InvalidOperationException("The sealed initial generation cohort is unavailable.");
        var Actor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (Actor <= 0 || !Room.Players.ContainsKey(Actor))
            throw new InvalidOperationException("The local initial generation actor is unavailable.");
        string? ReadyKey = null;
        for (var i = 0; i < Cohort.Count; i++)
            if (Cohort.ActorAt(i) == Actor)
            {
                ReadyKey = GenerationReadyKeys[i];
                break;
            }
        object[]? ExpectedCertificate = null;
        while (true)
        {
            Validate(Cohort, CohortValue, Room);
            if (MatchingCertificate(Cohort, Room, Room.CustomProperties[Map.GenerationGoKey]))
                yield break;

            if (ReadyKey is not null)
            {
                var Ready = MatchingReady(Room.CustomProperties[ReadyKey], Actor);
                if (!Ready && !Run.Network.RunProperties.IsPending(ReadyKey)
                    && !Run.Network.RunProperties.Publish(ReadyKey, GeneratedNetworkContractHash))
                    throw new InvalidOperationException("Initial generation readiness could not be published.");
                if (!Ready || Run.Network.RunProperties.IsPending(ReadyKey))
                {
                    yield return null;
                    continue;
                }
            }

            if (PhotonNetwork.IsMasterClient && AllActorsReady(Cohort, Room)
                && !Run.Network.RunProperties.IsPending(Map.GenerationGoKey))
            {
                ExpectedCertificate ??= new object[]
                {
                    (byte[])GeneratedNetworkContractHash.Clone(),
                    Cohort.SnapshotActors()
                };
                if (!Run.Network.RunProperties.MatchesRoomValue(Map.GenerationGoKey, ExpectedCertificate)
                    && !Run.Network.RunProperties.Publish(Map.GenerationGoKey, ExpectedCertificate))
                    throw new InvalidOperationException("Initial generation release could not be published.");
            }
            yield return null;
        }
    }
}
