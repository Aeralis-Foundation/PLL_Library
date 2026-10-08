using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PLL.Library;

internal sealed class LazySlotGenerator
{
    internal enum CampfireTransitionAdmission { Native, Deferred, Stale }

    internal static CampfireTransitionAdmission AdmitCampfireTransition(int CompletedIndex)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true) return CampfireTransitionAdmission.Native;
        if (CompletedIndex < Map.Index) return CampfireTransitionAdmission.Stale;
        if (CompletedIndex > Map.Index)
            return CanQueueTransition(Run.Transitions, CompletedIndex)
                ? CampfireTransitionAdmission.Deferred
                : CampfireTransitionAdmission.Stale;
        if (Run.HookedGeneration.TransitionBlocked) return CampfireTransitionAdmission.Deferred;
        switch (Run.Transitions.Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.Idle:
                if (ReleasedSlotRetirement.IsRunTerminal(CompletedIndex) || Map.Slots[CompletedIndex].PeakAttachment)
                    return CampfireTransitionAdmission.Native;
                if (!TryReserveCampfireProgression(Run, CompletedIndex))
                    throw new InvalidOperationException("A campfire transition could not reserve its destination.");
                return CampfireTransitionAdmission.Native;
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
            case MapTransitionCoordinator.TransitionLease.State.Active:
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                return CampfireTransitionAdmission.Deferred;
            default:
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
    }

    internal static void QueueCampfireTransition(int CompletedIndex)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true) return;
        QueueTransition(Run, new MapTransitionCoordinator.QueuedTransition(
            MapTransitionCoordinator.QueuedTransitionKind.Campfire, CompletedIndex));
    }

    internal static void CancelCampfireTransition(int CompletedIndex)
    {
        if (Map.CurrentRun is { } run) CancelReservedTransition(run, CompletedIndex + 1);
    }
    internal static bool TryReserveDirectTransition(MapRun Run, int CompletedIndex)
    {
        if (!ReferenceEquals(Map.CurrentRun, Run) || !Run.Active) return false;
        if (Run.HookedGeneration.TransitionBlocked)
        {
            QueueTransition(Run, new MapTransitionCoordinator.QueuedTransition(
                MapTransitionCoordinator.QueuedTransitionKind.DirectAdvance, CompletedIndex));
            return false;
        }
        if (CompletedIndex < Map.Index) return false;
        if (CompletedIndex > Map.Index)
        {
            QueueTransition(Run, new MapTransitionCoordinator.QueuedTransition(
                MapTransitionCoordinator.QueuedTransitionKind.DirectAdvance, CompletedIndex));
            return false;
        }
        switch (Run.Transitions.Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.Idle:
                Run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Reserved(CompletedIndex + 1);
                return true;
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
            case MapTransitionCoordinator.TransitionLease.State.Active:
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                QueueTransition(Run, new MapTransitionCoordinator.QueuedTransition(
                    MapTransitionCoordinator.QueuedTransitionKind.DirectAdvance, CompletedIndex));
                return false;
            default:
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
    }
    internal static void CancelReservedTransition(MapRun Run, int DestinationIndex)
    {
        switch (Run.Transitions.Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
                if (Run.Transitions.Lease.DestinationIndex == DestinationIndex)
                {
                    Run.Transitions.QueuedTransitions.Clear();
                    Run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Idle;
                }
                return;
            case MapTransitionCoordinator.TransitionLease.State.Idle:
            case MapTransitionCoordinator.TransitionLease.State.Active:
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                return;
            default:
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
    }

    static void QueueTransition(MapRun Run, MapTransitionCoordinator.QueuedTransition Operation)
    {
        if (Operation.CompletedIndex < Map.Index || Operation.CompletedIndex >= Run.Slots.Length
            || !CanQueueTransition(Run.Transitions, Operation.CompletedIndex)) return;
        var Queued = Run.Transitions.QueuedTransitions;
        if (!Queued.TryGetValue(Operation.CompletedIndex, out var existing))
        {
            Queued.Add(Operation.CompletedIndex, Operation);
            return;
        }
        if (existing.Kind == Operation.Kind) return;
        throw new InvalidOperationException("A map slot received conflicting delivered transition operations.");
    }

    static bool CanQueueTransition(MapTransitionCoordinator Transitions, int CompletedIndex)
    {
        var Lease = Transitions.Lease;
        switch (Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
            case MapTransitionCoordinator.TransitionLease.State.Active:
                return CompletedIndex == Map.Index || CompletedIndex == Lease.DestinationIndex;
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                return CompletedIndex == Map.Index;
            case MapTransitionCoordinator.TransitionLease.State.Idle:
                return false;
            default:
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
    }

    static void CompleteTransitionLease(MapRun run, int destinationIndex, bool completed)
    {
        switch (run.Transitions.Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.Active:
                if (run.Transitions.Lease.DestinationIndex != destinationIndex)
                    throw new InvalidOperationException("A native map transition released another destination's lease.");
                if (!completed)
                {
                    if (run.Active)
                        AbortLazyGeneration(destinationIndex, "Native map transition did not complete");
                    return;
                }
                if (!run.Active) return;
                if (run.Transitions.QueuedTransitions.Count == 0)
                {
                    run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Idle;
                    return;
                }
                run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.ReplayScheduled;
                try { Plugin.StartRoutine(ReplayTransitionNextFrame(run)); }
                catch (Exception exception)
                {
                    run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Idle;
                    AbortLazyGeneration(
                        destinationIndex, "Queued map transition replay could not be scheduled", exception);
                    throw;
                }
                return;
            case MapTransitionCoordinator.TransitionLease.State.Idle:
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                if (!run.Releasing && !run.Released)
                    throw new InvalidOperationException("A native map transition lost its active lease.");
                return;
            default:
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
    }

    static IEnumerator ReplayTransitionNextFrame(MapRun run)
    {
        yield return null;
        if (!ReferenceEquals(Map.CurrentRun, run) || !run.Active)
        {
            run.Transitions.QueuedTransitions.Clear();
            switch (run.Transitions.Lease.Current)
            {
                case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                    run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Idle;
                    yield break;
                case MapTransitionCoordinator.TransitionLease.State.Idle:
                    yield break;
                case MapTransitionCoordinator.TransitionLease.State.Reserved:
                case MapTransitionCoordinator.TransitionLease.State.Active:
                    throw new InvalidOperationException("A released map run retained an active transition lease.");
                default:
                    throw new InvalidOperationException("A map run has an unknown transition lease state.");
            }
        }
        switch (run.Transitions.Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Idle;
                break;
            case MapTransitionCoordinator.TransitionLease.State.Idle:
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
            case MapTransitionCoordinator.TransitionLease.State.Active:
                throw new InvalidOperationException("A map transition replay lost its scheduled state.");
            default:
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
        var First = FirstQueuedTransition(run.Transitions.QueuedTransitions);
        while (First.HasValue && First.Value.Key < Map.Index)
        {
            run.Transitions.QueuedTransitions.Remove(First.Value.Key);
            First = FirstQueuedTransition(run.Transitions.QueuedTransitions);
        }
        if (!First.HasValue || First.Value.Key != Map.Index) yield break;
        var Replay = First.Value.Value;
        run.Transitions.QueuedTransitions.Remove(First.Value.Key);
        try
        {
            switch (Replay.Kind)
            {
                case MapTransitionCoordinator.QueuedTransitionKind.Campfire:
                    ApplyCampfireTransition(Replay.CompletedIndex);
                    break;
                case MapTransitionCoordinator.QueuedTransitionKind.DirectAdvance:
                    run.Network.DeferredOperations.ApplyDirectAdvance(Map.Index);
                    break;
                default:
                    throw new InvalidOperationException("A queued map transition has an unknown operation kind.");
            }
        }
        catch (Exception exception)
        {
            CancelReservedTransition(run, Replay.CompletedIndex + 1);
            AbortLazyGeneration(Map.Index, "Queued map transition replay failed", exception);
        }
    }

    static void ApplyCampfireTransition(int CompletedIndex)
    {
        var Run = Map.CurrentRun;
        var Handler = Map.Owner;
        if (Run?.Active != true || !Handler || CompletedIndex != Map.Index)
            throw new InvalidOperationException("A queued campfire transition lost its source map slot.");
        if (ReleasedSlotRetirement.IsRunTerminal(CompletedIndex))
        {
            OnFinalCampfireLit(CompletedIndex);
            return;
        }
        if (Run.Slots[CompletedIndex].PeakAttachment)
            throw new InvalidOperationException("A Peak attachment queued an invalid campfire progression.");
        var Destination = CompletedIndex + 1;
        if (!TryReserveCampfireProgression(Run, CompletedIndex))
            throw new InvalidOperationException("A queued campfire transition could not reserve its destination.");
        try
        {
            Handler.GoToSegment(Run.Slots[Destination].NavigationRole);
        }
        catch
        {
            CancelReservedTransition(Run, Destination);
            MapQuicksave.CancelCampfireQuicksave(CompletedIndex);
            throw;
        }
        if (Map.Index != Destination)
        {
            CancelReservedTransition(Run, Destination);
            MapQuicksave.CancelCampfireQuicksave(CompletedIndex);
            throw new InvalidOperationException("A queued campfire transition did not enter its destination map slot.");
        }
        ReleasedSlotRetirement.OnCampfireTransition(CompletedIndex);
    }

    static bool TryReserveCampfireProgression(MapRun Run, int CompletedIndex)
    {
        if (!ReferenceEquals(Map.CurrentRun, Run) || !Run.Active || CompletedIndex != Map.Index) return false;
        if (Run.Transitions.Lease.Current != MapTransitionCoordinator.TransitionLease.State.Idle) return false;
        var Destination = CompletedIndex + 1;
        Run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Reserved(Destination);
        try
        {
            MapQuicksave.PrepareCampfireQuicksave(CompletedIndex);
        }
        catch
        {
            CancelReservedTransition(Run, Destination);
            throw;
        }
        return true;
    }

    static KeyValuePair<int, MapTransitionCoordinator.QueuedTransition>? FirstQueuedTransition(
        SortedDictionary<int, MapTransitionCoordinator.QueuedTransition> Queued)
    {
        foreach (var Operation in Queued) return Operation;
        return null;
    }

    internal static void OnFinalCampfireLit(int index)
    {
        if (!Map.Active || !Map.CurrentRun!.Configuration.Terminal.Winnable || !ReleasedSlotRetirement.IsRunTerminal(index) || Map.Slots[index].PeakAttachment) return;
        RequestFinishRun();
    }
    internal static void GenerateLazySlot(int Index)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true || Run.Retirement.ApplyingReleasedPrefix) return;
        if (Index < 0 || Index >= Map.Slots.Length) return;
        Index = Map.Slots[Index].Occurrence.RequestIndex;
        if (Map.Slots[Index].Released) return;
        if (Index != 0 && !Run.Network.ViewBatches.InitialBatchReady)
        {
            GenerateLazySlot(0);
            return;
        }
        var ready = true;
        foreach (var Slot in Map.Slots[Index].Occurrence.Slots)
            if (!Slot.FeaturesReady)
            {
                ready = false;
                break;
            }
        if (ready)
        {
            var TargetSlot = Map.Slots[Index];
            if (!TargetSlot.ViewBatchReady && Run.Network.ViewBatches.HasPendingBatch(Index))
                Run.Network.ViewBatches.TryApplyViewBatch(Index);
            var CurrentIndex = Map.Index;
            var TargetIsCurrent = CurrentIndex >= 0 && CurrentIndex < Run.Slots.Length
                && ReferenceEquals(
                    TargetSlot.Occurrence,
                    Run.Slots[CurrentIndex].Occurrence);
            if (!Run.LightVolumes.RebakePending(Run, TargetSlot, TargetIsCurrent)) return;
        }
        Run.RequestFeature(Index);
        var Writer = Run.TryAcquireWriter();
        if (Writer is null) return;
        try
        {
            Plugin.StartRoutine(RunLazyFeatureWriter(Run, Writer));
        }
        catch
        {
            Writer.Dispose();
            throw;
        }
    }

    internal static void RequestCurrentLightRebake(MapRun Run, int Index)
    {
        if (Index < 0 || Index >= Run.Slots.Length) return;
        var TargetSlot = Run.Slots[Index];
        if (!TargetSlot.FeaturesReady
            || !Run.LightVolumes.RebakePending(
                Run, TargetSlot, TargetIsCurrent: true)) return;
        GenerateLazySlot(Index);
    }

    static IEnumerator RunLazyFeatureWriter(MapRun run, MapRun.FeatureWriter writer)
    {
        Exception? failure = null;
        try
        {
            while (failure is null && writer.TryTakeRequest(out var index))
            {
                var routine = GenerateLazySlotRoutine(run, writer, index);
                try
                {
                    while (failure is null)
                    {
                        object? current = null;
                        bool moved;
                        try
                        {
                            writer.RequireCurrent();
                            moved = routine.MoveNext();
                            if (moved) current = routine.Current;
                        }
                        catch (Exception exception)
                        {
                            moved = false;
                            failure = exception;
                        }
                        if (!moved) break;
                        yield return current;
                    }
                }
                finally
                {
                    try
                    {
                        (routine as IDisposable)?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failure = failure is null
                            ? exception
                            : new AggregateException(
                                "Lazy feature generation and iterator cleanup both failed.", failure, exception);
                    }
                    writer.CompleteRequest(index);
                }
                while (failure is null && run.Network.ViewBatches.HasPendingBatches)
                {
                    try
                    {
                        writer.RequireCurrent();
                    }
                    catch (Exception ExceptionValue)
                    {
                        failure = ExceptionValue;
                        break;
                    }
                    yield return null;
                }
            }
        }
        finally
        {
            writer.Dispose();
        }
        if (failure is not null) ExceptionPropagation.Throw(failure);
    }

    static IEnumerator GenerateLazySlotRoutine(MapRun run, MapRun.FeatureWriter writer, int index)
    {
        writer.RequireCurrent();
        if (!ReferenceEquals(Map.CurrentRun, run) || index < 0 || index >= Map.Slots.Length)
            throw MapRun.FeatureWriter.Cancelled();
        index = Map.Slots[index].Occurrence.RequestIndex;
        if (Map.Slots[index].Released) yield break;
        var Occurrence = Map.Slots[index].Occurrence;
        var OccurrenceReady = true;
        foreach (var Slot in Occurrence.Slots)
            if (!Slot.FeaturesReady)
            {
                OccurrenceReady = false;
                break;
            }
        if (OccurrenceReady)
        {
            var CurrentIndex = Map.Index;
            if (CurrentIndex < 0 || CurrentIndex >= run.Slots.Length)
                throw MapRun.FeatureWriter.Cancelled();
            var TargetSlot = run.Slots[index];
            var TargetIsCurrent = ReferenceEquals(
                TargetSlot.Occurrence,
                run.Slots[CurrentIndex].Occurrence);
            var Rebake = run.LightVolumes.ScheduleRebake(
                run, TargetSlot, TargetIsCurrent, GeometryDirty: false);
            if (!Rebake) yield break;
            var LightGeneration = run.Features.BeginOperation();
            yield return FeatureGeneration.RunFeatureRoot(
                new LightVolumeRebake(
                    run.LightVolumes, run, TargetSlot, Rebake, writer).Execute(),
                LightGeneration,
                writer);
            if (LightGeneration.Failure is null) yield break;
            AbortLazyGeneration(
                index,
                "Lazy visual light-volume rebake failed",
                LightGeneration.Failure);
            ExceptionPropagation.Throw(LightGeneration.Failure);
        }
        var Ready = new (bool Terrain, bool Campfire)[Map.Slots.Length];
        for (var i = 0; i < Map.Slots.Length; i++)
            Ready[i] = (Map.Slots[i].TerrainReady, Map.Slots[i].CampfireReady);
        var Roots = run.Features.SnapshotRootIds();
        var generatedGroupers = new List<PropGrouper>();
        var addedGroupers = new List<PropGrouper>();
        var Baseline = default(ViewBatchSynchronization.ViewBatchBaseline);
        ViewBatchSynchronization.ViewBatch FrozenBatch = null!;
        Action? restoreActivation = null;
        var generationComplete = false;
        var lightingGeometryDirty = false;
        Exception? failure = null;
        var failureMessage = "Lazy feature generation failed";
        try
        {
            var randomState = UnityEngine.Random.state;
            try
            {
                writer.RequireCurrent();
                Baseline = run.Network.ViewBatches.BeginLazyViewBatch(index, false);
                writer.RequireCurrent();
                foreach (var Slot in Occurrence.Slots) Slot.MarkViewBatchPending();
                writer.RequireCurrent();
                restoreActivation = ActivateLazyGeneration(index);
                lightingGeometryDirty |= restoreActivation is not null;
                writer.RequireCurrent();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                UnityEngine.Random.state = randomState;
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(
                        "Lazy feature setup and random-state cleanup both failed.", failure, exception);
            }

            var Generation = run.Features.BeginOperation();
            var Routine = FeatureGeneration.GenerateFeatures(index, generatedGroupers, addedGroupers, Generation, writer);
            try
            {
                while (failure is null)
                {
                    object? current = null;
                    bool moved;
                    try
                    {
                        writer.RequireCurrent();
                        moved = Routine.MoveNext();
                        if (moved) current = Routine.Current;
                    }
                    catch (Exception exception)
                    {
                        moved = false;
                        failure = exception;
                    }
                    if (!moved) break;
                    yield return current;
                }
            }
            finally
            {
                try
                {
                    (Routine as IDisposable)?.Dispose();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            failure ??= Generation.Failure;
            lightingGeometryDirty |= Generation.Generated != 0;
            if (failure is null) try
            {
                failureMessage = "Lazy feature synchronization failed";
                writer.RequireCurrent();
                foreach (var Slot in Occurrence.Slots)
                {
                    SpawnTrackerTransaction.AttachLazySpawnTrackers(Slot.Index);
                    writer.RequireCurrent();
                    Campfire.Bind(Slot.Index);
                    writer.RequireCurrent();
                }
                WeatherProjection.BindEnvironment();
                writer.RequireCurrent();
                SpecialDayProjection.Refresh();
                writer.RequireCurrent();
                TerrainSplatProjection.Refresh();
                writer.RequireCurrent();
                failureMessage = "Lazy generated-authoring validation failed";
                foreach (var Slot in Occurrence.Slots) AuthoringRuntime.RefreshSlot(Slot.Index);
                writer.RequireCurrent();
                FrozenBatch = run.Network.ViewBatches.FreezeLazyViews(index, Baseline);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (failure is null)
            {
                writer.RequireCurrent();
                var CurrentIndex = Map.Index;
                if (CurrentIndex < 0 || CurrentIndex >= run.Slots.Length)
                    throw MapRun.FeatureWriter.Cancelled();
                var TargetSlot = run.Slots[index];
                var TargetIsCurrent = ReferenceEquals(
                    TargetSlot.Occurrence,
                    run.Slots[CurrentIndex].Occurrence);
                var Rebake = run.LightVolumes.ScheduleRebake(
                    run, TargetSlot, TargetIsCurrent, lightingGeometryDirty);
                if (Rebake)
                {
                    failureMessage = "Lazy visual light-volume rebake failed";
                    yield return FeatureGeneration.RunFeatureRoot(
                        new LightVolumeRebake(
                            run.LightVolumes, run, TargetSlot, Rebake, writer).Execute(),
                        Generation,
                        writer);
                    failure ??= Generation.Failure;
                }
            }
            if (failure is null) try
            {
                failureMessage = "Lazy feature synchronization failed";
                writer.RequireCurrent();
                run.Network.ViewBatches.SynchronizeViewBatch(index, FrozenBatch);
                writer.RequireCurrent();
                foreach (var Slot in Occurrence.Slots)
                {
                    GloomVoiceRuntime.CommitGloomVoiceClone(Slot.Index);
                    writer.RequireCurrent();
                    Slot.MarkFeaturesActivated();
                }
                generationComplete = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure is not null && !generationComplete)
            {
                try
                {
                    FeatureGeneration.ResetGeneratedFeatures(generatedGroupers, addedGroupers);
                }
                catch (Exception cleanup)
                {
                    failure = new AggregateException(
                        "Lazy feature generation and its rollback both failed.", failure, cleanup);
                }
                run.Features.RestoreRootIds(Roots);
                for (var i = 0; i < Map.Slots.Length; i++)
                {
                    Map.Slots[i].RestoreReadiness(Ready[i].Terrain, Ready[i].Campfire);
                }
            }
            if (failure is not null)
                foreach (var Slot in Occurrence.Slots) Slot.MarkViewBatchPending();
        }
        finally
        {
            try
            {
                if (restoreActivation is not null && (!generationComplete || index > Map.Index))
                    restoreActivation();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(
                        "Lazy feature generation and activation rollback both failed.", failure, exception);
            }
        }
        if (failure is null) yield break;
        AbortLazyGeneration(index, failureMessage, failure);
        ExceptionPropagation.Throw(failure);
    }
    internal static Action? ActivateLazyGeneration(int Index, bool ActivateGenerated = false)
    {
        var Occurrence = Map.Slots[Index].Occurrence;
        if (!ActivateGenerated && (!Map.Slots[Index].IsClone || Map.Slots[Index].FeaturesActivated)) return null;
        var Objects = new List<GameObject>(4);
        void Add(GameObject? Value)
        {
            if (!Value) return;
            foreach (var Candidate in Objects)
                if (Candidate == Value) return;
            Objects.Add(Value);
        }
        foreach (var Candidate in Occurrence.Slots)
        {
            Add(Candidate.Segment.segmentParent);
            Add(Candidate.Segment.segmentCampfire);
            Add(Candidate.PeakAttachment ? Candidate.PeakAttachment.gameObject : null);
        }
        Add(Occurrence.Container.gameObject);
        var States = Objects.ConvertAll(Value => Value.activeSelf).ToArray();
        var Queue = PhotonNetwork.IsMessageQueueRunning;
        var RandomState = UnityEngine.Random.state;
        try
        {
            PhotonNetwork.IsMessageQueueRunning = false;
            UnityEngine.Random.InitState(Occurrence.ActivationSeed);
            foreach (var Value in Objects) Value.SetActive(true);
            foreach (var Candidate in Occurrence.Slots) NativeFixtures.ValidateActivatedCustomClone(Candidate);
            Physics.SyncTransforms();
        }
        catch
        {
            for (var i = Objects.Count - 1; i >= 0; i--)
                RestoreLazyFeatureState(Objects[i], States[i]);
            throw;
        }
        finally
        {
            UnityEngine.Random.state = RandomState;
            PhotonNetwork.IsMessageQueueRunning = Queue;
        }
        return () =>
        {
            var RestoreQueue = PhotonNetwork.IsMessageQueueRunning;
            try
            {
                PhotonNetwork.IsMessageQueueRunning = false;
                for (var i = Objects.Count - 1; i >= 0; i--)
                    RestoreLazyFeatureState(Objects[i], States[i]);
                Physics.SyncTransforms();
            }
            finally
            {
                PhotonNetwork.IsMessageQueueRunning = RestoreQueue;
            }
        };
    }


    static void RestoreLazyFeatureState(GameObject Value, bool WasActive)
    {
        if (!Value) return;
        if (!WasActive)
        {
            SpecialDayProjection.DeactivatePreservingGlobals(Value);
            return;
        }
        Value.SetActive(true);
    }
    internal static bool SlotReady(int Index)
    {
        if (!GameHandler.IsOnIsland) return true;
        if (!MapHandler.ExistsAndInitialized) return false;
        if (!Map.Active) return true;
        if (Map.CurrentRun?.Retirement.ApplyingReleasedPrefix == true) return false;
        if (PhotonNetwork.CurrentRoom is { } currentRoom) ReleasedSlotRetirement.HandleReleasedProperties(currentRoom.CustomProperties);
        if (!ReleasedSlotRetirement.TryApplyReleasedPrefix()) return false;
        if (Map.CurrentRun!.Features.Aborted || Index < 0 || Index >= Map.Slots.Length || Map.Slots[Index].Released) return false;
        if (Map.CurrentRun!.HasDeferredSuffix)
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room is not null)
                for (var i = 0; i < Map.Slots.Length; i++)
                {
                    if (i == Index || Map.Slots[i].Released
                        || room.CustomProperties[Map.CurrentRun!.Network.ViewBatches.PropertyKey(i)] is not object[] { Length: > 0 } data
                        || data[0] is not string epoch || epoch != Map.GenerationEpoch) continue;
                    GenerateLazySlot(i);
                    if (!Map.Slots[i].FeaturesReady || !Map.Slots[i].ViewBatchReady) return false;
                }
            GenerateLazySlot(Index);
        }
        if (Map.CurrentRun!.Features.Aborted || !Map.CurrentRun!.Network.ViewBatches.InitialBatchReady) return false;
        if (!Map.CurrentRun.IsOccurrenceReady(Map.Slots[Index].Occurrence)) return false;
        var CurrentIndex = Map.Index;
        return CurrentIndex < 0 || CurrentIndex >= Map.Slots.Length
            || !ReferenceEquals(
                Map.Slots[Index].Occurrence,
                Map.Slots[CurrentIndex].Occurrence)
            || Map.CurrentRun.ReadyCurrentSlot is not null;
    }
    internal static int HostSpawnIndex()
    {
        if (!Peak.Quicksave.ShouldUseSaveData)
            return Map.HasPublishedLayout && Map.Index == Map.Slots.Length && Map.Slots.Length != 0
                ? Map.Slots.Length - 1
                : Map.Index;
        var saved = Peak.Quicksave.SavedRun;
        return SpawnTrackerTransaction.SpawnReadinessIndex(saved.biomeReached);
    }

    internal static void AbortLazyGeneration(int index, string message, Exception? exception = null)
    {
        var Run = Map.CurrentRun;
        if (Run is not null) AbortLazyGeneration(Run, index, message, exception);
    }

    internal static void AbortLazyGeneration(
        MapRun? Run, int Index, string Message, Exception? Failure = null)
    {
        if (Run?.Features.Aborted == true) return;
        if (Run is not null) Run.Features.Aborted = true;
        if (Run is { Releasing: false, Released: false })
        {
            try { Run.Network.ViewBatches.CancelPendingBatches(); }
            catch (Exception cleanup)
            {
                Failure = Failure is null ? cleanup
                    : new AggregateException("Lazy-generation abort cleanup also failed.", Failure, cleanup);
            }
            Run.Network.RunProperties.CancelAll();
            Run.Retirement.RequestedReleasedThrough = -1;
            Run.Transitions.RequestedDirectAdvance = -1;
            Run.Quicksave.PendingCampfireSaveTarget = -1;
            Run.Transitions.RunFinishRequested = false;
            try { Map.ReleaseRun(Run); }
            catch (Exception cleanup)
            {
                Failure = Failure is null ? cleanup
                    : new AggregateException("Lazy-generation abort release also failed.", Failure, cleanup);
            }
        }
        try { IslandLoadPublication.CancelCurrentLoad(); }
        catch (Exception Cleanup)
        {
            Failure = Failure is null ? Cleanup
                : new AggregateException("Lazy-generation abort load cleanup also failed.", Failure, Cleanup);
        }
        Plugin.PLLLog.LogError(
            $"{Message} for layout position {Index + 1}; the run was aborted.{(Failure is null ? string.Empty : $" {Failure}")}");
        var HadRoom = PhotonNetwork.CurrentRoom is not null;
        Player.LeaveCurrentGame();
        if (!HadRoom) SceneManager.LoadScene("Title");
    }
    internal static void FinishRun()
    {
        if (Map.CurrentRun!.Transitions.RunFinished || !PhotonNetwork.IsMasterClient || !Character.localCharacter) return;
        Map.CurrentRun!.Transitions.RunFinished = true;
        Character.localCharacter.EndGame();
    }
    internal static void RequestFinishRun()
    {
        if (Map.CurrentRun!.Transitions.RunFinished || Map.CurrentRun!.Transitions.RunFinishRequested || !CanFinishRun(true)) return;
        if (PhotonNetwork.CurrentRoom is null || PhotonNetwork.IsMasterClient)
        {
            FinishRun();
            return;
        }
        if (!Map.CurrentRun!.Network.RunProperties.Publish(Map.WinRequestKey, true))
        {
            Plugin.PLLLog.LogError("The synchronized run-finish request could not be published.");
            return;
        }
        Map.CurrentRun!.Transitions.RunFinishRequested = true;
    }
    internal static bool CanFinishRun(bool VerifyPeakProgress)
    {
        var Terminal = MapTransitionCoordinator.RunTerminalIndex();
        if (!Map.Active || !Map.CurrentRun!.Configuration.Terminal.Winnable || Terminal < 0 || Terminal >= Map.Slots.Length
            || Map.Index != Terminal || Map.Slots[Terminal].PeakAttachment && Ascents.shouldSpawnFlare) return false;
        if (!VerifyPeakProgress || !Map.Slots[Terminal].PeakAttachment) return true;
        return ProgressProjection.PeakProgressReached(Terminal);
    }
    internal static IEnumerator WaitForTransitionGeneration(IEnumerator Original)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true)
        {
            (Original as IDisposable)?.Dispose();
            throw MapRun.FeatureWriter.Cancelled();
        }
        int destination;
        switch (Run.Transitions.Lease.Current)
        {
            case MapTransitionCoordinator.TransitionLease.State.Reserved:
                destination = Run.Transitions.Lease.DestinationIndex;
                Run.Transitions.Lease = MapTransitionCoordinator.TransitionLease.Active(destination);
                break;
            case MapTransitionCoordinator.TransitionLease.State.Idle:
            case MapTransitionCoordinator.TransitionLease.State.Active:
            case MapTransitionCoordinator.TransitionLease.State.ReplayScheduled:
                (Original as IDisposable)?.Dispose();
                throw new InvalidOperationException("A native map transition started without its reserved lease.");
            default:
                (Original as IDisposable)?.Dispose();
                throw new InvalidOperationException("A map run has an unknown transition lease state.");
        }
        var Deferred = Run.IsDeferredSlot(destination);
        var index = destination;
        var disposeAttempted = false;
        var authoringTransition = false;
        var completed = false;
        var MessageQueueWasRunning = PhotonNetwork.IsMessageQueueRunning;

        void DisposeOriginal()
        {
            if (disposeAttempted) return;
            disposeAttempted = true;
            (Original as IDisposable)?.Dispose();
        }

        void CommitAuthoring()
        {
            AuthoringRuntime.CommitTransition();
            authoringTransition = false;
        }

        void RequireTarget()
        {
            Run.RequirePublished();
            var Slots = Run.Slots;
            if (Run.Features.Aborted)
                throw new InvalidOperationException("Lazy feature generation was aborted during a map transition.");
            if (index < 0 || index >= Slots.Length || Slots[index].Released)
                throw new InvalidOperationException("The target map slot was released during its transition.");
            if (!ReferenceEquals(Map.CurrentRun, Run) || Map.Index != index)
                throw new InvalidOperationException("The target map slot changed during its transition.");
        }

        bool ContinueAfterYield()
        {
            if (Run.Releasing || Run.Released) return false;
            Run.RequirePublished();
            return true;
        }

        bool TargetReady() =>
            Run.IsOccurrenceReady(Run.Slots[index].Occurrence)
            && Run.LightVolumes.ProjectionPrepared(Run.Slots[index]);

        try
        {
            AuthoringRuntime.BeginTransition();
            authoringTransition = true;
            Run.RequirePublished();
            while (Run.PendingQuicksave)
            {
                yield return null;
                Run.RequirePublished();
            }
            var NativeContinues = Original.MoveNext();
            RequireTarget();
            if (Deferred) GenerateLazySlot(index);
            RequestCurrentLightRebake(Run, index);
            if (NativeContinues)
            {
                yield return Original.Current;
                if (!ContinueAfterYield()) yield break;
            }
            while (!Run.Features.Aborted
                && index >= 0 && index < Run.Slots.Length && !Run.Slots[index].Released
                && !TargetReady())
            {
                yield return null;
                if (!ContinueAfterYield()) yield break;
            }
            RequireTarget();
            var Page = Run.LightVolumes.PrepareFallbackPage(Run, Run.Slots[index], RequireTarget);
            try
            {
                while (Page.MoveNext())
                {
                    yield return Page.Current;
                    if (!ContinueAfterYield()) yield break;
                }
            }
            finally
            {
                (Page as IDisposable)?.Dispose();
            }
            RequireTarget();
            if (MapTransitionCoordinator.SelectCurrentLightProjection(Run.Handler!))
                MapTransitionCoordinator.CommitCurrentOccurrence(Run.Handler!);
            MapEnvironmentProjection.SyncVoidWater();
            if (!NativeContinues)
            {
                DisposeOriginal();
                CommitAuthoring();
                completed = true;
                yield break;
            }
            var PendingCampfireSave = Run.Quicksave.PendingCampfireSaveTarget;
            if (PendingCampfireSave >= 0 && PendingCampfireSave != index)
                throw new InvalidOperationException("The pending campfire quicksave targets another map transition.");
            var CompletedSlot = index > 0 ? Run.Slots[index - 1] : null;
            var Save = PendingCampfireSave == index || CompletedSlot is not null
                && CompletedSlot.ProgressionBoundary != NativeProgressionBoundaryKind.None;
            if (PendingCampfireSave == index) Run.Quicksave.PendingCampfireSaveTarget = -1;

            MapEnvironmentProjection.SyncWater();
            if (Save)
            {
                Peak.Quicksave.SaveNow();
                GUIManager.instance.Quicksave();
            }
            ReleasedSlotRetirement.ReleaseOldZones();
            while (!Run.Features.Aborted
                && index >= 0 && index < Run.Slots.Length && !Run.Slots[index].Released)
            {
                if (!Original.MoveNext()) break;
                yield return Original.Current;
                if (!ContinueAfterYield()) yield break;
            }
            RequireTarget();
            DisposeOriginal();
            CommitAuthoring();
            completed = true;
        }
        finally
        {
            if (Run.Quicksave.PendingCampfireSaveTarget == index)
                Run.Quicksave.PendingCampfireSaveTarget = -1;
            try
            {
                DisposeOriginal();
            }
            finally
            {
                try
                {
                    if (authoringTransition && ReferenceEquals(Map.CurrentRun, Run) && Run.Active)
                        AuthoringRuntime.AbortTransition();
                }
                finally
                {
                    try
                    {
                        if (!completed && ReferenceEquals(Map.CurrentRun, Run))
                            PhotonNetwork.IsMessageQueueRunning = MessageQueueWasRunning;
                    }
                    finally
                    {
                        CompleteTransitionLease(Run, destination, completed);
                        if (completed) NativeContentTools.ObserveReadyCurrent(Run);
                    }
                }
            }
        }
    }
}
