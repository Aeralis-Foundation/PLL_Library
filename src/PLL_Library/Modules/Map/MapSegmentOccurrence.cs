using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal readonly struct VariationSelection
{
    internal VariationSelection(string Id, string RootPath)
    {
        this.Id = Id;
        this.RootPath = RootPath;
    }

    internal string Id { get; }
    internal string RootPath { get; }
}

internal sealed class FeatureOccurrence
{
    VariationSelection[] VariationValues = Array.Empty<VariationSelection>();
    MapSlot[] SlotValues = Array.Empty<MapSlot>();
    Transform[] RootValues = Array.Empty<Transform>();
    Transform[] CollisionRootValues = Array.Empty<Transform>();
    readonly NativeFeatureSourceContract.SupplementalRootContract[] SupplementalContractValues;
    readonly Transform[] SupplementalRootValues;
    bool ConfigurationCaptured;
    bool Bound;
    readonly List<(HazardAuthoringContract Definition, HazardInstance Instance)> HazardInstances = new();
    readonly List<(EntityAuthoringContract Definition, EntityBinding Instance)> EntityInstances = new();

    internal FeatureOccurrence(
        MapRun Run, Transform Container,
        Transform SourceContainer,
        IReadOnlyList<NativeFeatureSourceContract.SupplementalRootContract> SupplementalContracts,
        bool RequiresNativeVariantSelection,
        int ConfigurationSeed, int GenerationSeed, int ActivationSeed)
    {
        this.Run = Run; this.Container = Container;
        this.RequiresNativeVariantSelection = RequiresNativeVariantSelection;
        this.ConfigurationSeed = ConfigurationSeed;
        this.GenerationSeed = GenerationSeed;
        this.ActivationSeed = ActivationSeed;
        this.SupplementalContractValues = new NativeFeatureSourceContract.SupplementalRootContract[SupplementalContracts.Count];
        SupplementalRootValues = new Transform[SupplementalContracts.Count];
        for (var i = 0; i < SupplementalContracts.Count; i++)
        {
            var Contract = SupplementalContracts[i]
                ?? throw new InvalidOperationException("A supplemental native feature root has no source contract.");
            var Root = DeterministicObjectIdentity.ResolveHierarchy(Contract.Source, SourceContainer, Container)
                ?? throw new InvalidOperationException(
                    "A placed occurrence is missing a supplemental native feature root.");
            Contract.ValidateBinding(Root, Container);
            this.SupplementalContractValues[i] = Contract;
            SupplementalRootValues[i] = Root;
        }
        SupplementalReady = SupplementalRootValues.Length == 0;
    }

    internal Transform Container { get; }
    internal MapRun Run { get; }
    internal bool RequiresNativeVariantSelection { get; }
    internal int ConfigurationSeed { get; }
    internal int GenerationSeed { get; }
    internal int ActivationSeed { get; }
    internal IReadOnlyList<VariationSelection> Variations => VariationValues;
    internal IReadOnlyList<MapSlot> Slots => SlotValues;
    internal IReadOnlyList<Transform> Roots => RootValues;
    internal IReadOnlyList<Transform> CollisionRoots => CollisionRootValues;
    internal GeneratedGeometryBudget GeometryBudget { get; } = new();
    internal NativeFlowRuntime[] FixedFlows { get; private set; } = Array.Empty<NativeFlowRuntime>();
    internal EntityZoneBinding[] EntityZones { get; private set; } = Array.Empty<EntityZoneBinding>();
    internal HazardZoneBinding[] HazardZones { get; private set; } = Array.Empty<HazardZoneBinding>();
    internal HookedPropTarget[] HookedTargets { get; private set; } = Array.Empty<HookedPropTarget>();
    internal Transform[] ExcludedHookedSubtrees { get; private set; } = Array.Empty<Transform>();
    internal IReadOnlyList<NativeFeatureSourceContract.SupplementalRootContract> SupplementalContracts =>
        SupplementalContractValues;
    internal IReadOnlyList<Transform> SupplementalRoots => SupplementalRootValues;
    internal int BatchIndex => SlotValues.Length == 0
        ? throw new InvalidOperationException("A map occurrence has no bound layout slots.")
        : SlotValues[SlotValues.Length - 1].Index;
    internal int RequestIndex => SlotValues.Length != 0 && SlotValues[0].Index == 0 ? 0 : BatchIndex;
    internal bool ConfigurationReady { get; set; }
    internal bool SupplementalReady { get; set; }

    internal void PublishFixedFlows(NativeFlowRuntime[] Sources) => FixedFlows = Sources;

    internal Transform RequireCustomFrame(BiomeId Biome)
    {
        foreach (var Slot in SlotValues)
            if (Slot.CustomResolved is { } Resolved
                && BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract) && Contract.Id == Biome)
                return Resolved.Root.Value;
        throw new InvalidOperationException($"Occurrence {RequestIndex} has no custom frame for biome '{Biome}'.");
    }

    internal void EnrollActors(BiomeAuthoringContract Contract, Transform DefinitionRoot, Transform? Scope = null)
    {
        foreach (var Definition in Contract.Hazards)
        {
            if (Definition.Generated) continue;
            var Source = Definition.ResolveFixedSource(DefinitionRoot, Contract.Id);
            if (Source.Binding is not null || Scope && !TransformOwnership.Contains(Scope!, Source.transform)) continue;
            HazardInstances.Add((Definition, Definition.BindFixed(DefinitionRoot, Contract.Id, this, Definition.FixedPlacementOrdinal)));
        }
        foreach (var Definition in Contract.Entities)
        {
            if (Definition.Generated) continue;
            var Source = Definition.ResolveFixedSource(DefinitionRoot, Contract.Id);
            if (Source.Binding is not null || Scope && !TransformOwnership.Contains(Scope!, Source.transform)) continue;
            EntityInstances.Add((Definition, Definition.BindFixed(DefinitionRoot, Contract.Id, this, Definition.FixedPlacementOrdinal)));
        }
        var Root = Scope ? Scope! : DefinitionRoot;
        foreach (var Source in Root.GetComponentsInChildren<PllHazardAuthoring>(true))
        {
            if (Source.Binding is not null) continue;
            var Identity = Source.GetComponentInParent<PllGeneratedPrefabIdentity>();
            if (!Identity || Identity.ActorPlacement is not { } Placement)
                throw new InvalidOperationException("A placed Hazard has no generation-owned output identity.");
            var Matched = false;
            foreach (var Definition in Contract.Hazards)
                if (Definition.Generated && Definition.TryBindGenerated(Source, Contract.Id, this, Placement.Ordinal, out var Instance))
                {
                    HazardInstances.Add((Definition, Instance)); Matched = true; break;
                }
            if (!Matched) throw new InvalidOperationException("A generated Hazard has no sealed definition.");
        }
        foreach (var Source in Root.GetComponentsInChildren<PllEntityAuthoring>(true))
        {
            if (Source.Binding is not null) continue;
            var Identity = Source.GetComponentInParent<PllGeneratedPrefabIdentity>();
            if (!Identity || Identity.ActorPlacement is not { } Placement)
                throw new InvalidOperationException("A placed Entity has no generation-owned output identity.");
            var Matched = false;
            foreach (var Definition in Contract.Entities)
                if (Definition.Generated && Definition.TryBindGenerated(Source, Contract.Id, this, Placement.Ordinal, out var Instance))
                {
                    EntityInstances.Add((Definition, Instance)); Matched = true; break;
                }
            if (!Matched) throw new InvalidOperationException("A generated Entity has no sealed definition.");
        }
    }

    internal void PublishActors(Transform? Scope = null)
    {
        if (!Run.Active || !Run.IsOccurrenceReady(this)) return;
        foreach (var Entry in HazardInstances)
            if (Entry.Instance.Source && (!Scope || TransformOwnership.Contains(Scope!, Entry.Instance.Source.transform)))
            {
                if (Run.HookedGeneration.TryGetActorSourceCommand(this, Entry.Definition.Source, out var Target)) Entry.Instance.SetEnabled(Target);
                Entry.Instance.Publish();
            }
        foreach (var Entry in EntityInstances)
            if (Entry.Instance.Source && (!Scope || TransformOwnership.Contains(Scope!, Entry.Instance.Source.transform)))
            {
                if (Run.HookedGeneration.TryGetActorSourceCommand(this, Entry.Definition.Source, out var Target)) Entry.Instance.SetEnabled(Target);
                Entry.Instance.Publish();
            }
    }

    internal void ApplyActorSourceCommands()
    {
        foreach (var Entry in HazardInstances)
            if (Entry.Instance.Source && Run.HookedGeneration.TryGetActorSourceCommand(this, Entry.Definition.Source, out var Target))
                Entry.Instance.SetEnabled(Target);
        foreach (var Entry in EntityInstances)
            if (Entry.Instance.Source && Run.HookedGeneration.TryGetActorSourceCommand(this, Entry.Definition.Source, out var Target))
                Entry.Instance.SetEnabled(Target);
    }

    internal void RetireActors(GameObject Root)
    {
        List<Exception>? Failures = null;
        for (var Index = HazardInstances.Count - 1; Index >= 0; Index--)
        {
            var Instance = HazardInstances[Index].Instance;
            if (Instance.Source && !TransformOwnership.Contains(Root.transform, Instance.Source.transform)) continue;
            CleanupOperations.Capture(ref Failures, Instance.Retire);
            if (Instance.Source) Instance.Source.Binding = null;
            HazardInstances.RemoveAt(Index);
        }
        for (var Index = EntityInstances.Count - 1; Index >= 0; Index--)
        {
            var Instance = EntityInstances[Index].Instance;
            if (Instance.Source && !TransformOwnership.Contains(Root.transform, Instance.Source.transform)) continue;
            CleanupOperations.Capture(ref Failures, Instance.Retire);
            if (Instance.Source) Instance.Source.Binding = null;
            EntityInstances.RemoveAt(Index);
        }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Actor instance retirement failed.", Failures);
    }

    internal void Bind(MapSlot[] OccurrenceSlots, EntityZoneBinding[] EntityZones, HazardZoneBinding[] HazardZones)
    {
        if (Bound) throw new InvalidOperationException("A map occurrence repeated its slot binding.");
        if (OccurrenceSlots.Length == 0)
            throw new InvalidOperationException("A map occurrence requires at least one layout slot.");
        var OwnedRoots = new List<Transform>(OccurrenceSlots.Length * 2 + SupplementalRootValues.Length);
        var Seen = new HashSet<int>();
        foreach (var Slot in OccurrenceSlots)
        {
            if (!ReferenceEquals(Slot.Occurrence, this))
                throw new InvalidOperationException("A map occurrence received a foreign layout slot.");
            Add(Slot.Segment.segmentParent);
            Add(Slot.Segment.segmentCampfire);
        }
        foreach (var Supplemental in SupplementalRootValues)
        {
            foreach (var Owned in OwnedRoots)
                if (TransformOwnership.Overlaps(Supplemental, Owned))
                    throw new InvalidOperationException(
                        "A supplemental native feature root overlaps a declared slot root.");
            Add(Supplemental.gameObject);
        }
        SlotValues = OccurrenceSlots;
        if (OccurrenceSlots[0].CustomResolved is { } Custom)
        {
            if (!BiomeAuthoringCatalog.TryGet(OccurrenceSlots[0].Source.Id, out var Contract))
                throw new InvalidOperationException("A custom occurrence lost its sealed authoring contract.");
            HookedTargets = new HookedPropTarget[Contract.HookedGeneration.Count];
            ExcludedHookedSubtrees = new Transform[HookedTargets.Length];
            for (var i = 0; i < HookedTargets.Length; i++)
            {
                HookedTargets[i] = Contract.HookedGeneration[i].Resolve(Custom.Root.Value, Contract.Id, this, i);
                ExcludedHookedSubtrees[i] = HookedTargets[i].Root;
            }
        }
        this.EntityZones = EntityZones;
        foreach (var Zone in EntityZones)
            if (Zone is PluginEntityPopulation Plugin) Plugin.AttachOccurrence(this);
        this.HazardZones = HazardZones;
        foreach (var Zone in HazardZones) Zone.AttachOccurrence(this);
        RootValues = OwnedRoots.ToArray();
        CollisionRootValues = RootValues;
        for (var i = 0; i < SupplementalRootValues.Length; i++)
        {
            if (!SupplementalContractValues[i].UsesParentCollisionDomain) continue;
            if (ReferenceEquals(CollisionRootValues, RootValues))
                CollisionRootValues = (Transform[])RootValues.Clone();
            CollisionRootValues[RootValues.Length - SupplementalRootValues.Length + i] = SupplementalRootValues[i].parent;
        }
        Bound = true;

        void Add(GameObject? Value)
        {
            if (!Value || !Seen.Add(Value.GetInstanceID())) return;
            var Root = Value.transform;
            if (Root != Container && !Root.IsChildOf(Container))
                throw new InvalidOperationException("A feature root left its occurrence container.");
            OwnedRoots.Add(Root);
        }
    }

    internal void PublishCurrentEntityPopulation()
    {
        if (EntityZones.Length == 0 || Map.CurrentRun is not { } Run
            || !Run.NativeSpawns.Ready(this)) return;
        try
        {
            foreach (var Zone in EntityZones) Zone.Publish();
        }
        catch (Exception Failure)
        {
            if (ReferenceEquals(Map.CurrentRun, Run))
                try { LazySlotGenerator.AbortLazyGeneration(Run, BatchIndex, "Entity population admission failed", Failure); }
                catch (Exception Cleanup)
                {
                    throw new AggregateException("Entity admission and run abort both failed.", Failure, Cleanup);
                }
            throw;
        }
    }

    internal void RetirePluginEntityPopulation()
    {
        List<Exception>? Failures = null;
        foreach (var Zone in EntityZones)
            if (Zone is PluginEntityPopulation Plugin)
                CleanupOperations.Capture(ref Failures, Plugin.Retire);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Plugin entity population retirement failed.", Failures);
    }

    internal void RecordVariations(VariationSelection[] Selected)
    {
        if (ConfigurationCaptured)
            throw new InvalidOperationException("A map occurrence repeated its variation configuration.");
        VariationValues = Selected ?? throw new ArgumentNullException(nameof(Selected));
        ConfigurationCaptured = true;
        ConfigurationReady = true;
    }
}

internal enum NativeProgressionBoundaryKind : byte { None, PeakBridge, NadirGate }

internal readonly struct PlacementFrame
{
    readonly bool HasRotation;
    readonly Quaternion NormalizedRotation;
    readonly Quaternion InverseRotation;

    internal PlacementFrame(Vector3 Offset)
    {
        Pivot = Vector3.zero;
        this.Offset = Offset;
        NormalizedRotation = Quaternion.identity;
        InverseRotation = Quaternion.identity;
        HasRotation = false;
    }

    internal PlacementFrame(Vector3 Pivot, Vector3 Offset, Quaternion Rotation)
    {
        this.Pivot = Pivot;
        this.Offset = Offset;
        this.NormalizedRotation = Rotation.normalized;
        InverseRotation = Quaternion.Inverse(this.NormalizedRotation);
        HasRotation = true;
    }

    internal Vector3 Pivot { get; }
    internal Vector3 Offset { get; }
    internal Quaternion Rotation => HasRotation ? NormalizedRotation : Quaternion.identity;
    internal bool Rotated => HasRotation;

    internal Vector3 Point(Vector3 Value) => HasRotation
        ? Pivot + Offset + NormalizedRotation * (Value - Pivot)
        : Value + Offset;

    internal Vector3 InversePoint(Vector3 Value) => HasRotation
        ? Pivot + InverseRotation * (Value - Pivot - Offset)
        : Value - Offset;

    internal Vector3 Direction(Vector3 Value) => HasRotation ? NormalizedRotation * Value : Value;

    internal Quaternion Rotate(Quaternion Value) => HasRotation ? NormalizedRotation * Value : Value;

    internal Pose Pose(Pose Value) => new(Point(Value.position), Rotate(Value.rotation));

    internal Bounds TransformBounds(Bounds Value)
    {
        if (!HasRotation)
        {
            Value.center += Offset;
            return Value;
        }

        var X = NormalizedRotation * new Vector3(Value.extents.x, 0f, 0f);
        var Y = NormalizedRotation * new Vector3(0f, Value.extents.y, 0f);
        var Z = NormalizedRotation * new Vector3(0f, 0f, Value.extents.z);
        Value.center = Point(Value.center);
        Value.extents = new Vector3(
            Math.Abs(X.x) + Math.Abs(Y.x) + Math.Abs(Z.x),
            Math.Abs(X.y) + Math.Abs(Y.y) + Math.Abs(Z.y),
            Math.Abs(X.z) + Math.Abs(Y.z) + Math.Abs(Z.z));
        return Value;
    }
}

internal sealed class MapSlot
{
    internal abstract class NadirFacet
    {
        NadirFacet() { }

        internal sealed class Absent : NadirFacet
        {
            internal static Absent Instance { get; } = new();
            Absent() { }
        }

        internal sealed class Placed : NadirFacet
        {
            internal Placed(
                Transform Gate, Transform Peak, Transform Ambience,
                Peak.ForceHeroTextTrigger HeroTitle,
                Transform[] SpawnPoints, Transform VoidWaterRoot, bool RequiresScoutsHonor)
            {
                if (!Gate || !Peak || !Ambience || !HeroTitle || !VoidWaterRoot
                    || SpawnPoints.Length != 4)
                    throw new InvalidOperationException("A placed Nadir facet is incomplete.");
                for (var i = 0; i < SpawnPoints.Length; i++)
                    if (!SpawnPoints[i]) throw new InvalidOperationException("A placed Nadir spawn point is invalid.");
                this.Gate = Gate;
                this.Peak = Peak;
                this.Ambience = Ambience;
                this.HeroTitle = HeroTitle;
                this.SpawnPoints = (Transform[])SpawnPoints.Clone();
                this.VoidWaterRoot = VoidWaterRoot;
                this.RequiresScoutsHonor = RequiresScoutsHonor;
            }

            internal Transform Gate { get; }
            internal Transform Peak { get; }
            internal Transform Ambience { get; }
            internal Peak.ForceHeroTextTrigger HeroTitle { get; }
            internal Transform[] SpawnPoints { get; }
            internal Transform VoidWaterRoot { get; }
            internal bool RequiresScoutsHonor { get; }
        }
    }

    internal sealed class PeakBridgeTarget
    {
        internal PeakBridgeTarget(
            Transform Root,
            PeakAttachmentSource Source,
            Peak.ScoutStatue Statue)
        {
            if (!Root || Source is null || !Statue || Statue.transform.parent != Root
                || !Statue.gemSpot || !TransformOwnership.Contains(Statue.transform, Statue.gemSpot))
                throw new InvalidOperationException("A Peak bridge target is incomplete.");
            this.Root = Root;
            this.Source = Source;
            this.Statue = Statue;
            var SourcePose = new Pose(
                Source.AuthoredPosition(Source.BridgeCampfire),
                Source.AuthoredRotation(Source.BridgeCampfire));
            CampfirePose = PlacementPlan.PeakFrame(Source, Root).Pose(SourcePose);
            GemSpot = Statue.gemSpot;
        }

        internal Transform Root { get; }
        internal PeakAttachmentSource Source { get; }
        internal Peak.ScoutStatue Statue { get; }
        internal Pose CampfirePose { get; }
        internal Transform GemSpot { get; }
    }

    internal abstract class PeakAttachmentFacet
    {
        PeakAttachmentFacet() { }

        internal sealed class Absent : PeakAttachmentFacet
        {
            internal static Absent Instance { get; } = new();
            Absent() { }
        }

        internal sealed class Attached : PeakAttachmentFacet
        {
            internal Attached(Transform Attachment, Transform Respawn, PeakAttachmentSource Source)
            {
                if (!Attachment || !Respawn) throw new InvalidOperationException("A Peak attachment facet is incomplete.");
                this.Attachment = Attachment;
                this.Respawn = Respawn;
                this.Source = Source ?? throw new ArgumentNullException(nameof(Source));
            }

            internal Transform Attachment { get; }
            internal Transform Respawn { get; }
            internal PeakAttachmentSource Source { get; }
        }
    }

    PeakAttachmentFacet PeakFacet = PeakAttachmentFacet.Absent.Instance;

    internal MapSlot(
        int Index, LayoutSegmentSource Source, MapHandler.MapSegment Segment,
        NadirFacet Nadir, FeatureOccurrence Occurrence,
        NativeProgressionBoundaryKind ProgressionBoundary,
        Transform? NativeCampfireSupport,
        NativeTerrainMapProxy.ExternalSupport? NativeCampfireSurface,
        ResolvedSegment? CustomResolved, NativeModifierRuntime? Modifiers,
        PlacementFrame Frame, bool IsClone, bool TerrainReady, bool CampfireReady)
    {
        this.Index = Index;
        this.Source = Source;
        this.Segment = Segment;
        this.Nadir = Nadir ?? throw new ArgumentNullException(nameof(Nadir));
        this.Occurrence = Occurrence;
        this.ProgressionBoundary = ProgressionBoundary;
        this.NativeCampfireSupport = NativeCampfireSupport;
        this.NativeCampfireSurface = NativeCampfireSurface;
        this.CustomResolved = CustomResolved;
        this.Modifiers = Modifiers;
        this.Frame = Frame;
        this.IsClone = IsClone;
        this.TerrainReady = TerrainReady;
        this.CampfireReady = CampfireReady;
    }

    internal int Index { get; }
    internal LayoutSegmentSource Source { get; }
    internal MapHandler.MapSegment Segment { get; }
    internal NadirFacet Nadir { get; }
    internal NadirFacet.Placed? PlacedNadir => Nadir as NadirFacet.Placed;
    internal Transform? NadirGate => PlacedNadir?.Gate;
    internal Transform? NadirPeak => PlacedNadir?.Peak;
    internal Transform? NadirAmbience => PlacedNadir?.Ambience;
    internal Peak.ForceHeroTextTrigger? NadirHeroTitle => PlacedNadir?.HeroTitle;
    internal Transform? VoidWaterRoot => PlacedNadir?.VoidWaterRoot;
    internal bool NadirRequiresScoutsHonor => PlacedNadir?.RequiresScoutsHonor == true;
    internal FeatureOccurrence Occurrence { get; }
    internal Transform Container => Occurrence.Container;
    internal NativeProgressionBoundaryKind ProgressionBoundary { get; }
    internal Transform? NativeCampfireSupport { get; }
    internal NativeTerrainMapProxy.ExternalSupport? NativeCampfireSurface { get; }
    internal ResolvedSegment? CustomResolved { get; }
    internal NativeModifierRuntime? Modifiers { get; }
    internal PlacementFrame Frame { get; }
    internal Vector3 Offset => Frame.Offset;
    internal bool IsClone { get; }
    internal bool TerrainReady { get; private set; }
    internal bool CampfireReady { get; private set; }
    internal PeakAttachmentFacet PeakAttachmentState => PeakFacet;
    internal Transform? PeakAttachment => (PeakFacet as PeakAttachmentFacet.Attached)?.Attachment;
    internal Transform? PeakRespawn => (PeakFacet as PeakAttachmentFacet.Attached)?.Respawn;
    internal PeakAttachmentSource? PeakSource => (PeakFacet as PeakAttachmentFacet.Attached)?.Source;
    internal PeakBridgeTarget? BridgeTarget { get; private set; }
    internal Peak.ScoutStatue? BridgeStatue => BridgeTarget?.Statue;
    internal GloomVoiceRuntime.GloomVoiceClone? GloomVoice { get; private set; }
    internal bool ViewBatchReady { get; private set; } = true;
    internal bool FeaturesActivated { get; private set; }
    internal bool Released { get; private set; }
    internal Segment NavigationRole => NavigationRoles.Encode(Index);
    internal Segment? NativeRole => (Source as INativeLayoutSegmentSource)?.NativeRole;
    internal Vector3 PlacedEntry => Frame.Point(Source.AuthoredPosition(Source.EntryPorts[0].Pose));
    internal Vector3 PlacedExit => Frame.Point(Source.AuthoredPosition(Source.ExitPorts[0].Pose));
    internal Vector3 PlacedProgressionAxis => Frame.Direction(Source.ProgressionAxis);
    internal bool FeaturesReady => TerrainReady && CampfireReady && Occurrence.SupplementalReady;

    internal int CountPreviousBiomeOccurrences()
    {
        var Custom = Source as CustomLayoutSegmentSource;
        var Limit = Custom is null ? Index : Occurrence.Slots[0].Index;
        var Count = 0;
        for (var i = 0; i < Limit; i++)
        {
            var Previous = Occurrence.Run.Slots[i];
            if (Custom is null ? Previous.Source.StableId == Source.StableId
                : Previous.Source is CustomLayoutSegmentSource PreviousCustom
                    && PreviousCustom.MetadataBiome == Custom.MetadataBiome
                    && Previous.Index == Previous.Occurrence.Slots[0].Index) Count++;
        }
        return Count;
    }

    internal void AttachPeak(Transform Attachment, Transform Respawn, PeakAttachmentSource Source)
    {
        if (PeakFacet is not PeakAttachmentFacet.Absent)
            throw new InvalidOperationException("A map slot already owns a Peak attachment.");
        PeakFacet = new PeakAttachmentFacet.Attached(Attachment, Respawn, Source);
    }

    internal PeakAttachmentFacet DetachPeak()
    {
        var Previous = PeakFacet;
        PeakFacet = PeakAttachmentFacet.Absent.Instance;
        return Previous;
    }

    internal void RestorePeak(PeakAttachmentFacet Previous)
    {
        if (PeakFacet is not PeakAttachmentFacet.Absent)
            throw new InvalidOperationException("A map slot cannot overwrite a live Peak attachment.");
        PeakFacet = Previous ?? throw new ArgumentNullException(nameof(Previous));
    }

    internal void BindBridgeTarget(Peak.ScoutStatue Statue)
    {
        if (BridgeTarget is not null) throw new InvalidOperationException("A map slot already owns a Peak bridge target.");
        if (!Statue) throw new ArgumentNullException(nameof(Statue));
        Transform Root;
        PeakAttachmentSource Source;
        if (PeakFacet is PeakAttachmentFacet.Attached Full)
        {
            Root = Full.Attachment;
            Source = Full.Source;
        }
        else if (this.Source is NativeOrdinarySegmentSource { ContextualPeak: { } Passive })
        {
            Root = Statue.transform.parent
                ?? throw new InvalidOperationException("A passive Peak bridge statue lost its root.");
            Source = Passive;
        }
        else throw new InvalidOperationException("A Peak bridge target has no exact source binding.");
        BridgeTarget = new PeakBridgeTarget(Root, Source, Statue);
    }

    internal void BindGloomVoice(GloomVoiceRuntime.GloomVoiceClone Binding)
    {
        if (GloomVoice is not null) throw new InvalidOperationException("A map slot already owns a Gloom voice binding.");
        GloomVoice = Binding ?? throw new ArgumentNullException(nameof(Binding));
    }

    internal void MarkTerrainReady() => TerrainReady = true;
    internal void MarkCampfireReady() => CampfireReady = true;
    internal void RestoreReadiness(bool Terrain, bool Campfire)
    {
        TerrainReady = Terrain;
        CampfireReady = Campfire;
    }
    internal void ClearReadiness()
    {
        TerrainReady = false;
        CampfireReady = false;
    }
    internal void MarkViewBatchPending() => ViewBatchReady = false;
    internal void MarkViewBatchReady() => ViewBatchReady = true;
    internal void RestoreViewBatchReadiness(bool Ready) => ViewBatchReady = Ready;
    internal void MarkFeaturesActivated() => FeaturesActivated = true;
    internal void MarkReleased() => Released = true;
    internal void RestoreReleased() => Released = false;
}
