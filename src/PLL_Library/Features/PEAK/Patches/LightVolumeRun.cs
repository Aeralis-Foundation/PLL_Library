using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace PLL.Library;

internal sealed class LightVolumeRun
{
    sealed class PreparedVisualLease
    {
        internal PreparedVisualLease(LightVolumeVisualMap Visual, FeatureOccurrence Occurrence)
        {
            this.Visual = Visual;
            this.Occurrence = Occurrence;
        }

        internal LightVolumeVisualMap Visual { get; }
        internal FeatureOccurrence Occurrence { get; }
    }

    sealed class PageLease
    {
        internal PageLease(
            LightVolumeVisualMap.Page Visual,
            FeatureOccurrence Occurrence,
            Vector3 Translation)
        {
            this.Visual = Visual;
            this.Occurrence = Occurrence;
            this.Translation = Translation;
        }

        internal LightVolumeVisualMap.Page Visual { get; }
        internal FeatureOccurrence Occurrence { get; }
        internal Vector3 Translation { get; }

        internal void Publish(Vector3 CurrentTranslation)
        {
            if (!CurrentTranslation.Equals(Translation))
                throw new InvalidOperationException("A light-volume page occurrence changed its placement translation.");
            Visual.Publish(Translation);
        }

        internal long OwnedBytes => Visual.OwnedBytes;

        internal void Release() => Visual.Release();
    }

    readonly struct BakeCandidate
    {
        internal BakeCandidate(
            float Spacing,
            uint3 Grid,
            long RequiredBytes,
            bool MinimumGrid,
            bool Unrepresentable,
            LightVolumeBakePlan? Plan)
        {
            this.Spacing = Spacing;
            this.Grid = Grid;
            this.RequiredBytes = RequiredBytes;
            this.MinimumGrid = MinimumGrid;
            this.Unrepresentable = Unrepresentable;
            this.Plan = Plan;
        }

        internal float Spacing { get; }
        internal uint3 Grid { get; }
        internal long RequiredBytes { get; }
        internal bool MinimumGrid { get; }
        internal bool Unrepresentable { get; }
        internal LightVolumeBakePlan? Plan { get; }
    }

    const long BytesPerMiB = 1024L * 1024;
    readonly HashSet<FeatureOccurrence> DirtyOccurrences = new();
    readonly HashSet<string> WarnedUnsupportedReasons = new(StringComparer.Ordinal);
    readonly long RunIdentity;
    readonly long PageByteLimit;
    readonly ulong NoLiveCapacityBytes;
    readonly DxgiVideoMemory.LocalBudget? VideoMemory;
    long RetainedVisualByteCount;
    global::LightVolume? Volume;
    LightVolumeVisualMap? Canonical;
    LightVolumeVisualMap? Current;
    PreparedVisualLease? PreparedVisual;
    PageLease? FallbackPage;
    PageLease? PreparedPage;
    MapSlot? ProjectionSlot;
    FeatureOccurrence? BakingOccurrence;
    MapSlot? PreparedFallbackSlot;
    bool FallbackRequestSettled;
    bool FallbackProjectionActive;
    bool Released;
    (GeneratedBatchKey Key, int Revision)? PendingHookedPublication;

    internal LightVolumeRun(long RunIdentity)
    {
        this.RunIdentity = RunIdentity;
        var ConfiguredBytes = checked((long)FileManager.LightMapMinimum.Value * BytesPerMiB);
        var ReportedCapacityBytes = checked(
            (ulong)Math.Max(SystemInfo.graphicsMemorySize, 0) * (ulong)BytesPerMiB);
        VideoMemory = DxgiVideoMemory.Open(
            (uint)SystemInfo.graphicsDeviceVendorID,
            (uint)SystemInfo.graphicsDeviceID,
            SystemInfo.graphicsDeviceName);
        NoLiveCapacityBytes = NoLiveCapacity(
            VideoMemory?.NominalBytes ?? ReportedCapacityBytes, ConfiguredBytes,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        PageByteLimit = ConfiguredBytes;
    }

    internal global::LightVolume OwnedVolume => Volume
        ?? throw new InvalidOperationException("The map run has no canonical light-volume lease.");
    internal LightVolumeVisualMap CanonicalMap => Canonical
        ?? throw new InvalidOperationException("The map run has no canonical light-volume lease.");
    internal LightVolumeVisualMap CurrentMap => Current
        ?? throw new InvalidOperationException("The map run has no canonical light-volume lease.");

    internal void Capture(MapRun Run)
    {
        Run.RequirePublished();
        RequireRun(Run);
        if (Canonical is not null)
            throw new InvalidOperationException("The map run captured its canonical light volume more than once.");
        var CapturedVolume = global::LightVolume.Instance();
        if (!CapturedVolume) throw new InvalidOperationException("PEAK's light volume is unavailable.");
        var CapturedMap = LightVolumeVisualMap.Capture(CapturedVolume, RequireSampling: true);
        LightVolumeHarmonyAdapter.BindRun(this);
        Volume = CapturedVolume;
        Canonical = CapturedMap;
        Current = CapturedMap;
        RetainedVisualByteCount = MeasureRetainedVisualBytes();
    }

    internal LightVolumeBakeOperation BeginBake(
        MapRun Run, MapSlot TargetSlot, Action RequireCurrent)
    {
        RequireCapturedRun(Run);
        RequireSlot(Run, TargetSlot);
        var Owned = OwnedVolume;
        if (!Owned || Owned.Running)
            throw new InvalidOperationException("PEAK's owned light volume is unavailable or already running another bake.");
        var Published = CurrentMap;
        if (!Published.MatchesLive(Owned))
            throw new InvalidOperationException("The published visual light volume changed outside its map-run owner.");
        ReleasePreparedVisual();
        var Operation = new LightVolumeBakeOperation(
            this, Run, Owned, Published, TargetSlot, RequireCurrent);
        DirtyOccurrences.Add(TargetSlot.Occurrence);
        LightVolumeHarmonyAdapter.BindBake(this, Operation);
        BakingOccurrence = TargetSlot.Occurrence;
        return Operation;
    }

    internal bool ScheduleRebake(
        MapRun Run,
        MapSlot TargetSlot,
        bool TargetIsCurrent,
        bool GeometryDirty)
    {
        RequireCapturedRun(Run);
        RequireSlot(Run, TargetSlot);
        var Occurrence = TargetSlot.Occurrence;
        if (GeometryDirty) DirtyOccurrences.Add(Occurrence);
        return TargetIsCurrent && NeedsRebake(TargetSlot);
    }

    internal bool RebakePending(
        MapRun Run,
        MapSlot TargetSlot,
        bool TargetIsCurrent)
    {
        RequireCapturedRun(Run);
        RequireSlot(Run, TargetSlot);
        return TargetIsCurrent && NeedsRebake(TargetSlot);
    }

    internal bool NeedsRebake(MapSlot TargetSlot) => DirtyOccurrences.Contains(TargetSlot.Occurrence);

    internal void MarkOtherOccurrencesDirty(
        MapRun Run,
        MapSlot CurrentSlot)
    {
        RequireCapturedRun(Run);
        RequireSlot(Run, CurrentSlot);
        foreach (var Slot in Run.Slots)
            if (!Slot.Released
                && !ReferenceEquals(Slot.Occurrence, CurrentSlot.Occurrence))
                DirtyOccurrences.Add(Slot.Occurrence);
    }

    internal bool ProjectionPrepared(MapSlot CurrentSlot)
    {
        var Occurrence = CurrentSlot.Occurrence;
        return !Released && !DirtyOccurrences.Contains(Occurrence)
            && !ReferenceEquals(BakingOccurrence, Occurrence)
            && (!ReferenceEquals(PreparedFallbackSlot?.Occurrence, Occurrence)
                || FallbackRequestSettled);
    }

    internal bool ProjectionReady(MapSlot CurrentSlot) =>
        ProjectionPrepared(CurrentSlot) && ReferenceEquals(ProjectionSlot, CurrentSlot)
        && !ReferenceEquals(PreparedVisual?.Occurrence, CurrentSlot.Occurrence);

    internal bool JumpProjectionPrepared(MapSlot TargetSlot) =>
        ProjectionPrepared(TargetSlot) && (ReferenceEquals(ProjectionSlot?.Occurrence, TargetSlot.Occurrence)
            || ReferenceEquals(PreparedVisual?.Occurrence, TargetSlot.Occurrence)
            || ReferenceEquals(PreparedFallbackSlot?.Occurrence, TargetSlot.Occurrence));

    internal void PublishCompletedCurrent(MapRun Run, MapSlot TargetSlot)
    {
        if (PendingHookedPublication is not null) return;
        if (Run.SelectedActiveSlot is { } Slot
            && ReferenceEquals(Slot.Occurrence, TargetSlot.Occurrence)
            && Run.IsOccurrenceReady(TargetSlot.Occurrence))
            SelectProjection(Run, Slot.Index);
    }

    internal void BeginHookedPublication(GeneratedBatchKey Key, int Revision)
    {
        if (PendingHookedPublication is not null)
            throw new InvalidOperationException("A Hooked light publication is already staged.");
        PendingHookedPublication = (Key, Revision);
    }

    internal void CommitHooked(MapRun Run, HookedPropTarget Target, int Revision)
    {
        if (PendingHookedPublication is not { } Pending || Pending.Key != Target.BatchKey || Pending.Revision != Revision)
            throw new InvalidOperationException("Hooked light registration does not own the staged publication.");
        PendingHookedPublication = null;
        PublishCompletedCurrent(Run, Target.Occurrence.Slots[0]);
    }

    internal void CancelHooked(GeneratedBatchKey Key, int Revision)
    {
        if (PendingHookedPublication is not { } Pending) return;
        if (Pending.Key != Key || Pending.Revision != Revision)
            throw new InvalidOperationException("Hooked light cleanup does not own the staged publication.");
        PendingHookedPublication = null;
        List<Exception>? Failures = null;
        if (PreparedVisual?.Occurrence.RequestIndex == Key.RequestIndex)
        {
            try { ReleasePreparedVisual(); }
            catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
        }
        if (PreparedFallbackSlot?.Occurrence.RequestIndex == Key.RequestIndex)
        {
            DirtyOccurrences.Add(PreparedFallbackSlot.Occurrence);
            PreparedFallbackSlot = null; FallbackRequestSettled = false;
            var Page = PreparedPage; PreparedPage = null;
            try { Page?.Release(); }
            catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
            RetainedVisualByteCount = MeasureRetainedVisualBytes();
        }
        if (Failures is not null) throw new AggregateException("Hooked staged light cleanup failed.", Failures);
    }

    internal void CompleteBake(FeatureOccurrence Occurrence)
    {
        if (!ReferenceEquals(BakingOccurrence, Occurrence))
            throw new InvalidOperationException("A foreign occurrence completed the active light-volume bake.");
        BakingOccurrence = null;
        if (ReferenceEquals(PreparedVisual?.Occurrence, Occurrence))
            DirtyOccurrences.Remove(Occurrence);
    }

    internal void SelectProjection(MapRun Run, int CurrentSlotIndex)
    {
        RequireCapturedRun(Run);
        if (CurrentSlotIndex < 0 || CurrentSlotIndex >= Run.Slots.Length)
            throw new ArgumentOutOfRangeException(nameof(CurrentSlotIndex));
        var CurrentSlot = Run.Slots[CurrentSlotIndex];
        RequireSlot(Run, CurrentSlot);
        if (CurrentSlotIndex != Run.Handler!.currentSegment)
            throw new InvalidOperationException("The light-volume projection slot is not selected.");
        if (!ProjectionPrepared(CurrentSlot))
            throw new InvalidOperationException("The selected light-volume projection is incomplete.");
        if (ReferenceEquals(PreparedVisual?.Occurrence, CurrentSlot.Occurrence))
        {
            PublishPreparedVisual(CurrentSlot);
            return;
        }
        if (ReferenceEquals(PreparedFallbackSlot?.Occurrence, CurrentSlot.Occurrence))
        {
            PublishProjection(CurrentSlot);
            return;
        }
        if (ReferenceEquals(ProjectionSlot, CurrentSlot)) return;
        if (ProjectionSlot is null
            || !ReferenceEquals(ProjectionSlot.Occurrence, CurrentSlot.Occurrence))
        {
            ActivateCanonicalProjection(CurrentSlot);
            return;
        }
        PublishProjection(CurrentSlot);
    }

    internal IEnumerator PrepareFallbackPage(MapRun Run, MapSlot CurrentSlot, Action RequireCurrent)
    {
        if (RequireCurrent is null) throw new ArgumentNullException(nameof(RequireCurrent));
        RequireCurrent();
        RequireCapturedRun(Run);
        RequireSlot(Run, CurrentSlot);
        if (ReferenceEquals(PreparedVisual?.Occurrence, CurrentSlot.Occurrence)
            || !ReferenceEquals(Current, Canonical) && !FallbackProjectionActive
                && ReferenceEquals(ProjectionSlot?.Occurrence, CurrentSlot.Occurrence)) yield break;
        var Request = RequestFallbackPage(Run, CurrentSlot, RequireCurrent);
        try
        {
            while (Request.MoveNext()) yield return Request.Current;
        }
        finally
        {
            (Request as IDisposable)?.Dispose();
        }
        RequireCurrent();
        SettleFallbackProjection(CurrentSlot);
    }

    internal IEnumerator RequestFallbackPage(MapRun Run, MapSlot CurrentSlot, Action RequireCurrent)
    {
        if (RequireCurrent is null) throw new ArgumentNullException(nameof(RequireCurrent));
        RequireCurrent();
        RequireCapturedRun(Run);
        RequireSlot(Run, CurrentSlot);
        var Occurrence = CurrentSlot.Occurrence;
        if ((FallbackProjectionActive
                && ReferenceEquals(ProjectionSlot?.Occurrence, Occurrence))
            || ReferenceEquals(PreparedFallbackSlot?.Occurrence, Occurrence)) yield break;
        PreparedPage?.Release();
        PreparedPage = null;
        PreparedFallbackSlot = CurrentSlot;
        FallbackRequestSettled = false;
        RetainedVisualByteCount = MeasureRetainedVisualBytes();
        if (!TryOccurrenceBounds(CurrentSlot, out var SourceBounds, out var Translation))
            yield break;
        var OccurrenceId = Occurrence.Slots[0].Source.Id;
        if (!CanonicalMap.TryBeginPageBuild(
                SourceBounds, OccurrenceId, PageByteLimit,
                QueryAdditionalCapacity(out _), out var Build, out var Rejection))
        {
            RecordPageRejection(Rejection!);
            yield break;
        }

        var PageBuild = Build!;
        LightVolumeVisualMap.Page? Visual = null;
        var Transferred = false;
        var UploadRejected = false;
        try
        {
            while (true)
            {
                RequireCurrent();
                var Advance = PageBuild.Advance();
                if (Advance == LightVolumeVisualMap.PageAdvance.UploadRejected)
                {
                    UploadRejected = true;
                    break;
                }
                if (Advance == LightVolumeVisualMap.PageAdvance.Complete) break;
                yield return null;
                RequireCurrent();
            }
            if (!UploadRejected)
            {
                RequireCurrent();
                Visual = PageBuild.TakePage();
                var Lease = new PageLease(Visual, Occurrence, Translation);
                PreparedPage = Lease;
                Transferred = true;
                RetainedVisualByteCount = MeasureRetainedVisualBytes();
            }
        }
        finally
        {
            if (!Transferred) Visual?.Release();
            PageBuild.Dispose();
        }
        if (!UploadRejected) yield break;
        RecordPageRejection(LightVolumeVisualMap.PageUploadRejection(OccurrenceId));
    }

    internal void SettleFallbackProjection(MapSlot CurrentSlot)
    {
        if (!ReferenceEquals(PreparedFallbackSlot?.Occurrence, CurrentSlot.Occurrence))
        {
            if (PreparedFallbackSlot is null && FallbackProjectionActive
                && ReferenceEquals(ProjectionSlot?.Occurrence, CurrentSlot.Occurrence)) return;
            throw new InvalidOperationException("The settled fallback does not match its requested map slot.");
        }
        FallbackRequestSettled = true;
        DirtyOccurrences.Remove(CurrentSlot.Occurrence);
    }
    internal bool Owns(global::LightVolume Candidate) => !Released && Candidate && Candidate == Volume;

    internal Color SampleCurrentProjection(Vector3 WorldPosition)
    {
        var Visual = FallbackProjectionActive ? CanonicalMap : CurrentMap;
        var Slot = ProjectionSlot;
        if ((FallbackProjectionActive || ReferenceEquals(Visual, Canonical))
            && Slot is { Released: false, Source: INativeLayoutSegmentSource })
            WorldPosition = Slot.Frame.InversePoint(WorldPosition);
        return Visual.Sample(WorldPosition);
    }

    internal Color SampleCanonical(Vector3 WorldPosition) => CanonicalMap.Sample(WorldPosition);

    internal bool RecordUnsupportedReason(string Reason) => WarnedUnsupportedReasons.Add(Reason);

    internal void PublishVisualGlobals(LightVolumeVisualMap Visual, MapSlot? CurrentSlot)
    {
        if (FallbackProjectionActive && ProjectionSlot is { } ActiveSlot)
        {
            PublishFallbackGlobals(ActiveSlot);
            return;
        }
        if (!ReferenceEquals(Visual, Canonical) || CurrentSlot is null)
        {
            Visual.PublishMapGlobals();
            return;
        }
        var PublishedSlot = ProjectionSlot ?? CurrentSlot;
        CanonicalMap.PublishMapGlobals(
            TryProjectionTranslation(PublishedSlot, out var Translation)
                ? Translation
                : default);
    }

    void ActivateCanonicalProjection(MapSlot CurrentSlot)
    {
        var Previous = CurrentMap;
        var PreviousSlot = ProjectionSlot;
        try
        {
            CanonicalMap.RestoreLive(OwnedVolume);
            CanonicalMap.PublishMapGlobals(
                TryProjectionTranslation(CurrentSlot, out var Translation)
                    ? Translation
                    : default);
        }
        catch (Exception Failure)
        {
            try
            {
                Previous.RestoreLive(OwnedVolume);
                PublishVisualGlobals(Previous, PreviousSlot);
            }
            catch (Exception RestoreFailure)
            {
                throw new AggregateException(
                    "Occurrence light projection and prior projection restoration both failed.",
                    Failure,
                    RestoreFailure);
            }
            throw;
        }

        Current = Canonical;
        ProjectionSlot = CurrentSlot;
        PreparedFallbackSlot = null;
        FallbackRequestSettled = false;
        FallbackProjectionActive = false;
        List<Exception>? Failures = null;
        try { ReleaseFallbackPages(); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
        if (!ReferenceEquals(Previous, Canonical))
        {
            if (PreviousSlot is not null
                && !ReferenceEquals(PreviousSlot.Occurrence, CurrentSlot.Occurrence))
                DirtyOccurrences.Add(PreviousSlot.Occurrence);
            try { LightVolumeVisualMap.Destroy(Previous, CanonicalMap); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
        }
        RetainedVisualByteCount = MeasureRetainedVisualBytes();
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Prior occurrence light projection cleanup failed.", Failures);
    }

    void PublishProjection(MapSlot CurrentSlot)
    {
        var PreviousSlot = ProjectionSlot;
        if (ReferenceEquals(PreparedFallbackSlot?.Occurrence, CurrentSlot.Occurrence))
            FallbackProjectionActive = true;
        if (FallbackProjectionActive)
        {
            var Occurrence = CurrentSlot.Occurrence;
            if (PreparedPage is not null && ReferenceEquals(PreparedPage.Occurrence, Occurrence))
            {
                var Previous = FallbackPage;
                FallbackPage = PreparedPage;
                PreparedPage = null;
                Previous?.Release();
                RetainedVisualByteCount = MeasureRetainedVisualBytes();
            }
            else if (FallbackPage is null
                || !ReferenceEquals(FallbackPage.Occurrence, Occurrence))
                ReleaseFallbackPages();
            ProjectionSlot = CurrentSlot;
            PublishFallbackGlobals(CurrentSlot);
            if (PreviousSlot is not null && !ReferenceEquals(Current, Canonical)
                && !ReferenceEquals(PreviousSlot.Occurrence, CurrentSlot.Occurrence))
                DirtyOccurrences.Add(PreviousSlot.Occurrence);
            if (PreparedFallbackSlot is not null && FallbackRequestSettled
                && ReferenceEquals(PreparedFallbackSlot.Occurrence, CurrentSlot.Occurrence))
                PreparedFallbackSlot = CurrentSlot;
            AdoptFallbackOwner();
            return;
        }

        ReleaseFallbackPages();
        ProjectionSlot = CurrentSlot;
        if (ReferenceEquals(Current, Canonical)) PublishVisualGlobals(CurrentMap, CurrentSlot);
    }

    void PublishFallbackGlobals(MapSlot CurrentSlot)
    {
        if (!TryProjectionTranslation(CurrentSlot, out var Translation))
        {
            CanonicalMap.PublishMapGlobals();
            return;
        }
        if (FallbackPage is not null
            && ReferenceEquals(CurrentSlot.Occurrence, FallbackPage.Occurrence))
        {
            FallbackPage.Publish(Translation);
            return;
        }
        CanonicalMap.PublishMapGlobals(Translation);
    }
    void AdoptFallbackOwner()
    {
        if (!FallbackProjectionActive || !FallbackRequestSettled
            || PreparedFallbackSlot is null || ProjectionSlot is null
            || !ReferenceEquals(PreparedFallbackSlot, ProjectionSlot)) return;
        var Previous = CurrentMap;
        PreparedFallbackSlot = null;
        FallbackRequestSettled = false;
        if (ReferenceEquals(Previous, Canonical)) return;
        try
        {
            CanonicalMap.RestoreLive(OwnedVolume);
            PublishFallbackGlobals(ProjectionSlot);
            LightVolumeVisualMap.Destroy(Previous, CanonicalMap);
        }
        catch (Exception Failure)
        {
            try
            {
                Previous.RestoreLive(OwnedVolume);
                PublishFallbackGlobals(ProjectionSlot);
            }
            catch (Exception RestoreFailure)
            {
                throw new AggregateException(
                    "Fallback map adoption and prior-map restoration both failed.",
                    Failure,
                    RestoreFailure);
            }
            throw;
        }
        Current = Canonical;
        RetainedVisualByteCount = MeasureRetainedVisualBytes();
    }
    bool TryOccurrenceBounds(
        MapSlot CurrentSlot,
        out Bounds SourceBounds,
        out Vector3 Translation)
    {
        SourceBounds = default;
        Translation = default;
        var Occurrence = CurrentSlot.Occurrence;
        if (Occurrence.Slots.Count == 0
            || Occurrence.Slots[0].Source is not NativeOrdinarySegmentSource
            || !TryProjectionTranslation(CurrentSlot, out Translation)) return false;
        if (!LightVolumeBakeInventory.TryCapture(
                OwnedVolume, CurrentSlot, Compute: false,
                out var Inventory, out _)) return false;
        SourceBounds = Inventory!.Bounds;
        SourceBounds.center -= Translation;
        return true;
    }

    internal bool TryPrepareBake(
        global::LightVolume Volume,
        bool Compute,
        LightVolumeBakeInventory Inventory,
        out LightVolumeBakePlan? Plan,
        out string? Rejection)
    {
        Plan = null;
        Rejection = null;
        var Bounds = Inventory.Bounds;
        var Size = Bounds.size;
        var InitialSpacing = Volume.raySpacing;
        if (!UnityValueContract.Finite(Bounds.min)
            || !UnityValueContract.Finite(Bounds.center)
            || !UnityValueContract.Finite(Size)
            || Size.x <= 0f || Size.y <= 0f || Size.z <= 0f
            || InitialSpacing <= 0f || float.IsNaN(InitialSpacing)
            || float.IsInfinity(InitialSpacing))
            throw new InvalidOperationException("PEAK calculated invalid light-volume render bounds.");
        var AvailableBytes = QueryAdditionalCapacity(out var LiveSnapshot);
        var MaximumGridAxis = checked((uint)SystemInfo.maxTexture3DSize);
        const int FirstScaleStep = 256;
        var LowerFailure = FirstScaleStep;
        var Candidate = EvaluateBakeCandidate(
            Volume, Compute, Inventory, Size, InitialSpacing,
            FirstScaleStep, MaximumGridAxis, AvailableBytes);
        if (AvailableBytes <= 0)
        {
            Rejection = CapacityRejection(
                "the requested light-volume working set",
                Candidate.RequiredBytes,
                AvailableBytes,
                LiveSnapshot);
            return false;
        }
        if (Candidate.Unrepresentable)
        {
            Rejection = "the light-volume grid cannot fit the available local memory";
            return false;
        }
        if (Candidate.Plan is null)
        {
            if (Candidate.MinimumGrid)
            {
                Rejection = CapacityRejection(
                    "the minimum light-volume working set",
                    Candidate.RequiredBytes,
                    AvailableBytes,
                    LiveSnapshot);
                return false;
            }

            var UpperStep = FirstScaleStep * 2;
            while (true)
            {
                Candidate = EvaluateBakeCandidate(
                    Volume, Compute, Inventory, Size, InitialSpacing,
                    UpperStep, MaximumGridAxis, AvailableBytes);
                if (Candidate.Unrepresentable)
                {
                    Rejection = "the light-volume grid cannot fit the available local memory";
                    return false;
                }
                if (Candidate.Plan is not null) break;
                if (Candidate.MinimumGrid)
                {
                    Rejection = CapacityRejection(
                        "the minimum light-volume working set",
                        Candidate.RequiredBytes,
                        AvailableBytes,
                        LiveSnapshot);
                    return false;
                }
                LowerFailure = UpperStep;
                if (UpperStep == int.MaxValue)
                {
                    Rejection = "the light-volume grid cannot fit the available local memory";
                    return false;
                }
                UpperStep = UpperStep > int.MaxValue / 2
                    ? int.MaxValue
                    : UpperStep * 2;
            }

            while (UpperStep - LowerFailure > 1)
            {
                var MiddleStep = LowerFailure + (UpperStep - LowerFailure) / 2;
                var Middle = EvaluateBakeCandidate(
                    Volume, Compute, Inventory, Size, InitialSpacing,
                    MiddleStep, MaximumGridAxis, AvailableBytes);
                if (Middle.Unrepresentable || Middle.MinimumGrid && Middle.Plan is null)
                    throw new InvalidOperationException(
                        "The monotonic light-volume planner produced an invalid search interval.");
                if (Middle.Plan is null) LowerFailure = MiddleStep;
                else
                {
                    UpperStep = MiddleStep;
                    Candidate = Middle;
                }
            }
        }

        var PreparedPlan = Candidate.Plan
            ?? throw new InvalidOperationException("The light-volume planner lost its accepted candidate.");
        Volume.raySpacing = Candidate.Spacing;
        Volume.gridRes = Candidate.Grid;
        Volume.gridOffset = Bounds.min;
        Volume.gridCenter = Bounds.center;
        Plan = PreparedPlan;
        return true;
    }

    BakeCandidate EvaluateBakeCandidate(
        global::LightVolume Volume,
        bool Compute,
        LightVolumeBakeInventory Inventory,
        Vector3 Size,
        float InitialSpacing,
        int ScaleStep,
        uint MaximumGridAxis,
        long AvailableBytes)
    {
        const int ScaleResolution = 256;
        var Spacing = InitialSpacing * (ScaleStep / (float)ScaleResolution);
        if (float.IsInfinity(Spacing))
            return new BakeCandidate(
                Spacing, default, long.MaxValue,
                MinimumGrid: false, Unrepresentable: true, Plan: null);
        var Grid = LightGrid(Size, Spacing);
        if (Grid.x == 0 || Grid.y == 0 || Grid.z == 0)
            throw new InvalidOperationException("PEAK calculated an empty light-volume grid.");
        var Voxels = checked((long)Grid.x * Grid.y * Grid.z);
        var MapBytes = checked(Voxels * LightVolumeVisualMap.RgbaHalfBytesPerPixel);
        var SliceVoxels = checked((long)Grid.x * Grid.y);
        var MinimumTraceBytes = Compute ? checked(SliceVoxels * 256) : 0;
        var BasePeakBytes = BakeBasePeakBytes(
            MapBytes, Volume.blurRadius, Compute, Inventory);
        var RequiredBytes = checked(BasePeakBytes + MinimumTraceBytes);
        var Fits = Grid.x <= MaximumGridAxis && Grid.y <= MaximumGridAxis
            && Grid.z <= MaximumGridAxis && MapBytes <= PageByteLimit
            && MinimumTraceBytes <= PageByteLimit
            && MinimumTraceBytes <= int.MaxValue
            && MinimumTraceBytes <= AvailableBytes
            && BasePeakBytes <= AvailableBytes - MinimumTraceBytes;
        LightVolumeBakePlan? CandidatePlan = null;
        if (Fits)
        {
            CandidatePlan = new LightVolumeBakePlan(
                Compute,
                Inventory.BlasInitialBytes,
                MapBytes,
                RequiredBytes,
                Inventory.AccelerationPeakBytes,
                Inventory.BuildScratchBytes,
                MinimumTraceBytes,
                Inventory.TopLevelBytes);
        }
        return new BakeCandidate(
            Spacing, Grid, RequiredBytes,
            Grid.x <= 4 && Grid.y <= 4 && Grid.z <= 4,
            Unrepresentable: false,
            CandidatePlan);
    }

    static ulong NoLiveCapacity(ulong NominalBytes, long ConfiguredBytes, bool Windows) =>
        Windows
            ? Math.Min(NominalBytes, checked((ulong)ConfiguredBytes * 8))
            : NominalBytes;

    static ulong LiveAdditionalCapacity(
        DxgiVideoMemory.Snapshot Snapshot,
        ulong RetainedBytes)
    {
        var AccountedUsage = Math.Max(Snapshot.CurrentUsage, RetainedBytes);
        return AccountedUsage < Snapshot.Budget
            ? Snapshot.Budget - AccountedUsage
            : 0;
    }

    static uint3 LightGrid(Vector3 Size, float Spacing) => new(
        global::LightVolume.CeilToMultiple(Size.x / Spacing, 4),
        global::LightVolume.CeilToMultiple(Size.y / Spacing, 4),
        global::LightVolume.CeilToMultiple(Size.z / Spacing, 4));
    static long BakeBasePeakBytes(
        long MapBytes,
        int BlurRadius,
        bool Compute,
        LightVolumeBakeInventory Inventory)
    {
        var RenderTextureCount = BlurRadius > 0 ? 2L : 1L;
        var ReadableTextureBytes = checked(MapBytes * 2);
        var SavePhaseBytes = checked(
            MapBytes * RenderTextureCount + MapBytes + ReadableTextureBytes);
        if (!Compute) return checked(SavePhaseBytes + Inventory.LightBufferBytes);
        var Value = Inventory;
        const long ContextDispatchBytes = 12;
        var ResidentGeometryBytes = checked(
            Value.AccelerationResidentBytes + Value.BuildScratchBytes
            + Value.TopLevelBytes + Value.LightBufferBytes + ContextDispatchBytes);
        var PositionGrowthPhaseBytes = checked(
            MapBytes + Value.AccelerationPeakBytes
            + Value.LightBufferBytes + ContextDispatchBytes);
        var SaveWithGeometryBytes = checked(SavePhaseBytes + ResidentGeometryBytes);
        return Math.Max(PositionGrowthPhaseBytes, SaveWithGeometryBytes);
    }

    internal long QueryAdditionalCapacity(out DxgiVideoMemory.Snapshot? LiveSnapshot)
    {
        var RetainedBytes = (ulong)RetainedVisualByteCount;
        if (VideoMemory?.TryQuery(out var Snapshot) == true)
        {
            LiveSnapshot = Snapshot;
            return (long)Math.Min(
                LiveAdditionalCapacity(Snapshot, RetainedBytes),
                (ulong)long.MaxValue);
        }
        LiveSnapshot = null;
        return NoLiveCapacityBytes > RetainedBytes
            ? (long)Math.Min(NoLiveCapacityBytes - RetainedBytes, (ulong)long.MaxValue)
            : 0;
    }

    internal void RequireAdditionalAllocation(
        long ImmediateBytes,
        long PlannedPeakBytes,
        string Phase)
    {
        if (ImmediateBytes < 0 || PlannedPeakBytes < ImmediateBytes)
            throw new InvalidOperationException("The light-volume allocation plan is invalid.");
        var AvailableBytes = QueryAdditionalCapacity(out var LiveSnapshot);
        var RequiredBytes = LiveSnapshot.HasValue ? ImmediateBytes : PlannedPeakBytes;
        if (RequiredBytes > AvailableBytes)
            throw new InvalidOperationException(
                CapacityRejection(
                    $"The light-volume {Phase}",
                    RequiredBytes,
                    AvailableBytes,
                    LiveSnapshot) + ".");
    }

    string CapacityRejection(
        string Work,
        long RequestedBytes,
        long AvailableBytes,
        DxgiVideoMemory.Snapshot? LiveSnapshot)
    {
        if (LiveSnapshot is not { } Snapshot)
            return $"{Work} requires {RequestedBytes} bytes, but only {AvailableBytes} bytes"
                + " are available under the conservative fallback limit";
        return $"{Work} requires {RequestedBytes} bytes, but the live DXGI budget is"
            + $" {Snapshot.Budget} bytes, current usage is {Snapshot.CurrentUsage} bytes,"
            + $" and PLL retains {RetainedVisualByteCount} bytes, leaving {AvailableBytes}"
            + " bytes available";
    }

    long MeasureRetainedVisualBytes()
    {
        var Bytes = Canonical?.OwnedBytes ?? 0;
        if (Current is not null && !ReferenceEquals(Current, Canonical))
            Bytes = checked(Bytes + Current.OwnedBytes);
        if (PreparedVisual is not null)
            Bytes = checked(Bytes + PreparedVisual.Visual.OwnedBytes);
        if (FallbackPage is not null) Bytes = checked(Bytes + FallbackPage.OwnedBytes);
        if (PreparedPage is not null) Bytes = checked(Bytes + PreparedPage.OwnedBytes);
        return Bytes;
    }
    void RecordPageRejection(string Warning)
    {
        if (RecordUnsupportedReason(Warning)) Plugin.PLLLog.LogWarning(Warning);
    }

    void ReleaseFallbackPages()
    {
        var Fallback = FallbackPage;
        var Prepared = PreparedPage;
        FallbackPage = null;
        PreparedPage = null;
        List<Exception>? Failures = null;
        try { RetainedVisualByteCount = MeasureRetainedVisualBytes(); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
        try { Fallback?.Release(); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
        try { Prepared?.Release(); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Fallback light-volume page cleanup failed.", Failures);
    }

    internal bool RepublishProjectionGlobals()
    {
        var Visual = Current;
        if (Released || Visual is null) return false;
        if (FallbackProjectionActive && ProjectionSlot is { } ActiveSlot)
        {
            PublishFallbackGlobals(ActiveSlot);
            return true;
        }
        if (!ReferenceEquals(Visual, Canonical)) return false;
        PublishVisualGlobals(Visual, ProjectionSlot);
        return true;
    }

    internal void Release()
    {
        if (Released) return;
        Released = true;
        PendingHookedPublication = null;
        var CapturedCanonical = Canonical;
        var Published = Current;
        Exception? LeaseFailure = null;
        try { ReleasePreparedVisual(); }
        catch (Exception Exception) { LeaseFailure = Exception; }
        try { ReleaseFallbackPages(); }
        catch (Exception Exception)
        {
            LeaseFailure = LeaseFailure is null ? Exception
                : new AggregateException("Prepared visual and fallback page release both failed.", LeaseFailure, Exception);
        }
        try
        {
            if (Volume is { } CapturedVolume && CapturedVolume
                && CapturedCanonical is not null && CapturedCanonical.Texture)
                CapturedCanonical.ApplyVisual(CapturedVolume);
        }
        catch (Exception Exception)
        {
            LeaseFailure = LeaseFailure is null
                ? Exception
                : new AggregateException("Canonical visual restoration also failed.", LeaseFailure, Exception);
        }
        try
        {
            if (Published is not null && CapturedCanonical is not null
                && !ReferenceEquals(Published, CapturedCanonical))
                LightVolumeVisualMap.Destroy(Published, CapturedCanonical);
        }
        catch (Exception Exception)
        {
            LeaseFailure = LeaseFailure is null
                ? Exception
                : new AggregateException(
                    "Visual light cleanup also failed.", LeaseFailure, Exception);
        }
        try { VideoMemory?.Dispose(); }
        catch (Exception Exception)
        {
            LeaseFailure = LeaseFailure is null
                ? Exception
                : new AggregateException(
                    "Video-memory budget release also failed.", LeaseFailure, Exception);
        }
        LightVolumeHarmonyAdapter.UnbindRun(this);
        Volume = null;
        Canonical = null;
        Current = null;
        ProjectionSlot = null;
        PreparedFallbackSlot = null;
        FallbackRequestSettled = false;
        FallbackProjectionActive = false;
        BakingOccurrence = null;
        DirtyOccurrences.Clear();
        RetainedVisualByteCount = 0;
        if (LeaseFailure is not null) ExceptionPropagation.Throw(LeaseFailure);
    }
    internal void PrepareVisual(
        MapRun Run,
        LightVolumeVisualMap Previous,
        LightVolumeVisualMap Staged,
        MapSlot TargetSlot)
    {
        RequireCapturedRun(Run);
        RequireSlot(Run, TargetSlot);
        if (!ReferenceEquals(Current, Previous)
            || !ReferenceEquals(BakingOccurrence, TargetSlot.Occurrence))
            throw new InvalidOperationException("The map run cannot prepare this occurrence light volume.");
        var Bytes = checked(MeasureRetainedVisualBytes() + Staged.OwnedBytes);
        PreparedVisual = new PreparedVisualLease(Staged, TargetSlot.Occurrence);
        RetainedVisualByteCount = Bytes;
    }

    void PublishPreparedVisual(MapSlot TargetSlot)
    {
        var Staged = PreparedVisual!.Visual;
        var Previous = CurrentMap;
        var PreviousSlot = ProjectionSlot;
        try
        {
            Staged.ApplyVisual(OwnedVolume);
            Staged.PublishMapGlobals();
        }
        catch (Exception Failure)
        {
            try
            {
                Previous.RestoreLive(OwnedVolume);
                PublishVisualGlobals(Previous, ProjectionSlot ?? TargetSlot);
            }
            catch (Exception RestoreFailure)
            {
                throw new AggregateException(
                    "Light-volume publication and prior-map restoration both failed.",
                    Failure,
                    RestoreFailure);
            }
            throw;
        }

        Current = Staged;
        PreparedVisual = null;
        ProjectionSlot = TargetSlot;
        PreparedFallbackSlot = null;
        FallbackRequestSettled = false;
        FallbackProjectionActive = false;
        List<Exception>? Failures = null;
        try { ReleaseFallbackPages(); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
        if (!ReferenceEquals(Previous, Canonical))
        {
            if (PreviousSlot is not null
                && !ReferenceEquals(PreviousSlot.Occurrence, TargetSlot.Occurrence))
                DirtyOccurrences.Add(PreviousSlot.Occurrence);
            try { LightVolumeVisualMap.Destroy(Previous, CanonicalMap, Staged); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
        }
        RetainedVisualByteCount = MeasureRetainedVisualBytes();
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Published light-volume cleanup failed.", Failures);
    }

    void ReleasePreparedVisual()
    {
        var Prepared = PreparedVisual;
        if (Prepared is null) return;
        PreparedVisual = null;
        DirtyOccurrences.Add(Prepared.Occurrence);
        LightVolumeVisualMap.Destroy(Prepared.Visual, CanonicalMap, CurrentMap);
        RetainedVisualByteCount = MeasureRetainedVisualBytes();
    }

    bool TryProjectionTranslation(MapSlot CurrentSlot, out Vector3 Translation)
    {
        Translation = default;
        if (CurrentSlot.Released) return false;
        var Occurrence = CurrentSlot.Occurrence;
        var Found = false;
        var Active = false;
        foreach (var Root in Occurrence.Roots)
            if (Root && Root.gameObject.activeInHierarchy)
            {
                Active = true;
                break;
            }
        if (!TryNativeTranslation(Occurrence, out var OccurrenceTranslation)) return false;
        if (Active && !IncludeTranslation(ref Found, ref Translation, OccurrenceTranslation)) return false;
        foreach (var OccurrenceSlot in Occurrence.Slots)
        {
            var Attachment = OccurrenceSlot.PeakAttachment;
            if (!Attachment || !Attachment.gameObject.activeInHierarchy) continue;
            var Source = OccurrenceSlot.PeakSource;
            if (Source is null || !Attachment.lossyScale.Equals(Source.RootScale)) return false;
            var Frame = PlacementPlan.PlacedPeakFrame(OccurrenceSlot);
            if (Frame.Rotated || !IncludeTranslation(ref Found, ref Translation, Frame.Offset)) return false;
        }
        return Found;
    }

    static bool IncludeTranslation(ref bool Found, ref Vector3 Translation, Vector3 Candidate)
    {
        if (!Found)
        {
            Translation = Candidate;
            Found = true;
            return true;
        }
        return Translation.Equals(Candidate);
    }

    static bool TryNativeTranslation(FeatureOccurrence Occurrence, out Vector3 Translation)
    {
        var Frame = Occurrence.Slots[0].Frame;
        Translation = Frame.Offset;
        if (Frame.Rotated) return false;
        foreach (var Slot in Occurrence.Slots)
            if (Slot.Source is CustomLayoutSegmentSource || Slot.Frame.Rotated
                || !Slot.Frame.Offset.Equals(Translation)
                || !Slot.Container.lossyScale.Equals(Slot.Source.RootScale)) return false;
        return true;
    }

    void RequireRun(MapRun Run)
    {
        if (Released || Run.Identity != RunIdentity || !ReferenceEquals(Run.LightVolumes, this))
            throw new InvalidOperationException("The canonical light volume belongs to another map run.");
    }

    void RequireCapturedRun(MapRun Run)
    {
        RequireRun(Run);
        if (Volume is null || Canonical is null || Current is null)
            throw new InvalidOperationException("The map run has no canonical light-volume lease.");
    }

    static void RequireSlot(MapRun Run, MapSlot CurrentSlot)
    {
        var Index = CurrentSlot.Index;
        if (CurrentSlot.Released || Index < 0 || Index >= Run.Slots.Length
            || !ReferenceEquals(Run.Slots[Index], CurrentSlot))
            throw new InvalidOperationException("The light-volume projection slot does not belong to the map run.");
    }
}
