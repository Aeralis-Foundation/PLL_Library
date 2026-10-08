using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class OriginalMapSources
{
    internal sealed class StartupSuppression : IDisposable
    {
        Dictionary<GameObject, bool>? Active;

        internal StartupSuppression(MapHandler Handler)
        {
            var States = new Dictionary<GameObject, bool>();
            Active = States;
            try
            {
                for (var Index = 1; Index < Handler.segments.Length; Index++)
                {
                    var Segment = Handler.segments[Index];
                    Capture(Segment.segmentParent);
                    var Campfire = Segment.segmentCampfire;
                    if (Campfire) Capture(Campfire);
                }
                foreach (var Root in States.Keys)
                    SpecialDayProjection.DeactivatePreservingGlobals(Root);
            }
            catch (Exception Failure)
            {
                try { Dispose(); }
                catch (Exception CleanupFailure)
                {
                    throw new AggregateException(
                        "Native source suppression and restoration both failed.", Failure, CleanupFailure);
                }
                throw;
            }

            void Capture(GameObject Root)
            {
                if (!States.ContainsKey(Root)) States.Add(Root, Root.activeSelf);
            }
        }

        internal void TransferTo(
            OriginalMapSources Owner, IReadOnlyList<Transform> HeldContainers)
        {
            var States = Active
                ?? throw new InvalidOperationException("Native source suppression already transferred its state.");
            foreach (var State in States)
            {
                if (!State.Key) continue;
                foreach (var Container in HeldContainers)
                    if (State.Key.transform != Container
                        && TransformOwnership.Contains(Container, State.Key.transform))
                    {
                        State.Key.SetActive(State.Value);
                        break;
                    }
            }
            foreach (var State in Owner.Active) States.TryAdd(State.Key, State.Value);
            Owner.Active = States;
            Active = null;
        }

        public void Dispose() => Release();

        internal void Release(bool ResumeNativeSources = true)
        {
            var States = Active;
            Active = null;
            if (States is null) return;
            List<Exception>? Failures = null;
            foreach (var State in States)
                try
                {
                    if (!State.Key) continue;
                    if (ResumeNativeSources) State.Key.SetActive(State.Value);
                    else SpecialDayProjection.DeactivatePreservingGlobals(State.Key);
                }
                catch (Exception Failure) { (Failures ??= new()).Add(Failure); }
            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is not null)
                throw new AggregateException("Native source restoration failed.", Failures);
        }
    }

    enum VoidControllerPhase : byte
    {
        Native,
        OmittedPrepared, RetainedPrepared, TransferredPrepared,
        OmittedPublished, RetainedPublished, TransferredPublished,
        OmittedSourceRetained, OmittedRetired, TransferredRetired, Released
    }

    sealed class VoidControllerRollback
    {
        internal VoidControllerRollback(
            MapHandler.MapSegment Segment,
            Transform[] SpawnPoints,
            Peak.VoidBiome Singleton,
            bool ControllerActive,
            bool ControllerRootActive,
            bool ContentActive,
            bool SelectorEnabled,
            int SoulFreedStatus)
        {
            this.Segment = Segment;
            this.SpawnPoints = SpawnPoints;
            this.Singleton = Singleton;
            this.ControllerActive = ControllerActive;
            this.ControllerRootActive = ControllerRootActive;
            this.ContentActive = ContentActive;
            this.SelectorEnabled = SelectorEnabled;
            this.SoulFreedStatus = SoulFreedStatus;
        }

        internal MapHandler.MapSegment Segment { get; }
        internal Transform[] SpawnPoints { get; }
        internal Peak.VoidBiome Singleton { get; }
        internal bool ControllerActive { get; }
        internal bool ControllerRootActive { get; }
        internal bool ContentActive { get; }
        internal bool SelectorEnabled { get; }
        internal int SoulFreedStatus { get; }
    }

    internal enum VoidSegmentPublication : byte { Omitted, Retained, Transferred }

    sealed class RetirementUnit
    {
        internal RetirementUnit(Biome Owner)
        {
            this.Owner = Owner;
        }

        internal Biome Owner { get; }
        internal List<Transform> Sources { get; } = new();
    }

    readonly int SceneHandle;
    readonly RetirementUnit[] BiomeRetirementUnits;
    readonly Peak.VoidBiome VoidController;
    readonly int VoidControllerId;
    readonly Transform OriginalVoidContent;
    readonly VariantObjectSelector? VoidSelector;
    MapHandler.MapSegment? PublishedVoidSegment;
    Transform[]? PublishedVoidFallback;
    VoidControllerRollback? VoidRollback;
    VoidControllerPhase VoidPhase;
    int ActiveNadirIndex = -1;
    Transform? FullPeakRoot;
    Pose FullPeakPose;
    Peak.ScoutStatue? BridgeStatue;

    OriginalMapSources(
        int SceneHandle,
        RetirementUnit[] BiomeRetirementUnits,
        Peak.VoidBiome VoidController,
        Transform OriginalVoidContent,
        VariantObjectSelector? VoidSelector)
    {
        this.SceneHandle = SceneHandle;
        this.BiomeRetirementUnits = BiomeRetirementUnits;
        this.VoidController = VoidController;
        VoidControllerId = VoidController.GetInstanceID();
        this.OriginalVoidContent = OriginalVoidContent;
        this.VoidSelector = VoidSelector;
        OriginalVoidSegment = VoidController.segment
            ?? throw new InvalidOperationException("PEAK's native VOID controller segment is unavailable.");
    }

    internal Dictionary<GameObject, bool> Active { get; private set; } = new();
    internal MapHandler.MapSegment OriginalVoidSegment { get; }
    internal VoidSegmentPublication VoidPublication => VoidPhase switch
    {
        VoidControllerPhase.OmittedPublished => VoidSegmentPublication.Omitted,
        VoidControllerPhase.RetainedPublished => VoidSegmentPublication.Retained,
        VoidControllerPhase.TransferredPublished => VoidSegmentPublication.Transferred,
        _ => throw new InvalidOperationException("The VOID controller has not published a segment policy.")
    };
    internal bool LightingGeometryDirty => FullPeakRoot
        && (FullPeakRoot.position != FullPeakPose.position
            || Math.Abs(Quaternion.Dot(FullPeakRoot.rotation, FullPeakPose.rotation)) < 0.999999f);

    internal static OriginalMapSources Capture(
        MapHandler Handler,
        IReadOnlyDictionary<string, LayoutSegmentSource> Catalog)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        NadirLayoutSegmentSource? Nadir = null;
        foreach (var Source in Catalog.Values)
        {
            if (Source is not NadirLayoutSegmentSource Candidate) continue;
            if (Nadir is not null && !ReferenceEquals(Nadir, Candidate))
                throw new InvalidOperationException("PEAK's native Nadir catalog ownership is ambiguous.");
            Nadir = Candidate;
        }
        if (Nadir is null || !Nadir.Container || !Nadir.Container.parent)
            throw new InvalidOperationException("PEAK's native VOID controller is unavailable.");
        var VoidRoot = Nadir.Container.parent;
        var VoidControllers = VoidRoot.GetComponents<Peak.VoidBiome>();
        if (VoidControllers.Length != 1
            || VoidRoot.GetInstanceID() != VoidControllers[0].transform.GetInstanceID()
            || !ReferenceEquals(VoidControllers[0].segment, Nadir.AuthoredSegment))
            throw new InvalidOperationException("PEAK's native VOID controller ownership is ambiguous.");
        var Selectors = VoidRoot.GetComponents<VariantObjectSelector>();
        var VoidSelector = Selectors.Length == 1 ? Selectors[0] : null;

        var ByOwner = new Dictionary<int, RetirementUnit>();
        foreach (var Source in Catalog.Values)
        {
            if (Source is CustomLayoutSegmentSource || Source is PeakAttachmentSource || Source is NadirLayoutSegmentSource) continue;
            if (!Source.Container || !Source.SegmentParent
                || !TransformOwnership.Contains(Source.Container, Source.SegmentParent.transform))
                throw new InvalidOperationException($"PEAK's native {Source.Id} source lost its ordinary Biome ownership.");
            var Owner = BiomeOwner(Source.Container);
            if (TransformOwnership.Overlaps(Owner.transform, VoidRoot))
                throw new InvalidOperationException($"PEAK's native {Source.Id} source overlaps the VOID controller.");
            var Selector = Owner.transform.parent;
            if (!Selector || Selector.GetComponents<BiomeSelector>().Length != 1 || !Selector.parent
                || Selector.parent.GetInstanceID() != Handler.transform.GetInstanceID()
                || !Selector.name.StartsWith("Biome_", StringComparison.Ordinal))
                throw new InvalidOperationException($"PEAK's native {Source.Id} Biome selector shell is ambiguous.");
            var OwnerId = Owner.GetInstanceID();
            if (!ByOwner.TryGetValue(OwnerId, out var Unit))
            {
                Unit = new RetirementUnit(Owner);
                ByOwner.Add(OwnerId, Unit);
            }
            if (!Unit.Sources.Contains(Source.Container)) Unit.Sources.Add(Source.Container);
        }
        if (ByOwner.Count == 0)
            throw new InvalidOperationException("PEAK's ordinary native Biome owners are unavailable.");
        var Units = new List<RetirementUnit>(ByOwner.Values);
        Units.Sort((Left, Right) => Left.Owner.GetInstanceID().CompareTo(Right.Owner.GetInstanceID()));
        GameObject? EdgeWalls = null;
        foreach (var Root in Handler.gameObject.scene.GetRootGameObjects())
        {
            if (Root.name != "Misc") continue;
            foreach (Transform Child in Root.transform)
            {
                if (Child.name != "EdgeWalls") continue;
                if (EdgeWalls)
                    throw new InvalidOperationException("PEAK's original edge-wall root is ambiguous.");
                EdgeWalls = Child.gameObject;
            }
        }
        if (!EdgeWalls)
            throw new InvalidOperationException("PEAK's original edge-wall root is unavailable.");
        var Originals = new OriginalMapSources(
            Handler.gameObject.scene.handle,
            Units.ToArray(),
            VoidControllers[0],
            Nadir.Container,
            VoidSelector);
        Originals.Active.Add(EdgeWalls!, EdgeWalls!.activeSelf);
        return Originals;
    }

    void DeactivateVoidController()
    {
        if (VoidController.isActive) VoidController.Deactivate();
        else VoidController.gameObject.SetActive(false);
    }

    internal void ConfigureVoidController(
        IReadOnlyList<MapSlot> Slots,
        IReadOnlyCollection<Transform> Reused)
    {
        if (Slots is null) throw new ArgumentNullException(nameof(Slots));
        if (Reused is null) throw new ArgumentNullException(nameof(Reused));
        if (VoidPhase is not VoidControllerPhase.Native)
            throw new InvalidOperationException("The VOID controller publication was configured more than once.");
        var HasPlacedNadir = false;
        foreach (var Slot in Slots)
            if (Slot.Source is NadirLayoutSegmentSource)
            {
                HasPlacedNadir = true;
                break;
            }
        if (!HasPlacedNadir)
        {
            VoidPhase = VoidControllerPhase.OmittedPrepared;
            return;
        }
        VoidPhase = VoidControllerPhase.RetainedPrepared;
        var Terminal = Slots[Slots.Count - 1];
        if (!Terminal.IsClone || Terminal.Source is not NadirLayoutSegmentSource
            || ReferenceEquals(Terminal.Segment, OriginalVoidSegment)
            || TransformOwnership.Overlaps(Terminal.Container, OriginalVoidContent)
            || Terminal.PlacedNadir is not { } TerminalNadir)
            return;
        foreach (var Container in Reused)
        {
            if (!Container)
                throw new InvalidOperationException("A reused native source container was destroyed before publication.");
            if (TransformOwnership.Overlaps(OriginalVoidContent, Container)) return;
        }
        if (ForceSpawnCatalog.RequiresNativeSource(OriginalVoidContent)) return;
        PublishedVoidSegment = Terminal.Segment;
        PublishedVoidFallback = (Transform[])TerminalNadir.SpawnPoints.Clone();
        VoidPhase = VoidControllerPhase.TransferredPrepared;
    }
    internal void PublishVoidController()
    {
        var Publication = VoidPhase switch
        {
            VoidControllerPhase.OmittedPrepared => VoidSegmentPublication.Omitted,
            VoidControllerPhase.RetainedPrepared => VoidSegmentPublication.Retained,
            VoidControllerPhase.TransferredPrepared => VoidSegmentPublication.Transferred,
            _ => throw new InvalidOperationException("The VOID controller has no prepared publication policy.")
        };
        var CurrentSingleton = Peak.VoidBiome.instance;
        if (Publication is VoidSegmentPublication.Transferred
                && (PublishedVoidSegment is null || PublishedVoidFallback is null)
            || !VoidController || !OriginalVoidContent || !CurrentSingleton
            || CurrentSingleton.GetInstanceID() != VoidControllerId)
            throw new InvalidOperationException("The prepared VOID controller publication is unavailable.");
        var OriginalControllerSegment = VoidController.segment
            ?? throw new InvalidOperationException("PEAK's original VOID controller segment is unavailable.");
        var OriginalControllerSpawnPoints = VoidController.spawnPoints is { Length: 4 } SpawnPoints
            ? SpawnPoints
            : throw new InvalidOperationException("PEAK's original VOID controller spawn formation is unavailable.");
        VoidRollback = new VoidControllerRollback(
            OriginalControllerSegment,
            OriginalControllerSpawnPoints,
            CurrentSingleton,
            VoidController.isActive,
            VoidController.gameObject.activeSelf,
            OriginalVoidContent.gameObject.activeSelf,
            VoidSelector && VoidSelector.enabled,
            Peak.VoidBiome.SoulFreedStatus);
        VoidPhase = Publication switch
        {
            VoidSegmentPublication.Omitted => VoidControllerPhase.OmittedPublished,
            VoidSegmentPublication.Retained => VoidControllerPhase.RetainedPublished,
            VoidSegmentPublication.Transferred => VoidControllerPhase.TransferredPublished,
            _ => throw new InvalidOperationException("The VOID controller publication policy is invalid.")
        };
        try
        {
            DeactivateVoidController();
            if (Publication is VoidSegmentPublication.Omitted)
            {
                if (VoidSelector) VoidSelector.enabled = false;
                OriginalVoidContent.gameObject.SetActive(false);
                Peak.VoidBiome.instance = null!;
            }
            else if (Publication is VoidSegmentPublication.Transferred)
            {
                if (VoidSelector) VoidSelector.enabled = false;
                OriginalVoidContent.gameObject.SetActive(false);
                VoidController.segment = PublishedVoidSegment!;
                VoidController.spawnPoints = PublishedVoidFallback!;
                PublishedVoidFallback = null;
                Peak.VoidBiome.instance = VoidController;
            }
            else Peak.VoidBiome.instance = VoidController;
        }
        catch (Exception Failure)
        {
            try { RestoreVoidController(); }
            catch (Exception Restore)
            {
                throw new AggregateException("VOID controller publication and restoration both failed.", Failure, Restore);
            }
            throw;
        }
    }
    internal void ReconcileVoidController(IReadOnlyList<MapSlot> Slots, int Index)
    {
        if (VoidPhase is not VoidControllerPhase.RetainedPublished
            and not VoidControllerPhase.TransferredPublished
            and not VoidControllerPhase.TransferredRetired) return;
        var ExpectedSegment = VoidPhase is VoidControllerPhase.RetainedPublished
            ? OriginalVoidSegment
            : PublishedVoidSegment;
        if (!VoidController || ExpectedSegment is null
            || !ReferenceEquals(VoidController.segment, ExpectedSegment)
            || !Peak.VoidBiome.instance || Peak.VoidBiome.instance.GetInstanceID() != VoidControllerId)
            throw new InvalidOperationException("The published VOID controller is unavailable.");
        var Nadir = Index >= 0 && Index < Slots.Count && !Slots[Index].Released
            && Slots[Index].Source is NadirLayoutSegmentSource;
        if (!Nadir)
        {
            if (ActiveNadirIndex < 0 && !VoidController.isActive && !VoidController.gameObject.activeSelf) return;
            DeactivateVoidController();
            ActiveNadirIndex = -1;
            return;
        }
        if (ActiveNadirIndex == Index && VoidController.isActive) return;
        DeactivateVoidController();
        ActiveNadirIndex = -1;
        VoidController.Activate();
        ActiveNadirIndex = Index;
    }
    internal void RestoreVoidController(bool ResumeNativeSources = true)
    {
        if (VoidPhase is not VoidControllerPhase.OmittedPublished
            and not VoidControllerPhase.RetainedPublished
            and not VoidControllerPhase.TransferredPublished) return;
        var Rollback = VoidRollback;
        if (!VoidController || !OriginalVoidContent || Rollback is null)
            throw new InvalidOperationException("The original VOID controller state is unavailable.");
        DeactivateVoidController();
        VoidController.segment = Rollback.Segment;
        VoidController.spawnPoints = Rollback.SpawnPoints;
        if (VoidSelector) VoidSelector.enabled = Rollback.SelectorEnabled;
        OriginalVoidContent.gameObject.SetActive(ResumeNativeSources && Rollback.ContentActive);
        VoidController.gameObject.SetActive(ResumeNativeSources && Rollback.ControllerRootActive);
        if (ResumeNativeSources)
        {
            if (Rollback.ControllerActive) VoidController.Activate();
            NadirRuntime.RestoreSoulFreedStatus(VoidController, Rollback.SoulFreedStatus);
        }
        Peak.VoidBiome.instance = Rollback.Singleton;
        ActiveNadirIndex = -1;
        PublishedVoidSegment = null;
        PublishedVoidFallback = null;
        VoidRollback = null;
        VoidPhase = VoidControllerPhase.Native;
    }
    internal void ReleaseVoidController()
    {
        if (VoidPhase is not VoidControllerPhase.RetainedPublished
            and not VoidControllerPhase.OmittedSourceRetained
            and not VoidControllerPhase.OmittedRetired
            and not VoidControllerPhase.TransferredRetired) return;
        var ReleaseOmittedRoot = VoidPhase is VoidControllerPhase.OmittedSourceRetained;
        var RetainedRollback = VoidPhase is VoidControllerPhase.RetainedPublished
            ? VoidRollback ?? throw new InvalidOperationException("The retained VOID controller rollback is unavailable.")
            : null;
        if (VoidController)
        {
            DeactivateVoidController();
            if (RetainedRollback is not null)
            {
                VoidController.segment = RetainedRollback.Segment;
                VoidController.spawnPoints = RetainedRollback.SpawnPoints;
            }
        }
        var CurrentController = Peak.VoidBiome.instance;
        if (CurrentController is not null && CurrentController.GetInstanceID() == VoidControllerId)
            Peak.VoidBiome.instance = null!;
        ActiveNadirIndex = -1;
        if (ReleaseOmittedRoot && VoidController)
            ReleasedSlotRetirement.ReleaseObject(VoidController.gameObject);
        PublishedVoidSegment = null;
        PublishedVoidFallback = null;
        VoidRollback = null;
        VoidPhase = VoidControllerPhase.Released;
    }
    internal void CaptureFullPeakPose(Transform Root)
    {
        if (!Root) throw new ArgumentNullException(nameof(Root));
        if (FullPeakRoot is not null)
            throw new InvalidOperationException("A reused full Peak root pose was captured more than once.");
        FullPeakRoot = Root;
        FullPeakPose = new Pose(Root.position, Root.rotation);
    }

    internal void CaptureBridgeStatue(Peak.ScoutStatue Statue)
    {
        if (BridgeStatue is not null)
            throw new InvalidOperationException("A reused Peak Bridge Scout Statue was captured more than once.");
        BridgeStatue = Statue;
    }

    internal void DeactivateUnused(IReadOnlyCollection<Transform> Reused)
    {
        if (Reused is null) throw new ArgumentNullException(nameof(Reused));
        foreach (var Original in Active.Keys)
        {
            if (!Original) continue;
            var Keep = false;
            foreach (var Container in Reused)
            {
                if (!Container || !TransformOwnership.Overlaps(Original.transform, Container)) continue;
                Keep = true;
                break;
            }
            if (!Keep) Original.SetActive(false);
        }
    }

    internal void HoldCloneSourcesInactive(IReadOnlyList<Transform> Sources)
    {
        if (Sources is null) throw new ArgumentNullException(nameof(Sources));
        foreach (var Source in Sources)
        {
            if (!Source)
                throw new InvalidOperationException("A native clone source is unavailable.");
            var Root = Source.gameObject;
            if (!Active.ContainsKey(Root)) Active.Add(Root, Root.activeSelf);
            Root.SetActive(false);
        }
    }

    internal void Restore(
        IEnumerable<KeyValuePair<Transform, Vector3>> Positions, bool ResumeNativeSources = true)
    {
        if (Positions is null) throw new ArgumentNullException(nameof(Positions));
        if (!ResumeNativeSources) Quiesce();
        RestoreVoidController(ResumeNativeSources);
        var RestoredRoots = new List<Transform>();
        foreach (var State in Positions)
        {
            if (!State.Key) continue;
            var Translation = State.Value - State.Key.position;
            State.Key.position = State.Value;
            PlacementExecution.RebaseCondorFlightPositions(State.Key, Matrix4x4.Translate(Translation));
            RestoredRoots.Add(State.Key);
        }
        if (FullPeakRoot)
        {
            var PreviousWorldToLocal = FullPeakRoot.worldToLocalMatrix;
            FullPeakRoot.SetPositionAndRotation(FullPeakPose.position, FullPeakPose.rotation);
            PlacementExecution.RebaseCondorFlightPositions(
                FullPeakRoot, FullPeakRoot.localToWorldMatrix * PreviousWorldToLocal);
            RestoredRoots.Add(FullPeakRoot);
        }
        if (ResumeNativeSources && BridgeStatue) BridgeStatue.enabled = true;
        if (ResumeNativeSources)
            foreach (var State in Active)
                if (State.Key) State.Key.SetActive(State.Value);
        if (RestoredRoots.Count != 0)
            PlacementExecution.RebaseTrapWarningLines(RestoredRoots);
    }

    internal void Quiesce()
    {
        foreach (var Root in Active.Keys)
            if (Root) SpecialDayProjection.DeactivatePreservingGlobals(Root);
        if (BridgeStatue) BridgeStatue.enabled = false;
    }

    internal bool WillRetire(Transform Value, IReadOnlyCollection<Transform> Reused)
    {
        if (!Value) throw new ArgumentNullException(nameof(Value));
        if (Reused is null) throw new ArgumentNullException(nameof(Reused));
        foreach (var Unit in BiomeRetirementUnits)
            if (TransformOwnership.Contains(Unit.Owner.transform, Value)) return Retireable(Unit, Reused);
        return false;
    }

    internal void Retire(
        MapRun Run,
        IReadOnlyCollection<Transform> Reused,
        PeakHandler? OriginalPeakHandler,
        PeakHandler? InstalledPeakHandler)
    {
        if (Run is null) throw new ArgumentNullException(nameof(Run));
        if (Reused is null) throw new ArgumentNullException(nameof(Reused));
        var Candidates = new List<GameObject>();
        foreach (var Unit in BiomeRetirementUnits)
            if (Retireable(Unit, Reused)) Candidates.Add(Unit.Owner.gameObject);
        var RetireOmittedRoot = false;
        if (VoidPhase is VoidControllerPhase.OmittedPublished
            or VoidControllerPhase.TransferredPublished)
        {
            if (!OriginalVoidContent || !OriginalVoidContent.parent
                || OriginalVoidContent.parent.GetInstanceID() != VoidController.transform.GetInstanceID())
                throw new InvalidOperationException("The original VOID content left its controller before retirement.");
            if (VoidPhase is VoidControllerPhase.TransferredPublished)
                Candidates.Add(OriginalVoidContent.gameObject);
            else if (!ForceSpawnCatalog.RequiresNativeSource(OriginalVoidContent))
            {
                RetireOmittedRoot = true;
                Candidates.Add(VoidController.gameObject);
            }
        }
        if (Candidates.Count == 0) return;
        if (Run.SceneHandle != SceneHandle || !Run.Active || Run.OriginalMapSourcesRetired)
            throw new InvalidOperationException("The map run cannot retire its original map sources.");
        if (OriginalPeakHandler && InsideAny(OriginalPeakHandler.transform, Candidates))
        {
            if (!InstalledPeakHandler
                || InstalledPeakHandler.GetInstanceID() == OriginalPeakHandler.GetInstanceID()
                || InsideAny(InstalledPeakHandler.transform, Candidates))
                throw new InvalidOperationException("The published map has no independent Peak end-cutscene owner.");
        }
        try { ReleasedSlotRetirement.ReleaseRetirementBatch(Run, Candidates); }
        finally
        {
            if (Run.OriginalMapSourcesRetired && VoidPhase is VoidControllerPhase.OmittedPublished)
            {
                VoidPhase = RetireOmittedRoot
                    ? VoidControllerPhase.OmittedRetired
                    : VoidControllerPhase.OmittedSourceRetained;
                VoidRollback = null;
            }
            else if (Run.OriginalMapSourcesRetired
                && VoidPhase is VoidControllerPhase.TransferredPublished)
            {
                VoidPhase = VoidControllerPhase.TransferredRetired;
                VoidRollback = null;
                var Controller = VoidController;
                if (Controller)
                {
                    Controller.gameObject.name = "PLL_VoidRuntime";
                    foreach (var Grouper in Controller.GetComponents<PropGrouper>())
                        if (Grouper) UnityEngine.Object.Destroy(Grouper);
                }
                if (VoidSelector)
                {
                    VoidSelector.enabled = false;
                    UnityEngine.Object.Destroy(VoidSelector);
                }
            }
        }
    }

    static Biome BiomeOwner(Transform Source)
    {
        Biome? Result = null;
        for (var Current = Source; Current; Current = Current.parent)
        {
            var Biomes = Current.GetComponents<Biome>();
            if (Biomes.Length > 1 || Biomes.Length == 1 && Result)
                throw new InvalidOperationException("PEAK's ordinary native Biome ownership is ambiguous.");
            if (Biomes.Length == 1) Result = Biomes[0];
        }
        return Result
            ? Result
            : throw new InvalidOperationException("PEAK's ordinary native Biome owner is unavailable.");
    }

    static bool Retireable(RetirementUnit Unit, IReadOnlyCollection<Transform> Reused)
    {
        Validate(Unit);
        foreach (var Container in Reused)
        {
            if (!Container)
                throw new InvalidOperationException("A reused native source container was destroyed before publication.");
            if (TransformOwnership.Overlaps(Unit.Owner.transform, Container)) return false;
        }
        foreach (var Source in Unit.Sources)
            if (ForceSpawnCatalog.RequiresNativeSource(Source)) return false;
        return true;
    }

    static void Validate(RetirementUnit Unit)
    {
        if (!Unit.Owner || !Unit.Owner.gameObject || !Unit.Owner.gameObject.scene.IsValid()
            || !Unit.Owner.gameObject.scene.isLoaded || Unit.Sources.Count == 0)
            throw new InvalidOperationException("An ordinary native Biome retirement owner is unavailable.");
        foreach (var Source in Unit.Sources)
            if (!Source || !TransformOwnership.Contains(Unit.Owner.transform, Source))
                throw new InvalidOperationException("An ordinary native Biome source left its retirement owner.");
    }

    static bool InsideAny(Transform Value, IReadOnlyList<GameObject> Roots)
    {
        foreach (var Root in Roots)
            if (TransformOwnership.Contains(Root.transform, Value)) return true;
        return false;
    }
}
