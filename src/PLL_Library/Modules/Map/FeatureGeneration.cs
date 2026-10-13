using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zorro.Core;

namespace PLL.Library;

internal sealed class FeatureGeneration
{
    readonly HashSet<int> GeneratedRootIds = new();

    internal bool Aborted { get; set; }
    internal Operation BeginOperation() => new();
    internal bool ContainsRoot(Transform Root) =>
        GeneratedRootIds.Contains(Root.GetInstanceID());

    internal void TrackRoots(IReadOnlyList<Transform> Roots)
    {
        for (var i = 0; i < Roots.Count; i++) GeneratedRootIds.Add(Roots[i].GetInstanceID());
    }

    internal HashSet<int> SnapshotRootIds() => new(GeneratedRootIds);

    internal void RestoreRootIds(HashSet<int> RootIds)
    {
        GeneratedRootIds.Clear();
        GeneratedRootIds.UnionWith(RootIds);
    }

    internal void ClearRootIds() => GeneratedRootIds.Clear();

    internal sealed class Operation
    {
        internal int Generated { get; set; }
        internal Exception? Failure { get; set; }
    }
    internal static void CaptureAuthoredFeatures(
        MapHandler Handler,
        HashSet<int> Terrain,
        HashSet<int> Campfires)
    {
        foreach (var Segment in Handler.segments)
        {
            var Parent = Segment.segmentParent;
            if (Parent) Terrain.Add(Parent.GetInstanceID());
            var Campfire = Segment.segmentCampfire;
            if (Campfire && Campfire.GetComponentInChildren<global::Campfire>(true))
                Campfires.Add(Campfire!.GetInstanceID());
        }
    }
    internal static IEnumerator GenerateFeatures(
        int Index,
        List<PropGrouper> GeneratedGroupers,
        List<PropGrouper> AddedGroupers,
        FeatureGeneration.Operation Generation,
        MapRun.FeatureWriter Writer) =>
        GenerateFeatures(Index, 1, GeneratedGroupers, AddedGroupers, Generation, Writer, null);
    internal static IEnumerator GenerateFeatures(
        int Start,
        int Count,
        List<PropGrouper> GeneratedGroupers,
        List<PropGrouper> AddedGroupers,
        FeatureGeneration.Operation Generation,
        MapRun.FeatureWriter Writer,
        MapLoadingProgress? LoadingProgress)
    {
        Writer.RequireCurrent();
        var End = Math.Min(Start + Count, Map.Slots.Length);
        var Generated = new HashSet<FeatureOccurrence>();
        for (var i = Math.Max(0, Start); i < End; i++)
        {
            Writer.RequireCurrent();
            var Slot = Map.Slots[i];
            var Occurrence = Slot.Occurrence;
            if (!Generated.Add(Occurrence)) continue;
            LoadingProgress?.BeginUnit($"Generating {Slot.Source.Id}");
            var Future = new List<(Collider Collider, bool Enabled)>();
            var Seen = new HashSet<int>();
            var SeenColliders = new HashSet<int>();
            void Suppress(Transform? Root)
            {
                Writer.RequireCurrent();
                if (!Root || !Root.gameObject.activeInHierarchy
                    || !Seen.Add(Root.GetInstanceID())) return;
                foreach (var Collider in Root.GetComponentsInChildren<Collider>(true))
                {
                    if (!Collider || !SeenColliders.Add(Collider.GetInstanceID())) continue;
                    var Value = Collider.transform;
                    var Owned = false;
                    foreach (var Current in Occurrence.Roots)
                        if (TransformOwnership.Contains(Current, Value))
                        {
                            Owned = true;
                            break;
                        }
                    if (Owned) continue;
                    Future.Add((Collider, Collider.enabled));
                    if (Collider.enabled) Collider.enabled = false;
                }
            }
            try
            {
                int? LiveIndex = Map.CurrentRun!.Network.GenerationBarrier.GenerationRunning ? null : Map.Index;
                for (var j = 0; j < Map.Slots.Length; j++)
                {
                    if (ReferenceEquals(Map.Slots[j].Occurrence, Occurrence)
                        || LiveIndex is { } activeIndex && (j == activeIndex || j == activeIndex - 1))
                        continue;
                    var Other = Map.Slots[j];
                    var Container = Other.Container;
                    if (!Container) continue;
                    if (!TransformOwnership.Overlaps(Slot.Container, Container)) Suppress(Container);
                    else
                    {
                        Suppress(Other.Segment.segmentParent ? Other.Segment.segmentParent.transform : null);
                        Suppress(Other.Segment.segmentCampfire ? Other.Segment.segmentCampfire.transform : null);
                        foreach (var Supplemental in Other.Occurrence.SupplementalRoots)
                            Suppress(Supplemental);
                        Suppress(Other.PeakAttachment);
                    }
                }
                foreach (var Water in Map.CurrentRun!.Environment.WaterObjects)
                {
                    if (!Water || TransformOwnership.Overlaps(Slot.Container, Water.transform)) continue;
                    Suppress(Water.transform);
                }
                Physics.SyncTransforms();
                yield return RunFeatureRoot(
                    GenerateOccurrence(Occurrence,
                        GeneratedGroupers, AddedGroupers, Generation, Writer), Generation, Writer);
                if (Generation.Failure is not null) yield break;
                Writer.RequireCurrent();
            }
            finally
            {
                for (var j = Future.Count - 1; j >= 0; j--)
                {
                    var State = Future[j];
                    if (State.Collider && State.Collider.enabled != State.Enabled)
                        State.Collider.enabled = State.Enabled;
                }
                Physics.SyncTransforms();
            }
            LoadingProgress?.CompleteUnit();
        }
    }
    internal static IEnumerator RunFeatureRoot(
        IEnumerator Routine,
        FeatureGeneration.Operation Generation,
        MapRun.FeatureWriter Writer)
    {
        Exception? Failure = null;
        var Completed = false;
        try
        {
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
                catch (Exception Exception)
                {
                    Moved = false;
                    Failure = Exception;
                }
                if (!Moved)
                {
                    Completed = Failure is null;
                    break;
                }
                yield return Current;
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
                    : new AggregateException("Map feature generation and iterator cleanup both failed.", Failure, Exception);
            }
            if (!Completed && Failure is null) Failure = MapRun.FeatureWriter.Cancelled();
            if (Failure is not null) Generation.Failure = Failure;
        }
    }
    internal static bool NativeCampfireOptional(LayoutSegmentSource Source) =>
        Source is NadirLayoutSegmentSource
        || Source is NativeOrdinarySegmentSource { NativeRole: Segment.TheKiln };
    internal static bool NativeCaldera(LayoutSegmentSource Source) =>
        Source is NativeOrdinarySegmentSource
        { NativeRole: Segment.Caldera, IsNativeVariant: false };
    internal static bool RequiresCampfire(MapSlot Slot) =>
        !NativeCampfireOptional(Slot.Source)
        && Slot.ProgressionBoundary == NativeProgressionBoundaryKind.None;
    internal static IEnumerator GenerateOccurrence(
        FeatureOccurrence Occurrence,
        List<PropGrouper> GeneratedGroupers,
        List<PropGrouper> AddedGroupers,
        FeatureGeneration.Operation Generation,
        MapRun.FeatureWriter Writer)
    {
        Writer.RequireCurrent();
        var RequiresGeneration = !Occurrence.SupplementalReady;
        foreach (var Slot in Occurrence.Slots)
            if (!Slot.FeaturesReady)
            {
                RequiresGeneration = true;
                break;
            }
        if (!RequiresGeneration) yield break;

        var FeatureRoots = new List<Transform>(Occurrence.Roots.Count);
        var SeenRoots = new HashSet<int>();
        foreach (var Slot in Occurrence.Slots)
        {
            if (!Slot.TerrainReady) Add(Slot.Segment.segmentParent);
            if (Slot.CampfireReady) continue;
            if (!Slot.Segment.segmentCampfire && RequiresCampfire(Slot))
                throw new InvalidOperationException(
                    $"The {Slot.Source.Id} segment at layout position {Slot.Index + 1} lost its campfire feature root.");
            Add(Slot.Segment.segmentCampfire);
        }
        if (!Occurrence.SupplementalReady)
            for (var i = 0; i < Occurrence.SupplementalRoots.Count; i++)
            {
                var Root = Occurrence.SupplementalRoots[i];
                Occurrence.SupplementalContracts[i].ValidateBinding(Root, Occurrence.Container, false);
                Add(Root.gameObject);
            }
        if (FeatureRoots.Count == 0)
        {
            SetOccurrenceReady(Occurrence);
            yield break;
        }
        var FeatureRootSet = new NativeFeatureGeneration.FeatureRoots(FeatureRoots);
        var GeneratedRoots = 0;
        foreach (var Root in FeatureRootSet)
            if (Map.CurrentRun!.Features.ContainsRoot(Root)) GeneratedRoots++;
        if (GeneratedRoots != 0)
        {
            if (GeneratedRoots != FeatureRootSet.Count)
                throw new InvalidOperationException("A map occurrence has a partially published feature-root set.");
            SetOccurrenceReady(Occurrence);
            yield break;
        }

        var ResolvedConfiguration = Map.CurrentRun?.Configuration
            ?? throw new InvalidOperationException("Feature generation has no resolved run configuration.");
        var WasActive = new bool[FeatureRootSet.Count];
        for (var i = 0; i < WasActive.Length; i++)
            WasActive[i] = FeatureRootSet[i].gameObject.activeSelf;
        PropGrouper? Grouper = null;
        IEnumerator? Routine = null;
        Exception? Failure = null;
        var Added = false;
        BiomeAuthoringContract? Authoring = null;
        ResolvedSegment? Custom = null;
        NativeFlowRuntime[] FixedFlows = Array.Empty<NativeFlowRuntime>();
        NativeFeatureGeneration.FeatureOwnership? PreparedOwnership = null;
        foreach (var Slot in Occurrence.Slots)
        {
            if (Slot.CustomResolved is null) continue;
            if (Custom is not null && !ReferenceEquals(Custom, Slot.CustomResolved))
                throw new InvalidOperationException("A map occurrence contains multiple registered segment instances.");
            Custom = Slot.CustomResolved;
        }
        if (Custom is not null && !BiomeAuthoringCatalog.TryGet(Occurrence.Slots[0].Source.Id, out Authoring))
            throw new InvalidOperationException(
                $"Registered segment '{Occurrence.Slots[0].Source.Id}' lost its sealed authoring contract.");
        var RandomState = UnityEngine.Random.state;
        try
        {
            Writer.RequireCurrent();
            PreparedOwnership = new NativeFeatureGeneration.FeatureOwnership(
                FeatureRootSet, Occurrence.ExcludedHookedSubtrees,
                Custom is null ? null : Occurrence.GeometryBudget);
            PreparedOwnership.Outputs.PrepareSplines();
            foreach (var Slot in Occurrence.Slots)
            {
                if (!Slot.TerrainReady)
                    PlacementExecution.ActivateAndValidate(Slot, Slot.Segment.segmentParent
                        ?? throw new InvalidOperationException(
                            $"The {Slot.Source.Id} segment at layout position {Slot.Index + 1} lost its terrain feature root."));
                if (!Slot.CampfireReady && Slot.Segment.segmentCampfire)
                    PlacementExecution.ActivateAndValidate(Slot, Slot.Segment.segmentCampfire);
                else if (!Slot.CampfireReady && RequiresCampfire(Slot))
                    throw new InvalidOperationException(
                        $"The {Slot.Source.Id} segment at layout position {Slot.Index + 1} lost its campfire feature root.");
                NativeFixtures.ValidateActivatedCustomClone(Slot);
            }
            PreparedOwnership.Outputs.RequirePrepared();
            var Groupers = Occurrence.Container.GetComponents<PropGrouper>();
            if (Groupers.Length > 1)
                throw new InvalidOperationException("A map occurrence container contains multiple direct PropGroupers.");
            Grouper = Groupers.Length == 1 ? Groupers[0] : Occurrence.Container.gameObject.AddComponent<PropGrouper>();
            Added = Groupers.Length == 0;
            GeneratedGroupers.Add(Grouper);
            if (Added) AddedGroupers.Add(Grouper);
            Func<IDisposable?>? OpenExternalCollisionScope = null;
            foreach (var Slot in Occurrence.Slots)
            {
                if (Slot.CampfireReady || !NativeCaldera(Slot.Source)) continue;
                if (OpenExternalCollisionScope is not null)
                    throw new InvalidOperationException("A map occurrence contains multiple native Caldera campfire roots.");
                if (!Slot.NativeCampfireSupport)
                    throw new InvalidOperationException(
                        $"The native Caldera at layout position {Slot.Index + 1} lost its authored TheKiln support root.");
                if (!Slot.NativeCampfireSurface.HasValue)
                    throw new InvalidOperationException(
                        $"The native Caldera at layout position {Slot.Index + 1} lost its authored external campfire support.");
                var NativeCampfireSupport = Slot.NativeCampfireSupport!;
                var SpawnerRoot = Slot.Segment.segmentCampfire.transform;
                var NativeCampfireSurface = Slot.NativeCampfireSurface.Value;
                OpenExternalCollisionScope = () => new NativeTerrainMapProxy(
                    NativeCampfireSupport, Occurrence.Container, SpawnerRoot, NativeCampfireSurface);
            }
            Writer.RequireCurrent();
            NativeFeatureGeneration.GenerationOperation.Configuration? Configuration = null;
            if (!Occurrence.ConfigurationReady)
            {
                var VariationIndex = Authoring is null ? 0 : Occurrence.Slots[0].CountPreviousBiomeOccurrences();
                var Resolution = Authoring is null
                    ? null
                    : new NativeFeatureGeneration.GenerationOperation.VariationResolution(
                        Root => Authoring.PrepareVariations(Root,
                            (Group, Entry) => ResolvedConfiguration.Biomes[Authoring.Id]
                                .EffectiveVariationWeight(Group, Entry, VariationIndex)));
                Configuration = new NativeFeatureGeneration.GenerationOperation.Configuration(
                    Occurrence.RequiresNativeVariantSelection, Occurrence.ConfigurationSeed,
                    Occurrence.RecordVariations, Resolution,
                    Authoring is null ? () => ApplyNativeVariations(Occurrence, ResolvedConfiguration.Biomes) : null);
            }
            var RunSynchronously = Custom is null && !PhotonNetwork.OfflineMode && PhotonNetwork.CurrentRoom is not null;
            var Operation = new NativeFeatureGeneration.GenerationOperation(
                Grouper, FeatureRootSet, Occurrence.CollisionRoots,
                Occurrence.Slots[0].Frame, Occurrence.GenerationSeed,
                ResolvedConfiguration.Environment.NoSkyProps, RunSynchronously, Writer,
                Configuration, OpenExternalCollisionScope, Custom?.FinalForce,
                Authoring?.ResolveCriticalGeneration(Occurrence.Container), Occurrence.ExcludedHookedSubtrees,
                ActorGenerationOwner: Authoring is not null && (Authoring.Hazards.Count != 0 || Authoring.Entities.Count != 0)
                    ? Occurrence.Slots[0].Source.StableId : null,
                BindGeneratedFlows: Authoring is { HasGeneratedFlows: true }
                    ? (Prefab, Instance) => Authoring.BindGeneratedFlows(Prefab, Instance, Occurrence.GeometryBudget,
                        Occurrence.RequireCustomFrame(Authoring.Id)) : null,
                InitializeFixedFlows: Authoring is { HasFixedFlows: true }
                    ? Trace => Authoring.InitializeFixedFlows(FixedFlows, Trace) : null,
                PrepareFixedFlows: Authoring is { HasFixedFlows: true }
                    ? () => FixedFlows = Authoring.PrepareFixedFlows(
                        Custom!.Root.Value, Occurrence.GeometryBudget, Custom.FinalForce) : null,
                PreparedOwnership: PreparedOwnership,
                CompleteAuthoring: Authoring is not null ? () =>
                {
                    var Run = Map.CurrentRun!;
                    var NeedsPlatforms = false;
                    foreach (var Prop in Authoring.PropInteractionContracts)
                        if (Prop.Endpoints == PllVineEndpoints.NativePlatformConnection && Prop.Vine.HasValue)
                        { NeedsPlatforms = true; break; }
                    Run.PropInteractions.Enroll(Occurrence,
                        NeedsPlatforms ? NativeFeatureGeneration.CaptureOwnedTreePlatforms() : null, Occurrence.Container);
                    Run.SurfaceTextures.Enroll(Occurrence);
                    Run.NativeMaterials.Enroll(Occurrence);
                } : null);
            Routine = NativeFeatureGeneration.Run(Operation);
        }
        catch (Exception Exception)
        {
            Failure = Exception;
        }
        try
        {
            UnityEngine.Random.state = RandomState;
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException(
                    "Map feature setup and random-state cleanup both failed.", Failure, Exception);
        }

        void Cleanup(Action Action)
        {
            try { Action(); }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException(
                        "Map feature generation and support cleanup both failed.", Failure, Exception);
            }
        }

        var Completed = false;
        try
        {
            while (Failure is null && Routine is not null)
            {
                object? Current = null;
                bool Moved;
                try
                {
                    Writer.RequireCurrent();
                    Moved = Routine.MoveNext();
                    if (Moved) Current = Routine.Current;
                }
                catch (Exception Exception)
                {
                    Moved = false;
                    Failure = Exception;
                }
                if (!Moved) break;
                yield return Current;
            }

            if (Failure is null) try
            {
                Writer.RequireCurrent();
                Physics.SyncTransforms();
                Map.CurrentRun!.ClimbingFixtures.Enroll(Occurrence);
                if (Authoring is not null) Occurrence.EnrollActors(Authoring, Occurrence.Container);
                var Validated = new HashSet<int>();
                foreach (var Slot in Occurrence.Slots)
                {
                    if (!Slot.TerrainReady && Validated.Add(Slot.Segment.segmentParent.GetInstanceID()))
                        Authoring?.ValidateGeneratedPlacement(Slot.Segment.segmentParent.transform);
                    if (!Slot.CampfireReady && Slot.Segment.segmentCampfire)
                    {
                        var Root = Slot.Segment.segmentCampfire.transform;
                        if (RequiresCampfire(Slot) && !Root.GetComponentInChildren<global::Campfire>(true))
                            throw new InvalidOperationException(
                                $"PEAK did not generate a campfire for layout position {Slot.Index + 1}.");
                        if (Validated.Add(Root.GetInstanceID())) Authoring?.ValidateGeneratedPlacement(Root);
                    }
                    NativeFixtures.ValidateActivatedCustomClone(Slot);
                }
                Completed = true;
            }
            catch (Exception Exception)
            {
                Failure = Exception;
            }
        }
        finally
        {
            if (!Completed && Failure is null) Failure = MapRun.FeatureWriter.Cancelled();
            Cleanup(() => (Routine as IDisposable)?.Dispose());
            if (!NativeFeatureGeneration.HasLiveOperation) Cleanup(() =>
            {
                if (Failure is null && Occurrence.BatchIndex <= Map.Index) return;
                for (var i = 0; i < FeatureRootSet.Count; i++)
                {
                    var Root = FeatureRootSet[i];
                    if (Root) Root.gameObject.SetActive(WasActive[i]);
                }
            });
            if (!NativeFeatureGeneration.HasLiveOperation && Failure is not null && Custom is not null)
            {
                Cleanup(Occurrence.GeometryBudget.ReleaseAll);
                Cleanup(Custom.FinalForce.Rollback);
            }
            if (!NativeFeatureGeneration.HasLiveOperation && Failure is not null)
                Cleanup(() => PreparedOwnership?.Outputs.Dispose());
            if (Failure is not null)
            {
                Completed = false;
                Generation.Failure = Failure;
            }
        }
        if (!Completed) yield break;

        Writer.RequireCurrent();
        Occurrence.PublishFixedFlows(FixedFlows);
        SetOccurrenceReady(Occurrence);
        Occurrence.ConfigurationReady = true;
        Map.CurrentRun!.Features.TrackRoots(FeatureRootSet);
        Generation.Generated += FeatureRootSet.Count;

        void Add(GameObject? Value)
        {
            if (!Value || !SeenRoots.Add(Value.GetInstanceID())) return;
            FeatureRoots.Add(Value.transform);
        }
    }
    internal static void SetOccurrenceReady(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots)
        {
            if (!Slot.Segment.segmentParent)
                throw new InvalidOperationException("A missing or destroyed terrain root cannot be marked ready.");
            Slot.MarkTerrainReady();
            if (Slot.Segment.segmentCampfire || !RequiresCampfire(Slot)) Slot.MarkCampfireReady();
        }
        foreach (var Supplemental in Occurrence.SupplementalRoots)
            if (!Supplemental)
                throw new InvalidOperationException(
                    "A missing or destroyed supplemental native feature root cannot be marked ready.");
        Occurrence.SupplementalReady = true;
    }
    internal static void ResetGeneratedFeatures(
        IEnumerable<PropGrouper> GeneratedGroupers,
        IEnumerable<PropGrouper> AddedGroupers)
    {
        var Failures = new List<Exception>();
        var FailedGroupers = new HashSet<int>();
        foreach (var Grouper in GeneratedGroupers)
        {
            if (!Grouper) continue;
            try
            {
                NativeFeatureGeneration.Clear(Grouper);
                ResetSupplementalReadiness(Grouper);
            }
            catch (Exception Exception)
            {
                Failures.Add(Exception);
                FailedGroupers.Add(Grouper.GetInstanceID());
            }
        }
        foreach (var Grouper in AddedGroupers)
        {
            if (!Grouper || FailedGroupers.Contains(Grouper.GetInstanceID())) continue;
            try
            {
                UnityEngine.Object.DestroyImmediate(Grouper);
            }
            catch (Exception Exception)
            {
                Failures.Add(Exception);
            }
        }
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0)
            throw new AggregateException("Generated map feature rollback failed.", Failures);

        static void ResetSupplementalReadiness(PropGrouper Grouper)
        {
            var Slots = Map.Pending?.Slots ?? Map.Slots;
            foreach (var Slot in Slots)
            {
                var Occurrence = Slot.Occurrence;
                if (Occurrence.SupplementalRoots.Count == 0 || Occurrence.Container != Grouper.transform)
                    continue;
                Occurrence.SupplementalReady = false;
                return;
            }
        }
    }

    internal static int FeatureSeed(int Seed, int Index, string Name)
    {
        var Hash = (2166136261u ^ unchecked((uint)Seed) ^ unchecked((uint)Index)) * 16777619u;
        foreach (var Character in Name) Hash = (Hash ^ Character) * 16777619u;
        return unchecked((int)(Hash == 0 ? 0x9E3779B9u : Hash));
    }

    internal static int MapSeed() => Map.CurrentRun?.Configuration.Layout.Seed
        ?? throw new InvalidOperationException("The current map run has no resolved configuration.");
    static (bool SelectorRequired, VariationSwapper[] ForcedSwappers) ApplyNativeVariations(
        FeatureOccurrence Occurrence, BiomeLayoutConfigSnapshot Configuration)
    {
        var Forced = false;
        var ForcedSwappers = new List<VariationSwapper>();
        foreach (var Slot in Occurrence.Slots)
        {
            if (Slot.Source is CustomLayoutSegmentSource) continue;
            var Selection = Configuration.NativeVariations[Slot.Source.Id];
            if (Selection.IsRandom) continue;
            var Selected = Selection.At(Slot.CountPreviousBiomeOccurrences());
            var Root = Slot.Segment.segmentParent.transform;
            if (Selected.StartsWith("Terrain: ", StringComparison.Ordinal))
            {
                var PreviousCount = ForcedSwappers.Count;
                foreach (var Swapper in Root.GetComponentsInChildren<VariationSwapper>(true))
                {
                    GameObject? SelectedRoot = null;
                    foreach (var Variation in Swapper.Variations)
                        if (NativeSegmentCatalog.VariationToken(Variation.parent.transform) == Selected)
                        {
                            SelectedRoot = Variation.parent;
                            break;
                        }
                    if (!SelectedRoot) continue;
                    foreach (var Variation in Swapper.Variations)
                        Variation.parent.SetActive(Variation.parent == SelectedRoot);
                    ForcedSwappers.Add(Swapper);
                }
                if (ForcedSwappers.Count == PreviousCount)
                    throw new InvalidOperationException($"Native biome '{Slot.Source.Id}' lost selected variation '{Selected}'.");
                continue;
            }
            var Variants = Slot.Segment.segmentParent.GetComponentsInChildren<VariantObject>(true);
            VariantObject? Chosen = null;
            foreach (var Variant in Variants)
                if (NativeSegmentCatalog.VariationToken(Variant) == Selected)
                {
                    Chosen = Variant;
                    break;
                }
            if (!Chosen)
                throw new InvalidOperationException($"Native biome '{Slot.Source.Id}' lost selected variation '{Selected}'.");
            foreach (var Variant in Variants)
                if (Variant.group == Chosen.group) Variant.spawnChance = Variant == Chosen ? 1f : 0f;
            Forced = true;
        }
        return (Forced, ForcedSwappers.ToArray());
    }
    internal static Pose? PassivePeakPose(
        IReadOnlyList<LayoutSegmentSource> PlacementSources, LayoutSegmentSource Owner)
    {
        if (Owner is not NativeOrdinarySegmentSource
            { NativeRole: Segment.TheKiln, ContextualPeak: { } Peak }
            || PlacementPlan.Selected(PlacementSources, Peak)) return null;
        return new Pose(Peak.HashPose.position + Peak.Translation, Peak.HashPose.rotation);
    }
}
