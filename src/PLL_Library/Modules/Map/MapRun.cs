using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class MapRun
{
    internal enum Phase { Preparing, Publishing, Active, Releasing, Released }

    internal sealed class GenerationCohort
    {
        readonly int[] actors;

        internal GenerationCohort(string generationEpoch, string finalManifestRevision, int[] actors)
        {
            if (string.IsNullOrEmpty(generationEpoch))
                throw new ArgumentException("An initial generation cohort requires a generation epoch.", nameof(generationEpoch));
            if (string.IsNullOrEmpty(finalManifestRevision))
                throw new ArgumentException(
                    "An initial generation cohort requires a final manifest revision.", nameof(finalManifestRevision));
            if (actors is null) throw new ArgumentNullException(nameof(actors));
            if (actors.Length == 0)
                throw new ArgumentException("An initial generation cohort requires at least one actor.", nameof(actors));
            this.actors = (int[])actors.Clone();
            for (var i = 0; i < this.actors.Length; i++)
                if (this.actors[i] <= 0 || i != 0 && this.actors[i - 1] >= this.actors[i])
                    throw new ArgumentException(
                        "Initial generation cohort actors must be positive, unique, and sorted.", nameof(actors));
            GenerationEpoch = generationEpoch;
            FinalManifestRevision = finalManifestRevision;
        }

        internal string GenerationEpoch { get; }
        internal string FinalManifestRevision { get; }
        internal int Count => actors.Length;
        internal int ActorAt(int index) => actors[index];

        internal bool Matches(int[] candidate)
        {
            if (candidate.Length != actors.Length) return false;
            for (var i = 0; i < actors.Length; i++)
                if (candidate[i] != actors[i]) return false;
            return true;
        }

        internal bool Matches(GenerationCohort Candidate) =>
            ReferenceEquals(this, Candidate) || Matches(Candidate.actors);

        internal int[] SnapshotActors() => (int[])actors.Clone();
    }

    internal sealed class LayoutIdentity
    {
        internal LayoutIdentity(string generationEpoch, int revision)
        {
            if (string.IsNullOrEmpty(generationEpoch))
                throw new ArgumentException("A map layout requires a generation epoch.", nameof(generationEpoch));
            if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
            GenerationEpoch = generationEpoch;
            Revision = revision;
        }

        internal string GenerationEpoch { get; }
        internal int Revision { get; }
    }

    internal sealed class FeatureWriter : IDisposable
    {
        internal sealed class OperationLease : IDisposable
        {
            FeatureWriter? writer;

            internal OperationLease(FeatureWriter writer)
            {
                this.writer = writer;
            }

            public void Dispose()
            {
                var owner = writer;
                if (owner is null) return;
                writer = null;
                owner.ReleaseOperation();
            }
        }

        MapRun? run;
        int operations;
        bool disposeRequested;

        internal FeatureWriter(MapRun run)
        {
            this.run = run;
        }

        internal bool CancellationRequested => disposeRequested || run is null || run.WriterCancellationRequested(this);

        internal void RequireCurrent()
        {
            if (disposeRequested || run is null) throw Cancelled();
            run.RequireWriter(this);
        }

        internal OperationLease RetainOperation()
        {
            RequireCurrent();
            var operation = new OperationLease(this);
            operations = checked(operations + 1);
            return operation;
        }

        internal bool TryTakeRequest(out int index)
        {
            if (disposeRequested || run is null) throw Cancelled();
            return run.TryTakeRequest(this, out index);
        }

        internal void CompleteRequest(int index)
        {
            if (run is null) return;
            run.CompleteRequest(this, index);
        }

        public void Dispose()
        {
            if (disposeRequested) return;
            disposeRequested = true;
            TryRelease();
        }

        internal static OperationCanceledException Cancelled() =>
            new("Map feature generation was cancelled by its owning map run.");

        void ReleaseOperation()
        {
            if (operations <= 0)
                throw new InvalidOperationException("A map feature operation released an unowned writer lease.");
            operations--;
            TryRelease();
        }

        void TryRelease()
        {
            if (!disposeRequested || operations != 0 || run is not { } owner) return;
            run = null;
            owner.ReleaseWriter(this);
        }
    }

    readonly Queue<int> featureRequests = new();
    readonly HashSet<int> requestedFeatures = new();
    static long nextIdentity;
    MapHandler? handler;
    MapSlot[] StagedSlots = Array.Empty<MapSlot>();
    NativeAmuletRecipe? NativeAmulets;
    FeatureWriter? writer;
    Action? deferredRelease;
    bool releaseCleanupStarted;

    internal MapRun(
        MapHandler Handler,
        ResolvedRunConfiguration Configuration,
        MapRunIngress Ingress,
        GenerationCohort? InitialGenerationCohort = null)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        var NativeRun = RunManager.Instance;
        if (!NativeRun) throw new InvalidOperationException("A map run requires PEAK's native RunManager.");
        if (nextIdentity == long.MaxValue)
            throw new InvalidOperationException("Map run identity space is exhausted.");
        Identity = ++nextIdentity;
        this.handler = Handler;
        this.Configuration = Configuration ?? throw new ArgumentNullException(nameof(Configuration));
        this.Ingress = Ingress ?? throw new ArgumentNullException(nameof(Ingress));
        Environment = new MapEnvironmentProjection();
        Weather = new WeatherProjection();
        Progress = new ProgressProjection();
        Retirement = new ReleasedSlotRetirement();
        Transitions = new MapTransitionCoordinator();
        Peak = new PeakSuccessorRuntime(this);
        Quicksave = new MapQuicksave(
            global::Peak.Quicksave.ShouldUseSaveData ? Ingress.LoadedQuicksaveLayout : null);
        Features = new FeatureGeneration();
        NativeSpawns = new NativeSpawnAdmission(this, NativeRun);
        ClimbingFixtures = new NativeClimbingFixtureRuntime(this);
        Network = new MapNetworkSession(this);
        WorldInteractions = new WorldInteractionLedger(this);
        PropInteractions = new PropInteractionInstances(this, GamefeelHandler.instance);
        SurfaceTextures = new SurfaceTextureInstances(this);
        NativeMaterials = new NativeMaterialInstances(this);
        NativeSpawners = new NativeSpawnerInstances(this);
        HookedGeneration = new HookedPropGeneration(this);
        LightVolumes = new LightVolumeRun(Identity);
        SceneHandle = Handler.gameObject.scene.handle;
        this.InitialGenerationCohort = InitialGenerationCohort;
        Lifecycle = Phase.Preparing;
    }

    internal long Identity { get; }
    internal int SceneHandle { get; }
    internal MapHandler? Handler => handler;
    internal ResolvedRunConfiguration Configuration { get; }
    internal MapRunIngress Ingress { get; }
    internal MapEnvironmentProjection Environment { get; }
    internal WeatherProjection Weather { get; }
    internal ProgressProjection Progress { get; }
    internal ReleasedSlotRetirement Retirement { get; }
    internal MapTransitionCoordinator Transitions { get; }
    internal PeakSuccessorRuntime Peak { get; }
    internal MapQuicksave Quicksave { get; }
    internal NativeAmuletRecipe AmuletRecipe(NativeAmuletSourceContract Provider) =>
        NativeAmulets ??= new NativeAmuletRecipe(Provider, Handler!.transform);
    internal FeatureGeneration Features { get; }
    internal NativeSpawnAdmission NativeSpawns { get; }
    internal NativeClimbingFixtureRuntime ClimbingFixtures { get; }
    internal MapNetworkSession Network { get; }
    internal WorldInteractionLedger WorldInteractions { get; }
    internal PropInteractionInstances PropInteractions { get; }
    internal SurfaceTextureInstances SurfaceTextures { get; }
    internal NativeMaterialInstances NativeMaterials { get; }
    internal NativeSpawnerInstances NativeSpawners { get; }
    internal HookedPropGeneration HookedGeneration { get; }
    internal bool PresentationClockReady => InitialGenerationCohort is null || HookedGeneration.PresentationClockReady;
    internal double PresentationClock => InitialGenerationCohort is not null
        ? HookedGeneration.PresentationClock : Time.realtimeSinceStartupAsDouble;
    internal double? ReadPresentationClock() => !HasPublishedLayout || Features.Aborted ? null
        : InitialGenerationCohort is not null ? HookedGeneration.ReadPresentationClock()
        : Time.realtimeSinceStartupAsDouble;
    internal GeneratedReplayBudget ReplayBudget { get; } = new();
    internal bool PendingQuicksave => HookedGeneration.PendingQuicksave || WorldInteractions.HasPending || PropInteractions.HasPendingIntent;
    internal LightVolumeRun LightVolumes { get; }
    internal GenerationCohort? InitialGenerationCohort { get; }
    internal LayoutIdentity? Layout { get; private set; }
    internal MapPreparation? PreparationOwner { get; private set; }
    internal PreparedMap? RollbackOwner { get; private set; }
    internal PreparedMap? PublicationOwner { get; private set; }
    internal MapSlot[] Slots { get; private set; } = Array.Empty<MapSlot>();
    internal int InitialGenerationSlotCount { get; private set; }
    internal bool HasDeferredSuffix => InitialGenerationSlotCount < Slots.Length;
    internal bool IsDeferredSlot(int Index) => Index >= InitialGenerationSlotCount;
    internal Phase Lifecycle { get; private set; }
    internal bool Active => Lifecycle == Phase.Active && Handler && Slots.Length != 0;
    internal bool Publishing => Lifecycle == Phase.Publishing && Handler && Slots.Length != 0;
    internal bool HasPublishedLayout => (Lifecycle == Phase.Publishing || Lifecycle == Phase.Active)
        && Handler && Slots.Length != 0;
    internal bool Releasing => Lifecycle == Phase.Releasing;
    internal bool Released => Lifecycle == Phase.Released;
    internal bool OriginalMapSourcesRetired { get; private set; }
    internal bool SceneGone { get; private set; }
    internal bool FeatureMutationActive => writer is not null;
    internal string GenerationEpoch => Layout?.GenerationEpoch
        ?? throw new InvalidOperationException("The map run has no staged layout identity.");
    internal int LayoutRevision => Layout?.Revision
        ?? throw new InvalidOperationException("The map run has no staged layout revision.");

    internal bool Owns(MapHandler handler) =>
        !Released && this.handler && handler
        && this.handler.GetInstanceID() == handler.GetInstanceID();

    internal bool Matches(long identity, string generationEpoch, int revision) =>
        !Released && Identity == identity && Layout?.Revision == revision
        && string.Equals(Layout.GenerationEpoch, generationEpoch, StringComparison.Ordinal);

    internal bool IsOccurrenceReady(FeatureOccurrence Occurrence)
    {
        var Index = Occurrence.RequestIndex;
        if (!HasPublishedLayout || Features.Aborted || Index >= Slots.Length
            || !ReferenceEquals(Slots[Index].Occurrence, Occurrence)) return false;
        foreach (var Slot in Occurrence.Slots)
            if (Slot.Released || !Slot.FeaturesReady || !Slot.ViewBatchReady) return false;
        return HookedGeneration.CommittedOutputsReady(Occurrence);
    }

    internal MapSlot? SelectedActiveSlot
    {
        get
        {
            if (!Active) return null;
            var Index = Handler!.currentSegment;
            if (Index < 0 || Index >= Slots.Length) return null;
            var Slot = Slots[Index];
            return !Slot.Released && Slot.Container && Slot.Container.gameObject.activeInHierarchy
                && Slot.Segment.segmentParent.activeInHierarchy ? Slot : null;
        }
    }

    internal MapSlot? ReadyCurrentSlot => SelectedActiveSlot is { } Slot
        && IsOccurrenceReady(Slot.Occurrence) && LightVolumes.ProjectionReady(Slot) ? Slot : null;

    internal void AttachPreparation(MapPreparation Preparation)
    {
        if (Lifecycle != Phase.Preparing || PreparationOwner is not null
            || RollbackOwner is not null || Layout is not null
            || !ReferenceEquals(Preparation.Owner, this))
            throw new InvalidOperationException("A map run can only attach its preparation owner once while preparing.");
        PreparationOwner = Preparation;
    }

    internal void RequirePreparation(MapPreparation Preparation)
    {
        if (Lifecycle is Phase.Releasing or Phase.Released || !handler)
            throw new OperationCanceledException("Map preparation was cancelled by its owning map run.");
        if (Lifecycle != Phase.Preparing || !ReferenceEquals(PreparationOwner, Preparation))
            throw new InvalidOperationException("The map preparation transaction is not owned by this preparing map run.");
    }

    internal void ReleasePreparation(Action<Action> ApplyCleanup)
    {
        var Preparation = PreparationOwner;
        if (Preparation is null) return;
        PreparationOwner = null;
        Preparation.Release(ApplyCleanup);
    }

    internal void Stage(MapPreparation Preparation, PreparedMap Prepared, string GenerationEpoch, int Revision)
    {
        if (Lifecycle != Phase.Preparing || !ReferenceEquals(PreparationOwner, Preparation)
            || RollbackOwner is not null || Layout is not null
            || Prepared.Slots.Length == 0 || !Owns(Prepared.Handler))
            throw new InvalidOperationException("A prepared map can only be staged once by its preparing map run.");
        if (InitialGenerationCohort is { } Cohort
            && !string.Equals(Cohort.GenerationEpoch, GenerationEpoch, StringComparison.Ordinal))
            throw new InvalidOperationException("The initial generation cohort belongs to a different generation epoch.");
        var Identity = new LayoutIdentity(GenerationEpoch, Revision);
        Preparation.TransferTo(this);
        RollbackOwner = Prepared;
        Layout = Identity;
        StagedSlots = Prepared.Slots;
        HookedGeneration.Bind(StagedSlots, MapQuicksave.RestoringActivePllQuicksave());
        InitialGenerationSlotCount = Prepared.InitialGenerationSlotCount;
        PreparationOwner = null;
    }

    internal void BeginPublishing()
    {
        if (Lifecycle != Phase.Preparing || RollbackOwner is null || Layout is null || StagedSlots.Length == 0)
            throw new InvalidOperationException("A map run cannot publish without a prepared map layout.");
        Slots = StagedSlots;
        Lifecycle = Phase.Publishing;
        NativeContentTools.BindRun(this);
    }

    internal void Activate()
    {
        RequireActivation();
        PublicationOwner = RollbackOwner;
        RollbackOwner = null;
        StagedSlots = Array.Empty<MapSlot>();
        Lifecycle = Phase.Active;
        foreach (var Slot in Slots)
            if (ReferenceEquals(Slot, Slot.Occurrence.Slots[0])) Slot.Occurrence.PublishActors();
    }

    internal void RequireActivation()
    {
        if (Lifecycle != Phase.Publishing || writer is not null || RollbackOwner is null)
            throw new InvalidOperationException("A map run cannot become active before publication completes.");
    }

    internal void RequirePublished()
    {
        if (!HasPublishedLayout) throw FeatureWriter.Cancelled();
    }

    internal void MarkOriginalMapSourcesRetired()
    {
        if (!Active || OriginalMapSourcesRetired)
            throw new InvalidOperationException("Original map source retirement requires one active map publication.");
        OriginalMapSourcesRetired = true;
    }

    internal void MarkSceneGone() => SceneGone = true;

    internal FeatureWriter AcquireWriter()
    {
        if (Lifecycle != Phase.Publishing && Lifecycle != Phase.Active)
            throw FeatureWriter.Cancelled();
        if (writer is not null)
            throw new InvalidOperationException("A map run already owns a feature writer.");
        writer = new FeatureWriter(this);
        return writer;
    }

    internal FeatureWriter? TryAcquireWriter()
    {
        if (Lifecycle != Phase.Active || writer is not null || featureRequests.Count == 0) return null;
        writer = new FeatureWriter(this);
        return writer;
    }

    internal bool RequestFeature(int index)
    {
        if (Lifecycle != Phase.Active || index < 0 || index >= Slots.Length) return false;
        if (!requestedFeatures.Add(index)) return false;
        featureRequests.Enqueue(index);
        return true;
    }

    internal void BeginRelease()
    {
        if (Lifecycle == Phase.Released || Lifecycle == Phase.Releasing) return;
        Lifecycle = Phase.Releasing;
        NativeContentTools.RunReleased(this);
        HookedGeneration.Cancel();
        featureRequests.Clear();
        requestedFeatures.Clear();
    }

    internal void CompleteRelease()
    {
        if (Lifecycle == Phase.Released) return;
        if (Lifecycle != Phase.Releasing || writer is not null)
            throw new InvalidOperationException("Map run final release requires completed releasing cleanup.");
        var Failures = new List<Exception>();
        void ExecuteCleanup(Action Operation)
        {
            try { Operation(); }
            catch (Exception Exception) { Failures.Add(Exception); }
        }

        ExecuteCleanup(HookedGeneration.Release);
        ExecuteCleanup(() => OccurrenceHeightmapMeshes.ReleaseRun(this));
        ExecuteCleanup(Progress.Release);
        ExecuteCleanup(Weather.Release);
        ExecuteCleanup(Environment.Release);
        ExecuteCleanup(Retirement.ReleaseState);
        ExecuteCleanup(Transitions.ResetState);
        ExecuteCleanup(Peak.ReleaseState);
        ExecuteCleanup(Quicksave.ReleaseState);
        ExecuteCleanup(Features.ClearRootIds);
        ExecuteCleanup(WorldInteractions.Clear);
        ExecuteCleanup(PropInteractions.Clear);
        ExecuteCleanup(SurfaceTextures.Clear);
        ExecuteCleanup(NativeMaterials.Clear);
        ExecuteCleanup(NativeSpawners.Clear);
        ExecuteCleanup(ClimbingFixtures.Clear);
        ExecuteCleanup(NativeSpawns.Clear);
        if (NativeAmulets is not null) ExecuteCleanup(NativeAmulets.Dispose);
        NativeAmulets = null;
        ExecuteCleanup(Network.ReleaseState);
        ExecuteCleanup(LightVolumes.Release);
        ExecuteCleanup(Ingress.ClearTransient);
        deferredRelease = null;
        handler = null;
        PreparationOwner = null;
        RollbackOwner = null;
        PublicationOwner = null;
        StagedSlots = Array.Empty<MapSlot>();
        Slots = Array.Empty<MapSlot>();
        Lifecycle = Phase.Released;
        ExecuteCleanup(() => Map.OnRunReleased(this));

        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0) throw new AggregateException("Map run final release failed.", Failures);
    }

    internal bool DeferRelease(Action release)
    {
        if (Lifecycle != Phase.Releasing || writer is null) return false;
        deferredRelease ??= release;
        return true;
    }

    internal bool TryBeginReleaseCleanup()
    {
        if (Lifecycle != Phase.Releasing)
            throw new InvalidOperationException("Map run cleanup requires the releasing phase.");
        if (releaseCleanupStarted) return false;
        releaseCleanupStarted = true;
        return true;
    }

    bool WriterCancellationRequested(FeatureWriter lease) =>
        !ReferenceEquals(writer, lease) || Lifecycle == Phase.Releasing || Lifecycle == Phase.Released || !handler;

    void RequireWriter(FeatureWriter lease)
    {
        if (WriterCancellationRequested(lease)) throw FeatureWriter.Cancelled();
    }

    bool TryTakeRequest(FeatureWriter lease, out int index)
    {
        RequireWriter(lease);
        if (featureRequests.Count == 0)
        {
            index = -1;
            return false;
        }
        index = featureRequests.Dequeue();
        return true;
    }

    void CompleteRequest(FeatureWriter lease, int index)
    {
        if (!ReferenceEquals(writer, lease)) return;
        requestedFeatures.Remove(index);
    }

    void ReleaseWriter(FeatureWriter Lease)
    {
        if (!ReferenceEquals(writer, Lease)) return;
        writer = null;
        var Release = deferredRelease;
        deferredRelease = null;
        Release?.Invoke();
    }
}
