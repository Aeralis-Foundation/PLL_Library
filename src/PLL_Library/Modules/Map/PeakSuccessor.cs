using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class PeakSuccessorRuntime
{
    readonly MapRun Owner;
    bool[] FixtureSlots = Array.Empty<bool>();
    bool[] HonorSlots = Array.Empty<bool>();
    PeakCompletionProjection? InitialProjection;
    PeakCompletionProjection? Projection;
    PeakCompletionProjection? DesiredProjection;
    bool ProjectionApplied;
    bool FixturesPublished;
    bool ApplyingProjection;
    bool AuthorityRecoveryRequested;
    bool PublishingDesired;

    internal PeakSuccessorRuntime(MapRun Owner)
    {
        this.Owner = Owner ?? throw new ArgumentNullException(nameof(Owner));
        Checkpoints = new PeakCheckpointRuntime(Owner);
        Bestow = new PeakBestowRuntime(Owner, Checkpoints);
        Campfire = new PeakCampfireRuntime(Owner, Checkpoints);
        Reward = new PeakStatueRewardRuntime(Owner);
    }

    internal PeakCheckpointRuntime Checkpoints { get; }
    internal PeakBestowRuntime Bestow { get; }
    internal PeakCampfireRuntime Campfire { get; }
    internal PeakStatueRewardRuntime Reward { get; }
    internal bool HasFixtures { get; private set; }
    internal bool ApplyingCompletion => ApplyingProjection;
    internal bool CampfireOutputsReady => FixturesPublished && ProjectionApplied && !ApplyingProjection;
    internal bool RewardOpening => ProjectionApplied && Projection?.RewardOpening == true;
    internal bool HasPendingCompletion => DesiredProjection is not null;
    internal bool StableCampfireCompletionPending
    {
        get
        {
            if (!PhotonNetwork.IsMasterClient) return false;
            var Index = Map.Index;
            if (!TryGetStableCampfire(Index, out var Checkpoint)) return false;
            return Checkpoint.HasSuccessor
                ? Owner.Transitions.RequestedDirectAdvance != Index
                : Owner.Configuration.Terminal.Winnable
                    && !Owner.Transitions.RunFinishRequested && !Owner.Transitions.RunFinished;
        }
    }

    internal bool CanApplyDirectAdvance(int Index) =>
        TryGetStableCampfire(Index, out var Checkpoint) && Checkpoint.HasSuccessor;

    internal bool CheckpointMutationOpen(int Index) =>
        Owner.Active && AllowsCheckpointMutation(
            Index, Map.Index, Owner.Transitions.RequestedDirectAdvance,
            Owner.Transitions.Lease.Current == MapTransitionCoordinator.TransitionLease.State.Idle);

    internal static bool AllowsCheckpointMutation(
        int Index,
        int CurrentIndex,
        int RequestedDirectAdvance,
        bool TransitionIdle) =>
        TransitionIdle && Index == CurrentIndex && RequestedDirectAdvance != Index;

    internal bool CompletionReady(int Index) =>
        !IsFixture(Index) || PhotonNetwork.CurrentRoom is not { IsOffline: false } || ProjectionApplied;

    internal bool CampfireLit(int Index) =>
        IsFixture(Index) && ProjectionApplied && Projection!.CampfireLit(Index);

    internal bool HonorRequired(int Index) =>
        Index >= 0 && Index < HonorSlots.Length && HonorSlots[Index];

    internal bool HonorComplete(int Index) =>
        IsFixture(Index) && ProjectionApplied && Projection!.HonorComplete(Index);

    internal byte CampfireOutputs(int Index) =>
        IsFixture(Index) && ProjectionApplied
            ? (DesiredProjection ?? Projection!).OutputState(Index)
            : (byte)0;

    internal bool RewardAvailable(int Index) =>
        IsFixture(Index) && ProjectionApplied
        && (DesiredProjection ?? Projection!).RewardState(Index) == PeakRewardState.Available;

    internal bool RewardSpent(int Index) =>
        IsFixture(Index) && ProjectionApplied
        && Projection!.RewardState(Index) == PeakRewardState.Spent;
    internal void Configure(
        PreparedMap Prepared,
        NativeCampfireCheckpoint CampfireCheckpoint,
        NativePeakCampfireSource? CampfireSource)
    {
        if (!Checkpoints.Unconfigured || !Bestow.Unconfigured || !Campfire.Unconfigured || !Reward.Unconfigured
            || FixtureSlots.Length != 0 || HonorSlots.Length != 0 || InitialProjection is not null)
            throw new InvalidOperationException("A map run configured its Peak successor more than once.");
        Checkpoints.Configure(Prepared, CampfireCheckpoint);
        Bestow.Configure(Prepared);
        Campfire.Configure(Prepared, CampfireSource);
        Reward.Configure(Prepared, CampfireSource);
        FixtureSlots = new bool[Prepared.Slots.Length];
        HonorSlots = new bool[Prepared.Slots.Length];
        for (var i = 0; i < FixtureSlots.Length; i++)
        {
            FixtureSlots[i] = Prepared.Slots[i].BridgeTarget is not null;
            HonorSlots[i] = Checkpoints.HonorRequired(i);
            if (HonorSlots[i] && !FixtureSlots[i]
                || Campfire.HasFixture(i) != FixtureSlots[i] || Reward.HasFixture(i) != FixtureSlots[i]
                || Checkpoints.HasCheckpoint(i) != FixtureSlots[i])
                throw new InvalidOperationException("A Peak bridge fixture set is not slot-aligned.");
            HasFixtures |= FixtureSlots[i];
        }
        var UsesLocalQuicksave = MapQuicksave.RestoringActivePllQuicksave()
            && (PhotonNetwork.CurrentRoom is not { IsOffline: false } || PhotonNetwork.IsMasterClient);
        PeakQuicksaveState? Saved = null;
        if (UsesLocalQuicksave && Map.Ingress.LoadedQuicksaveLayout is { } Layout)
            Saved = new PeakQuicksaveState(
                Layout.peakCheckpointStates, Layout.peakCheckpointOutputs);
        InitialProjection = PeakCompletionProjection.Initial(FixtureSlots, HonorSlots, Saved);
    }

    internal void Publish()
    {
        Campfire.Publish();
        Reward.Publish();
        Checkpoints.EnableBridgeStatues();
        FixturesPublished = true;
        if (!HasFixtures) return;
        var Initial = InitialProjection
            ?? throw new InvalidOperationException("Peak completion state was not configured.");
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false } Room)
        {
            AcceptCompletion(Initial);
            return;
        }
        if (OwnsCompletionProperty(Room.CustomProperties[Map.PeakCompletionKey])
            && !HandleCompletionProperty(Room.CustomProperties[Map.PeakCompletionKey]))
            throw new InvalidOperationException("The room contains malformed Peak completion state.");
        if (!ProjectionApplied && PhotonNetwork.IsMasterClient)
        {
            DesiredProjection = Initial;
            if (!PublishPendingCompletion())
                throw new InvalidOperationException("The initial Peak completion state could not be published.");
        }
    }

    internal bool OwnsCompletionProperty(object? Value) =>
        Value is object[] { Length: 3 } Envelope
        && Envelope[0] is string Epoch && Epoch == Owner.GenerationEpoch
        && Envelope[1] is int Revision && Revision == Owner.LayoutRevision;

    internal bool HandleCompletionProperty(object? Value)
    {
        if (!HasFixtures) return Value is null;
        if (!PhotonPropertyProtocol.TryReadRunValue<object[]>(
                Value, Owner.GenerationEpoch, Owner.LayoutRevision, out var Encoded)
            || !PeakCompletionProjection.TryRead(
                Encoded, FixtureSlots, HonorSlots, out var Received)) return false;
        if (PhotonNetwork.CurrentRoom is { IsOffline: false }
            && !ValidOnlineActors(Received)) return false;
        return AcceptCompletion(Received);
    }

    internal bool PublishCampfireCompletion(int Index, byte OutputState)
    {
        if (!IsFixture(Index) || ApplyingProjection || !ProjectionApplied
            || PhotonNetwork.CurrentRoom is { IsOffline: false } && !PhotonNetwork.IsMasterClient) return false;
        var Current = DesiredProjection ?? Projection!;
        if (Current.CampfireLit(Index)) return false;
        DesiredProjection = Current.WithCampfireLit(
            FixtureSlots, HonorSlots, Index, OutputState);
        return PublishPendingCompletion();
    }

    internal bool PublishHonorCompletion(int Index)
    {
        if (!HonorRequired(Index) || !ProjectionApplied
            || PhotonNetwork.CurrentRoom is { IsOffline: false } && !PhotonNetwork.IsMasterClient) return false;
        var Current = DesiredProjection ?? Projection!;
        if (Current.HonorComplete(Index)) return true;
        DesiredProjection = Current.WithHonorComplete(FixtureSlots, HonorSlots, Index);
        return PublishPendingCompletion();
    }

    internal bool PublishCampfireOutputs(int Index, byte OutputState)
    {
        if (!IsFixture(Index) || !ProjectionApplied
            || PhotonNetwork.CurrentRoom is { IsOffline: false } && !PhotonNetwork.IsMasterClient) return false;
        var Current = DesiredProjection ?? Projection!;
        DesiredProjection = Current.WithOutputs(
            FixtureSlots, HonorSlots, Index, OutputState);
        return PublishPendingCompletion();
    }

    internal bool PublishRewardOpening(int Index, int RewardActor)
    {
        if (!IsFixture(Index) || !ProjectionApplied || !CheckpointMutationOpen(Index)
            || PhotonNetwork.CurrentRoom is { IsOffline: false } && (!PhotonNetwork.IsMasterClient || RewardActor <= 0))
            return false;
        var Current = DesiredProjection ?? Projection!;
        if (Current.RewardOpening || Current.RewardState(Index) != PeakRewardState.Available) return false;
        DesiredProjection = Current.WithRewardOpening(
            FixtureSlots, HonorSlots, Index,
            Math.Max(0, RewardActor));
        return PublishPendingCompletion();
    }

    internal bool TryCompleteRewardOpening()
    {
        if (!PhotonNetwork.IsMasterClient || !ProjectionApplied || Projection is not { } Applied) return false;
        var OpeningIndex = Applied.RewardOpeningIndex;
        if (OpeningIndex < 0) return false;
        var AppliedState = Applied.RewardState(OpeningIndex);
        var Current = DesiredProjection ?? Applied;
        var CurrentState = Current.RewardState(OpeningIndex);
        if (AppliedState == PeakRewardState.Opening)
        {
            if (CurrentState != PeakRewardState.Opening) return PublishPendingCompletion();
            DesiredProjection = Current.WithRewardResolution(
                FixtureSlots, HonorSlots, OpeningIndex, Reward.SelectResolution());
            return PublishPendingCompletion();
        }
        if (CurrentState == PeakRewardState.Spent) return PublishPendingCompletion();
        if (!Reward.TryCompleteOpening(OpeningIndex, AppliedState)) return false;
        DesiredProjection = Current.WithRewardSpent(FixtureSlots, HonorSlots, OpeningIndex);
        return PublishPendingCompletion();
    }

    internal bool BeginAuthorityRecovery()
    {
        if (!AuthorityRecoveryRequested || !PhotonNetwork.IsMasterClient) return false;
        if (!HasFixtures)
        {
            ProjectionApplied = true;
            AuthorityRecoveryRequested = false;
            return true;
        }
        if (PhotonNetwork.CurrentRoom is not { IsOffline: false } Room)
        {
            if (Projection is null)
                AcceptCompletion(InitialProjection
                    ?? throw new InvalidOperationException("Peak completion state was not configured."));
            AuthorityRecoveryRequested = false;
            return true;
        }
        var RoomValue = Room.CustomProperties[Map.PeakCompletionKey];
        if (OwnsCompletionProperty(RoomValue))
        {
            if (!HandleCompletionProperty(RoomValue))
                throw new InvalidOperationException("The room contains malformed Peak completion state.");
            AuthorityRecoveryRequested = false;
            return true;
        }
        DesiredProjection = CurrentCompletion();
        if (!PublishPendingCompletion())
            throw new InvalidOperationException("Peak completion authority could not publish its recovered state.");
        if (DesiredProjection is not null) return false;
        AuthorityRecoveryRequested = false;
        return true;
    }

    internal void PrepareAuthoritySwitch()
    {
        AuthorityRecoveryRequested = PhotonNetwork.IsMasterClient;
        DesiredProjection = null;
        PublishingDesired = false;
        Owner.Network.RunProperties.Cancel(Map.PeakCompletionKey);
    }

    internal bool TakeAuthorityRecovery()
    {
        if (!AuthorityRecoveryRequested || !PhotonNetwork.IsMasterClient
            || HasFixtures && !ProjectionApplied) return false;
        AuthorityRecoveryRequested = false;
        return true;
    }

    internal PeakQuicksaveState CaptureQuicksaveState()
    {
        if (DesiredProjection is not null
            || Owner.Network.RunProperties.IsPending(Map.PeakCompletionKey))
            throw new InvalidOperationException("A quicksave cannot capture pending Peak completion state.");
        if (FixtureSlots.Length == 0) return PeakQuicksaveState.Empty(0);
        return (Projection ?? InitialProjection
            ?? throw new InvalidOperationException("Peak completion state was not configured."))
            .CapturePersistent(FixtureSlots, HonorSlots);
    }

    internal static bool ValidQuicksaveState(byte State) =>
        PeakCompletionProjection.ValidPersistentState(State);

    internal static bool ValidQuicksaveOutputs(byte Outputs) =>
        PeakCompletionProjection.ValidPersistentOutputs(Outputs);

    internal static bool QuicksaveCampfireLit(byte State) =>
        PeakCompletionProjection.PersistentCampfireLit(State);

    internal void RetireSlot(int Index)
    {
        Checkpoints.RetireSlot(Index);
        Campfire.RetireSlot(Index);
        Reward.RetireSlot(Index);
        Bestow.RetireSlot(Index);
    }
    internal void ReleaseState()
    {
        var Failures = new List<Exception>();
        try { Reward.ReleaseState(); }
        catch (Exception Exception) { Failures.Add(Exception); }
        try { Campfire.ReleaseState(); }
        catch (Exception Exception) { Failures.Add(Exception); }
        try { Bestow.ReleaseState(); }
        catch (Exception Exception) { Failures.Add(Exception); }
        try { Checkpoints.ReleaseState(); }
        catch (Exception Exception) { Failures.Add(Exception); }
        FixtureSlots = Array.Empty<bool>();
        HonorSlots = Array.Empty<bool>();
        InitialProjection = null;
        Projection = null;
        DesiredProjection = null;
        ProjectionApplied = false;
        FixturesPublished = false;
        ApplyingProjection = false;
        AuthorityRecoveryRequested = false;
        PublishingDesired = false;
        HasFixtures = false;
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0)
            throw new AggregateException("Peak successor release failed.", Failures);
    }

    internal static void RequirePublishedViews(Transform Root, string Fixture)
    {
        var Views = Root.GetComponentsInChildren<PhotonView>(true);
        if (Views.Length == 0)
            throw new InvalidOperationException($"A {Fixture} fixture has no PhotonViews.");
        foreach (var View in Views)
            if (!View || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
                throw new InvalidOperationException($"A {Fixture} fixture has an unpublished PhotonView.");
    }

    internal bool PublishPendingCompletion()
    {
        if (DesiredProjection is null) return true;
        if (PhotonNetwork.CurrentRoom is { IsOffline: false } && !PhotonNetwork.IsMasterClient)
        {
            DesiredProjection = null;
            return true;
        }
        if (PublishingDesired) return true;
        PublishingDesired = true;
        try
        {
            while (DesiredProjection is { } Candidate)
            {
                if (PhotonNetwork.CurrentRoom is not { IsOffline: false })
                {
                    if (!AcceptCompletion(Candidate)) return false;
                    if (ReferenceEquals(DesiredProjection, Candidate)
                        && Projection?.Same(Candidate) == true) DesiredProjection = null;
                    continue;
                }
                if (Owner.Network.RunProperties.IsPending(Map.PeakCompletionKey)) return true;
                var Encoded = Candidate.Serialize();
                if (Owner.Network.RunProperties.MatchesRoomValue(Map.PeakCompletionKey, Encoded))
                {
                    if (!HandleCompletionProperty(
                            PhotonNetwork.CurrentRoom.CustomProperties[Map.PeakCompletionKey])) return false;
                }
                else
                {
                    if (!Owner.Network.RunProperties.Publish(Map.PeakCompletionKey, Encoded)) return false;
                    if (Owner.Network.RunProperties.MatchesRoomValue(Map.PeakCompletionKey, Encoded)
                        && !HandleCompletionProperty(
                            PhotonNetwork.CurrentRoom.CustomProperties[Map.PeakCompletionKey])) return false;
                }
                if (ReferenceEquals(DesiredProjection, Candidate)
                    && Projection?.Same(Candidate) == true) DesiredProjection = null;
                return true;
            }
            return true;
        }
        finally
        {
            PublishingDesired = false;
        }
    }

    bool AcceptCompletion(PeakCompletionProjection Received)
    {
        var Previous = Projection;
        if (Previous is not null)
        {
            if (!Received.CanFollow(Previous)) return false;
            if (Received.Same(Previous))
            {
                if (DesiredProjection?.Same(Received) == true) DesiredProjection = null;
                if (FixturesPublished && !ProjectionApplied) ApplyCompletion(Received, Previous);
                return true;
            }
        }
        Projection = Received;
        if (DesiredProjection?.Same(Received) == true) DesiredProjection = null;
        ProjectionApplied = false;
        if (FixturesPublished) ApplyCompletion(Received, Previous);
        return true;
    }

    void ApplyCompletion(
        PeakCompletionProjection Received,
        PeakCompletionProjection? Previous)
    {
        ApplyingProjection = true;
        try
        {
            for (var i = 0; i < FixtureSlots.Length; i++)
            {
                if (!FixtureSlots[i] || Owner.Slots[i].Released) continue;
                if (Received.HonorComplete(i)) Checkpoints.ApplyHonorCompletion(i);
                if (Received.CampfireLit(i)) Campfire.HydrateLit(i);
                Reward.ApplyCompletion(i, Received.RewardState(i), Received.RewardActor(i));
                if (Received.HonorComplete(i)) Reward.ApplyHonorCompletion(i);
            }
            ProjectionApplied = true;
        }
        finally
        {
            ApplyingProjection = false;
        }
        if (!PhotonNetwork.IsMasterClient) return;
        IReadOnlyDictionary<PeakRoomItemOperation, PhotonView>? Inventory = null;
        for (var i = 0; i < FixtureSlots.Length; i++)
        {
            if (!FixtureSlots[i] || Owner.Slots[i].Released) continue;
            var OutputsChanged = Previous is null
                || Previous.OutputState(i) != Received.OutputState(i);
            var SpentChanged = Received.RewardState(i) == PeakRewardState.Spent
                && (Previous is null || Previous.RewardState(i) != PeakRewardState.Spent);
            if (!OutputsChanged && !SpentChanged) continue;
            if (Previous is null
                && (Received.OutputState(i) != 0 || SpentChanged))
                Inventory ??= PeakRoomItemOperations.CaptureInventory(Owner);
            if (OutputsChanged)
                Campfire.ReconcileOutputs(
                    i, RestoreCompleted: Previous is null, Inventory);
            if (SpentChanged) Reward.ReconcileSpent(i, Inventory);
        }
        CompleteStableCampfire();
    }

    internal void CompleteStableCampfire()
    {
        if (!StableCampfireCompletionPending) return;
        var Index = Map.Index;
        var Checkpoint = Checkpoints.TryGet(Index, out var Current)
            ? Current
            : throw new InvalidOperationException(
                "A stable Peak campfire has no checkpoint authority.");
        Owner.Network.DeferredOperations.CompletePeakCampfire(
            Index, Checkpoint.HasSuccessor);
    }

    bool TryGetStableCampfire(
        int Index,
        out PeakCheckpointRuntime.PeakCheckpoint Checkpoint)
    {
        Checkpoint = null!;
        if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.Active
            || Index != Map.Index || !ProjectionApplied || !IsFixture(Index)
            || Owner.Slots[Index].Released || AuthorityRecoveryRequested
            || DesiredProjection is not null || PublishingDesired
            || Owner.Network.RunProperties.IsPending(Map.PeakCompletionKey)
            || !Projection!.CampfireCompletionReady(
                Index, Campfire.EligibleOutputMask(Index))) return false;
        if (!Checkpoints.TryGet(Index, out Checkpoint))
            throw new InvalidOperationException(
                "A stable Peak campfire has no checkpoint authority.");
        return true;
    }

    PeakCompletionProjection CurrentCompletion() =>
        Projection ?? InitialProjection
        ?? throw new InvalidOperationException("Peak completion state was not configured.");

    bool IsFixture(int Index) =>
        Index >= 0 && Index < FixtureSlots.Length && FixtureSlots[Index];

    bool ValidOnlineActors(PeakCompletionProjection ProjectionValue)
    {
        for (var i = 0; i < ProjectionValue.Length; i++)
            if (ProjectionValue.RewardState(i) is PeakRewardState.Opening
                    or PeakRewardState.OpeningReward or PeakRewardState.OpeningRevive
                && ProjectionValue.RewardActor(i) <= 0) return false;
        return true;
    }
}
