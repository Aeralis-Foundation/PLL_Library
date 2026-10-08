using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class MapPublication
{
    static int InitializeMapFinalizations;

    internal static void SuppressOriginals()
    {
        if (Map.Pending is null) return;
        Map.Pending.Originals.DeactivateUnused(Map.Pending.Reused);
    }

    static void CompleteCommit(MapRun run)
    {
        var prepared = run.RollbackOwner;
        if (!ReferenceEquals(Map.CurrentRun, run) || !run.Publishing || prepared is null)
            throw new InvalidOperationException("The current map run is not ready to complete publication.");
        run.RequireActivation();
        NativeFixtures.ValidateActivatedCustomClones();
        SpawnTrackerTransaction.ApplySavedSpawnHistories();
        run.RequireActivation();
        if (!run.Network.ViewBatches.ReplayDeferredFakeItemSync())
            throw new InvalidOperationException("Deferred FakeItem state could not be published with the map run.");
        run.RequireActivation();
        AuthoringRuntime.Bind(run);
        run.RequireActivation();
        var Profile = prepared.Slots[0].Segment._dayNightProfile;
        var DiscardedPlane = run.Configuration.Environment.NoPlane
            && prepared.StartKit.Plane.Value is { } plane && plane
            ? plane.gameObject
            : null;
        run.Activate();
        var initialIndex = prepared.Slots[0].Index;
        NadirRuntime.ReconcileVoidController(run, initialIndex);
        ReleasedSlotRetirement.ReleaseOldZones();
        if (!run.Active) throw new InvalidOperationException("Initial released-zone cleanup aborted the map run.");
        MapTransitionCoordinator.SelectCurrentLightProjection(prepared.Handler);
        if (!ReferenceEquals(run.ReadyCurrentSlot, prepared.Slots[0]))
            throw new InvalidOperationException("The initial occurrence has not completed current presentation.");
        if (Profile && DayNightManager.instance) DayNightManager.instance.BlendProfiles(Profile);
        AuthoringRuntime.Tick();
        if (!ReferenceEquals(Map.CurrentRun, run)
            || !ReferenceEquals(run.ReadyCurrentSlot, prepared.Slots[0]))
            throw new InvalidOperationException("Initial authoring callbacks changed the published occurrence.");
        Effects.Commit();
        Sounds.Commit(run, initialIndex);
        SpawnTrackerTransaction.CommitSpawnTrackers(prepared);
        if (DiscardedPlane) ReleasedSlotRetirement.ReleaseObject(DiscardedPlane);
        prepared.Originals.Retire(
            run, prepared.Reused, prepared.OriginalPeakHandler, Zorro.Core.Singleton<PeakHandler>._instance);
        NativeContentTools.ObserveReadyCurrent(run);
    }

    static void ExecuteCleanup(List<Exception> Failures, Action Operation)
    {
        try { Operation(); }
        catch (Exception Exception) { Failures.Add(Exception); }
    }

    static void CompleteRelease(MapRun Run, List<Exception> Failures, string FailureMessage)
    {
        ExecuteCleanup(Failures, Run.CompleteRelease);
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0) throw new AggregateException(FailureMessage, Failures);
    }

    internal static void ReleasePublished(MapRun Run)
    {
        Run.BeginRelease();
        Run.Weather.Quiesce();
        NativeFeatureGeneration.Reset();
        if (Run.DeferRelease(() => ReleasePublished(Run))) return;
        var Prepared = Run.PublicationOwner
            ?? throw new InvalidOperationException("Published map release requires its publication owner.");
        if (!Run.TryBeginReleaseCleanup()) return;

        var Failures = new List<Exception>();
        var RestoreOriginalState = !Run.OriginalMapSourcesRetired && !Run.SceneGone;
        var ResumeNativeSources = !Run.Features.Aborted;

        if (Run.Peak.Bestow.Pending is { } Pending)
            ExecuteCleanup(Failures, () => Run.Peak.Bestow.ReleasePendingPeakBestow(Pending, true, false));
        ExecuteCleanup(Failures, Run.Network.ReleaseBridge);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.CancelPendingBatches);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.ReleasePublishedViewIds);
        ExecuteCleanup(Failures, () => ReleasedSlotRetirement.ClearReleasedZones(Run.SceneGone));
        ExecuteCleanup(Failures, () => FeatureGeneration.ResetGeneratedFeatures(Prepared.GeneratedGroupers, Prepared.AddedGroupers));
        ExecuteCleanup(Failures, () => RollbackFinalForce(Prepared.Slots));
        if (RestoreOriginalState) Prepared.RestoreHandler(
            Operation => ExecuteCleanup(Failures, Operation), ResumeNativeSources);
        else
        {
            if (!ResumeNativeSources) ExecuteCleanup(Failures, Prepared.Originals.Quiesce);
            ExecuteCleanup(Failures, Prepared.Originals.ReleaseVoidController);
        }
        ExecuteCleanup(Failures, () => FogRuntime.Release(RestoreOriginalState));
        if (RestoreOriginalState) ExecuteCleanup(Failures, ProgressProjection.RestoreProgress);
        ExecuteCleanup(Failures, RestoreOriginalState ? Campfire.RollbackTransaction : Campfire.CommitTransaction);
        ExecuteCleanup(Failures, AuthoringRuntime.Clear);
        ExecuteCleanup(Failures, () => Run.Weather.ClearEnvironment(RestoreOriginalState && !Run.SceneGone));
        if (RestoreOriginalState) ExecuteCleanup(Failures, () => MapEnvironmentProjection.RestoreWater(Prepared));
        if (RestoreOriginalState) ExecuteCleanup(Failures, Prepared.Environment.Restore);
        if (RestoreOriginalState) ExecuteCleanup(Failures, Prepared.StartKit.Restore);
        if (RestoreOriginalState && Prepared.FakeItems is not null) ExecuteCleanup(Failures, Prepared.FakeItems.Restore);
        if (RestoreOriginalState) ExecuteCleanup(Failures,
            () => Prepared.Originals.Restore(Prepared.Positions, ResumeNativeSources));
        ExecuteCleanup(Failures, () => DestroyImmediate(Prepared.CreatedObjects));
        ExecuteCleanup(Failures, () => Destroy(Prepared.Slots));
        ExecuteCleanup(Failures, () => GloomVoiceRuntime.DestroyGloomProfiles(Prepared.CreatedGloomProfiles));
        ExecuteCleanup(Failures, BiomeRegistration.ClearLiveCatalogs);
        ExecuteCleanup(Failures, () => Effects.Release(RestoreOriginalState));
        ExecuteCleanup(Failures, Run.Network.ViewBatches.ClearDeferredFakeItemSync);
        CompleteRelease(Run, Failures, "Published map release failed.");
    }
    internal static void ReleaseUnpublished(MapRun Run)
    {
        Run.BeginRelease();
        Run.Weather.Quiesce();
        NativeFeatureGeneration.Reset();
        if (Run.DeferRelease(() => ReleaseUnpublished(Run))) return;
        if (!Run.TryBeginReleaseCleanup()) return;
        var Failures = new List<Exception>();

        ExecuteCleanup(Failures, Run.Network.ReleaseBridge);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.CancelPendingBatches);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.ReleasePublishedViewIds);
        ExecuteCleanup(Failures, () => ReleasedSlotRetirement.ClearReleasedZones(Run.SceneGone));
        ExecuteCleanup(Failures, () => Run.ReleasePreparation(Operation => ExecuteCleanup(Failures, Operation)));

        ExecuteCleanup(Failures, () => FogRuntime.Release(true));
        ExecuteCleanup(Failures, ProgressProjection.RestoreProgress);
        ExecuteCleanup(Failures, Campfire.RollbackTransaction);
        ExecuteCleanup(Failures, AuthoringRuntime.Clear);
        ExecuteCleanup(Failures, () => Run.Weather.ClearEnvironment(!Run.SceneGone));
        ExecuteCleanup(Failures, BiomeRegistration.ClearLiveCatalogs);
        ExecuteCleanup(Failures, Effects.Rollback);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.ClearDeferredFakeItemSync);
        CompleteRelease(Run, Failures, "Unpublished map release failed.");
    }
    internal static void RollbackPending(MapRun Run)
    {
        Run.BeginRelease();
        Run.Weather.Quiesce();
        NativeFeatureGeneration.Reset();
        if (Run.DeferRelease(() => RollbackPending(Run))) return;
        var Prepared = Run.RollbackOwner
            ?? throw new InvalidOperationException("Map layout rollback requires its prepared map owner.");
        if (!Run.TryBeginReleaseCleanup()) return;
        var Failures = new List<Exception>();

        ExecuteCleanup(Failures, Run.Network.ReleaseBridge);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.ReleasePublishedViewIds);
        if (Prepared.Committed)
        {
            ExecuteCleanup(Failures, () => FeatureGeneration.ResetGeneratedFeatures(Prepared.GeneratedGroupers, Prepared.AddedGroupers));
            ExecuteCleanup(Failures, () => RollbackFinalForce(Prepared.Slots));
            Prepared.RestoreHandler(
                Operation => ExecuteCleanup(Failures, Operation), ResumeNativeSources: !Run.Features.Aborted);
            Run.Features.ClearRootIds();
            Run.Network.RunProperties.CancelAll();
            ExecuteCleanup(Failures, Run.Network.ViewBatches.CancelPendingBatches);
            Run.Transitions.PendingJumps.Clear();
            Run.Transitions.RequestedDirectAdvance = -1;
            Run.Transitions.RunFinished = false;
            Run.Transitions.RunFinishRequested = false;
        }
        ExecuteCleanup(Failures, () => FogRuntime.Release(true));
        ExecuteCleanup(Failures, ProgressProjection.RestoreProgress);
        ExecuteCleanup(Failures, Campfire.RollbackTransaction);
        ExecuteCleanup(Failures, AuthoringRuntime.Clear);
        ExecuteCleanup(Failures, () => Run.Weather.ClearEnvironment(!Run.SceneGone));
        ExecuteCleanup(Failures, () => ReleasedSlotRetirement.ClearReleasedZones(Run.SceneGone));
        ExecuteCleanup(Failures, () => DiscardPrepared(Run));
        ExecuteCleanup(Failures, Effects.Rollback);
        ExecuteCleanup(Failures, Run.Network.ViewBatches.ClearDeferredFakeItemSync);
        CompleteRelease(Run, Failures, "Map layout rollback failed.");
    }

    static void RollbackFinalForce(IEnumerable<MapSlot> Slots)
    {
        var occurrences = new HashSet<FinalForceOccurrence>();
        foreach (var Slot in Slots)
            if (Slot.CustomResolved is { } custom && occurrences.Add(custom.FinalForce))
                custom.FinalForce.Rollback();
    }

    static void DiscardPrepared(MapRun Run)
    {
        var Prepared = Run.RollbackOwner;
        var FakeItems = Prepared?.FakeItems;
        var Failures = new List<Exception>();

        ExecuteCleanup(Failures, Run.Network.ViewBatches.CancelPendingBatches);
        if (Prepared is not null)
        {
            foreach (var Slot in Prepared.Slots)
                ExecuteCleanup(Failures, Slot.Occurrence.RetirePluginEntityPopulation);
            ExecuteCleanup(Failures, () => DestroyImmediate(Prepared.CreatedObjects));
            ExecuteCleanup(Failures,
                () => Prepared.Originals.Restore(Prepared.Positions, ResumeNativeSources: !Run.Features.Aborted));
            ExecuteCleanup(Failures, Prepared.StartKit.Restore);
            ExecuteCleanup(Failures, () => MapEnvironmentProjection.RestoreWater(Prepared));
            ExecuteCleanup(Failures, Prepared.Environment.Restore);
            ExecuteCleanup(Failures, () => Destroy(Prepared.Slots));
            ExecuteCleanup(Failures, () => GloomVoiceRuntime.DestroyGloomProfiles(Prepared.CreatedGloomProfiles));
        }
        ExecuteCleanup(Failures, BiomeRegistration.ClearLiveCatalogs);
        if (FakeItems is not null) ExecuteCleanup(Failures, FakeItems.Restore);
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0) throw new AggregateException("Pending map cleanup failed.", Failures);
    }

    internal static void Destroy(IEnumerable<MapSlot> Slots)
    {
        var Containers = new HashSet<Transform>();
        var Failures = new List<Exception>();
        foreach (var Slot in Slots)
        {
            ExecuteCleanup(Failures, Slot.Occurrence.RetirePluginEntityPopulation);
            if (Slot.IsClone && Slot.Container) Containers.Add(Slot.Container);
        }
        foreach (var Container in Containers)
        {
            if (!Container) continue;
            Map.CurrentRun?.NativeSpawns.RetireOutputs(Container.gameObject);
            OccurrenceHeightmapMeshes.ReleaseNow(Container);
            UnityEngine.Object.DestroyImmediate(Container.gameObject);
        }
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0) throw new AggregateException("Plugin entity retirement failed before map destruction.", Failures);
    }

    internal static IEnumerator BeforeRunStart(IEnumerator Original)
    {
        var Baseline = InitializeMapFinalizations;
        var OnlineStartupIntent = IslandLoadPublication.ConsumeOnlineStartupIntent();
        var StartedOnline = !PhotonNetwork.OfflineMode || OnlineStartupIntent;
        MapHandler? sealedCompatibilityOwner = null;
        MapRun? startupRun = null;
        var Handler = Zorro.Core.Singleton<MapHandler>.Instance;
        var Suppression = Handler ? new OriginalMapSources.StartupSuppression(Handler) : null;
        var process = BeforeRunStartCore(
            OnlineStartupIntent, Original, Suppression,
            Owner => sealedCompatibilityOwner = Owner, Run => startupRun = Run);
        Exception? failure = null;
        var Completed = false;
        try
        {
            while (true)
            {
                object? current = null;
                bool moved;
                try
                {
                    moved = process.MoveNext();
                    if (moved) current = process.Current;
                }
                catch (Exception exception)
                {
                    moved = false;
                    failure = exception;
                }
                if (!moved)
                {
                    Completed = failure is null;
                    break;
                }
                yield return current;
            }
        }
        finally
        {
            void Dispose(IDisposable? value)
            {
                try { value?.Dispose(); }
                catch (Exception exception)
                {
                    failure = failure is null
                        ? exception
                        : new AggregateException("Run startup and iterator cleanup both failed.", failure, exception);
                }
            }
            Dispose(process as IDisposable);
            if (!ReferenceEquals(process, Original)) Dispose(Original as IDisposable);
            var ResumeNativeSources = failure is null || failure is OperationCanceledException
                || !StartedOnline && !MapQuicksave.RestoringPllQuicksave();
            if (failure is not null && failure is not OperationCanceledException
                && InitializeMapFinalizations == Baseline)
                failure = HandleRunStartFailure(sealedCompatibilityOwner, startupRun, StartedOnline, failure);
            try { Suppression?.Release(ResumeNativeSources: ResumeNativeSources
                && startupRun?.Features.Aborted != true); }
            catch (Exception Exception)
            {
                failure = failure is null ? Exception
                    : new AggregateException("Run startup and source restoration both failed.", failure, Exception);
            }
            if (!Completed || failure is not null) try { MapLoadingProgress.StopInitial(); }
            catch (Exception Exception)
            {
                failure = failure is null
                    ? Exception
                    : new AggregateException(
                        "Run startup and progress cleanup both failed.", failure, Exception);
            }
        }

        if (failure is null) yield break;
        if (failure is OperationCanceledException && startupRun is { HasPublishedLayout: false } cancelled
            && (cancelled.Releasing || cancelled.Released)) yield break;
        ExceptionPropagation.Throw(failure);
    }

    static IEnumerator BeforeRunStartCore(
        bool OnlineStartupIntent,
        IEnumerator original,
        OriginalMapSources.StartupSuppression? Suppression,
        Action<MapHandler?> captureCompatibilityOwner,
        Action<MapRun?> captureRun)
    {
        var StartupAuthority = RunConfigurationResolution.WaitForStartupAuthority(OnlineStartupIntent);
        try
        {
            while (StartupAuthority.MoveNext()) yield return StartupAuthority.Current;
        }
        finally
        {
            (StartupAuthority as IDisposable)?.Dispose();
        }
        NativeBaseMap.ReleaseCurrent();
        var Compatibility = Map.SealLiveCompatibility();
        MapRun.GenerationCohort? initialGenerationCohort = null;
        captureCompatibilityOwner(Compatibility?.Handler);
        MapLoadingProgress? LoadingProgress = null;
        if (Compatibility is not null
            && MapQuicksave.RuntimeMapProcessingRequired(Compatibility.Configuration))
        {
            LoadingProgress = MapLoadingProgress.BeginInitial();
            LoadingProgress?.SetActivity("Synchronizing map content");
            var Manifest = FinalManifestConsensus.Wait(Compatibility.Configuration);
            try
            {
                while (Manifest.MoveNext())
                {
                    Compatibility.RecordSuspension();
                    yield return Manifest.Current;
                }
            }
            finally
            {
                (Manifest as IDisposable)?.Dispose();
            }
        }
        if (Compatibility is not null
            && MapQuicksave.RuntimeLayoutPublicationRequired(Compatibility.Configuration))
        {
            LoadingProgress?.SetActivity("Synchronizing map generation");
            var Cohort = InitialGenerationCohortBarrier.Wait(
                Compatibility.Configuration, Value => initialGenerationCohort = Value);
            try
            {
                while (Cohort.MoveNext())
                {
                    Compatibility.RecordSuspension();
                    yield return Cohort.Current;
                }
            }
            finally
            {
                (Cohort as IDisposable)?.Dispose();
            }
        }
        LoadingProgress?.SetActivity("Preparing map layout");
        var Preparation = Map.BeginPreparation(Compatibility, initialGenerationCohort, Suppression);
        var Run = Preparation?.Owner ?? Map.CurrentRun;
        captureRun(Run);
        captureCompatibilityOwner(null);
        if (Preparation is not null)
        {
            var Startup = Preparation.Start(LoadingProgress);
            try
            {
                while (Startup.MoveNext()) yield return Startup.Current;
            }
            finally
            {
                (Startup as IDisposable)?.Dispose();
            }
        }
        Commit(LoadingProgress);
        if (Run?.Features.Aborted == true) throw MapRun.FeatureWriter.Cancelled();
        if (Run?.Released == true)
        {
            Run = Map.CurrentRun;
            captureRun(Run);
        }
        if (Run is null)
        {
            MapLoadingProgress.StopInitial();
            Suppression?.Dispose();
            while (original.MoveNext()) yield return original.Current;
            NativeBaseMap.ApplyCurrent();
            yield break;
        }
        var Network = Run.Network;
        while (Network.GenerationBarrier.GenerationRunning)
        {
            Run.RequirePublished();
            yield return null;
        }
        var Failure = Network.GenerationBarrier.TakeGenerationFailure();
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
        Run.RequirePublished();
        while (true)
        {
            Run.RequirePublished();
            if (Network.ViewBatches.InitialBatchReady || Run.Features.Aborted) break;
            yield return null;
        }
        if (Run.Features.Aborted)
            throw new InvalidOperationException("Initial generated-object synchronization was aborted.");
        while (!Run.HookedGeneration.InitialOutputsReady())
        {
            Run.RequirePublished(); Run.HookedGeneration.Tick();
            if (Run.Features.Aborted) throw new InvalidOperationException("Initial Hooked generation was aborted.");
            yield return null;
        }
        if (Run.LightVolumes.NeedsRebake(Run.Slots[0]))
        {
            using var Writer = Run.AcquireWriter();
            var Generation = Run.Features.BeginOperation();
            yield return FeatureGeneration.RunFeatureRoot(
                new LightVolumeRebake(Run.LightVolumes, Run,
                    Run.Slots[0], true, Writer).Execute(), Generation, Writer);
            if (Generation.Failure is { } BakeFailure) ExceptionPropagation.Throw(BakeFailure);
        }
        var Barrier = Network.GenerationBarrier.Wait();
        try
        {
            while (Barrier.MoveNext())
            {
                yield return Barrier.Current;
                Run.RequirePublished();
            }
        }
        finally
        {
            (Barrier as IDisposable)?.Dispose();
        }
        LoadingProgress?.CompleteUnit();
        LoadingProgress?.BeginUnit("Starting PEAK");
        Run.Peak.Publish();
        while (original.MoveNext())
        {
            yield return original.Current;
            Run.RequirePublished();
        }
        if (!Run.Active)
            throw new InvalidOperationException("PEAK completed run startup before the map run became active.");
        LoadingProgress?.CompleteUnit();
        LoadingProgress?.Ready();
    }

    static Exception ClearPreRunState(MapHandler? compatibilityOwner, Exception failure)
    {
        var failures = new List<Exception> { failure };

        if (compatibilityOwner is not null) ExecuteCleanup(failures, BiomeRegistration.ClearLiveCatalogs);
        ExecuteCleanup(failures, NativeBaseMap.ReleaseCurrent);
        ExecuteCleanup(failures, Map.Ingress.ClearFakeItemSync);
        return failures.Count == 1
            ? failure
            : new AggregateException("Map initialization and its cleanup both failed.", failures);
    }

    static Exception HandleRunStartFailure(
        MapHandler? compatibilityOwner, MapRun? run, bool startedOnline, Exception failure)
    {
        var Restoring = MapQuicksave.RestoringPllQuicksave();
        if (run?.Features.Aborted == true)
        {
            MapQuicksave.ClearRejectedQuicksaveState();
            return failure;
        }
        var Abort = Restoring || startedOnline;
        if (Abort)
        {
            if (run is null) failure = ClearPreRunState(compatibilityOwner, failure);
            LazySlotGenerator.AbortLazyGeneration(run, 0,
                Restoring ? "Quicksave map restoration failed" : "Map initialization failed", failure);
            MapQuicksave.ClearRejectedQuicksaveState();
            return failure;
        }
        if (run is null) failure = ClearPreRunState(compatibilityOwner, failure);
        else try
        {
            if (run.RollbackOwner is not null) RollbackPending(run);
            else run.Network.ViewBatches.ClearDeferredFakeItemSync();
        }
        catch (Exception rollback)
        {
            failure = new AggregateException(
                "Map initialization and its rollback both failed.", failure, rollback);
        }
        MapQuicksave.ClearRejectedQuicksaveState();
        return failure;
    }

    internal static Exception? FinalizeInitializeMap(Exception? Failure)
    {
        InitializeMapFinalizations++;
        var Run = Map.CurrentRun;
        if (Failure is null)
        {
            try
            {
                if (Run?.Publishing == true) CompleteCommit(Run);
                else if (Run is null)
                {
                    NativeBaseMap.ApplyCurrent();
                    ViewBatchSynchronization.ReplayPreflightFakeItemSync();
                }
                return null;
            }
            catch (Exception Exception)
            {
                Failure = Exception;
            }
        }
        var Restoring = MapQuicksave.RestoringPllQuicksave();
        if (Restoring || Run?.Active == true
            && ReferenceEquals(Map.CurrentRun, Run))
        {
            LazySlotGenerator.AbortLazyGeneration(Run, 0,
                Restoring ? "Quicksave map restoration failed"
                    : "Map initialization failed", Failure);
            MapQuicksave.ClearRejectedQuicksaveState();
            return Failure;
        }
        try
        {
            if (Run?.RollbackOwner is not null) RollbackPending(Run);
            else if (Run is not null) Run.Network.ViewBatches.ClearDeferredFakeItemSync();
            else Map.Ingress.ClearFakeItemSync();
        }
        catch (Exception Rollback)
        {
            var Aggregate = new AggregateException(
                "Map initialization and its rollback both failed.", Failure, Rollback);
            MapQuicksave.ClearRejectedQuicksaveState();
            return Aggregate;
        }
        MapQuicksave.ClearRejectedQuicksaveState();
        return Failure;
    }
    internal static void DestroyImmediate(IEnumerable<GameObject> Values)
    {
        foreach (var Value in Values)
        {
            if (!Value) continue;
            OccurrenceHeightmapMeshes.ReleaseNow(Value.transform);
            UnityEngine.Object.DestroyImmediate(Value);
        }
    }
    static void Commit(MapLoadingProgress? LoadingProgress)
    {
        var Run = Map.CurrentRun;
        var Prepared = Map.Pending;
        if (Prepared is null)
        {
            if (MapQuicksave.RestoringActivePllQuicksave())
                throw MapQuicksave.RejectQuicksave("the restored PLL layout was not prepared for commit.");
            return;
        }
        if (!Prepared.Handler)
        {
            if (Run is not null) RollbackPending(Run);
            if (MapQuicksave.RestoringActivePllQuicksave())
                throw MapQuicksave.RejectQuicksave("PEAK's map handler was destroyed before quicksave restoration committed.");
            return;
        }
        if (Run is null || !ReferenceEquals(Run.RollbackOwner, Prepared))
            throw new InvalidOperationException("The prepared map is not owned by the current map run.");
        var Handler = Prepared.Handler;
        LoadingProgress?.BeginUnit("Publishing map");
        Run.BeginPublishing();
        Run.Quicksave.ValidateRestoredHostCampfire();
        MapRun.FeatureWriter? Writer = Run.AcquireWriter();
        try
        {
            Writer.RequireCurrent();
            Prepared.OriginalSegments = Handler.segments;
            Prepared.OriginalBiomeTypes = new List<Biome.BiomeType>(Handler.biomes);
            Prepared.OriginalPeakRespawn = Handler.respawnThePeak;
            Prepared.OriginalPeakHandler = Zorro.Core.Singleton<PeakHandler>._instance;
            var PublishedPeakHandler = PeakLifecycle.PublishedPeakHandler(Prepared.Slots);
            var Configuration = Run.Configuration;
            var OriginalPeakWillRetire = Prepared.OriginalPeakHandler
                && Prepared.Originals.WillRetire(Prepared.OriginalPeakHandler.transform, Prepared.Reused);
            var LastSlot = Prepared.Slots.Length - 1;
            if (OriginalPeakWillRetire && !PublishedPeakHandler
                || Configuration.Environment.RemoveOldZones
                    && !Prepared.Slots[LastSlot].PeakAttachment
                    && (!PublishedPeakHandler || LastSlot == 0
                        || Prepared.Slots[LastSlot - 1].PeakAttachment != PublishedPeakHandler.transform))
                Prepared.EndCutscenePeakHandler = NativeFixtures.ClonePeakEndCutsceneOwner(
                    Prepared.OriginalPeakHandler
                        ?? throw new InvalidOperationException("PEAK's native Peak handler is unavailable for the retained ending."),
                    Handler.transform,
                    Prepared.CreatedObjects);
            var OriginalVoidInitialized = Handler.voidInitialized;
            Prepared.OriginalVoidInitialized = OriginalVoidInitialized;
            Prepared.FakeItems = new FakeItemTransaction(Handler);
            var OriginalTrackers = Handler._spawnTrackers;
            Prepared.OriginalSpawnTrackers = OriginalTrackers is null
                ? null
                : new List<Peak.SpawnedItemTracker>(OriginalTrackers);
            Prepared.OriginalTrackerIds = new HashSet<int>();
            foreach (var Tracker in Handler.GetComponentsInChildren<Peak.SpawnedItemTracker>(true))
                Prepared.OriginalTrackerIds.Add(Tracker.GetInstanceID());
            if (!Prepared.Level)
                throw new InvalidOperationException("PEAK's LevelGeneration component was destroyed before map commit.");
            Campfire.BeginTransaction();
            Prepared.Committed = true;
            Prepared.OriginalLevelSeed = Prepared.Level.seed;
            Prepared.Level.seed = Configuration.Layout.Seed;
            var RandomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(Configuration.Layout.Seed);
                Peak.StatueSpawnRandomizationStep.statueSpawnOffset =
                    UnityEngine.Random.Range(0, 12);
            }
            finally { UnityEngine.Random.state = RandomState; }

            Writer.RequireCurrent();
            Prepared.Originals.PublishVoidController();
            var VoidPublication = Prepared.Originals.VoidPublication;
            Handler.segments = PeakLifecycle.BuildPublishedSegments(
                Prepared.Slots,
                Prepared.OriginalSegments,
                Prepared.NativeCatalogSegments,
                Prepared.Originals.OriginalVoidSegment,
                OriginalVoidInitialized,
                VoidPublication);
            Handler.voidInitialized =
                VoidPublication is not OriginalMapSources.VoidSegmentPublication.Omitted;
            MapHandler.EnsureSpawnTrackersAttached();
            if (Prepared.TerminalRespawn)
                Handler.respawnThePeak = Prepared.TerminalRespawn;
            Zorro.Core.Singleton<PeakHandler>._instance = (PublishedPeakHandler ?? Prepared.EndCutscenePeakHandler ?? Prepared.OriginalPeakHandler)!;
            Handler.biomes.Clear();
            foreach (var Slot in Prepared.Slots)
                if (LayoutSegmentBehavior.Resolve(Slot.Source) is { } Compatibility)
                    Handler.biomes.Add(Compatibility.Biome);
            Prepared.Originals.DeactivateUnused(Prepared.Reused);
            var ActiveContainers = new HashSet<int>();
            if (Run.HasDeferredSuffix)
            {
                for (var i = Run.InitialGenerationSlotCount; i < Prepared.Slots.Length; i++)
                {
                    var Slot = Prepared.Slots[i];
                    for (var j = 0; j < Run.InitialGenerationSlotCount; j++)
                        if (TransformOwnership.Overlaps(Slot.Container, Prepared.Slots[j].Container))
                        {
                            ActiveContainers.Add(Slot.Container.GetInstanceID());
                            break;
                        }
                    if (!Configuration.Environment.NoVoidWater
                        && !Configuration.Environment.VoidWaterFollowsNadir
                        && Slot.VoidWaterRoot && Slot.Container)
                        ActiveContainers.Add(Slot.Container.GetInstanceID());
                }
            }
            for (var i = 0; i < Run.InitialGenerationSlotCount; i++) Map.Slots[i].MarkViewBatchPending();
            var Baseline = Run.Network.ViewBatches.BeginLazyViewBatch(0, true);
            for (var i = 0; i < Prepared.Slots.Length; i++)
            {
                Writer.RequireCurrent();
                var Slot = Prepared.Slots[i];
                var Deferred = Run.IsDeferredSlot(i);
                if (!Deferred && Slot.IsClone)
                {
                    if (ReferenceEquals(Slot, Slot.Occurrence.Slots[0]))
                        PlacementExecution.ActivateClonedOccurrence(Slot.Occurrence, Writer);
                    continue;
                }
                if (Deferred && Slot.IsClone && !ActiveContainers.Contains(Slot.Container.GetInstanceID()))
                {
                    Slot.Container.gameObject.SetActive(false);
                    continue;
                }
                PlacementExecution.ActivateAndValidate(Slot, Slot.Container.gameObject);
                if (Deferred && Slot.IsClone)
                {
                    Slot.Segment.segmentParent.SetActive(false);
                    if (Slot.Segment.segmentCampfire) Slot.Segment.segmentCampfire.SetActive(false);
                    if (Slot.PeakAttachment) Slot.PeakAttachment.gameObject.SetActive(false);
                    continue;
                }
                PlacementExecution.ActivateAndValidate(Slot, Slot.Segment.segmentParent);
                if (Slot.Segment.segmentCampfire) PlacementExecution.ActivateAndValidate(Slot, Slot.Segment.segmentCampfire);
                if (Slot.PeakAttachment && !TransformOwnership.Contains(Slot.Container, Slot.PeakAttachment))
                    PlacementExecution.ActivateAndValidate(Slot, Slot.PeakAttachment.gameObject);
                Slot.MarkFeaturesActivated();
            }

            Run.Environment.WaterObjects = Prepared.WaterObjects;
            Run.Environment.AuthoredWaterPositions = Prepared.WaterPositions;
            Run.Features.ClearRootIds();
            Run.Network.ViewBatches.ResetForInitialBatch();
            Run.Network.RunProperties.CancelAll();
            Run.Transitions.ResetState();
            Run.Network.GenerationBarrier.ClearGeneratedNetworkContractHash();
            var StaleBridge = Handler.GetComponent<MapNetworkBridge>();
            if (StaleBridge) UnityEngine.Object.DestroyImmediate(StaleBridge);
            var Bridge = Handler.gameObject.AddComponent<MapNetworkBridge>();
            Run.Network.BindBridge(Bridge);
            if (PhotonNetwork.CurrentRoom is { } room) ReleasedSlotRetirement.HandleReleasedProperties(room.CustomProperties);
            Writer.RequireCurrent();
            Run.Network.GenerationBarrier.BeginGeneration();
            LoadingProgress?.CompleteUnit();
            Plugin.StartRoutine(FinishInitialCommit(
                Run, Writer, Prepared, Handler, Baseline, LoadingProgress));
            Writer = null;
        }
        catch (Exception Exception)
        {
            Run.Network.GenerationBarrier.CompleteGeneration(null);
            try
            {
                throw FailCommit(Run, Exception);
            }
            finally
            {
                Writer?.Dispose();
            }
        }
    }
    static bool InitialLightingGeometryDirty(PreparedMap Prepared)
    {
        if (Prepared.StartKit.LightingGeometryDirty || Prepared.Originals.LightingGeometryDirty) return true;
        foreach (var Position in Prepared.Positions)
            if (Position.Key && Position.Key.position != Position.Value) return true;
        foreach (var Active in Prepared.Originals.Active)
            if (Active.Key && Active.Key.activeSelf != Active.Value) return true;
        for (var i = 0; i < Prepared.WaterObjects.Length; i++)
            if (Prepared.WaterObjects[i]
                && (Prepared.WaterObjects[i].activeSelf != Prepared.WaterStates[i]
                    || Prepared.WaterObjects[i].transform.position != Prepared.WaterPositions[i])) return true;
        var Occurrences = new HashSet<FeatureOccurrence>();
        foreach (var Slot in Prepared.Slots)
        {
            var Occurrence = Slot.Occurrence;
            if (!Slot.IsClone || !Occurrences.Add(Occurrence)) continue;
            foreach (var Root in Occurrence.Roots)
                if (Root && Root.gameObject.activeInHierarchy) return true;
        }
        return false;
    }
    static IEnumerator FinishInitialCommit(
        MapRun Run,
        MapRun.FeatureWriter Writer,
        PreparedMap Prepared,
        MapHandler Handler,
        ViewBatchSynchronization.ViewBatchBaseline Baseline,
        MapLoadingProgress? LoadingProgress)
    {
        var Generation = Run.Features.BeginOperation();
        var Routine = FeatureGeneration.GenerateFeatures(
            0, Run.InitialGenerationSlotCount, Prepared.GeneratedGroupers, Prepared.AddedGroupers,
            Generation, Writer, LoadingProgress);
        Exception? Failure = null;
        var Completed = false;
        var LightingGeometryDirty = false;
        ViewBatchSynchronization.ViewBatch FrozenBatch = null!;
        try
        {
            try
            {
                Writer.RequireCurrent();
                Run.LightVolumes.Capture(Run);
                LightingGeometryDirty = InitialLightingGeometryDirty(Prepared);
            }
            catch (Exception Exception)
            {
                Failure = Exception;
            }
            while (Failure is null)
            {
                object? Current = null;
                bool Moved;
                try
                {
                    Writer.RequireCurrent();
                    Moved = Routine.MoveNext();
                    if (Moved) Current = Routine.Current;
                }
                catch (Exception exception)
                {
                    Moved = false;
                    Failure = exception;
                    break;
                }
                if (!Moved) break;
                yield return Current;
            }
            Failure ??= Generation.Failure;
            LightingGeometryDirty |= Generation.Generated != 0;
            var TrackerSlots = Run.InitialGenerationSlotCount;
            if (Failure is null) try
            {
                Writer.RequireCurrent();
                for (var i = 0; i < TrackerSlots; i++)
                {
                    SpawnTrackerTransaction.AttachLazySpawnTrackers(i);
                    Writer.RequireCurrent();
                }
                MapEnvironmentProjection.SyncWater();
                Writer.RequireCurrent();
                Campfire.Bind();
                Writer.RequireCurrent();
                WeatherProjection.BindEnvironment();
                Writer.RequireCurrent();
                FogRuntime.Bind(Map.Slots, Run.Configuration.Environment);
                var Progress = UnityEngine.Object.FindAnyObjectByType<MountainProgressHandler>();
                if (Progress) ProgressProjection.InitializeProgress(Progress);
                Writer.RequireCurrent();
                if (!LightingGeometryDirty)
                    LightingGeometryDirty = InitialLightingGeometryDirty(Prepared);
                if (TrackerSlots > Map.Slots[0].Occurrence.BatchIndex + 1 && LightingGeometryDirty)
                    Run.LightVolumes.MarkOtherOccurrencesDirty(Run, Run.Slots[0]);
                FrozenBatch = Run.Network.ViewBatches.FreezeLazyViews(0, Baseline, true);
            }
            catch (Exception Exception)
            {
                Failure = Exception;
            }
            if (Failure is null)
            {
                LoadingProgress?.BeginUnit("Processing light volume");
                yield return FeatureGeneration.RunFeatureRoot(
                    new LightVolumeRebake(
                        Run.LightVolumes, Run, Run.Slots[0], LightingGeometryDirty, Writer).Execute(),
                    Generation,
                    Writer);
                Failure ??= Generation.Failure;
                if (Failure is null) LoadingProgress?.CompleteUnit();
            }
            if (Failure is null)
                LoadingProgress?.BeginUnit("Synchronizing generated objects");
            if (Failure is null) try
            {
                Writer.RequireCurrent();
                Run.Network.ViewBatches.SynchronizeViewBatch(0, FrozenBatch);
                Writer.RequireCurrent();
                for (var i = 0; i < TrackerSlots; i++)
                {
                    GloomVoiceRuntime.CommitGloomVoiceClone(i);
                    Writer.RequireCurrent();
                }
                if (PhotonNetwork.CurrentRoom is not null)
                    Run.Network.DeferredOperations.HandleRunProperties(PhotonNetwork.CurrentRoom.CustomProperties);
                Writer.RequireCurrent();
                Completed = true;
            }
            catch (Exception Exception)
            {
                Failure = Exception;
            }
        }
        finally
        {
            try
            {
                (Routine as IDisposable)?.Dispose();
            }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("Map generation and iterator cleanup both failed.", Failure, Exception);
            }
            finally
            {
                if (!Completed && Failure is null) Failure = MapRun.FeatureWriter.Cancelled();
                Exception? GenerationFailure = null;
                try
                {
                    if (Failure is not null) GenerationFailure = FailCommit(Run, Failure);
                }
                catch (Exception Rollback)
                {
                    GenerationFailure = Failure is null
                        ? Rollback
                        : new AggregateException(
                            "Map generation and its failure handling both failed.", Failure, Rollback);
                }
                finally
                {
                    Run.Network.GenerationBarrier.CompleteGeneration(GenerationFailure);
                    Writer.Dispose();
                }
            }
        }
    }
    static Exception FailCommit(MapRun Run, Exception Exception)
    {
        LazySlotGenerator.AbortLazyGeneration(Run, 0, "Map layout commit failed", Exception);
        return Exception;
    }
    static void WriteLayoutSourceFingerprint(
        BinaryWriter Writer,
        LayoutSegmentSource Source,
        IReadOnlyList<LayoutSegmentSource> PlacementSources)
    {
        var Native = Source as INativeLayoutSegmentSource;
        Writer.Write(Native is not null);
        if (Native is not null) Writer.Write((int)Native.NativeRole);
        Writer.Write(Native is not null);
        if (Native is not null) Writer.Write((int)Native.NativeBiome);
        Writer.Write(Source.StartConnector is not null);
        if (Source.StartConnector is not null) Writer.Write(Source.StartConnector);
        ManifestEncoding.WritePose(Writer, Source.RootPose);
        ManifestEncoding.WriteVector(Writer, Source.RootScale);
        Writer.Write(Source.Start.HasValue);
        if (Source.Start.HasValue) ManifestEncoding.WritePose(Writer, Source.Start.Value);
        Writer.Write(Source.EntryPorts.Count);
        foreach (var Port in Source.EntryPorts)
        {
            Writer.Write(Port.Connector);
            ManifestEncoding.WritePose(Writer, Port.Pose);
        }
        Writer.Write(Source.ExitPorts.Count);
        foreach (var Port in Source.ExitPorts)
        {
            Writer.Write(Port.Connector);
            ManifestEncoding.WritePose(Writer, Port.Pose);
        }

        var Peak = Source as PeakAttachmentSource;
        Writer.Write(Peak is not null);
        if (Peak is not null) ManifestEncoding.WritePose(Writer, Peak.HashPose);
        var Ordinary = Source as NativeOrdinarySegmentSource;
        Writer.Write(Ordinary is not null);
        Ordinary?.NadirAmulets.WriteFingerprint(Writer);

        Writer.Write(Peak is not null);
        if (Peak is not null)
        {
            Writer.Write(Peak.BridgeExit.Connector);
            ManifestEncoding.WritePose(Writer, Peak.BridgeExit.Pose);
        }
        Writer.Write(Peak is not null);
        if (Peak is not null) ManifestEncoding.WritePose(Writer, Peak.BridgeContact);
        Pose? OwnerBoundary = Peak?.BridgeOwnerBoundary;
        LayoutPort? SuccessorBoundary = Peak?.BridgeSuccessorBoundary;
        if (Ordinary is { ContextualPeak: { } PassivePeak }
            && !PlacementPlan.Selected(PlacementSources, PassivePeak))
        {
            OwnerBoundary ??= PassivePeak.BridgeOwnerBoundary;
            SuccessorBoundary ??= PassivePeak.BridgeSuccessorBoundary;
        }
        Writer.Write(OwnerBoundary.HasValue);
        if (OwnerBoundary.HasValue) ManifestEncoding.WritePose(Writer, OwnerBoundary.Value);
        Writer.Write(SuccessorBoundary.HasValue);
        if (SuccessorBoundary.HasValue)
        {
            Writer.Write(SuccessorBoundary.Value.Connector);
            ManifestEncoding.WritePose(Writer, SuccessorBoundary.Value.Pose);
        }

        var Nadir = Source as NadirLayoutSegmentSource;
        Writer.Write(Nadir is not null);
        if (Nadir is not null)
        {
            Writer.Write(Nadir.PeakSuccessorEntry.Connector);
            ManifestEncoding.WritePose(Writer, Nadir.PeakSuccessorEntry.Pose);
        }
        Writer.Write(Peak is not null);
        if (Peak is not null)
        {
            ManifestEncoding.WritePose(Writer, Peak.RespawnPose);
            ManifestEncoding.WritePose(Writer, Peak.BridgeCampfire);
        }
        ManifestEncoding.WriteVector(Writer, Peak?.Translation ?? Vector3.zero);
        var PassivePose = FeatureGeneration.PassivePeakPose(PlacementSources, Source);
        Writer.Write(PassivePose.HasValue);
        if (PassivePose.HasValue) ManifestEncoding.WritePose(Writer, PassivePose.Value);

        var SpawnPoints = Nadir?.SpawnPoints ?? Array.Empty<Transform>();
        Writer.Write(SpawnPoints.Count);
        foreach (var SpawnPoint in SpawnPoints)
            ManifestEncoding.WritePose(
                Writer, NativeSegmentPorts.LocalPose(Source.Container, SpawnPoint));
        Writer.Write(Nadir is not null);
        if (Nadir is not null)
        {
            ManifestEncoding.WritePose(
                Writer, NativeSegmentPorts.LocalPose(Source.Container, Nadir.VoidWaterVisual));
            ManifestEncoding.WriteVector(Writer, Nadir.VoidWaterVisual.localScale);
            ManifestEncoding.WritePose(
                Writer, NativeSegmentPorts.LocalPose(Source.Container, Nadir.VoidWaterZone));
            ManifestEncoding.WriteVector(Writer, Nadir.VoidWaterZone.localScale);
        }
        var Custom = Source as CustomLayoutSegmentSource;
        Writer.Write(Custom is not null);
        Custom?.Registration.FinalForce.WriteExternalFingerprint(Writer);
    }
    internal static int LayoutRevision(
        IReadOnlyList<LayoutSegmentSource> Plan,
        IReadOnlyList<PlacementPlan.Placement> Placements,
        IReadOnlyList<PlacementPlan.LayoutLink> Links)
    {
        var Configuration = Map.CurrentRun?.Configuration
            ?? throw new InvalidOperationException("The current map run has no resolved configuration.");
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true), true))
        {
            Writer.Write("PLL/layout/v38");
            Writer.Write(Configuration.Layout.Seed);
            Writer.Write(Configuration.Layout.ReplaceGenerated);
            Writer.Write(Configuration.Environment.NoSkyProps);
            Writer.Write(Configuration.Environment.RelocateStart);
            Writer.Write(Configuration.Environment.RelocatePlane && !Configuration.Environment.NoPlane);
            Writer.Write(Configuration.Environment.NoPlane);
            Writer.Write(SegmentRegistry.ManifestRevision);
            NativeResolverSourceCatalog.WriteSourceFingerprint(Writer);
            Writer.Write(Plan.Count);
            foreach (var Placement in Placements)
            {
                ManifestEncoding.WriteVector(Writer, Placement.Frame.Offset);
                ManifestEncoding.WritePose(
                    Writer,
                    new Pose(Placement.Frame.Pivot, Placement.Frame.Rotation));
                Writer.Write(Placement.Sources.Length);
                foreach (var Source in Placement.Sources)
                {
                    Writer.Write(Source.Id);
                    Writer.Write(Source.Revision);
                    Writer.Write(Source.SegmentTypeId);
                    Writer.Write(Source.BiomeId);
                    WriteLayoutSourceFingerprint(Writer, Source, Placement.Sources);
                }
                Writer.Write(Placement.SupplementalRoots.Length);
                foreach (var Supplemental in Placement.SupplementalRoots)
                    Supplemental.WriteFingerprint(Writer);
            }
            Writer.Write(Links.Count);
            foreach (var Link in Links)
            {
                Writer.Write(Link.Previous.Index);
                Writer.Write(Link.Next.Index);
                Writer.Write(Link.Transition.Connector);
                ManifestEncoding.WritePose(Writer, Link.Transition.Exit);
                ManifestEncoding.WritePose(Writer, Link.Transition.Entry);
            }
        }

        using var Sha = SHA256.Create();
        var Hash = Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
        var Result = Hash[0] | Hash[1] << 8 | Hash[2] << 16 | Hash[3] << 24;
        return Result == 0 ? unchecked((int)0x9E3779B9u) : Result;
    }

}
