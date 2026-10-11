using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class MapPreparation
{
    enum PreparationPhase { Preparing, Transferred, Released }

    readonly bool RestoringQuicksave;
    readonly OriginalMapSources.StartupSuppression? InitialSuppression;
    readonly List<MapSlot> CreatedSlots = new();
    readonly Dictionary<Transform, Vector3> Positions = new();
    readonly HashSet<Transform> Reused = new();
    readonly MapEnvironmentProjection.EnvironmentState Environment = new();
    readonly List<GameObject> CreatedObjects = new();
    readonly List<GloomVoiceRuntime.GloomProfileClone> CreatedGloomProfiles = new();
    GameObject[] WaterObjects = Array.Empty<GameObject>();
    bool[] WaterStates = Array.Empty<bool>();
    Vector3[] WaterPositions = Array.Empty<Vector3>();
    NativeFixtures.StartKit? StartKit;
    AsyncInstantiateOperation<GameObject>? PendingNativeClone;
    PreparationPhase Lifecycle;

    internal MapPreparation(
        MapRun Owner,
        MapHandler Handler,
        LevelGeneration Level,
        NativeSegmentCatalog NativeCatalog,
        OriginalMapSources Originals,
        bool RestoringQuicksave,
        OriginalMapSources.StartupSuppression? InitialSuppression)
    {
        this.Owner = Owner;
        this.Handler = Handler;
        this.Level = Level;
        this.NativeCatalog = NativeCatalog;
        this.Originals = Originals;
        this.RestoringQuicksave = RestoringQuicksave;
        this.InitialSuppression = InitialSuppression;
    }

    internal MapRun Owner { get; }
    internal MapHandler Handler { get; }
    internal LevelGeneration Level { get; }
    internal NativeSegmentCatalog NativeCatalog { get; }
    internal OriginalMapSources Originals { get; }

    internal IEnumerator Start(MapLoadingProgress? LoadingProgress)
    {
        var Process = StartCore(LoadingProgress);
        Exception? Failure = null;
        var Completed = false;
        try
        {
            while (true)
            {
                object? Current = null;
                bool Moved;
                try
                {
                    Moved = Process.MoveNext();
                    if (Moved) Current = Process.Current;
                    else Completed = true;
                }
                catch (Exception Exception)
                {
                    Moved = false;
                    Failure = Exception;
                }
                if (!Moved) break;
                yield return Current;
            }
        }
        finally
        {
            var Interrupted = Failure is null && !Completed;
            try { (Process as IDisposable)?.Dispose(); }
            catch (Exception Exception)
            {
                Failure = Failure is null ? Exception
                    : new AggregateException("Map preparation and iterator cleanup both failed.", Failure, Exception);
            }
            if (Lifecycle == PreparationPhase.Preparing) try
            {
                if (Failure is not null && Failure is not OperationCanceledException)
                    LazySlotGenerator.AbortLazyGeneration(Owner, 0, "Map layout preparation failed", Failure);
                else Map.ReleaseRun(Owner);
            }
            catch (Exception Exception)
            {
                Failure = Failure is null ? Exception
                    : new AggregateException(
                        "Map preparation and owned-state cleanup both failed.", Failure, Exception);
            }
            if (Interrupted && Failure is not null) ExceptionPropagation.Throw(Failure);
        }

        if (Failure is null) yield break;
        ExceptionPropagation.Throw(Failure);
    }

    IEnumerator StartCore(MapLoadingProgress? LoadingProgress)
    {
        Owner.RequirePreparation(this);
        FogRuntime.Capture(Handler);
        var AuthoredTerrain = new HashSet<int>();
        var AuthoredCampfires = new HashSet<int>();
        FeatureGeneration.CaptureAuthoredFeatures(Handler, AuthoredTerrain, AuthoredCampfires);
        var Configuration = Owner.Configuration;
        var Catalog = NativeCatalog.IncludeRegisteredSegments();
        BiomeMetadataLayoutSolver.ValidateGroupConfiguration(Catalog, Configuration.Biomes);
        var Sources = PlacementPlan.CatalogSources(Catalog);
        var NativeCatalogSegments = new HashSet<MapHandler.MapSegment>();
        foreach (var Source in Sources)
            if (Source is NativeOrdinarySegmentSource Native)
                NativeCatalogSegments.Add(Native.AuthoredSegment);
        var NativeVolcano = NativeCatalog.RequireOrdinary("Volcano");
        var NativeVolcanoParent = NativeVolcano.SegmentParent.transform;
        var Mode = Configuration.Mode;
        var Sequence = Mode == MapLayoutMode.Generative
            ? GenerativeMap.CreateSequence(Catalog, Configuration.Biomes, Configuration.Layout.Seed)
            : LayoutRegistry.ResolveEffectiveOrder(
                Catalog,
                Mode == MapLayoutMode.Ordered
                    ? Configuration.Layout.SelectedLayoutId.IsDefault
                        ? Configuration.Layout.Order
                        : Configuration.Layout.SelectedLayoutId.Value
                    : null,
                () => Mode == MapLayoutMode.Ordered
                    ? BiomeMetadataLayoutSolver.CreateConfiguredSequence(
                        Catalog, Configuration.Layout.Order, "Configured map layout")
                    : BiomeMetadataLayoutSolver.CreateLockedSequence(
                        Catalog, PlacementPlan.CurrentLayout(Catalog, Handler), "PEAK's current map layout"),
                !Configuration.Layout.IsLibraryBase);
        Sequence = InfiniteMap.Apply(Catalog, Sequence, Configuration);
        var Plan = BiomeMetadataLayoutSolver.Resolve(Sequence);
        if (Plan.Count == 0)
        {
            if (RestoringQuicksave)
                throw MapQuicksave.RejectQuicksave("the restored PLL layout plan is empty.");
            Map.ReleaseRun(Owner);
            yield break;
        }
        PlacementPlan.ApplyMiniRunTerminal(Plan);
        LayoutPlan.AttachTerminalPeak(
            Plan,
            NativeCatalog.PeakPrototype,
            Configuration.Terminal.PeakAtEnd,
            Configuration.Terminal.GemlessNadir,
            Configuration.Terminal.ValidNadir,
            Sequence.Description);
        var NadirScoutsHonor = LayoutPlan.ResolveNadirScoutsHonor(
            Plan, Configuration.Terminal.GemlessNadir,
            Configuration.Terminal.ValidNadir, Sequence.Description);
        var AuthoredContainers = new HashSet<Transform>();
        foreach (var Source in Sources) AuthoredContainers.Add(Source.Container);
        var NativeCampfireSurfaces =
            new Dictionary<LayoutSegmentSource, NativeTerrainMapProxy.ExternalSupport>();
        for (var PlanIndex = 0; PlanIndex < Plan.Count; PlanIndex++)
        {
            var Source = Plan[PlanIndex];
            if (FeatureGeneration.NativeCaldera(Source) && PlacementPlan.IsNativeMountainPair(Plan, PlanIndex)
                && !NativeCampfireSurfaces.ContainsKey(Source))
                NativeCampfireSurfaces.Add(
                    Source, NativeTerrainMapProxy.ResolveExternalSupport(Source, AuthoredContainers));
        }
        var AuthoredBeach = NativeCatalog.RequireOrdinary("Beach");
        NativeFixtures.FindStartKitRoots(AuthoredBeach, out var StartPlane, out var StartSpawns, out var StartAnchor);
        var ResolvedStart = NativePlacement.ResolveLayoutStart(Plan[0], StartSpawns, StartAnchor);
        var FirstStartPose = ResolvedStart.Pose;
        foreach (var Source in Sources)
        {
            if (Source is not CustomLayoutSegmentSource and not NadirLayoutSegmentSource
                && !Originals.Active.ContainsKey(Source.Container.gameObject))
                Originals.Active.Add(Source.Container.gameObject, Source.Container.gameObject.activeSelf);
            if (Source is not NadirLayoutSegmentSource Nadir) continue;
            PlacementExecution.RememberActive(Originals.Active, Nadir.VoidWaterVisual.gameObject);
            PlacementExecution.RememberActive(Originals.Active, Nadir.VoidWaterZone.gameObject);
        }

        var Placements = PlacementPlan.CreatePlacements(
            Plan, Sources, Originals, StartPlane, StartSpawns, out var Links, out var SegmentCount);
        var InitialGenerationSlotCount = SegmentCount;
        if (Configuration.LazyGeneration)
            InitialGenerationSlotCount = Placements.Count == 1 ? SegmentCount : Placements[1].Index;
        else
            foreach (var Placement in Placements)
                if (Catalog.TryGetGroupOwner(Placement.Sources[0], out _))
                {
                    InitialGenerationSlotCount = Math.Max(1, Placement.Index);
                    break;
                }
        var InitialGenerationOccurrences = 0;
        foreach (var Placement in Placements)
            if (Placement.Index < InitialGenerationSlotCount) InitialGenerationOccurrences++;
        LoadingProgress?.SetPlan(Placements.Count, InitialGenerationOccurrences);
        MapQuicksave.ValidateQuicksaveSlotCount(SegmentCount);
        var Revision = MapPublication.LayoutRevision(Plan, Placements, Links);
        MapQuicksave.ValidateQuicksaveLayoutRevision(Revision);
        var CloneParent = Handler.globalParent ? Handler.globalParent : Handler.transform;
        var FirstStart = PlacementPlan.PlacedPosition(Placements[0], Plan[0], FirstStartPose);

        var Candidates = new List<int>(Placements.Count);
        for (var i = 0; i < Placements.Count; i++) Candidates.Add(i);
        Candidates.Sort((Left, Right) =>
        {
            var Order = PlacementExecution.Depth(Placements[Left].Container).CompareTo(
                PlacementExecution.Depth(Placements[Right].Container));
            return Order != 0 ? Order : Placements[Left].Index.CompareTo(Placements[Right].Index);
        });
        foreach (var Candidate in Candidates)
        {
            var Placement = Placements[Candidate];
            if (PlacementPlan.AlwaysClone(Placement)
                || !PlacementPlan.CanReuseAuthoredFeatures(
                    Placement,
                    AuthoredTerrain,
                    AuthoredCampfires,
                    Configuration.Layout.ReplaceGenerated, Configuration.Biomes)) continue;
            var Overlaps = false;
            foreach (var Container in Reused)
            {
                if (!TransformOwnership.Overlaps(Placement.Container, Container)) continue;
                Overlaps = true;
                break;
            }
            if (Overlaps) continue;
            Placement.Reuse = true;
            Reused.Add(Placement.Container);
        }

        StartKit = NativeFixtures.CaptureStartKit(
            CloneParent, StartPlane, StartSpawns, StartAnchor, Configuration.Environment);
        var NativeCloneSources = new List<Transform>();
        var SeenNativeCloneSources = new HashSet<int>();
        foreach (var Placement in Placements)
            if (Placement.Sources[0] is not CustomLayoutSegmentSource
                && SeenNativeCloneSources.Add(Placement.Container.GetInstanceID()))
                NativeCloneSources.Add(Placement.Container);
        Originals.HoldCloneSourcesInactive(NativeCloneSources);
        InitialSuppression?.TransferTo(Originals, NativeCloneSources);
        yield return null;
        Owner.RequirePreparation(this);

        foreach (var Placement in Placements)
        {
            Owner.RequirePreparation(this);
            if (Placement.Reuse) continue;
            LoadingProgress?.BeginUnit($"Placing {Placement.Sources[0].Id}");
            var RestoreStartKit = StartKit.OwnedBy(Placement.Container);
            var Custom = Placement.Sources[0] is CustomLayoutSegmentSource;
            GameObject? NativeClone = null;
            Exception? PlacementFailure = null;
            if (RestoreStartKit) StartKit.Detach();
            if (!Custom)
            {
                var OriginalRoot = Placement.Container.gameObject;
                try
                {
                    PendingNativeClone = UnityEngine.Object.InstantiateAsync(OriginalRoot, CloneParent);
                }
                catch (Exception Exception)
                {
                    PlacementFailure = Exception;
                }
                if (RestoreStartKit) try { StartKit.Restore(); }
                catch (Exception Exception)
                {
                    PlacementFailure = PlacementFailure is null
                        ? Exception
                        : new AggregateException(
                            "Native map clone creation and start-kit restoration both failed.",
                            PlacementFailure, Exception);
                }
                if (PlacementFailure is not null) ExceptionPropagation.Throw(PlacementFailure);

                var Operation = PendingNativeClone
                    ?? throw new InvalidOperationException("Native map clone operation was not created.");
                while (!Operation.isDone)
                {
                    yield return null;
                    Owner.RequirePreparation(this);
                }
                NativeClone = Operation.Result[0];
            }
            try
            {
                Placement.Slots = PlacementExecution.Place(
                    Placement.Sources, Sources, Placement.Index, SegmentCount, InitialGenerationSlotCount,
                    NadirScoutsHonor, Placement.Frame,
                    Placement.Container, Placement.SupplementalRoots, NativeVolcanoParent,
                    NativeCampfireSurfaces, CloneParent, NativeClone,
                    AuthoredTerrain, AuthoredCampfires, Originals, Positions, Environment, CreatedObjects,
                    CreatedGloomProfiles, out var Respawn);
                Placement.AttachmentRespawn = Respawn;
                CreatedSlots.AddRange(Placement.Slots);
            }
            catch (Exception Exception)
            {
                PlacementFailure = Exception;
            }
            if (Custom && RestoreStartKit) try { StartKit.Restore(); }
            catch (Exception Exception)
            {
                PlacementFailure = PlacementFailure is null
                    ? Exception
                    : new AggregateException(
                        "Map placement and start-kit restoration both failed.", PlacementFailure, Exception);
            }
            if (PlacementFailure is not null) ExceptionPropagation.Throw(PlacementFailure);
            if (!Custom) PendingNativeClone = null;
            yield return null;
            LoadingProgress?.CompleteUnit();
        }

        Owner.RequirePreparation(this);
        WaterObjects = MapEnvironmentProjection.FindWaterObjects(Handler);
        WaterStates = new bool[WaterObjects.Length];
        WaterPositions = new Vector3[WaterObjects.Length];
        for (var i = 0; i < WaterObjects.Length; i++)
        {
            WaterStates[i] = WaterObjects[i].activeSelf;
            WaterPositions[i] = WaterObjects[i].transform.position;
        }
        Environment.CaptureWater(WaterObjects);
        StartKit.Detach();

        foreach (var Placement in Placements)
        {
            if (!Placement.Reuse) continue;
            LoadingProgress?.BeginUnit($"Placing {Placement.Sources[0].Id}");
            Placement.Slots = PlacementExecution.Place(
                Placement.Sources, Sources, Placement.Index, SegmentCount, InitialGenerationSlotCount,
                NadirScoutsHonor, Placement.Frame,
                Placement.Container, Placement.SupplementalRoots, NativeVolcanoParent,
                NativeCampfireSurfaces, CloneParent, null,
                AuthoredTerrain, AuthoredCampfires, Originals, Positions, Environment, CreatedObjects,
                CreatedGloomProfiles, out var Respawn);
            Placement.AttachmentRespawn = Respawn;
            CreatedSlots.AddRange(Placement.Slots);
            LoadingProgress?.CompleteUnit();
        }

        var Ordered = new List<MapSlot>(SegmentCount);
        foreach (var Placement in Placements) Ordered.AddRange(Placement.Slots);
        if (Ordered.Count != SegmentCount)
            throw new InvalidOperationException("The placed segment count did not match the explicit layout plan.");
        Transform? TerminalRespawn = null;
        foreach (var Slot in Ordered)
        {
            if (!Slot.PeakAttachment) continue;
            if (TerminalRespawn)
                throw new InvalidOperationException("The placed layout contains multiple Peak respawn anchors.");
            TerminalRespawn = Slot.PeakRespawn;
        }

        StartKit.Place(Ordered[0], ResolvedStart);
        if (!Configuration.Environment.RelocatePlane && !Configuration.Environment.NoPlane)
        {
            MapSlot? Beach = null;
            foreach (var Slot in Ordered)
                if (Slot.Source.Id == "Beach")
                {
                    Beach = Slot;
                    break;
                }
            if (Beach is null) StartKit.Plane.Value.gameObject.SetActive(false);
            else StartKit.Plane.Bind(Beach.Segment.segmentParent.transform, Beach.Offset);
        }

        PlacementPlan.BindIncomingAnchors(Links, CreatedObjects);
        PlacementExecution.ValidateTransitions(Ordered);
        if (!Configuration.Environment.NoWater
            && Configuration.Environment.WaterFollowsBeach) MapEnvironmentProjection.ValidateWater(Ordered);

        Originals.DeactivateUnused(Reused);
        if (Configuration.Environment.NoWater)
        {
            foreach (var Value in WaterObjects) Value.SetActive(false);
        }
        else
        {
            var Delta = Configuration.Environment.WaterFollowsBeach
                ? Plan[0].Id == "Beach" ? Ordered[0].Offset : Vector3.zero
                : FirstStart - AuthoredBeach.AuthoredPosition(
                    AuthoredBeach.Start
                    ?? throw new InvalidOperationException("PEAK's Beach Start port was not found."));
            MapEnvironmentProjection.MoveWater(WaterObjects, WaterPositions, Delta);
            MapEnvironmentProjection.SetActive(
                WaterObjects,
                !Configuration.Environment.WaterFollowsBeach || Plan[0].Id == "Beach");
        }

        var Prepared = new PreparedMap(
            Handler, Level, Ordered.ToArray(), InitialGenerationSlotCount, Originals, Positions, Reused,
            WaterObjects, WaterStates, WaterPositions, Environment, TerminalRespawn,
            NativeCatalogSegments, StartKit, CreatedObjects, CreatedGloomProfiles);
        NativePeakCampfireSource? PeakCampfireSource = null;
        foreach (var Slot in Prepared.Slots)
            if (Slot.BridgeTarget is not null)
            {
                PeakCampfireSource = NativeCatalog.CapturePeakCampfireSource();
                break;
            }
        Owner.Peak.Configure(Prepared, NativeCatalog.CampfireCheckpoint, PeakCampfireSource);
        Owner.Stage(this, Prepared, Configuration.Protocol.GenerationEpoch, Revision);
    }

    internal void TransferTo(MapRun Run)
    {
        if (Lifecycle != PreparationPhase.Preparing || !ReferenceEquals(Owner, Run)
            || PendingNativeClone is not null)
            throw new InvalidOperationException("Map preparation can only transfer once to its owning map run.");
        Run.RequirePreparation(this);
        Lifecycle = PreparationPhase.Transferred;
    }

    internal void Release(Action<Action> ApplyCleanup)
    {
        if (Lifecycle == PreparationPhase.Released) return;
        if (Lifecycle != PreparationPhase.Preparing)
            throw new InvalidOperationException("Transferred map preparation state cannot be released as unpublished work.");
        Lifecycle = PreparationPhase.Released;
        ApplyCleanup(ReleasePendingNativeClone);
        ApplyCleanup(() => MapPublication.DestroyImmediate(CreatedObjects));
        ApplyCleanup(() => Originals.Restore(Positions, ResumeNativeSources: !Owner.Features.Aborted));
        if (StartKit is not null) ApplyCleanup(StartKit.Restore);
        ApplyCleanup(RestoreWaterPositions);
        ApplyCleanup(RestoreWaterStates);
        ApplyCleanup(Environment.Restore);
        ApplyCleanup(() => MapPublication.Destroy(CreatedSlots));
        ApplyCleanup(() => GloomVoiceRuntime.DestroyGloomProfiles(CreatedGloomProfiles));
    }

    void ReleasePendingNativeClone()
    {
        var Operation = PendingNativeClone;
        PendingNativeClone = null;
        if (Operation is null) return;
        if (!Operation.isDone)
        {
            Operation.Cancel();
            return;
        }
        MapPublication.DestroyImmediate(Operation.Result);
    }

    void RestoreWaterPositions()
    {
        for (var i = 0; i < WaterObjects.Length; i++)
            if (WaterObjects[i]) WaterObjects[i].transform.position = WaterPositions[i];
    }

    void RestoreWaterStates()
    {
        for (var i = 0; i < WaterObjects.Length; i++)
            if (WaterObjects[i]) WaterObjects[i].SetActive(WaterStates[i]);
    }
}
