using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine.Rendering.Universal;
using Zorro.Core;

namespace PLL.Library;

internal sealed class ReleasedSlotRetirement
{
    static ReleasedSlotRetirement Current => Map.CurrentRun?.Retirement
        ?? throw new InvalidOperationException("No map run owns released-slot retirement.");
    internal GameObject? ReleasedRoot { get; set; }
    internal HashSet<int> RetiredViewIds { get; } = new();
    internal int ReleasedThrough { get; set; } = -1;
    internal int PendingReleasedThrough { get; set; } = -1;
    internal int RequestedReleasedThrough { get; set; } = -1;
    internal bool ApplyingReleasedPrefix { get; set; }

    internal void ReleaseState()
    {
        ReleasedRoot = null;
        RetiredViewIds.Clear();
        ReleasedThrough = -1;
        PendingReleasedThrough = -1;
        RequestedReleasedThrough = -1;
        ApplyingReleasedPrefix = false;
    }
    internal static bool TryGetSlot(global::Campfire campfire, out int index)
    {
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (Map.Slots[i].Released || Map.Slots[i].Source is NadirLayoutSegmentSource) continue;
            var CampfireRoot = Map.Slots[i].Segment.segmentCampfire;
            var SegmentRoot = Map.Slots[i].Segment.segmentParent;
            if (CampfireRoot && TransformOwnership.Contains(CampfireRoot.transform, campfire.transform)
                || SegmentRoot && TransformOwnership.Contains(SegmentRoot.transform, campfire.transform))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    internal static bool IsRunTerminal(int Index) => Map.Active && Index == MapTransitionCoordinator.RunTerminalIndex();
    internal static bool IsNadirAdvance(int Index) =>
        Map.Active && Index >= 0 && Index < Map.Slots.Length - 1 && Map.Slots[Index].Source is NadirLayoutSegmentSource;

    static bool IsPeakAdvance(int index) =>
        Map.Active && index >= 0 && index < Map.Slots.Length - 1
        && Map.Slots[index].PeakAttachment && !IsRunTerminal(index);

    static bool IsActiveNativeProgressionBoundary(int index) =>
        Map.Active && index == Map.Index && index >= 0 && index < Map.Slots.Length
        && !Map.Slots[index].Released
        && Map.Slots[index].ProgressionBoundary != NativeProgressionBoundaryKind.None;

    static bool IsNativeProgressionAdvance(int index) =>
        IsActiveNativeProgressionBoundary(index) && index < Map.Slots.Length - 1;
    internal static bool CanDirectAdvance(int Index) =>
        IsPeakAdvance(Index) || IsNadirAdvance(Index) || IsNativeProgressionAdvance(Index);

    internal static void OnCampfireTransition(int completedIndex)
    {
        var index = completedIndex + 1;
        if (!Map.Active || index != Map.Index || index >= Map.Slots.Length) return;
        if (Map.CurrentRun!.IsDeferredSlot(index)) LazySlotGenerator.GenerateLazySlot(index);
    }

    internal static void ReleaseOldZones()
    {
        if (!Map.Active || !Map.CurrentRun!.Configuration.Environment.RemoveOldZones) return;
        try
        {
            Map.CurrentRun!.Quicksave.AdvanceRestoredHostCampfireRetention(Map.Index);
            if (PhotonNetwork.CurrentRoom is { } room) HandleReleasedProperties(room.CustomProperties);
            RequestReleasedThrough(Math.Min(Map.Index - 2, Map.Slots.Length - 1));
        }
        catch (Exception exception)
        {
            AbortReleasedZoneFailure("Released-zone cleanup failed", exception);
            throw;
        }
    }

    static void AbortReleasedZoneFailure(string message, Exception failure)
    {
        try
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, message, failure);
        }
        catch (Exception abort)
        {
            throw new AggregateException("Released-zone cleanup and run abort both failed.", failure, abort);
        }
    }

    static void RequestReleasedThrough(int target)
    {
        if (target < 0 || target >= Map.Slots.Length) return;
        target = RetirementTarget(target);
        if (target < 0) return;
        var room = PhotonNetwork.CurrentRoom;
        if (PhotonNetwork.OfflineMode)
        {
            Current.RequestedReleasedThrough = -1;
            Current.PendingReleasedThrough = Math.Max(Current.PendingReleasedThrough, target);
            TryApplyReleasedPrefix();
            return;
        }
        if (room is null) return;

        HandleReleasedProperties(room.CustomProperties);
        if (target <= Math.Max(Math.Max(Current.ReleasedThrough, Current.PendingReleasedThrough), Current.RequestedReleasedThrough)
            || !PhotonNetwork.IsMasterClient) return;
        var Requested = Current.RequestedReleasedThrough;
        Current.RequestedReleasedThrough = target;
        if (!Map.CurrentRun!.Network.RunProperties.Publish(Map.ReleasedKey, target))
        {
            Current.RequestedReleasedThrough = Requested;
            Plugin.PLLLog.LogError("The released-zone prefix could not be published.");
            return;
        }
    }
    internal static void HandleReleasedProperties(PhotonHashtable Properties)
    {
        if (Map.CurrentRun?.Configuration.Environment.RemoveOldZones != true
            || !Properties.ContainsKey(Map.ReleasedKey)) return;
        if (Properties[Map.ReleasedKey] is object[] { Length: 0 }) return;
        if (Properties[Map.ReleasedKey] is not object[] { Length: 3 } data
            || data[0] is not string epoch)
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Released-zone state validation failed");
            return;
        }
        if (epoch != Map.GenerationEpoch) return;
        if (data[1] is not int revision || revision != Map.LayoutRevision
            || data[2] is not int target || target < 0 || target >= Map.Slots.Length)
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Released-zone state validation failed");
            return;
        }
        target = RetirementTarget(target);
        if (target < Current.ReleasedThrough || target < Current.PendingReleasedThrough)
        {
            Plugin.PLLLog.LogWarning("Ignored a regressive released-zone prefix.");
            return;
        }
        Current.PendingReleasedThrough = target;
        if (target >= Current.RequestedReleasedThrough) Current.RequestedReleasedThrough = -1;
        TryApplyReleasedPrefix();
    }
    internal static bool TryApplyReleasedPrefix()
    {
        var Run = Map.CurrentRun;
        if (Run?.Retirement.ApplyingReleasedPrefix == true) return false;
        if (Current.PendingReleasedThrough <= Current.ReleasedThrough) return true;
        if (Run is null
            || Run.PendingQuicksave
            || Run.HookedGeneration.TransitionBlocked
            || Run.Network.DeferredOperations.HasPending
            || Run.Transitions.Lease.Current != MapTransitionCoordinator.TransitionLease.State.Idle
            || Run.Transitions.QueuedTransitions.Count != 0) return false;
        if (Map.Pending is not null) return false;
        if (Run.FeatureMutationActive) return false;
        if (!Map.CurrentRun!.Network.ViewBatches.InitialBatchReady) return false;
        var Target = RetirementTarget(
            Math.Min(Current.PendingReleasedThrough, Math.Min(Map.Index - 2, Map.Slots.Length - 1)));
        if (Target <= Current.ReleasedThrough) return true;
        for (var i = Current.ReleasedThrough + 1; i <= Target; i++)
        {
            if (!ValidateReleasedViewBatch(i))
            {
                LazySlotGenerator.AbortLazyGeneration(Map.Index, "Released-zone object batch validation failed");
                return false;
            }
            if (Map.Slots[i].GloomVoice is { } Binding && i == Binding.FirstSlot)
                Binding.RequireSlotOwnership(Map.Slots);
        }

        var changed = false;
        Run.Retirement.ApplyingReleasedPrefix = true;
        try
        {
            for (var i = Current.ReleasedThrough + 1; i <= Target; i++)
            {
                if (!Map.Slots[i].Released && !ReleaseSlot(i, null)) return false;
                Current.ReleasedThrough = i;
                changed = true;
            }
        }
        catch (Exception exception)
        {
            AbortReleasedZoneFailure("Released-zone retirement failed", exception);
            throw;
        }
        finally
        {
            Run.Retirement.ApplyingReleasedPrefix = false;
        }
        if (!changed) return true;
        try
        {
            RefreshReleasedZones();
        }
        catch (Exception exception)
        {
            AbortReleasedZoneFailure("Released-zone refresh failed", exception);
            throw;
        }
        return true;
    }

    static int ReleasedOccurrenceBoundary(int target)
    {
        while (target >= 0 && Map.Slots[target].Occurrence.BatchIndex > target)
            target = Map.Slots[target].Occurrence.Slots[0].Index - 1;
        return target;
    }

    static int RetirementTarget(int Target)
    {
        var BaseCamp = SpawnTrackerTransaction.BaseCampSlotIndex();
        if (BaseCamp >= 0) Target = Math.Min(Target, BaseCamp - 1);
        if (Map.CurrentRun?.Quicksave.RetainedRestoredHostCampfireSlot is { } RestoredCampfire)
            Target = Math.Min(Target, RestoredCampfire - 1);
        return ReleasedOccurrenceBoundary(Target);
    }

    static bool ValidateReleasedViewBatch(int index)
    {
        if (!TryReadReleasedViewBatch(index, out _, out var NextFakeId)) return false;
        Map.CurrentRun!.Network.ViewBatches.IncludeFakeItemHighWater(NextFakeId);
        return true;
    }

    static bool TryReadReleasedViewBatch(
        int index, out int[] ids, out int NextFakeId)
    {
        ids = Array.Empty<int>();
        NextFakeId = 0;
        if (index <= 0 || PhotonNetwork.CurrentRoom is not { } room) return true;
        var Value = room.CustomProperties[Map.CurrentRun!.Network.ViewBatches.PropertyKey(index)];
        if (Value is null) return true;
        if (Value is not object[] data) return false;
        if (data.Length == 0) return true;
        if (data[0] is not string epoch) return false;
        if (epoch != Map.GenerationEpoch) return true;
        if (!ViewBatchEnvelope.TryParse(
                Value, Map.GenerationEpoch, Map.LayoutRevision, FeatureGeneration.MapSeed(), out var Envelope, out _)
            || Envelope.ReplaceFakeItems) return false;
        var unique = new HashSet<int>();
        foreach (var id in Envelope.ViewIds)
            if (id <= 0 || id >= PhotonNetwork.MAX_VIEW_IDS || !unique.Add(id)) return false;
        unique.Clear();
        foreach (var id in Envelope.FakeIds)
            if (id < 0 || id >= Envelope.NextFakeId || !unique.Add(id)) return false;
        ids = Envelope.ViewIds;
        NextFakeId = Envelope.NextFakeId;
        return true;
    }

    static bool ReleaseSlot(int index, Transform? retainedSpawn)
    {
        if (index < 0 || index >= Map.Slots.Length || Map.Slots[index].Released) return false;
        var Slot = Map.Slots[index];
        var Campfire = Slot.Segment.segmentCampfire;
        var RetainedCampfire = retainedSpawn && Campfire
            ? Campfire.GetComponentInChildren<global::Campfire>(true)
            : null;
        if (retainedSpawn && (!Campfire || !RetainedCampfire
            || !Campfire.GetComponentInChildren<RespawnChest>(true)))
            throw new InvalidOperationException(
                $"Caldera base-camp retention failed for layout position {index + 1}.");

        var Parent = Slot.Segment.segmentParent;
        var shared = false;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (i == index || Map.Slots[i].Released || !Map.Slots[i].Container || !Slot.Container
                || !TransformOwnership.Overlaps(Slot.Container, Map.Slots[i].Container)) continue;
            shared = true;
            break;
        }
        var releasedObjects = new List<GameObject>(2);
        if (Slot.Container)
        {
            if (!shared) releasedObjects.Add(Slot.Container.gameObject);
            else
            {
                if (Parent && !RequiredByLiveSlot(Parent.transform, index)) releasedObjects.Add(Parent);
                if (!retainedSpawn && Campfire && (!Parent || !Campfire.transform.IsChildOf(Parent.transform))
                    && !RequiredByLiveSlot(Campfire.transform, index)) releasedObjects.Add(Campfire);
            }
        }
        if (Slot.PeakAttachment && (!Slot.Container || !TransformOwnership.Contains(Slot.Container, Slot.PeakAttachment))
            && !RequiredByLiveSlot(Slot.PeakAttachment, index))
            releasedObjects.Add(Slot.PeakAttachment.gameObject);
        var OriginalParent = Slot.Segment.segmentParent;
        var OriginalCampfire = Slot.Segment.segmentCampfire;
        var OriginalReconnect = Slot.Segment.reconnectSpawnPos;
        var OriginalWallNext = Slot.Segment.wallNext;
        var OriginalWallPrevious = Slot.Segment.wallPrevious;
        var OriginalPeak = Slot.PeakAttachmentState;
        var OriginalPeakAttachment = Slot.PeakAttachment;
        var OriginalPeakHandler = Zorro.Core.Singleton<PeakHandler>._instance;
        var EndCutscenePeakHandler = Map.CurrentRun?.PublicationOwner?.EndCutscenePeakHandler;
        var ReplacePeakHandler = EndCutscenePeakHandler && OriginalPeakHandler && OriginalPeakAttachment
            && OriginalPeakAttachment == OriginalPeakHandler.transform;
        var OriginalTerrainReady = Slot.TerrainReady;
        var OriginalCampfireReady = Slot.CampfireReady;
        var OriginalSupplementalReady = Slot.Occurrence.SupplementalReady;
        var OriginalViewBatchReady = Slot.ViewBatchReady;
        var ViewBatchRetirement = Map.CurrentRun!.Network.ViewBatches.CapturePendingBatchRetirement(index);
        var PendingJumps = Map.CurrentRun!.Transitions.PendingJumps;
        var RetiredJumps = new List<MapTransitionCoordinator.QueuedJump>(3);
        if (PendingJumps.TryGetValue(Slot.NavigationRole, out var OrdinaryJump)) RetiredJumps.Add(OrdinaryJump);
        if (OriginalPeakAttachment && PendingJumps.TryGetValue(Segment.Peak, out var PeakJump)) RetiredJumps.Add(PeakJump);
        if (NadirRuntime.IsVoidControllerIndex(Map.Owner!, index)
            && PendingJumps.TryGetValue(Segment.Void, out var VoidJump)) RetiredJumps.Add(VoidJump);
        var Fixture = retainedSpawn && Campfire ? new NativeFixtures.DetachedFixture(Campfire.transform) : null;
        var FakeItems = retainedSpawn && Map.Owner ? new FakeItemTransaction(Map.Owner) : null;
        var retirement = PrepareViewRetirement(
            releasedObjects,
            retainedSpawn && Campfire ? Campfire.transform : null,
            index);
        GameObject? marker = null;
        var peakHandlerReplaced = false;
        try
        {
            CommitViewRetirement(retirement);
            marker = new GameObject($"PLL_Released_{index:D3}");
            ReplaceReleasedSegment(Slot, retainedSpawn, RetainedCampfire, marker);
            Slot.ClearReadiness();
            Slot.Occurrence.SupplementalReady = false;
            Slot.MarkViewBatchPending();
            Map.CurrentRun!.Network.ViewBatches.CommitPendingBatchRetirement(ViewBatchRetirement);
            foreach (var Jump in RetiredJumps) PendingJumps.Remove(Jump.Segment);
            Slot.MarkReleased();
            FogCutoutProjection.Refresh();
            SpecialDayProjection.Refresh();
            AuthoringRuntime.RefreshSlot(index);
            if (ReplacePeakHandler)
            {
                Zorro.Core.Singleton<PeakHandler>._instance = EndCutscenePeakHandler!;
                peakHandlerReplaced = true;
            }
        }
        catch (Exception exception)
        {
            var failures = new List<Exception> { exception };
            void Restore(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception rollback)
                {
                    failures.Add(rollback);
                }
            }

            Restore(() => Slot.Segment._segmentParent = OriginalParent);
            Restore(() => Slot.Segment._segmentCampfire = OriginalCampfire);
            Restore(() =>
            {
                Slot.Segment.reconnectSpawnPos = OriginalReconnect;
                Slot.Segment.wallNext = OriginalWallNext;
                Slot.Segment.wallPrevious = OriginalWallPrevious;
            });
            Restore(() =>
            {
                Slot.RestorePeak(OriginalPeak);
            });
            if (Fixture is not null) Restore(Fixture.Restore);
            if (marker is not null) Restore(() =>
            {
                if (marker) UnityEngine.Object.DestroyImmediate(marker);
            });
            Restore(() =>
            {
                Slot.RestoreReadiness(OriginalTerrainReady, OriginalCampfireReady);
                Slot.Occurrence.SupplementalReady = OriginalSupplementalReady;
                Slot.RestoreViewBatchReadiness(OriginalViewBatchReady);
                Slot.RestoreReleased();
            });
            Restore(() => Map.CurrentRun!.Network.ViewBatches.RestorePendingBatchRetirement(ViewBatchRetirement));
            Restore(() =>
            {
                foreach (var Jump in RetiredJumps) PendingJumps[Jump.Segment] = Jump;
            });
            if (FakeItems is not null) Restore(FakeItems.Restore);
            Restore(() => RollbackViewRetirement(retirement));
            if (peakHandlerReplaced) Restore(() => Zorro.Core.Singleton<PeakHandler>._instance = OriginalPeakHandler!);
            Restore(() => AuthoringRuntime.RefreshSlot(index));
            Restore(SpecialDayProjection.Refresh);
            Restore(FogCutoutProjection.Refresh);
            if (failures.Count != 1)
                throw new AggregateException("Released-zone retirement and its rollback both failed.", failures);
            throw;
        }
        Map.CurrentRun!.Peak.RetireSlot(index);
        if (index == Slot.Occurrence.BatchIndex && Slot.Occurrence.RequestIndex != 0)
            Map.CurrentRun!.Network.ViewBatches.RetireReplayBatch(Slot.Occurrence.RequestIndex);
        GloomVoiceRuntime.DismissReleasedGloomVoiceClone(Slot.GloomVoice);
        Slot.Occurrence.RetirePluginEntityPopulation();
        foreach (var Value in releasedObjects)
            if (Value) Slot.Occurrence.RetireActors(Value);
        ReleaseObjectsAfterRetirement(releasedObjects);
        Map.CurrentRun!.WorldInteractions.PruneReleased();
        return true;
    }

    static void ReplaceReleasedSegment(
        MapSlot Slot,
        Transform? retainedSpawn,
        global::Campfire? retainedCampfire,
        GameObject marker)
    {
        var root = ReleasedZones();
        var Anchor = Slot.Segment.reconnectSpawnPos;
        marker.transform.SetParent(root.transform, false);
        if (Anchor) marker.transform.SetPositionAndRotation(Anchor.position, Anchor.rotation);
        var Campfire = Slot.Segment.segmentCampfire;
        if (retainedSpawn && retainedCampfire && Campfire)
        {
            RetireFakeItems(Campfire);
            Campfire.transform.SetParent(marker.transform, true);
            Campfire.transform.rotation = retainedSpawn.rotation
                * Quaternion.Inverse(retainedCampfire.transform.rotation) * Campfire.transform.rotation;
            Campfire.transform.position += retainedSpawn.position - retainedCampfire.transform.position;
        }
        marker.SetActive(false);
        Slot.Segment._segmentParent = marker;
        Slot.Segment._segmentCampfire = retainedSpawn ? Campfire! : null!;
        Slot.Segment.reconnectSpawnPos = retainedSpawn ? retainedCampfire!.transform : marker.transform;
        Slot.Segment.wallNext = null!;
        Slot.Segment.wallPrevious = null;
        Slot.DetachPeak();
    }

    static GameObject ReleasedZones()
    {
        if (Current.ReleasedRoot) return Current.ReleasedRoot;
        Current.ReleasedRoot = new GameObject("PLL_ReleasedZones");
        Current.ReleasedRoot.transform.SetParent(Map.Owner ? Map.Owner.transform : null, false);
        Current.ReleasedRoot.SetActive(false);
        return Current.ReleasedRoot;
    }

    static bool RequiredByLiveSlot(Transform root, int released)
    {
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (i == released || Map.Slots[i].Released) continue;
            var Slot = Map.Slots[i];
            if (Slot.Segment.segmentParent && TransformOwnership.Contains(root, Slot.Segment.segmentParent.transform)
                || Slot.Segment.segmentCampfire && TransformOwnership.Contains(root, Slot.Segment.segmentCampfire.transform)
                || TransformOwnership.Contains(root, Slot.Segment.reconnectSpawnPos)
                || Slot.Segment.wallNext && TransformOwnership.Contains(root, Slot.Segment.wallNext.transform)
                || Slot.Segment.wallPrevious && TransformOwnership.Contains(root, Slot.Segment.wallPrevious.transform)
                || TransformOwnership.Contains(root, Slot.PeakAttachment)
                || TransformOwnership.Contains(root, Slot.PeakRespawn)) return true;
        }
        return false;
    }

    internal static void ReleaseRetirementBatch(MapRun Run, IReadOnlyList<GameObject> Values)
    {
        if (Run is null) throw new ArgumentNullException(nameof(Run));
        if (Values is null) throw new ArgumentNullException(nameof(Values));
        var Retirement = PrepareViewRetirement(Values, null, -1);
        CommitViewRetirement(Retirement);
        try { Run.MarkOriginalMapSourcesRetired(); }
        catch (Exception Failure)
        {
            try { RollbackViewRetirement(Retirement); }
            catch (Exception Rollback)
            {
                throw new AggregateException(
                    "Original map source retirement publication and its rollback both failed.", Failure, Rollback);
            }
            throw;
        }
        ReleaseObjectsAfterRetirement(Values);
    }
    internal static void ReleaseObject(GameObject Value)
    {
        if (!Value) return;
        var Values = new[] { Value };
        CommitViewRetirement(PrepareViewRetirement(Values, null, -1));
        ReleaseObjectsAfterRetirement(Values);
    }

    static void ReleaseObjectsAfterRetirement(IReadOnlyList<GameObject> Values)
    {
        var Failures = new List<Exception>();
        void Collect(Action Cleanup)
        {
            try { Cleanup(); }
            catch (Exception Failure) { Failures.Add(Failure); }
        }

        foreach (var Value in Values)
        {
            if (!Value) continue;
            Collect(() => Map.CurrentRun?.HookedGeneration.RetireRoot(Value));
            Collect(() => Map.CurrentRun?.PropInteractions.Retire(Value));
            Collect(() => Map.CurrentRun?.SurfaceTextures.Retire(Value));
            Collect(() => Map.CurrentRun?.ClimbingFixtures.Retire(Value));
            Collect(() =>
            {
                if (Map.CurrentRun is not { } Run) return;
                foreach (var Slot in Run.Slots)
                    if (ReferenceEquals(Slot, Slot.Occurrence.Slots[0])) Slot.Occurrence.RetireActors(Value);
            });
            Collect(() => Map.CurrentRun?.NativeSpawns.RetireOutputs(Value));
            Collect(() => SpecialDayProjection.DeactivatePreservingGlobals(Value));
            Collect(() => RetireFakeItems(Value));
            Collect(() =>
            {
                if (!Map.Owner) return;
                var Root = Value.transform;
                var Trackers = Map.Owner._spawnTrackers;
                Trackers?.RemoveAll(Tracker =>
                    !Tracker || TransformOwnership.Contains(Root, Tracker.transform));
            });
            try
            {
                foreach (var Grouper in OutermostGroupers(Value))
                {
                    if (Grouper) Collect(() => NativeFeatureGeneration.Clear(Grouper));
                }
            }
            catch (Exception Failure)
            {
                Failures.Add(Failure);
            }
            Collect(() => OccurrenceHeightmapMeshes.ReleaseDeferred(Value.transform));
            Collect(() =>
            {
                if (Value) UnityEngine.Object.Destroy(Value);
            });
        }
        if (Failures.Count != 0)
            throw new AggregateException("Released map object cleanup failed.", Failures);
    }

    static PropGrouper[] OutermostGroupers(GameObject value)
    {
        var groupers = value.GetComponentsInChildren<PropGrouper>(true);
        var result = new List<PropGrouper>(groupers.Length);
        for (var i = 0; i < groupers.Length; i++)
        {
            var grouper = groupers[i];
            if (!grouper) continue;
            var root = grouper.transform;
            var nested = false;
            for (var j = 0; j < groupers.Length; j++)
            {
                var other = groupers[j];
                if (j == i || !other) continue;
                var otherRoot = other.transform;
                if (root == otherRoot ? j < i : root.IsChildOf(otherRoot))
                {
                    nested = true;
                    break;
                }
            }
            if (!nested) result.Add(grouper);
        }
        return result.ToArray();
    }

    static ReleasedSlotRetirement.ViewRetirement PrepareViewRetirement(
        IEnumerable<GameObject> roots,
        Transform? excluded,
        int index)
    {
        var Result = new ReleasedSlotRetirement.ViewRetirement();
        var SourceIds = new HashSet<int>();
        var Seen = new HashSet<int>();
        foreach (var Root in roots)
        {
            if (!Root) continue;
            foreach (var View in Root.GetComponentsInChildren<PhotonView>(true))
            {
                if (!View || View.ViewID <= 0 || !Seen.Add(View.GetInstanceID())
                    || excluded && TransformOwnership.Contains(excluded, View.transform)) continue;
                var Id = View.ViewID;
                var Registered = PhotonNetwork.GetPhotonView(Id);
                if (!SourceIds.Add(Id) || Current.RetiredViewIds.Contains(Id)
                    || Registered is not null && !ReferenceEquals(Registered, View))
                    throw new InvalidOperationException($"PhotonView {Id} cannot be retired safely.");
                Result.Entries.Add((View, Id));
            }
        }

        if (!TryReadReleasedViewBatch(index, out var reserved, out var NextFakeId))
            throw new InvalidOperationException("The released-zone object batch is invalid.");
        Map.CurrentRun!.Network.ViewBatches.IncludeFakeItemHighWater(NextFakeId);
        foreach (var Id in reserved)
        {
            if (SourceIds.Contains(Id)) continue;
            var Registered = PhotonNetwork.GetPhotonView(Id);
            if (Current.RetiredViewIds.Contains(Id))
            {
                if (Registered is not null)
                    throw new InvalidOperationException($"Retired PhotonView {Id} regained a live registry owner.");
                continue;
            }
            if (Registered is not null)
                throw new InvalidOperationException($"PhotonView {Id} is already owned outside its released batch.");
            Result.Entries.Add((null, Id));
        }
        return Result;
    }

    static void CommitViewRetirement(ReleasedSlotRetirement.ViewRetirement Plan)
    {
        try
        {
            foreach (var Entry in Plan.Entries)
            {
                if (!Current.RetiredViewIds.Add(Entry.Id))
                    throw new InvalidOperationException($"PhotonView {Entry.Id} is already retired.");
                Plan.Committed.Add(Entry);
                if (Entry.Source is not null) Entry.Source.ViewID = 0;
                if (PhotonNetwork.GetPhotonView(Entry.Id) is not null)
                    throw new InvalidOperationException($"PhotonView {Entry.Id} did not unregister during retirement.");
            }
        }
        catch (Exception exception)
        {
            try
            {
                RollbackViewRetirement(Plan);
            }
            catch (Exception rollback)
            {
                throw new AggregateException("PhotonView retirement and its rollback both failed.", exception, rollback);
            }
            throw;
        }
    }

    static void RollbackViewRetirement(ReleasedSlotRetirement.ViewRetirement Plan)
    {
        Exception? Failure = null;
        for (var i = Plan.Committed.Count - 1; i >= 0; i--)
        {
            var State = Plan.Committed[i];
            if (!Current.RetiredViewIds.Remove(State.Id))
                Failure ??= new InvalidOperationException($"PhotonView {State.Id} lost its retirement reservation.");
            try
            {
                if (State.Source is null) continue;
                var Registered = PhotonNetwork.GetPhotonView(State.Id);
                if (State.Source.ViewID == 0)
                {
                    if (Registered is not null)
                        throw new InvalidOperationException($"PhotonView {State.Id} changed ownership during retirement rollback.");
                    State.Source.ViewID = State.Id;
                    if (!ReferenceEquals(PhotonNetwork.GetPhotonView(State.Id), State.Source))
                        throw new InvalidOperationException($"PhotonView {State.Id} did not register during retirement rollback.");
                    continue;
                }
                if (State.Source.ViewID != State.Id || !ReferenceEquals(Registered, State.Source))
                    throw new InvalidOperationException($"PhotonView {State.Id} changed ownership during retirement rollback.");
            }
            catch (Exception Exception)
            {
                Failure ??= Exception;
            }
        }
        Plan.Committed.Clear();
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }
    internal static void ClearReleasedZones(bool SceneGone)
    {
        if (!SceneGone) GloomVoiceRuntime.DismissAllGloomVoiceClones();
        if (Current.ReleasedRoot) UnityEngine.Object.DestroyImmediate(Current.ReleasedRoot);
        Current.ReleasedRoot = null;
    }

    static void RetireFakeItems(GameObject value)
    {
        var manager = FakeItemManager.Instance;
        if (!manager || !value) return;
        var descendants = value.GetComponentsInChildren<FakeItem>(true);
        if (descendants.Length == 0) return;
        var targets = new HashSet<int>();
        foreach (var item in descendants)
            if (item) targets.Add(item.GetInstanceID());
        if (targets.Count == 0) return;

        ref var Items = ref manager.allFakeItems;
        if (Items is null) return;
        var retired = new HashSet<int>();
        Items.RemoveAll(Item =>
        {
            if (!Item) return true;
            if (!targets.Contains(Item.GetInstanceID())) return false;
            if (Item.index >= 0) retired.Add(Item.index);
            return true;
        });
        if (retired.Count == 0) return;
        ref var Data = ref manager.fakeItemData;
        Data.hiddenItems?.RemoveAll(retired.Contains);
    }

    static void RefreshReleasedZones()
    {
        var Configuration = Map.CurrentRun!.Configuration.Environment;
        if (!Configuration.NoWater && Configuration.WaterFollowsBeach)
        {
            var beach = false;
            for (var i = 0; i < Map.Slots.Length; i++)
                if (!Map.Slots[i].Released && Map.Slots[i].Source.Id == "Beach")
                {
                    beach = true;
                    break;
                }
            if (!beach)
            {
                var ReleasedWater = Map.CurrentRun!.Environment.WaterObjects;
                var Retirement = PrepareViewRetirement(ReleasedWater, null, -1);
                CommitViewRetirement(Retirement);
                Map.CurrentRun!.Environment.Release();
                ReleaseObjectsAfterRetirement(ReleasedWater);
            }
        }
        Campfire.Bind();
        AuthoringRuntime.Tick();
        WeatherProjection.BindEnvironment();
        TerrainSplatProjection.Refresh();
        MapEnvironmentProjection.SyncWater();
        WeatherProjection.PublishStorm();
    }
    internal sealed class ViewRetirement
    {
        internal List<(PhotonView? Source, int Id)> Entries { get; } = new();
        internal List<(PhotonView? Source, int Id)> Committed { get; } = new();
    }

}
