using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PLL.Library;

internal sealed class HookedPropGeneration
{
    enum ZoneActions { Generation = 1, Fog = 2, NativeSource = 4, Interaction = 8 }

    sealed class Declaration
    {
        internal Declaration(FeatureOccurrence Occurrence, BiomeAuthoringContract Authoring, GeneratedPropAssetCatalog? Catalog,
            EffectZoneAuthoringContract[] Zones, ResolvedEffectZone[][] ResolvedZones,
            IReadOnlyList<FogZoneAuthoringContract> FogZones)
        {
            this.Occurrence = Occurrence; this.Authoring = Authoring; this.Catalog = Catalog;
            this.Zones = Zones; this.ResolvedZones = ResolvedZones;
            this.FogZones = FogZones;
            for (var i = 0; i < Zones.Length; i++)
            {
                ActionCount += Zones[i].PropTriggers.Count;
                FogActionCount += Zones[i].FogTriggers.Count;
                NativeSourceActionCount += Zones[i].NativeSourceTriggers.Count;
                ActorSourceActionCount += Zones[i].ActorSourceActionCount;
                InteractionActionCount += Zones[i].InteractionTriggers.Count;
                ZoneIndices.Add(Zones[i], i);
            }
            Observed = new bool[Zones.Length];
            FogObserved = new bool[Zones.Length];
            NativeSourceObserved = NativeSourceActionCount == 0 ? Array.Empty<bool>() : new bool[Zones.Length];
            var ReferencedSources = new HashSet<int>();
            foreach (var Zone in Zones)
                foreach (var Action in Zone.NativeSourceTriggers) ReferencedSources.Add(Action.TargetIndex);
            SourceTargets = ReferencedSources.Count == 0 ? Array.Empty<BiomeAuthoringContract.SourceActivationTarget>()
                : Authoring.SourceActivationTargets.Where(Source => ReferencedSources.Contains(Source.TargetIndex)).ToArray();
            for (var i = 0; i < SourceTargets.Length; i++) NativeSourceIndices.Add(SourceTargets[i].TargetIndex, i);
            InteractionObserved = InteractionActionCount == 0 ? Array.Empty<bool>() : new bool[Zones.Length];
            var ReferencedProps = new HashSet<int>();
            foreach (var Zone in Zones)
                foreach (var Action in Zone.InteractionTriggers) ReferencedProps.Add(Action.TargetIndex);
            PropTargets = new PropInteractionContract[ReferencedProps.Count];
            var PropIndex = 0;
            for (var i = 0; i < Authoring.PropInteractionContracts.Count; i++)
                if (ReferencedProps.Contains(i))
                {
                    var Prop = Authoring.PropInteractionContracts[i];
                    PropTargets[PropIndex] = Prop; PropIndices.Add(Prop, PropIndex++);
                }
        }
        internal FeatureOccurrence Occurrence { get; }
        internal BiomeAuthoringContract Authoring { get; }
        internal GeneratedPropAssetCatalog? Catalog { get; }
        internal EffectZoneAuthoringContract[] Zones { get; }
        internal ResolvedEffectZone[][] ResolvedZones { get; }
        internal Dictionary<EffectZoneAuthoringContract, int> ZoneIndices { get; } = new();
        internal IReadOnlyList<FogZoneAuthoringContract> FogZones { get; }
        internal int FlowPublicationCount => Authoring.HasFixedFlows || Authoring.HasGeneratedFlows
            ? 1 + (Authoring.HasGeneratedFlows ? Occurrence.HookedTargets.Length : 0) : 0;
        internal int ActionCount { get; }
        internal int FogActionCount { get; }
        internal int NativeSourceActionCount { get; }
        internal int ActorSourceActionCount { get; }
        internal int InteractionActionCount { get; }
        internal PropInteractionContract[] PropTargets { get; }
        internal Dictionary<PropInteractionContract, int> PropIndices { get; } = new();
        internal BiomeAuthoringContract.SourceActivationTarget[] SourceTargets { get; }
        internal Dictionary<int, int> NativeSourceIndices { get; } = new();
        internal bool[] Observed { get; }
        internal bool ObservedInitialized { get; set; }
        internal bool[] FogObserved { get; }
        internal bool FogObservedInitialized { get; set; }
        internal bool[] NativeSourceObserved { get; }
        internal bool NativeSourceObservedInitialized { get; set; }
        internal bool[] InteractionObserved { get; }
        internal bool InteractionObservedInitialized { get; set; }
    }

    sealed class Record
    {
        internal Record(int[] Desired, int[] Committed, byte[] Occupied, byte[] Consumed,
            string[][] TrackerIds, byte[] FogConsumed, FogCommand[] FogCommands, byte[] FogOccupied,
            FlowPresentationStart[] FlowStarts, byte[] NativeSourceConsumed, NativeSourceCommand?[] NativeSourceCommands,
            byte[] NativeSourceOccupied, bool NativeSourceInitialized,
            byte[] InteractionConsumed, PropInteractionIntent?[] PropIntents, byte[] InteractionOccupied, bool InteractionInitialized,
            bool Initialized = false, bool FogInitialized = false)
        {
            this.Desired = Desired; this.Committed = Committed;
            this.Occupied = Occupied; this.Consumed = Consumed;
            this.Initialized = Initialized;
            this.TrackerIds = TrackerIds;
            this.FogConsumed = FogConsumed; this.FogCommands = FogCommands;
            this.FogOccupied = FogOccupied; this.FogInitialized = FogInitialized;
            this.FlowStarts = FlowStarts;
            this.NativeSourceConsumed = NativeSourceConsumed; this.NativeSourceCommands = NativeSourceCommands;
            this.NativeSourceOccupied = NativeSourceOccupied; this.NativeSourceInitialized = NativeSourceInitialized;
            this.InteractionConsumed = InteractionConsumed; this.PropIntents = PropIntents;
            this.InteractionOccupied = InteractionOccupied; this.InteractionInitialized = InteractionInitialized;
        }
        internal int[] Desired { get; }
        internal int[] Committed { get; }
        internal byte[] Occupied { get; }
        internal byte[] Consumed { get; }
        internal bool Initialized { get; }
        internal string[][] TrackerIds { get; }
        internal byte[] FogConsumed { get; }
        internal FogCommand[] FogCommands { get; }
        internal byte[] FogOccupied { get; }
        internal bool FogInitialized { get; }
        internal FlowPresentationStart[] FlowStarts { get; }
        internal byte[] NativeSourceConsumed { get; }
        internal NativeSourceCommand?[] NativeSourceCommands { get; }
        internal byte[] NativeSourceOccupied { get; }
        internal bool NativeSourceInitialized { get; }
        internal byte[] InteractionConsumed { get; }
        internal PropInteractionIntent?[] PropIntents { get; }
        internal byte[] InteractionOccupied { get; }
        internal bool InteractionInitialized { get; }
        internal Record Copy(bool? Initialized = null, bool? FogInitialized = null, bool? NativeSourceInitialized = null,
            bool? InteractionInitialized = null) => new((int[])Desired.Clone(), (int[])Committed.Clone(),
            (byte[])Occupied.Clone(), (byte[])Consumed.Clone(), (string[][])TrackerIds.Clone(),
            (byte[])FogConsumed.Clone(), (FogCommand[])FogCommands.Clone(), (byte[])FogOccupied.Clone(),
            (FlowPresentationStart[])FlowStarts.Clone(),
            (byte[])NativeSourceConsumed.Clone(), (NativeSourceCommand?[])NativeSourceCommands.Clone(),
            (byte[])NativeSourceOccupied.Clone(), NativeSourceInitialized ?? this.NativeSourceInitialized,
            (byte[])InteractionConsumed.Clone(), (PropInteractionIntent?[])PropIntents.Clone(),
            (byte[])InteractionOccupied.Clone(), InteractionInitialized ?? this.InteractionInitialized,
            Initialized ?? this.Initialized, FogInitialized ?? this.FogInitialized);
    }

    internal readonly struct FlowPresentationStart
    {
        internal FlowPresentationStart(int GenerationRevision, double StartClock, long Revision, bool RestoreEndpoint)
        {
            this.GenerationRevision = GenerationRevision; this.StartClock = StartClock;
            this.Revision = Revision; this.RestoreEndpoint = RestoreEndpoint;
        }
        internal int GenerationRevision { get; }
        internal double StartClock { get; }
        internal long Revision { get; }
        internal bool RestoreEndpoint { get; }
        internal bool Equals(FlowPresentationStart Other) => GenerationRevision == Other.GenerationRevision
            && StartClock == Other.StartClock && Revision == Other.Revision && RestoreEndpoint == Other.RestoreEndpoint;
    }

    internal readonly struct FogCommand
    {
        internal FogCommand(bool Target, float StartCoverage, double StartClock, float CoverDelaySeconds, long Revision)
        {
            this.Target = Target; this.StartCoverage = StartCoverage; this.StartClock = StartClock;
            this.CoverDelaySeconds = CoverDelaySeconds; this.Revision = Revision;
        }
        internal bool Target { get; }
        internal float StartCoverage { get; }
        internal double StartClock { get; }
        internal float CoverDelaySeconds { get; }
        internal long Revision { get; }
        internal bool Equals(FogCommand Other) => Target == Other.Target && StartCoverage == Other.StartCoverage
            && StartClock == Other.StartClock && CoverDelaySeconds == Other.CoverDelaySeconds && Revision == Other.Revision;
    }

    readonly struct Preparation
    {
        internal Preparation(int RecordIndex, int TargetIndex, int Revision)
        {
            this.RecordIndex = RecordIndex; this.TargetIndex = TargetIndex; this.Revision = Revision;
        }
        internal int RecordIndex { get; }
        internal int TargetIndex { get; }
        internal int Revision { get; }
    }

    readonly struct OutputEffects
    {
        internal OutputEffects(bool Weather, bool SpecialDay, bool Geometry, bool Lighting)
        {
            this.Weather = Weather; this.SpecialDay = SpecialDay;
            this.Geometry = Geometry; this.Lighting = Lighting;
        }
        internal bool Weather { get; }
        internal bool SpecialDay { get; }
        internal bool Geometry { get; }
        internal bool Lighting { get; }
    }

    sealed class Ledger
    {
        internal Ledger(long Revision, Record?[] Records, long ClockEpoch, uint ClockAnchor,
            double? CheckpointRestoreClock, Preparation? Preparing = null)
        {
            this.Revision = Revision; this.Records = Records; this.Preparing = Preparing;
            this.ClockEpoch = ClockEpoch; this.ClockAnchor = ClockAnchor;
            this.CheckpointRestoreClock = CheckpointRestoreClock;
        }
        internal long Revision { get; }
        internal long ClockEpoch { get; }
        internal uint ClockAnchor { get; }
        internal double? CheckpointRestoreClock { get; }
        internal Record?[] Records { get; }
        internal Record ActiveRecord(int Index) => Records[Index]
            ?? throw new InvalidOperationException("Hooked generation references a retired occurrence.");
        internal Preparation? Preparing { get; }
    }

    sealed class Publication
    {
        internal Publication(Ledger Ledger, object[] Value, Hashtable Expected, Hashtable Properties, bool JournalHead)
        {
            this.Ledger = Ledger; this.Value = Value; this.Expected = Expected; this.Properties = Properties;
            this.JournalHead = JournalHead;
        }
        internal Ledger Ledger { get; }
        internal object[] Value { get; }
        internal Hashtable Expected { get; }
        internal Hashtable Properties { get; }
        internal bool JournalHead { get; }
    }

    sealed class LocalOutput
    {
        internal LocalOutput(GeneratedBatchKey Key, int Revision,
            ViewBatchSynchronization.PendingViewBatch Batch, object[] Target, byte[] Hash,
            GeneratedRpcDeferral.BatchCatalog Seed, int[] RequiredActors, string[] TrackerIds, int MaximumCatalogBytes)
        {
            this.Key = Key; this.Revision = Revision; this.Batch = Batch; this.Target = Target;
            this.Hash = Hash; this.Seed = Seed; this.RequiredActors = RequiredActors;
            this.TrackerIds = TrackerIds; this.MaximumCatalogBytes = MaximumCatalogBytes;
            ViewIds = new int[Batch.Batch.Views.Length];
            for (var i = 0; i < ViewIds.Length; i++) ViewIds[i] = Batch.Batch.Views[i].ViewID;
            FakeIds = new int[Batch.Batch.FakeItems.Length];
            for (var i = 0; i < FakeIds.Length; i++) FakeIds[i] = Batch.Batch.FakeItems[i].index;
            FakeRegistry = FakeItemManager.Instance;
        }
        internal GeneratedBatchKey Key { get; }
        internal int Revision { get; }
        internal ViewBatchSynchronization.PendingViewBatch Batch { get; }
        internal object[] Target { get; }
        internal byte[] Hash { get; }
        internal GeneratedRpcDeferral.BatchCatalog Seed { get; }
        internal int[] RequiredActors { get; }
        internal int MaximumCatalogBytes { get; }
        internal int[] ViewIds { get; }
        internal int[] FakeIds { get; }
        internal FakeItemManager? FakeRegistry { get; }
        internal string[] TrackerIds { get; }
        internal bool Initialized { get; set; }
        internal bool MasterInitialized { get; set; }
        internal bool LocalReplaySent { get; set; }
        internal Dictionary<int, object> ReplayedRegistrations { get; } = new();
        internal object[]? RpcSource { get; set; }
        internal GeneratedRpcDeferral.BatchCatalog? RpcCatalog { get; set; }
        internal int[] Delivered { get; set; } = Array.Empty<int>();
        internal object[]? RpcMutation { get; set; }
        internal Dictionary<int, (int Sender, int State)> MobStates { get; } = new();
        internal HashSet<int> MobActors { get; } = new();
        internal GeneratedPropOutputCapture? Origins { get; set; }
    }

    readonly struct Acknowledgement
    {
        internal Acknowledgement(int Revision, byte[] Hash, bool Initialized)
        {
            this.Revision = Revision; this.Hash = Hash; this.Initialized = Initialized;
        }
        internal int Revision { get; }
        internal byte[] Hash { get; }
        internal bool Initialized { get; }
    }

    sealed class RpcSnapshot
    {
        internal RpcSnapshot(object[] Source, GeneratedBatchKey Key, int Revision, byte[] Hash,
            GeneratedRpcDeferral.BatchCatalog Catalog, int[] Delivered, int MaximumCatalogBytes)
        {
            this.Source = Source; this.Key = Key; this.Revision = Revision;
            this.Hash = (byte[])Hash.Clone(); this.Catalog = Catalog; this.Delivered = (int[])Delivered.Clone();
            this.MaximumCatalogBytes = MaximumCatalogBytes;
        }
        internal object[] Source { get; }
        internal GeneratedBatchKey Key { get; }
        internal int Revision { get; }
        internal byte[] Hash { get; }
        internal GeneratedRpcDeferral.BatchCatalog Catalog { get; }
        internal int[] Delivered { get; }
        internal int MaximumCatalogBytes { get; }
    }

    readonly struct PropertyReservation
    {
        internal PropertyReservation(int HeldBytes, int ActualBytes, object? SourceVersion,
            RpcSnapshot? Rpc, RpcSnapshot? ConfirmedRpc)
        {
            this.HeldBytes = HeldBytes; this.ActualBytes = ActualBytes; this.SourceVersion = SourceVersion; this.Rpc = Rpc;
            this.ConfirmedRpc = ConfirmedRpc;
        }
        internal int HeldBytes { get; }
        internal int ActualBytes { get; }
        internal int RequiredBytes => Rpc?.MaximumCatalogBytes ?? 0;
        internal object? SourceVersion { get; }
        internal RpcSnapshot? Rpc { get; }
        internal RpcSnapshot? ConfirmedRpc { get; }
    }
    const string ControlKey = PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-control";
    const string ControlProtocol = "af.pll.library/hooked-prop-generation-control/v5";
    const string TargetProtocol = "af.pll.library/hooked-prop-generation-target/v1";
    const string RpcProtocol = "af.pll.library/hooked-prop-generation-rpc/v2";
    const string AckProtocol = "af.pll.library/hooked-prop-generation-target-ack/v1";
    const double ClockCycleSeconds = 4294967.296d;
    const uint ClockPhaseMilliseconds = 1431655766;
    readonly MapRun Run;
    readonly Dictionary<int, int> RecordIndices = new();
    readonly Dictionary<Transform, HookedPropTarget> TargetRoots = new();
    readonly Dictionary<GeneratedBatchKey, LocalOutput> Outputs = new();
    readonly Dictionary<PhotonView, LocalOutput> Views = new();
    readonly Queue<(int Record, int Zone, PllEffectZoneTransition Transition, ZoneActions Actions, double Clock)> Edges = new();
    readonly RoomPropertyPublication AckPublication = new();
    readonly Dictionary<int, (object Source, Dictionary<GeneratedBatchKey, Acknowledgement> Values)> ActorAcknowledgements = new();
    readonly Dictionary<string, PropertyReservation> RetainedPropertyBytes = new(StringComparer.Ordinal);
    int FixedNetworkBytes;
    object[]? AckValue;
    Declaration[] Declarations = Array.Empty<Declaration>();
    Ledger? Current;
    object[]? CurrentValue;
    Publication? Pending;
    Room? Room;
    IEnumerator? Routine;
    MapRun.FeatureWriter? Writer;
    bool Failed;
    bool Migrating;
    bool Restoring;
    bool SavePending;
    bool Saving;
    global::Campfire? SaveCampfire;
    bool Released;
    Exception? CancellationFailure;
    int Authority;
    double ClockNow;
    bool RestoreCheckpointClocks;
    GeneratedBatchKey? Replacing;

    internal HookedPropGeneration(MapRun Run) => this.Run = Run;
    internal GeneratedPropOutputCapture? ActiveCapture { get; private set; }
    internal bool HasWork => !Released && (Declarations.Length != 0 || Run.InitialGenerationCohort is not null) || SavePending;
    internal bool PresentationClockReady => !Released && !Failed && CurrentValue is not null
        && Room is not null && ReferenceEquals(Room, PhotonNetwork.CurrentRoom);
    internal double PresentationClock => ReadPresentationClock()
        ?? throw new InvalidOperationException("The shared presentation clock has no confirmed room anchor.");
    internal double? ReadPresentationClock()
    {
        if (!PresentationClockReady) return null;
        var Ledger = Current!;
        var Elapsed = unchecked((int)(RoomTimestamp - Ledger.ClockAnchor));
        return ClockNow = Math.Max(ClockNow,
            Ledger.ClockEpoch * ClockCycleSeconds + (Ledger.ClockAnchor + Math.Max(0L, Elapsed)) * .001d);
    }
    static uint RoomTimestamp => unchecked((uint)Math.Round(PhotonNetwork.Time * 1000d));
    internal bool Stable => !Failed && Routine is null && Pending is null && Edges.Count == 0
        && Current is { Preparing: null } && !Run.WorldInteractions.HasPending
        && !Run.PropInteractions.HasPendingIntent && AllCommittedLocallyReady();
    internal bool PendingQuicksave => SavePending;
    internal double? CheckpointRestoreClock => CurrentValue is null ? null : Current!.CheckpointRestoreClock;
    internal bool PropIntentBirthReady => !Released && !Failed && Pending is null && Edges.Count == 0;
    internal bool TransitionBlocked
    {
        get
        {
            if (Routine is not null || Pending is not null || Current?.Preparing is not null || SavePending || Restoring
                || Run.WorldInteractions.HasPending || Run.PropInteractions.HasPendingIntent) return true;
            if (Current is null) return false;
            for (var i = 0; i < Declarations.Length; i++)
                if (Current.Records[i] is { } Record)
                    for (var j = 0; j < Record.Desired.Length; j++)
                        if (Record.Desired[j] > Record.Committed[j]
                            && (Record.Committed[j] != 0 || !Declarations[i].Occurrence.HookedTargets[j].Contract.GenerateOnLoad)) return true;
            return false;
        }
    }
    bool Authoritative => Run.InitialGenerationCohort is null || PhotonNetwork.IsMasterClient;

    internal void Bind(MapSlot[] Slots, bool RestoreFlowEndpoint)
    {
        var Values = new List<Declaration>();
        var Seen = new HashSet<FeatureOccurrence>();
        var Targets = 0; var Zones = 0; var Actions = 0;
        foreach (var Slot in Slots)
        {
            var Occurrence = Slot.Occurrence;
            if (!Seen.Add(Occurrence)) continue;
            if (!BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract)) continue;
            if (Occurrence.HookedTargets.Length == 0 && Contract.FogZoneContracts.Count == 0
                && !Contract.HasFixedFlows && !Contract.HasGeneratedFlows && Contract.PropInteractionContracts.Count == 0) continue;
            if (Occurrence.HookedTargets.Length != 0 && Contract.GeneratedPropCatalog is null)
                throw new InvalidOperationException("Hooked generation lost its sealed asset catalog.");
            var ZoneContracts = new List<EffectZoneAuthoringContract>();
            var RuntimeZones = new List<ResolvedEffectZone[]>();
            foreach (var Zone in Contract.EffectZoneContracts)
                if (Zone.PropTriggers.Count != 0 || Zone.FogTriggers.Count != 0 || Zone.NativeSourceTriggers.Count != 0 || Zone.InteractionTriggers.Count != 0)
                {
                    ZoneContracts.Add(Zone);
                    RuntimeZones.Add(Zone.Generated ? Array.Empty<ResolvedEffectZone>()
                        : new[] { Zone.ResolveStable(Slot.CustomResolved!.Root.Value, Contract.Id) });
                    Actions += Zone.PropTriggers.Count + Zone.FogTriggers.Count + Zone.NativeSourceTriggers.Count + Zone.InteractionTriggers.Count;
                }
            var Declaration = new Declaration(Occurrence, Contract, Contract.GeneratedPropCatalog,
                ZoneContracts.ToArray(), RuntimeZones.ToArray(), Contract.FogZoneContracts);
            Targets += Occurrence.HookedTargets.Length + Contract.FogZoneContracts.Count + Declaration.SourceTargets.Length + Declaration.PropTargets.Length;
            Zones += ZoneContracts.Count;
            if (Targets > 256 || Zones > 256 || Actions > PllBundleSerialization.MaximumHookedActions)
                throw new InvalidOperationException("The map exceeds its Hooked generation declaration capacity.");
            foreach (var Target in Occurrence.HookedTargets) TargetRoots.Add(Target.Root, Target);
            Values.Add(Declaration);
        }
        Values.Sort((Left, Right) => Left.Occurrence.RequestIndex.CompareTo(Right.Occurrence.RequestIndex));
        Declarations = Values.ToArray();
        for (var i = 0; i < Declarations.Length; i++) RecordIndices.Add(Declarations[i].Occurrence.RequestIndex, i);
        var Records = new Record?[Declarations.Length];
        var Layout = Run.Ingress.LoadedQuicksaveLayout;
        var Saved = Layout?.hookedGeneration;
        var Retired = Layout?.retiredHookedGeneration;
        var SavedIndex = 0; var RetiredIndex = 0;
        var TrackerOwners = new HashSet<string>(StringComparer.Ordinal);
        Restoring = Saved is { Length: > 0 };
        for (var i = 0; i < Records.Length; i++)
        {
            var Declaration = Declarations[i];
            var Desired = new int[Declaration.Occurrence.HookedTargets.Length];
            for (var j = 0; j < Desired.Length; j++) Desired[j] = Declaration.Occurrence.HookedTargets[j].Contract.GenerateOnLoad ? 1 : 0;
            var FogCommands = new FogCommand[Declaration.FogZones.Count];
            var SourceCommands = new NativeSourceCommand?[Declaration.SourceTargets.Length];
            var SourceConsumed = Bits(Declaration.NativeSourceActionCount);
            var SourceOccupied = Declaration.NativeSourceActionCount == 0 ? Array.Empty<byte>() : Bits(Declaration.Zones.Length);
            var SourceInitialized = false;
            var PropIntents = new PropInteractionIntent?[Declaration.PropTargets.Length];
            var InteractionConsumed = Bits(Declaration.InteractionActionCount);
            var InteractionOccupied = Declaration.InteractionActionCount == 0 ? Array.Empty<byte>() : Bits(Declaration.Zones.Length);
            var InteractionInitialized = false;
            for (var j = 0; j < FogCommands.Length; j++)
                FogCommands[j] = new FogCommand(Declaration.FogZones[j].Settings.InitiallyEnabled,
                    0f, 0d, Declaration.FogZones[j].Settings.Transition.CoverDelaySeconds, 0);
            if ((Layout?.version is >= 25)
                && (Desired.Length != 0 || Layout.version >= 26 && (Declaration.FogZones.Count != 0 || Declaration.SourceTargets.Length != 0)
                    || Layout.version >= MapQuicksave.PropIntentMetadataVersion && Declaration.PropTargets.Length != 0))
            {
                if (RetiredIndex < Retired!.Length && Retired[RetiredIndex] == Declaration.Occurrence.RequestIndex)
                {
                    RetiredIndex++;
                    continue;
                }
                if (SavedIndex >= Saved!.Length || Saved[SavedIndex].requestIndex != Declaration.Occurrence.RequestIndex)
                    throw new InvalidOperationException("The save does not partition the declared Hooked occurrences.");
                var Record = Saved[SavedIndex++];
                var Occupied = Bits(Declaration.Zones.Length);
                var TrackerIds = new string[Record.trackerIds.Length][];
                for (var j = 0; j < TrackerIds.Length; j++) TrackerIds[j] = Record.trackerIds[j].ids;
                ValidateRecord(Declaration, Record.desired, Record.committed, Occupied, Record.consumed, TrackerIds, Record.initialized, TrackerOwners);
                for (var j = 0; j < TrackerIds.Length; j++) TrackerIds[j] = (string[])TrackerIds[j].Clone();
                var FogConsumed = Bits(Declaration.FogActionCount);
                if (Layout.version >= 26)
                {
                    if (Record.fogTargets!.Length != FogCommands.Length)
                        throw new InvalidOperationException("The saved fog state has a different declaration shape.");
                    for (var j = 0; j < FogCommands.Length; j++)
                        if (Record.fogTargets[j] != 0)
                            FogCommands[j] = new FogCommand(Record.fogTargets[j] == 2,
                                Record.fogTargets[j] == 2 ? 1f : 0f, 0d,
                                Declaration.FogZones[j].Settings.Transition.CoverDelaySeconds, 1);
                    ValidateFogRecord(Declaration, Record.fogConsumed!, FogCommands,
                        Bits(Declaration.Zones.Length), Record.fogInitialized!.Value, 1);
                    FogConsumed = (byte[])Record.fogConsumed!.Clone();
                    if (Record.sourceTargets!.Length != SourceCommands.Length)
                        throw new InvalidOperationException("The saved native source state has a different declaration shape.");
                    SourceConsumed = (byte[])Record.sourceConsumed!.Clone();
                    SourceInitialized = Record.sourceInitialized!.Value;
                    for (var j = 0; j < SourceCommands.Length; j++)
                        if (Record.sourceTargets[j] != 0)
                        {
                            var Generations = new int[Declaration.FlowPublicationCount];
                            if (Generations.Length > 1) Record.committed.CopyTo(Generations, 1);
                            var Coverages = new float[Generations.Length];
                            if (Record.sourceTargets[j] == 2) Array.Fill(Coverages, 1f);
                            SourceCommands[j] = new NativeSourceCommand(Record.sourceTargets[j] == 2, 0d, 1, Generations, Coverages);
                        }
                    ValidateNativeSourceRecord(Declaration, SourceConsumed, SourceCommands, SourceOccupied,
                        SourceInitialized);
                }
                if (Layout.version >= MapQuicksave.PropIntentMetadataVersion)
                {
                    if (Record.propIntents!.Length != PropIntents.Length)
                        throw new InvalidOperationException("Saved prop intent has a different declaration shape.");
                    for (var j = 0; j < PropIntents.Length; j++)
                        if (Record.propIntents[j].payload.Length != 0) PropIntents[j] = PropInteractionIntent.Decode(Record.propIntents[j].payload);
                    InteractionConsumed = (byte[])Record.interactionConsumed!.Clone();
                    InteractionInitialized = Record.interactionInitialized!.Value;
                    ValidateInteractionRecord(Declaration, InteractionConsumed, PropIntents, InteractionOccupied, InteractionInitialized, long.MaxValue);
                    RestoreCheckpointClocks |= Array.Exists(PropIntents, Intent => Intent is not null);
                }
                Records[i] = new Record((int[])Record.desired.Clone(), (int[])Record.committed.Clone(),
                    Occupied, (byte[])Record.consumed.Clone(), TrackerIds, FogConsumed, FogCommands,
                    Bits(Declaration.Zones.Length), new FlowPresentationStart[Declaration.FlowPublicationCount],
                    SourceConsumed, SourceCommands, SourceOccupied, SourceInitialized,
                    InteractionConsumed, PropIntents, InteractionOccupied, InteractionInitialized,
                    Record.initialized, Layout.version >= 26 && Record.fogInitialized!.Value);
            }
            else Records[i] = new Record(Desired, new int[Desired.Length], Bits(Declaration.Zones.Length),
                Bits(Declaration.ActionCount), EmptyTrackerIds(Desired.Length), Bits(Declaration.FogActionCount), FogCommands,
                Bits(Declaration.Zones.Length), new FlowPresentationStart[Declaration.FlowPublicationCount],
                SourceConsumed, SourceCommands, SourceOccupied, false, InteractionConsumed, PropIntents, InteractionOccupied, false);
            var Bound = Records[i]!;
            for (var j = 0; j < Bound.FlowStarts.Length; j++)
                Bound.FlowStarts[j] = new FlowPresentationStart(j == 0 ? 0 : Bound.Committed[j - 1], 0d, 0,
                    RestoreFlowEndpoint && (j == 0 || Bound.Committed[j - 1] != 0));
        }
        if ((Layout?.version is >= 25) && (SavedIndex != Saved!.Length || RetiredIndex != Retired!.Length))
            throw new InvalidOperationException("The save references a foreign or duplicate Hooked occurrence.");
        var InitialRevision = Layout?.version >= 26 ? 1L : 0L;
        foreach (var Record in Records)
            if (Record is not null)
                foreach (var Intent in Record.PropIntents) if (Intent is not null) InitialRevision = Math.Max(InitialRevision, Intent.Revision);
        Current = new Ledger(InitialRevision, Records, 0,
            Run.InitialGenerationCohort is not null ? RoomTimestamp : 0, null);
        RestoreCheckpointClocks |= Layout?.worldInteractions is { Length: > 0 };
        ReserveDeclarationShape(PhotonNetwork.CurrentRoom);
    }

    internal bool TryGetPropIntent(FeatureOccurrence Occurrence, PropInteractionContract Definition, out PropInteractionIntent? Intent)
    {
        Intent = null;
        if (Released || Failed || CurrentValue is null || !Run.Network.OwnsCurrentRun
            || !RecordIndices.TryGetValue(Occurrence.RequestIndex, out var Index)
            || Current!.Records[Index] is not { } Record || !Declarations[Index].PropIndices.TryGetValue(Definition, out var Target)) return false;
        Intent = Record.PropIntents[Target]; return Intent is not null;
    }

    internal bool TryGetFogCommand(FeatureOccurrence Occurrence, int TargetIndex, out FogCommand Command)
    {
        Command = default;
        if (Released || Failed || Current is null || !Run.Network.OwnsCurrentRun) return false;
        if (Run.InitialGenerationCohort is not null && !PresentationClockReady) return false;
        var Record = Current.Records[RecordIndex(Occurrence.RequestIndex)];
        if (Record is null) return false;
        Command = Record.FogCommands[TargetIndex];
        return Command.Revision != 0;
    }

    internal bool TryGetNativeSourceCommand(FeatureOccurrence Occurrence, int TargetIndex, out NativeSourceCommand? Command)
    {
        Command = null;
        if (Released || Failed || Current is null || !Run.Network.OwnsCurrentRun) return false;
        if (Run.InitialGenerationCohort is not null && !PresentationClockReady) return false;
        var Index = RecordIndex(Occurrence.RequestIndex);
        if (Current.Records[Index] is not { } Record
            || !Declarations[Index].NativeSourceIndices.TryGetValue(TargetIndex, out var SourceIndex)) return false;
        Command = Record.NativeSourceCommands[SourceIndex];
        return true;
    }

    internal bool TryGetActorSourceCommand(FeatureOccurrence Occurrence, Component Source, out bool Target)
    {
        Target = false;
        if (Released || Failed || Current is null || !Run.Network.OwnsCurrentRun) return false;
        var Index = RecordIndex(Occurrence.RequestIndex);
        var Declaration = Declarations[Index];
        if (Current.Records[Index] is not { } Record
            || !Declaration.NativeSourceIndices.TryGetValue(Declaration.Authoring.ActorSourceTargetIndex(Source), out var SourceIndex)
            || Record.NativeSourceCommands[SourceIndex] is not { } Command) return false;
        Target = Command.Target; return true;
    }

    internal sealed class NativeSourceCommand
    {
        readonly int[] Generations;
        readonly float[] StartCoverages;
        internal NativeSourceCommand(bool Target, double StartClock, long Revision, int[] Generations, float[] StartCoverages)
        {
            this.Target = Target; this.StartClock = StartClock; this.Revision = Revision;
            this.Generations = Generations; this.StartCoverages = StartCoverages;
        }
        internal bool Target { get; }
        internal double StartClock { get; }
        internal long Revision { get; }
        internal object[] Encode() => new object[] { Target, StartClock, Revision, Generations, StartCoverages };

        internal void Adopt(ref PllPresentationCoverage Coverage, FlowPresentationStart Start, int Group,
            float CoverDelaySeconds, double Now)
        {
            var SameBirth = Generations[Group] == Start.GenerationRevision && StartClock >= Start.StartClock;
            Coverage.Adopt(Target, SameBirth ? StartCoverages[Group] : Start.RestoreEndpoint && Target ? 1f : 0f,
                SameBirth ? StartClock : Start.StartClock, CoverDelaySeconds, Now);
        }

        internal bool Equals(NativeSourceCommand Other) => Target == Other.Target && StartClock == Other.StartClock
            && Revision == Other.Revision && Generations.AsSpan().SequenceEqual(Other.Generations)
            && StartCoverages.AsSpan().SequenceEqual(Other.StartCoverages);
    }

    internal bool TryGetFlowPresentationStart(FeatureOccurrence Occurrence, int TargetIndex,
        int GenerationRevision, out FlowPresentationStart Start)
    {
        Start = default;
        if (Released || Failed || CurrentValue is null || !Run.Network.OwnsCurrentRun
            || !Run.Active || !Run.OriginalMapSourcesRetired || !Run.IsOccurrenceReady(Occurrence)
            || !FlowLightingReady(Occurrence)) return false;
        if (Run.InitialGenerationCohort is not null && !PresentationClockReady) return false;
        if (!RecordIndices.TryGetValue(Occurrence.RequestIndex, out var Index)
            || Current!.Records[Index] is not { } Record || Record.FlowStarts.Length == 0) return false;
        if (TargetIndex >= 0)
        {
            var Key = GeneratedBatchKey.Hooked(Occurrence.RequestIndex, TargetIndex);
            if (TargetIndex >= Record.Committed.Length || Record.Committed[TargetIndex] != GenerationRevision
                || Replacing == Key || !Outputs.TryGetValue(Key, out var Output)
                || Output.Revision != GenerationRevision || !Output.Initialized) return false;
        }
        if (TargetIndex + 1 >= Record.FlowStarts.Length) return false;
        Start = Record.FlowStarts[TargetIndex + 1];
        return Start.Revision != 0 && Start.GenerationRevision == GenerationRevision;
    }

    internal void ReplaceGeneratedZones(FeatureOccurrence Occurrence,
        IReadOnlyList<(EffectZoneAuthoringContract Contract, ResolvedEffectZone Zone)> Zones)
    {
        if (Released || Current is null) return;
        var Declaration = Declarations[RecordIndex(Occurrence.RequestIndex)];
        var Values = new List<ResolvedEffectZone>?[Declaration.Zones.Length];
        foreach (var Entry in Zones)
        {
            if (!Declaration.ZoneIndices.TryGetValue(Entry.Contract, out var Index)) continue;
            if (!Entry.Contract.Generated)
                throw new InvalidOperationException("Generated world-zone publication contains a stable declaration.");
            (Values[Index] ??= new List<ResolvedEffectZone>()).Add(Entry.Zone);
        }
        for (var i = 0; i < Values.Length; i++)
            if (Declaration.Zones[i].Generated)
                Declaration.ResolvedZones[i] = Values[i]?.ToArray() ?? Array.Empty<ResolvedEffectZone>();
    }

    internal bool OwnedByOuter(Transform Root, Transform Candidate)
    {
        for (var Node = Candidate; Node; Node = Node.parent)
            if (TargetRoots.TryGetValue(Node, out var Target)) return TransformOwnership.Contains(Target.Root, Root);
        return true;
    }

    internal bool CommittedOutputsReady(FeatureOccurrence Occurrence)
    {
        if (Occurrence.HookedTargets.Length == 0) return true;
        if (Failed || Current is null) return false;
        var Index = RecordIndex(Occurrence.RequestIndex);
        if (Current.Records[Index] is not { } Record) return true;
        for (var i = 0; i < Occurrence.HookedTargets.Length; i++)
        {
            var Key = GeneratedBatchKey.Hooked(Occurrence.RequestIndex, i);
            if (Replacing == Key || Occurrence.HookedTargets[i].Contract.GenerateOnLoad && Record.Committed[i] == 0
                || Record.Committed[i] > 0 && (!Outputs.TryGetValue(Key, out var Output)
                    || Output.Revision != Record.Committed[i] || !Output.Initialized)) return false;
        }
        return true;
    }

    internal int Request(string Hook)
    {
        HookedPropRuntime.RequireMainThread(nameof(Request));
        HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
        if (Failed || Released || !Run.Network.OwnsCurrentRun || !Run.OriginalMapSourcesRetired || !Authoritative
            || PhotonNetwork.CurrentRoom is { IsOffline: false } && !PhotonNetwork.IsMasterClient
            || Pending is not null || Edges.Count != 0 || Current is not { Preparing: null }
            || Run.Transitions.Lease.Current != MapTransitionCoordinator.TransitionLease.State.Idle
            || Run.ReadyCurrentSlot is not { } Slot || Slot.Occurrence.HookedTargets.Length == 0) return 0;
        var Index = RecordIndex(Slot.Occurrence.RequestIndex);
        var Matches = new List<int>();
        if (Current.Records[Index] is not { } Record) return 0;
        for (var i = 0; i < Slot.Occurrence.HookedTargets.Length; i++)
            if (Slot.Occurrence.HookedTargets[i].Contract.EmitHook == Hook)
            {
                if (Record.Desired[i] == int.MaxValue || Record.Desired[i] - Record.Committed[i] >= 256) return 0;
                Matches.Add(i);
            }
        if (Matches.Count == 0) return 0;
        var Records = (Record?[])Current.Records.Clone(); var Updated = Records[Index] = Record.Copy();
        foreach (var IndexValue in Matches) Updated.Desired[IndexValue]++;
        return Publish(new Ledger(checked(Current.Revision + 1), Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock)) ? Matches.Count : 0;
    }

    internal void Tick()
    {
        if (Released || Failed || !Run.HasPublishedLayout || !Run.Network.OwnsCurrentRun) return;
        try
        {
            BindRoom();
            ObserveControl();
            if (Room is null && CurrentValue is null) { Publish(Current!); return; }
            if (Authoritative && Pending is null && PublishClockAnchor()) return;
            if (Authoritative && RetireCompletedOccurrences()) return;
            if (Restoring && RestorationComplete()) { Restoring = false; Migrating = true; }
            if (Authoritative && Pending is null && PublishReadyFlows()) return;
            if (Authoritative && Pending is null && PublishInitialFog()) return;
            if (Authoritative && !Restoring) ObserveZones();
            if (Pending is not null) return;
            if (Routine is not null)
            {
                if (Routine.MoveNext()) return;
                (Routine as IDisposable)?.Dispose(); Routine = null; Writer?.Dispose(); Writer = null;
                if (CancellationFailure is { } CleanupFailure) ExceptionPropagation.Throw(CleanupFailure);
            }
            MaintainOutputs();
            if (Current is null) return;
            if (Replacing is { } LocalKey && Outputs.TryGetValue(LocalKey, out var LocalOutput) && !LocalOutput.Initialized) return;
            if (Current.Preparing is { } Preparing)
            {
                var Declaration = Declarations[Preparing.RecordIndex];
                var Key = GeneratedBatchKey.Hooked(Declaration.Occurrence.RequestIndex, Preparing.TargetIndex);
                if (!Outputs.TryGetValue(Key, out var Output) || Output.Revision != Preparing.Revision)
                {
                    if (Room is not null && !Authoritative)
                    {
                        if (ReadTarget(Key, Preparing.Revision, out _, out var Actors, out _, out _) is null
                            || Array.BinarySearch(Actors, PhotonNetwork.LocalPlayer.ActorNumber) < 0) return;
                    }
                    if (!Run.FeatureMutationActive && OrdinaryReady(Declaration.Occurrence)
                        && Declaration.Occurrence.HookedTargets[Preparing.TargetIndex].Grouper.gameObject.activeInHierarchy) Start(Preparing);
                    return;
                }
                if (Authoritative && Output.Initialized && RequiredReady(Output))
                {
                    var Records = (Record?[])Current.Records.Clone();
                    var Record = Records[Preparing.RecordIndex] = Current.ActiveRecord(Preparing.RecordIndex).Copy();
                    Record.Committed[Preparing.TargetIndex] = Preparing.Revision;
                    Record.TrackerIds[Preparing.TargetIndex] = Output.TrackerIds;
                    if (Record.FlowStarts.Length > 1 && Run.Active && Run.OriginalMapSourcesRetired
                        && FlowLightingReady(Declaration.Occurrence))
                        Record.FlowStarts[Preparing.TargetIndex + 1] = new FlowPresentationStart(
                            Preparing.Revision, Run.PresentationClock, checked(Current.Revision + 1), false);
                    Publish(new Ledger(checked(Current.Revision + 1), Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock));
                }
                return;
            }
            for (var i = 0; i < Declarations.Length; i++)
            {
                var Declaration = Declarations[i];
                if (Current.Records[i] is not { } Record || !OrdinaryReady(Declaration.Occurrence)) continue;
                for (var j = 0; j < Record.Committed.Length; j++)
                {
                    var Revision = Record.Committed[j];
                    var Key = GeneratedBatchKey.Hooked(Declaration.Occurrence.RequestIndex, j);
                    if (!Declaration.Occurrence.HookedTargets[j].Grouper.gameObject.activeInHierarchy
                        || Run.Publishing && !EffectivelySelected(Declaration.Occurrence)) continue;
                    if (Revision > 0 && (!Outputs.TryGetValue(Key, out var Output) || Output.Revision != Revision))
                    {
                        if (!Run.FeatureMutationActive) Start(new Preparation(i, j, Revision));
                        return;
                    }
                }
            }
            if (Authoritative && Edges.Count != 0) { PublishEdge(); return; }
            if (Authoritative)
                for (var i = 0; i < Declarations.Length; i++)
                    if (Current.Records[i] is { } Record && !OccurrenceReleased(Declarations[i].Occurrence)
                        && (Declarations[i].ObservedInitialized && !Record.Initialized
                            || Declarations[i].FogObservedInitialized && !Record.FogInitialized
                            || Declarations[i].NativeSourceObservedInitialized && !Record.NativeSourceInitialized
                            || Declarations[i].InteractionObservedInitialized && !Record.InteractionInitialized))
                    {
                        var Records = (Record?[])Current.Records.Clone();
                        Records[i] = Record.Copy(Record.Initialized || Declarations[i].ObservedInitialized,
                            Record.FogInitialized || Declarations[i].FogObservedInitialized,
                            Record.NativeSourceInitialized || Declarations[i].NativeSourceObservedInitialized,
                            Record.InteractionInitialized || Declarations[i].InteractionObservedInitialized);
                        Publish(new Ledger(checked(Current.Revision + 1), Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock)); return;
                    }
            if (Authoritative && !Run.FeatureMutationActive)
                for (var i = 0; i < Declarations.Length; i++)
                {
                    if (Current.Records[i] is not { } Record || !OrdinaryReady(Declarations[i].Occurrence)) continue;
                    for (var j = 0; j < Record.Desired.Length; j++)
                        if (Record.Desired[j] > Record.Committed[j])
                        {
                            var Target = Declarations[i].Occurrence.HookedTargets[j];
                            if (!Target.Grouper.gameObject.activeInHierarchy
                                || Record.Committed[j] == 0 && Target.Contract.GenerateOnLoad
                                    && !EffectivelySelected(Target.Occurrence)) continue;
                            Publish(new Ledger(checked(Current.Revision + 1), Current.Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock,
                                new Preparation(i, j, checked(Record.Committed[j] + 1))));
                            return;
                        }
                }
            CompleteSave();
        }
        catch (Exception Failure)
        {
            Failed = true;
            if (Run.Network.OwnsCurrentRun)
                LazySlotGenerator.AbortLazyGeneration(Run, Replacing?.RequestIndex ?? Map.Index,
                    "Hooked generation failed", Failure);
        }
    }

    void BindRoom()
    {
        var CurrentRoom = PhotonNetwork.CurrentRoom;
        if (Run.InitialGenerationCohort is null)
        {
            if (CurrentRoom is { IsOffline: false })
                throw new InvalidOperationException("An offline Hooked generation run entered an online Photon room.");
            return;
        }
        if (CurrentRoom is null || CurrentRoom.IsOffline || PhotonNetwork.NetworkClientState != ClientState.Joined
            || Room is not null && !ReferenceEquals(Room, CurrentRoom))
            throw new InvalidOperationException("Hooked generation lost its Photon room.");
        if (CurrentRoom.Players.Count > GeneratedRpcDeferral.MaximumReplayActors)
            throw new InvalidOperationException("Hooked generation exceeds its supported Photon actor count.");
        Room = CurrentRoom;
        if (Authority == CurrentRoom.MasterClientId) return;
        Migrating = Authority != 0 || CurrentRoom.CustomProperties[ControlKey] is not null;
        Authority = CurrentRoom.MasterClientId;
        CancelRoutine();
        Pending = null; Edges.Clear(); AckPublication.Reset();
        ActorAcknowledgements.Clear(); AckValue = null;
        foreach (var Declaration in Declarations)
        {
            Declaration.ObservedInitialized = false; Declaration.FogObservedInitialized = false;
            Declaration.NativeSourceObservedInitialized = false;
            Declaration.InteractionObservedInitialized = false;
        }
        foreach (var Output in Outputs.Values)
        {
            Output.RpcMutation = null; Output.MobStates.Clear(); Output.MobActors.Clear();
            Output.MasterInitialized = false;
            Output.LocalReplaySent = false; Output.ReplayedRegistrations.Clear();
        }
    }

    void ObserveControl()
    {
        if (Room is null) return;
        var Value = Room.CustomProperties[ControlKey];
        if (Pending is { } Publication)
        {
            if (PhotonPropertyProtocol.SameValue(Value, Publication.Value))
            {
                foreach (var Pair in Publication.Properties)
                    if (!PhotonPropertyProtocol.SameValue(Room.CustomProperties[Pair.Key], Pair.Value))
                        throw new InvalidOperationException("Hooked generation publication has incomplete siblings.");
                RequireBudget(Publication.Properties, Confirmed: true);
                ObserveRetainedTargets(Publication.Ledger);
                ApplyLedger(Publication.Ledger, Publication.Value); Pending = null;
                if (Publication.JournalHead) Edges.Dequeue();
                return;
            }
            foreach (var Expected in Publication.Expected)
                if (!PhotonPropertyProtocol.SameValue(Room.CustomProperties[Expected.Key], Expected.Value))
                    throw new InvalidOperationException("Hooked generation publication lost its compare-and-swap ownership.");
            return;
        }
        if (Value is null)
        {
            if (PhotonNetwork.IsMasterClient) Publish(Current!);
            return;
        }
        if (ReferenceEquals(Value, CurrentValue)) return;
        var Parsed = ParseLedger(Value);
        if (CurrentValue is not null)
        {
            if (Parsed.Revision < Current!.Revision || Parsed.Revision == Current.Revision
                && !PhotonPropertyProtocol.SameValue(Value, CurrentValue))
                throw new InvalidOperationException("Hooked generation control changed or moved backwards at one revision.");
            if (Parsed.ClockEpoch < Current.ClockEpoch
                || Parsed.ClockEpoch == Current.ClockEpoch && Parsed.ClockAnchor < Current.ClockAnchor)
                throw new InvalidOperationException("The shared room clock moved backwards.");
            if (Parsed.CheckpointRestoreClock != Current.CheckpointRestoreClock)
                throw new InvalidOperationException("The checkpoint restoration clock changed.");
            for (var i = 0; i < Parsed.Records.Length; i++)
            {
                var Old = Current.Records[i]; var Next = Parsed.Records[i];
                if (Old is null && Next is not null) throw new InvalidOperationException("A retired Hooked occurrence became active.");
                if (Old is null || Next is null) continue;
                if (Old.Initialized && !Next.Initialized)
                    throw new InvalidOperationException("Hooked action initialization moved backwards.");
                if (Old.FogInitialized && !Next.FogInitialized)
                    throw new InvalidOperationException("Fog action initialization moved backwards.");
                if (Old.NativeSourceInitialized && !Next.NativeSourceInitialized)
                    throw new InvalidOperationException("Native source action initialization moved backwards.");
                if (Old.InteractionInitialized && !Next.InteractionInitialized)
                    throw new InvalidOperationException("Prop interaction action initialization moved backwards.");
                for (var j = 0; j < Old.PropIntents.Length; j++)
                    if (Old.PropIntents[j] is { } Before)
                    {
                        if (Next.PropIntents[j] is not { } After || After.Revision < Before.Revision
                            || After.Revision == Before.Revision && !After.Equals(Before)
                            || Before.BreakClock.HasValue && After.BreakClock != Before.BreakClock
                            || Before.Motion is { } Motion && (After.Motion is not { } NextMotion
                                || NextMotion.FirstClock != Motion.FirstClock || NextMotion.Origin < Motion.Origin
                                || NextMotion.ActiveSeconds < Motion.ActiveSeconds))
                            throw new InvalidOperationException("Prop intent changed or moved backwards at one revision.");
                    }
                for (var j = 0; j < Old.InteractionConsumed.Length; j++)
                    if ((Next.InteractionConsumed[j] & Old.InteractionConsumed[j]) != Old.InteractionConsumed[j])
                        throw new InvalidOperationException("Prop interaction action consumption moved backwards.");
                for (var j = 0; j < Old.Desired.Length; j++)
                {
                    if (Next.Desired[j] < Old.Desired[j] || Next.Committed[j] < Old.Committed[j])
                        throw new InvalidOperationException("Hooked target revision history moved backwards.");
                    if (Next.Committed[j] == Old.Committed[j] && !SameTrackerIds(Old.TrackerIds[j], Next.TrackerIds[j]))
                        throw new InvalidOperationException("Hooked tracker inventory changed at one committed revision.");
                }
                for (var j = 0; j < Old.FogCommands.Length; j++)
                    if (Next.FogCommands[j].Revision < Old.FogCommands[j].Revision
                        || Next.FogCommands[j].Revision == Old.FogCommands[j].Revision
                            && !Next.FogCommands[j].Equals(Old.FogCommands[j]))
                        throw new InvalidOperationException("Fog command changed or moved backwards at one revision.");
                for (var j = 0; j < Old.FlowStarts.Length; j++)
                {
                    var Before = Old.FlowStarts[j]; var After = Next.FlowStarts[j];
                    if (After.GenerationRevision < Before.GenerationRevision
                        || After.Revision < Before.Revision
                        || After.GenerationRevision == Before.GenerationRevision
                            && Before.Revision != 0 && !After.Equals(Before)
                        || After.GenerationRevision > Before.GenerationRevision
                            && Before.Revision != 0 && After.Revision == Before.Revision)
                        throw new InvalidOperationException("Flow publication changed or moved backwards at one generation.");
                }
                for (var j = 0; j < Old.FogConsumed.Length; j++)
                    if ((Next.FogConsumed[j] & Old.FogConsumed[j]) != Old.FogConsumed[j])
                        throw new InvalidOperationException("Fog action consumption moved backwards.");
                for (var j = 0; j < Old.NativeSourceCommands.Length; j++)
                    if (Old.NativeSourceCommands[j] is { } Before)
                    {
                        if (Next.NativeSourceCommands[j] is not { } After || After.Revision < Before.Revision
                            || After.Revision == Before.Revision && !After.Equals(Before))
                            throw new InvalidOperationException("Native source command changed or moved backwards at one revision.");
                    }
                for (var j = 0; j < Old.NativeSourceConsumed.Length; j++)
                    if ((Next.NativeSourceConsumed[j] & Old.NativeSourceConsumed[j]) != Old.NativeSourceConsumed[j])
                        throw new InvalidOperationException("Native source action consumption moved backwards.");
                for (var j = 0; j < Old.Consumed.Length; j++)
                    if ((Next.Consumed[j] & Old.Consumed[j]) != Old.Consumed[j])
                        throw new InvalidOperationException("Hooked action consumption moved backwards.");
            }
        }
        ObserveRetainedTargets(Parsed);
        ApplyLedger(Parsed, (object[])Value);
    }

    void ApplyLedger(Ledger Ledger, object[] Value)
    {
        var Previous = Current; Current = Ledger; CurrentValue = Value; RestoreCheckpointClocks = false;
        var ShapeChanged = false;
        for (var i = 0; i < Ledger.Records.Length; i++)
        {
            if (Ledger.Records[i] is not { } Record)
            {
                ShapeChanged |= Previous?.Records[i] is not null;
                for (var j = 0; j < Declarations[i].ResolvedZones.Length; j++)
                    Declarations[i].ResolvedZones[j] = Array.Empty<ResolvedEffectZone>();
                continue;
            }
            for (var j = 0; j < Record.Committed.Length; j++)
                if (Previous?.Records[i] is not { } Old || Record.Committed[j] != Old.Committed[j])
                {
                    ShapeChanged = true;
                    Run.LightVolumes.PublishCompletedCurrent(Run, Declarations[i].Occurrence.Slots[0]);
                }
            if (Declarations[i].ActorSourceActionCount != 0)
            {
                var Changed = true;
                if (Previous?.Records[i] is { } OldRecord)
                {
                    Changed = false;
                    for (var j = 0; j < Record.NativeSourceCommands.Length; j++)
                        if (Record.NativeSourceCommands[j]?.Revision != OldRecord.NativeSourceCommands[j]?.Revision)
                        {
                            Changed = true; break;
                        }
                }
                if (Changed) Declarations[i].Occurrence.ApplyActorSourceCommands();
            }
        }
        for (var i = 0; i < Ledger.Records.Length; i++)
            if (Ledger.Records[i] is { } Record)
                for (var j = 0; j < Record.PropIntents.Length; j++)
                    if (Record.PropIntents[j]?.Revision != Previous?.Records[i]?.PropIntents[j]?.Revision)
                    { Run.PropInteractions.Reconcile(Declarations[i].Occurrence); break; }
        if (ShapeChanged) ReserveDeclarationShape(Room);
    }

    bool Publish(Ledger Ledger, Hashtable? Siblings = null, bool JournalHead = false, object? BudgetTransfer = null,
        RpcSnapshot? Rpc = null)
    {
        if (Pending is not null) throw new InvalidOperationException("Hooked generation has a concurrent control publisher.");
        if (Room is not null)
        {
            var Timestamp = RoomTimestamp;
            if (unchecked((int)(Timestamp - Ledger.ClockAnchor)) >= 0)
                Ledger = new Ledger(Ledger.Revision, Ledger.Records,
                    checked(Ledger.ClockEpoch + (Timestamp < Ledger.ClockAnchor ? 1 : 0)), Timestamp,
                    Ledger.CheckpointRestoreClock, Ledger.Preparing);
        }
        if (RestoreCheckpointClocks && CurrentValue is null)
        {
            var Shift = Room is null ? Run.PresentationClock
                : Ledger.ClockEpoch * ClockCycleSeconds + Ledger.ClockAnchor * .001d;
            var Records = (Record?[])Ledger.Records.Clone();
            for (var i = 0; i < Records.Length; i++)
                if (Records[i] is { } Record)
                {
                    var Updated = Records[i] = Record.Copy();
                    for (var j = 0; j < Updated.PropIntents.Length; j++)
                        Updated.PropIntents[j] = Updated.PropIntents[j]?.Rebase(Shift);
                }
            Ledger = new Ledger(Ledger.Revision, Records, Ledger.ClockEpoch, Ledger.ClockAnchor, Shift, Ledger.Preparing);
        }
        var Value = EncodeLedger(Ledger);
        var Values = Siblings ?? new Hashtable(); Values[ControlKey] = Value;
        RequireBudget(Values, BudgetTransfer, CandidateRpc: Rpc);
        if (Room is null)
        {
            ApplyLedger(Ledger, Value);
            if (JournalHead) Edges.Dequeue();
            return true;
        }
        var Expected = new Hashtable();
        if (CurrentValue is not null) Expected[ControlKey] = CurrentValue;
        foreach (var Pair in Values)
            if (!Equals(Pair.Key, ControlKey)
                && Room.CustomProperties.TryGetValue(Pair.Key, out var Current))
                Expected[Pair.Key] = Current;
        if (!Room.SetCustomProperties(Values, Expected.Count == 0 ? null : Expected)) return false;
        Pending = new Publication(Ledger, Value, Expected, Values, JournalHead); return true;
    }

    void Start(Preparation Preparation)
    {
        Writer = Run.AcquireWriter();
        Replacing = GeneratedBatchKey.Hooked(Declarations[Preparation.RecordIndex].Occurrence.RequestIndex, Preparation.TargetIndex);
        Routine = Generate(Preparation, Writer);
    }

    IEnumerator Generate(Preparation Preparation, MapRun.FeatureWriter Writer)
    {
        var Declaration = Declarations[Preparation.RecordIndex];
        var Occurrence = Declaration.Occurrence;
        var Target = Occurrence.HookedTargets[Preparation.TargetIndex];
        var Key = GeneratedBatchKey.Hooked(Occurrence.RequestIndex, Preparation.TargetIndex);
        var OnlineMaster = Room is not null && PhotonNetwork.IsMasterClient;
        var Existing = ReadTarget(Key, Preparation.Revision, out var Envelope, out var Required, out var ExpectedRaw, out var ExpectedTrackers);
        if (Room is not null && Existing is null && !OnlineMaster) { Replacing = null; yield break; }
        if (Existing is null) Required = RequiredActors();
        else RequireBudget(new Hashtable { [TargetKey(Key)] = Existing, [RpcKey(Key)] = Room!.CustomProperties[RpcKey(Key)] }, Confirmed: true);
        var OldEffects = CaptureEffects(Target);
        Run.ClimbingFixtures.Retire(Target.Root.gameObject);
        Run.PropInteractions.Retire(Target.Root.gameObject);
        Run.SurfaceTextures.Retire(Target.Root.gameObject);
        Run.NativeMaterials.Retire(Target.Root.gameObject);
        Run.NativeSpawners.Retire(Target.Root.gameObject);
        Occurrence.RetireActors(Target.Root.gameObject);
        var HasPrevious = Outputs.TryGetValue(Key, out var PreviousOutput);
        var PreviousRecord = Current!.ActiveRecord(Preparation.RecordIndex);
        if (!HasPrevious && PreviousRecord.Committed[Preparation.TargetIndex] > 0
            && PreviousRecord.Committed[Preparation.TargetIndex] != Preparation.Revision)
        {
            Run.NativeSpawns.RetireHookedSources(Target, PreviousRecord.Committed[Preparation.TargetIndex]);
            RetireSavedHistory(PreviousRecord.TrackerIds[Preparation.TargetIndex]);
        }
        RetireLocal(Key, !HasPrevious || PreviousOutput!.Revision != Preparation.Revision);
        var Baseline = Run.Network.ViewBatches.BeginHookedViewBatch(Target);
        Run.NativeSpawns.RetireOutputs(Target.Root.gameObject);
        SpawnTrackerTransaction.RemoveSpawnTrackers(Target.Root);
        var Seed = SeedFor(Target, Occurrence, Preparation.Revision);
        var Capture = new GeneratedPropOutputCapture(Target, Declaration.Catalog!, Preparation.Revision, Occurrence.GeometryBudget, Seed);
        ActiveCapture = Capture;
        var Operation = new NativeFeatureGeneration.GenerationOperation(Target.Grouper, Target.Roots,
            Occurrence.Roots, Occurrence.Slots[0].Frame, Seed, Run.Configuration.Environment.NoSkyProps,
            false, Writer, null, null, null, ReplaceExisting: true, AfterClear: Capture.AfterClear,
            GeometryBudget: Occurrence.GeometryBudget,
            BindGeneratedFlows: Declaration.Authoring.HasGeneratedFlows
                ? (Prefab, Instance) => Declaration.Authoring.BindGeneratedFlows(Prefab, Instance, Occurrence.GeometryBudget,
                    Occurrence.RequireCustomFrame(Declaration.Authoring.Id)) : null,
            CompleteAuthoring: () =>
            {
                var NeedsPlatforms = false;
                foreach (var Prop in Declaration.Authoring.PropInteractionContracts)
                    if (Prop.Vine.HasValue && Prop.Endpoints == PllVineEndpoints.NativePlatformConnection)
                    { NeedsPlatforms = true; break; }
                Run.PropInteractions.Enroll(Occurrence,
                    NeedsPlatforms ? NativeFeatureGeneration.CaptureOwnedTreePlatforms() : null, Target.Root);
                Run.SurfaceTextures.Enroll(Occurrence);
                Run.NativeMaterials.Enroll(Occurrence);
            });
        var Generation = Run.Features.BeginOperation();
        ViewBatchSynchronization.PendingViewBatch? PendingBatch = null;
        var LightStaged = false;
        var Completed = false;
        try
        {
            var Native = FeatureGeneration.RunFeatureRoot(NativeFeatureGeneration.Run(Operation), Generation, Writer);
            try { while (Native.MoveNext()) yield return Native.Current; }
            finally { (Native as IDisposable)?.Dispose(); }
            if (Generation.Failure is { } Failure) ExceptionPropagation.Throw(Failure);
            Writer.RequireCurrent();
            var Trackers = SpawnTrackerTransaction.AttachHookedSpawnTrackers(Target, Occurrence.RequestIndex, Preparation.Revision, out var TrackerIds);
            var Record = Current!.ActiveRecord(Preparation.RecordIndex);
            if (Record.Committed[Preparation.TargetIndex] == Preparation.Revision
                && !SameTrackerIds(Record.TrackerIds[Preparation.TargetIndex], TrackerIds))
                throw new InvalidOperationException("Hooked reconstruction changed its committed tracker identities.");
            Run.NativeSpawns.EnrollHookedSources(Target);
            var Raw = Capture.Freeze();
            if (Existing is not null && (!Raw.AsSpan().SequenceEqual(ExpectedRaw) || !Trackers.AsSpan().SequenceEqual(ExpectedTrackers)))
                throw new InvalidOperationException("Hooked generation raw output or spawn tracker identity diverged.");
            Run.ClimbingFixtures.Enroll(Occurrence);
            Occurrence.EnrollActors(Declaration.Authoring, Occurrence.Container, Target.Root);
            foreach (var Slot in Occurrence.Slots) AuthoringRuntime.RefreshSlot(Slot.Index);
            var NewEffects = CaptureEffects(Target);
            if (OldEffects.Weather || NewEffects.Weather) WeatherProjection.InvalidateEnvironment(Occurrence);
            if (OldEffects.SpecialDay || NewEffects.SpecialDay) SpecialDayProjection.Refresh();
            if (OldEffects.Geometry || NewEffects.Geometry) TerrainSplatProjection.Refresh();
            var Frozen = Run.Network.ViewBatches.FreezeLazyViews(Occurrence.RequestIndex, Baseline,
                GenerationSeed: Seed, NativeSourceRoot: Target.Root);
            PendingBatch = new ViewBatchSynchronization.PendingViewBatch(Frozen);
            var MaximumCatalogBytes = MaximumRpcBytes(Key, Preparation.Revision, Frozen.Views, Room is not null);
            if (Existing is not null)
            {
                _ = ReadRpc(Key, Preparation.Revision, Hash(Existing), out _, out _);
                if (RetainedPropertyBytes[RpcKey(Key)].RequiredBytes != MaximumCatalogBytes)
                    throw new InvalidOperationException("Hooked replay capacity differs from its frozen native inventory.");
            }
            if (Run.LightVolumes.ScheduleRebake(Run, Occurrence.Slots[0],
                    ReferenceEquals(Run.SelectedActiveSlot?.Occurrence, Occurrence), OldEffects.Lighting || NewEffects.Lighting))
            {
                Run.LightVolumes.BeginHookedPublication(Key, Preparation.Revision); LightStaged = true;
                var Bake = FeatureGeneration.RunFeatureRoot(new LightVolumeRebake(Run.LightVolumes, Run,
                    Occurrence.Slots[0], true, Writer).Execute(), Generation, Writer);
                try { while (Bake.MoveNext()) yield return Bake.Current; }
                finally { (Bake as IDisposable)?.Dispose(); }
                if (Generation.Failure is { } BakeFailure) ExceptionPropagation.Throw(BakeFailure);
            }
            if (Room is null || Existing is null)
            {
                ReserveDeclarationShape(Room, Preparation, TrackerIds);
                var Reserve = EncodeTarget(Key, Preparation.Revision, Required, Raw, Trackers,
                    Run.Network.ViewBatches.MaximumViewValue(Frozen));
                var TargetBytes = GeneratedRpcDeferral.SerializePhotonValue(Reserve).Length;
                if (TargetBytes > GeneratedRpcDeferral.MaximumPropertyBytes)
                    throw new InvalidOperationException("Hooked output exceeds its Photon property budget.");
                Run.ReplayBudget.Reserve(Capture, checked(TargetBytes + MaximumCatalogBytes));
                if (Room is not null && !Run.Network.ViewBatches.HasRoomViewCapacity(Frozen.Views.Length))
                    throw new InvalidOperationException("The Photon room cannot fit the Hooked object batch.");
                var FakeIds = Run.Network.ViewBatches.AllocateFakeItemIds(Frozen, out var NextFakeId);
                var Ids = new int[Frozen.Views.Length];
                for (var i = 0; i < Frozen.Views.Length; i++)
                {
                    var View = Frozen.Views[i];
                    var Allocated = Room is null ? PhotonNetwork.AllocateViewID(View) : PhotonNetwork.AllocateRoomViewID(View);
                    if (View.ViewID > 0) PendingBatch.ProvisionalViewIds.Add((View, View.ViewID));
                    if (!Allocated || View.ViewID <= 0) throw new InvalidOperationException("Hooked generation could not allocate a PhotonView.");
                    Ids[i] = View.ViewID;
                }
                GeneratedRpcDeferral.HoldRegisteredBatch(Run, Key, Frozen.Views);
                if (Room is null)
                {
                    if (!Run.Network.ViewBatches.ApplyFakeItemBatch(Frozen, FakeIds, NextFakeId))
                        throw new InvalidOperationException("Hooked generation could not register local FakeItems.");
                    var OfflineCatalog = GeneratedRpcDeferral.InitializeLocalBatch(Run, Frozen.Roots, Frozen.Views);
                    Writer.RequireCurrent();
                    Envelope = new ViewBatchEnvelope(Run.GenerationEpoch, Run.LayoutRevision, Seed, Frozen.PathHash,
                        Ids, OfflineCatalog.ImmutableFingerprint, Frozen.FakePathHash, FakeIds, false, Frozen.NetworkContract.Hash, NextFakeId);
                    Existing = EncodeTarget(Key, Preparation.Revision, Required, Raw, Trackers, Envelope.ToPropertyValue());
                    RequireBudget(new Hashtable { [TargetKey(Key)] = Existing }, Capture);
                    Run.Network.ViewBatches.CommitProvisionalViewIds(PendingBatch);
                    PendingBatch.LocalRegistrationComplete = true;
                    var Local = new LocalOutput(Key, Preparation.Revision, PendingBatch, Existing, Hash(Existing), OfflineCatalog, Required, TrackerIds, MaximumCatalogBytes)
                        { Initialized = true, MasterInitialized = true, Origins = Capture };
                    Outputs.Add(Key, Local); AddViews(Local); Replacing = null; Completed = true;
                    Run.ClimbingFixtures.Admit(Occurrence, Target.Root);
                    Run.PropInteractions.Admit(Occurrence, Target.Root);
                    Occurrence.PublishActors(Target.Root);
                    if (LightStaged) Run.LightVolumes.CommitHooked(Run, Target, Preparation.Revision);
                    yield break;
                }
                GeneratedRpcDeferral.RunMasterInitialization(Run, Frozen.Views);
                var Beehive = false;
                foreach (var View in Frozen.Views) Beehive |= View.GetComponent<Beehive>();
                var Catalog = GeneratedRpcDeferral.SnapshotBatch(Run, Frozen.Roots, Frozen.Views,
                    PhotonNetwork.LocalPlayer.ActorNumber, Beehive, 0);
                Envelope = new ViewBatchEnvelope(Run.GenerationEpoch, Run.LayoutRevision, Seed, Frozen.PathHash, Ids,
                    Catalog.ImmutableFingerprint, Frozen.FakePathHash, FakeIds, false, Frozen.NetworkContract.Hash, NextFakeId);
                Existing = EncodeTarget(Key, Preparation.Revision, Required, Raw, Trackers, Envelope.ToPropertyValue());
                var TargetHash = Hash(Existing);
                var Rpc = EncodeRpc(Key, Preparation.Revision, TargetHash, Catalog, Array.Empty<int>(), 0, MaximumCatalogBytes);
                if (!Publish(new Ledger(checked(Current!.Revision + 1), Current.Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock, Current.Preparing),
                        new Hashtable { [TargetKey(Key)] = Existing, [RpcKey(Key)] = Rpc }, BudgetTransfer: Capture,
                        Rpc: new RpcSnapshot(Rpc, Key, Preparation.Revision, TargetHash, Catalog, Array.Empty<int>(), MaximumCatalogBytes)))
                    throw new InvalidOperationException("Hooked generation target publication was rejected.");
                while (Pending is not null) { Writer.RequireCurrent(); yield return null; ObserveControl(); }
            }
            Writer.RequireCurrent();
            var RpcCatalog = ReadRpc(Key, Preparation.Revision, Hash(Existing!), out _, out _);
            Run.Network.ViewBatches.RegisterHookedBatch(Key, PendingBatch, Envelope.ToPropertyValue());
            var LocalCatalog = GeneratedRpcDeferral.SealBatch(Run, Frozen.Roots, Frozen.Views);
            var ViewIds = Envelope.ViewIds;
            if (!RpcCatalog.IncludesLocalFacts(LocalCatalog,
                    RpcCatalog.InitializationActor == PhotonNetwork.LocalPlayer.ActorNumber, ViewIds)
                || !RpcCatalog.ImmutableFingerprint.AsSpan().SequenceEqual(Envelope.ReplayCatalogFingerprint))
                throw new InvalidOperationException("Hooked generation replay facts diverged.");
            var Output = new LocalOutput(Key, Preparation.Revision, PendingBatch, Existing!, Hash(Existing!), RpcCatalog, Required, TrackerIds, MaximumCatalogBytes)
                { Origins = Capture };
            Outputs.Add(Key, Output); AddViews(Output); Completed = true;
            if (LightStaged) Run.LightVolumes.CommitHooked(Run, Target, Preparation.Revision);
        }
        finally
        {
            ActiveCapture = null;
            List<Exception>? Failures = null;
            void Cleanup(Action Action)
            {
                try { Action(); }
                catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
            }
            Cleanup(() => Run.ReplayBudget.Reserve(Capture, 0));
            if (!Completed)
            {
                Cleanup(() => Run.PropInteractions.Retire(Target.Root.gameObject));
                Cleanup(() => Run.SurfaceTextures.Retire(Target.Root.gameObject));
                Cleanup(() => Run.NativeMaterials.Retire(Target.Root.gameObject));
                Cleanup(() => Run.NativeSpawners.Retire(Target.Root.gameObject));
            }
            if (!Completed && LightStaged) Cleanup(() => Run.LightVolumes.CancelHooked(Key, Preparation.Revision));
            if (!Completed && PendingBatch is not null) Cleanup(() => Run.Network.ViewBatches.ReleaseProvisionalViewIds(PendingBatch));
            if (Failures is not null)
            {
                if (CancellationFailure is { } Previous) Failures.Insert(0, Previous);
                CancellationFailure = new AggregateException("Hooked generation support cleanup failed.", Failures);
            }
        }
    }

    void MaintainOutputs()
    {
        if (Room is null) return;
        foreach (var Pair in Outputs)
        {
            var Output = Pair.Value;
            var Index = RecordIndex(Output.Key.RequestIndex);
            var Revision = Current?.Preparing is { } Preparing && Preparing.RecordIndex == Index
                && Preparing.TargetIndex == Output.Key.TargetIndex
                ? Preparing.Revision : Current!.Records[Index]?.Committed[Output.Key.TargetIndex] ?? 0;
            if (Revision != Output.Revision) continue;
            var Catalog = ObserveRpc(Output, out var Delivered, out var Source);
            if (Output.RpcMutation is { } Mutation)
            {
                var ObservedRevision = (int)Source[12]; var PendingRevision = (int)Mutation[12];
                if (ObservedRevision == PendingRevision - 1) continue;
                if (ObservedRevision < PendingRevision - 1 || ObservedRevision == PendingRevision
                    && !PhotonPropertyProtocol.SameValue(Source, Mutation))
                    throw new InvalidOperationException("Hooked RPC publication changed or moved backwards at one revision.");
                Output.RpcMutation = null;
            }
            var LocalActor = PhotonNetwork.LocalPlayer.ActorNumber;
            var WasInitialized = Output.Initialized;
            if (PhotonNetwork.IsMasterClient)
            {
                if (Output.MobStates.Count != 0)
                {
                    var UpdatedCatalog = Catalog;
                    foreach (var State in Output.MobStates)
                        UpdatedCatalog = UpdatedCatalog.WithMobState(State.Key, State.Value.Sender, State.Value.State);
                    var UpdatedActors = new List<int>(Delivered.Length);
                    foreach (var Actor in Delivered) if (!Output.MobActors.Contains(Actor)) UpdatedActors.Add(Actor);
                    Output.MobStates.Clear(); Output.MobActors.Clear();
                    if (!ReferenceEquals(UpdatedCatalog, Catalog) || UpdatedActors.Count != Delivered.Length)
                    {
                        MutateRpc(Output, UpdatedCatalog, UpdatedActors.ToArray(), Source); continue;
                    }
                }
                if (Catalog.HasBeehiveInitialization && Catalog.BeehiveEffectOwnerActor != LocalActor)
                {
                    if (Catalog.BeehiveEffectOwnerActor > 0 && Room.Players.ContainsKey(Catalog.BeehiveEffectOwnerActor))
                        throw new InvalidOperationException("Hooked Beehive effect ownership cannot move from a living actor.");
                    MutateRpc(Output, Catalog.WithBeehiveEffectOwner(LocalActor), Delivered, Source); continue;
                }
                foreach (var Actor in RequiredOrCurrentActors(Output))
                {
                    if (!Room.Players.ContainsKey(Actor)) continue;
                    var AlreadyDelivered = Array.BinarySearch(Delivered, Actor) >= 0;
                    if (Actor == LocalActor)
                    {
                        if (AlreadyDelivered && (Output.Initialized || Output.LocalReplaySent)) continue;
                    }
                    else
                    {
                        if (!AckMatches(Actor, Output)) continue;
                        var Registration = ActorAcknowledgements[Actor];
                        if (AlreadyDelivered && (Registration.Values[Output.Key].Initialized
                            || Output.ReplayedRegistrations.TryGetValue(Actor, out var SourceValue)
                                && ReferenceEquals(SourceValue, Registration.Source))) continue;
                        Output.ReplayedRegistrations[Actor] = Registration.Source;
                    }
                    Catalog.ReplayActor(Run, Room, Actor, Run.Retirement.RetiredViewIds);
                    if (Actor == LocalActor) Output.LocalReplaySent = true;
                    if (AlreadyDelivered) continue;
                    var Updated = new int[Delivered.Length + 1]; Array.Copy(Delivered, Updated, Delivered.Length);
                    Updated[Delivered.Length] = Actor; Array.Sort(Updated);
                    MutateRpc(Output, Catalog, Updated, Source); break;
                }
                if (Output.RpcMutation is not null) continue;
                if (!Output.Initialized)
                    Output.Initialized = GeneratedRpcDeferral.CompletePeerInitialization(Run, Output.ViewIds, Catalog);
                if (Output.Initialized && !Output.MasterInitialized)
                    Output.MasterInitialized = GeneratedRpcDeferral.CompleteMasterInitialization(Run, Output.ViewIds, Catalog);
            }
            else
            {
                if (!Output.Initialized && (!Catalog.HasCalls || Array.BinarySearch(Delivered, LocalActor) >= 0))
                    Output.Initialized = GeneratedRpcDeferral.CompletePeerInitialization(Run, Output.ViewIds, Catalog);
            }
            if (Output.Initialized != WasInitialized) AckValue = null;
            if (!Output.Initialized) continue;
            if (Replacing == Output.Key) Replacing = null;
            var Occurrence = Declarations[RecordIndex(Output.Key.RequestIndex)].Occurrence;
            if (!WasInitialized) Run.ClimbingFixtures.Admit(Occurrence,
                Occurrence.HookedTargets[Output.Key.TargetIndex].Root);
            if (!WasInitialized) Run.PropInteractions.Admit(Occurrence,
                Occurrence.HookedTargets[Output.Key.TargetIndex].Root);
            if (!WasInitialized) Occurrence.PublishActors(Occurrence.HookedTargets[Output.Key.TargetIndex].Root);
            if (!WasInitialized) Run.LightVolumes.PublishCompletedCurrent(Run, Occurrence.Slots[0]);
        }
        if (!PhotonNetwork.IsMasterClient && Declarations.Length != 0) PublishAck();
    }

    bool RequiredReady(LocalOutput Output)
    {
        if (Room is null) return true;
        if (PhotonNetwork.IsMasterClient && !Output.MasterInitialized) return false;
        var Catalog = ObserveRpc(Output, out var Delivered, out _);
        foreach (var Actor in Output.RequiredActors)
            if (Room.Players.ContainsKey(Actor) && (Catalog.HasCalls && Array.BinarySearch(Delivered, Actor) < 0
                || Actor != PhotonNetwork.LocalPlayer.ActorNumber && !AckMatches(Actor, Output, true))) return false;
        return true;
    }

    int[] RequiredOrCurrentActors(LocalOutput Output)
    {
        if (Current?.Preparing is { } Preparing
            && Declarations[Preparing.RecordIndex].Occurrence.RequestIndex == Output.Key.RequestIndex
            && Preparing.TargetIndex == Output.Key.TargetIndex && Preparing.Revision == Output.Revision) return Output.RequiredActors;
        var Result = new int[Room!.Players.Count]; var i = 0;
        foreach (var Actor in Room.Players.Keys) Result[i++] = Actor;
        Array.Sort(Result); return Result;
    }

    void PublishAck()
    {
        var Actor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (AckValue is null)
        {
            var Ordered = new List<LocalOutput>(Outputs.Values);
            Ordered.Sort((Left, Right) => Left.Key.CompareTo(Right.Key));
            var Entries = new object[Ordered.Count];
            for (var i = 0; i < Entries.Length; i++)
            {
                var Output = Ordered[i];
                Entries[i] = new object[] { Output.Key.RequestIndex, Output.Key.TargetIndex,
                    Output.Revision, Output.Hash, (byte)(Output.Initialized ? 1 : 0) };
            }
            AckValue = new object[] { AckProtocol, Run.GenerationEpoch, Run.LayoutRevision, Entries };
            RequireBudget(new Hashtable { [AckKey(Actor)] = AckValue });
        }
        if (!AckPublication.AwaitEcho(Room!, AckKey(Actor), AckValue, "Hooked generation acknowledgement was rejected.")
            && AckPublication.Failure is { } Failure) throw new InvalidOperationException(Failure);
    }

    bool AckMatches(int Actor, LocalOutput Output, bool Initialized = false)
    {
        var Value = Room!.CustomProperties[AckKey(Actor)];
        if (Value is null) return false;
        if (Value is object[] { Length: > 1 } Old && Old[1] is string OldEpoch && OldEpoch != Run.GenerationEpoch) return false;
        if (!ActorAcknowledgements.TryGetValue(Actor, out var Snapshot) || !ReferenceEquals(Snapshot.Source, Value))
        {
            if (Value is not object[] { Length: 4 } Fields || !Header(Fields, AckProtocol)
                || Fields[3] is not object[] Entries || Entries.Length > 256)
                throw new InvalidOperationException("Hooked generation acknowledgement is malformed.");
            var Parsed = new Dictionary<GeneratedBatchKey, Acknowledgement>(Entries.Length);
            GeneratedBatchKey? Previous = null;
            foreach (var Entry in Entries)
            {
                if (Entry is not object[] { Length: 5 } Row || Row[0] is not int Request || Request < 0 || Request >= Run.Slots.Length
                    || Row[1] is not int Target || Target is < 0 or >= 256 || Row[2] is not int Revision || Revision <= 0
                    || Row[3] is not byte[] { Length: 32 } Hash || Row[4] is not byte Stage || Stage > 1)
                    throw new InvalidOperationException("Hooked acknowledgement entries are malformed.");
                var Key = GeneratedBatchKey.Hooked(Request, Target);
                if (Target >= Declarations[RecordIndex(Request)].Occurrence.HookedTargets.Length)
                    throw new InvalidOperationException("Hooked acknowledgement references a foreign target.");
                if (Previous is { } Prior && Key.CompareTo(Prior) <= 0)
                    throw new InvalidOperationException("Hooked acknowledgement entries are unordered or duplicate.");
                Parsed.Add(Key, new Acknowledgement(Revision, (byte[])Hash.Clone(), Stage == 1)); Previous = Key;
            }
            Snapshot = (Value, Parsed); ActorAcknowledgements[Actor] = Snapshot;
        }
        if (!Snapshot.Values.TryGetValue(Output.Key, out var Acknowledgement) || Acknowledgement.Revision != Output.Revision) return false;
        if (!Acknowledgement.Hash.AsSpan().SequenceEqual(Output.Hash))
            throw new InvalidOperationException("Hooked generation acknowledgement output hash diverged.");
        return !Initialized || Acknowledgement.Initialized;
    }

    void MutateRpc(LocalOutput Output, GeneratedRpcDeferral.BatchCatalog Catalog, int[] Delivered, object[] Source)
    {
        var Revision = checked((int)Source[12] + 1);
        var Value = EncodeRpc(Output.Key, Output.Revision, Output.Hash, Catalog, Delivered, Revision, Output.MaximumCatalogBytes);
        RequireBudget(new Hashtable { [RpcKey(Output.Key)] = Value },
            CandidateRpc: new RpcSnapshot(Value, Output.Key, Output.Revision, Output.Hash, Catalog, Delivered, Output.MaximumCatalogBytes));
        if (!Room!.SetCustomProperties(new Hashtable { [RpcKey(Output.Key)] = Value },
                new Hashtable { [RpcKey(Output.Key)] = Source }))
            throw new InvalidOperationException("Hooked RPC publication was rejected.");
        Output.RpcMutation = Value;
    }

    internal bool OriginReady(PhotonView View) => Views.TryGetValue(View, out var Output) && Output.Initialized;
    internal bool FakeRegistryUnstable => Replacing is { } Key
        && !Outputs.ContainsKey(Key);

    internal bool InteractionReady(Transform Value)
    {
        for (var Node = Value; Node; Node = Node.parent)
            if (TargetRoots.TryGetValue(Node, out var Target))
                return Replacing != Target.BatchKey && Outputs.TryGetValue(Target.BatchKey, out var Output) && Output.Initialized;
        return true;
    }

    internal bool TryGetGeneratedRoot(Transform Value, GameObject Source, out Transform Root)
    {
        if (ActiveCapture is { } Capture && Capture.TryGetGeneratedRoot(Value, Source, out Root)) return true;
        foreach (var Output in Outputs.Values)
            if (Output.Origins is { } Origins && Origins.TryGetGeneratedRoot(Value, Source, out Root)) return true;
        Root = null!; return false;
    }

    internal bool TryGetSourceLifetime(Transform Source, out HookedPropTarget Target, out int Revision)
    {
        for (var Node = Source; Node; Node = Node.parent)
        {
            if (!TargetRoots.TryGetValue(Node, out Target!)) continue;
            if (ActiveCapture is { } Capture && ReferenceEquals(Capture.Owner, Target))
            {
                Revision = Capture.Revision; return true;
            }
            if (Outputs.TryGetValue(Target.BatchKey, out var Output)) { Revision = Output.Revision; return true; }
            throw new InvalidOperationException("Hooked native source has no actual generation lifetime.");
        }
        Target = null!; Revision = 0; return false;
    }

    internal int? SourceLifetimeRevision(HookedPropTarget Target)
    {
        var Index = RecordIndex(Target.Occurrence.RequestIndex);
        return Current?.Preparing is { } Preparing && Preparing.RecordIndex == Index
            && Preparing.TargetIndex == Target.TargetIndex
            ? Preparing.Revision : Current!.Records[Index]?.Committed[Target.TargetIndex];
    }

    internal bool TryRefreshMobState(PhotonView View, int SenderActor, int TargetActor, int State)
    {
        if (!Views.TryGetValue(View, out var Output)) return false;
        if (Room is null || !PhotonNetwork.IsMasterClient) return true;
        Output.MobStates[View.ViewID] = (SenderActor, State);
        Output.MobActors.Add(TargetActor); return true;
    }

    internal int IncludeFakeItemHighWater(int Next)
    {
        foreach (var Declaration in Declarations)
            for (var i = 0; i < Declaration.Occurrence.HookedTargets.Length; i++)
            {
                var Key = GeneratedBatchKey.Hooked(Declaration.Occurrence.RequestIndex, i);
                if (Room is null || Room.CustomProperties[TargetKey(Key)] is not object[] Value) continue;
                var Revision = Value.Length > 5 && Value[5] is int Parsed ? Parsed : 0;
                _ = ReadTarget(Key, Revision, out var Envelope, out _, out _, out _);
                Next = Math.Max(Next, Envelope.NextFakeId);
            }
        return Next;
    }

    internal bool TryFindPendingView(int ViewId, out GeneratedBatchKey Key)
    {
        foreach (var Declaration in Declarations)
            for (var i = 0; i < Declaration.Occurrence.HookedTargets.Length; i++)
            {
                Key = GeneratedBatchKey.Hooked(Declaration.Occurrence.RequestIndex, i);
                if (Room is null || Room.CustomProperties[TargetKey(Key)] is not object[] Value) continue;
                var Revision = Value.Length > 5 && Value[5] is int Parsed ? Parsed : 0;
                _ = ReadTarget(Key, Revision, out var Envelope, out _, out _, out _);
                foreach (var Id in Envelope.ViewIds) if (Id == ViewId) return true;
            }
        Key = default; return false;
    }

    bool PublishClockAnchor()
    {
        if (Room is null || Current is null) return false;
        var Timestamp = RoomTimestamp;
        return Timestamp / ClockPhaseMilliseconds != Current.ClockAnchor / ClockPhaseMilliseconds
            && unchecked((int)(Timestamp - Current.ClockAnchor)) >= 0
            && Publish(new Ledger(checked(Current.Revision + 1), Current.Records,
                Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock, Current.Preparing));
    }

    bool PublishInitialFog()
    {
        if (Current is null || Run.FeatureMutationActive) return false;
        Record?[]? Records = null;
        var Revision = checked(Current.Revision + 1);
        double? Now = null;
        for (var i = 0; i < Declarations.Length; i++)
        {
            var Declaration = Declarations[i];
            if (Current.Records[i] is not { } Record || !OrdinaryReady(Declaration.Occurrence)
                || !CommittedOutputsReady(Declaration.Occurrence)) continue;
            Record? Updated = null;
            for (var j = 0; j < Record.FogCommands.Length; j++)
            {
                if (Record.FogCommands[j].Revision != 0) continue;
                Records ??= (Record?[])Current.Records.Clone();
                Updated ??= Records[i] = Record.Copy();
                var Settings = Declaration.FogZones[j].Settings;
                Now ??= Run.PresentationClock;
                Updated.FogCommands[j] = new FogCommand(Settings.InitiallyEnabled,
                    0f, Now.Value, Settings.Transition.CoverDelaySeconds, Revision);
            }
        }
        return Records is not null && Publish(new Ledger(Revision, Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock, Current.Preparing));
    }

    bool PublishReadyFlows()
    {
        if (Current is null || !Run.Active || !Run.OriginalMapSourcesRetired || Run.FeatureMutationActive
            || Current.Preparing is not null || !Run.PresentationClockReady) return false;
        Record?[]? Records = null;
        var Revision = checked(Current.Revision + 1);
        double? Now = null;
        for (var i = 0; i < Declarations.Length; i++)
        {
            var Occurrence = Declarations[i].Occurrence;
            if (Current.Records[i] is not { } Record) continue;
            Record? Updated = null;
            for (var j = 0; j < Record.FlowStarts.Length; j++)
            {
                var Start = Record.FlowStarts[j];
                var GenerationRevision = j == 0 ? 0 : Record.Committed[j - 1];
                if (Start.Revision != 0 && Start.GenerationRevision == GenerationRevision
                    || j != 0 && GenerationRevision == 0) continue;
                if (Updated is null)
                {
                    if (!Run.IsOccurrenceReady(Occurrence) || !FlowLightingReady(Occurrence)) break;
                }
                Records ??= (Record?[])Current.Records.Clone();
                Updated ??= Records[i] = Record.Copy();
                Now ??= Run.PresentationClock;
                Updated.FlowStarts[j] = new FlowPresentationStart(GenerationRevision, Now.Value,
                    Revision, Start.GenerationRevision == GenerationRevision && Start.RestoreEndpoint);
            }
        }
        return Records is not null && Publish(new Ledger(Revision, Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock));
    }

    bool FlowLightingReady(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots)
            if (!Run.LightVolumes.ProjectionPrepared(Slot)) return false;
        return true;
    }

    void ObserveZones()
    {
        if (Current is null || !Run.Active || !Run.OriginalMapSourcesRetired || Migrating && !Character.localCharacter) return;
        var CurrentOccurrence = Run.ReadyCurrentSlot?.Occurrence;
        List<Vector3>? Centers = null;
        for (var i = 0; i < Declarations.Length; i++)
        {
            var Declaration = Declarations[i];
            if (Current.Records[i] is not { } Record) continue;
            if (OccurrenceReleased(Declaration.Occurrence)) continue;
            var Generation = Declaration.ActionCount != 0
                && (Record.Initialized || ReferenceEquals(CurrentOccurrence, Declaration.Occurrence));
            var Fog = Declaration.FogActionCount != 0 && Run.IsOccurrenceReady(Declaration.Occurrence);
            var NativeSource = Declaration.NativeSourceActionCount != 0 && Run.IsOccurrenceReady(Declaration.Occurrence)
                && FlowLightingReady(Declaration.Occurrence);
            var Interaction = Declaration.InteractionActionCount != 0 && Run.IsOccurrenceReady(Declaration.Occurrence);
            if (!Generation && !Fog && !NativeSource && !Interaction) continue;
            if (Centers is null)
            {
                Centers = new List<Vector3>();
                foreach (var Character in PlayerHandler.GetAllPlayerCharacters())
                    if (Character && !Character.data.dead
                        && (Room is null || Room.Players.ContainsKey(Character.photonView.OwnerActorNr))) Centers.Add(Character.Center);
            }
            var Baseline = Record.Initialized && (!Declaration.ObservedInitialized || Migrating);
            var FogBaseline = Record.FogInitialized && (!Declaration.FogObservedInitialized || Migrating);
            var SourceBaseline = Record.NativeSourceInitialized && (!Declaration.NativeSourceObservedInitialized || Migrating);
            var InteractionBaseline = Record.InteractionInitialized && (!Declaration.InteractionObservedInitialized || Migrating);
            for (var j = 0; j < Declaration.Zones.Length; j++)
            {
                var Occupied = false;
                foreach (var Zone in Declaration.ResolvedZones[j])
                {
                    if (Centers.Count == 0 || !Zone.Root || !Zone.Root.activeInHierarchy) continue;
                    var WorldToLocal = Zone.Root.transform.worldToLocalMatrix;
                    foreach (var Center in Centers)
                        if (PllZoneMath.Contains(Zone.Volume, WorldToLocal.MultiplyPoint3x4(Center))) { Occupied = true; break; }
                    if (Occupied) break;
                }
                var GenerationEdge = Generation && Declaration.Zones[j].PropTriggers.Count != 0
                    && !Baseline && Occupied != Declaration.Observed[j];
                var FogEdge = Fog && Declaration.Zones[j].FogTriggers.Count != 0
                    && !FogBaseline && Occupied != Declaration.FogObserved[j];
                var SourceEdge = NativeSource && Declaration.Zones[j].NativeSourceTriggers.Count != 0
                    && !SourceBaseline && Occupied != Declaration.NativeSourceObserved[j];
                var InteractionEdge = Interaction && Declaration.Zones[j].InteractionTriggers.Count != 0
                    && !InteractionBaseline && Occupied != Declaration.InteractionObserved[j];
                if (Generation) Declaration.Observed[j] = Occupied;
                if (Fog) Declaration.FogObserved[j] = Occupied;
                if (NativeSource) Declaration.NativeSourceObserved[j] = Occupied;
                if (Interaction) Declaration.InteractionObserved[j] = Occupied;
                if (!GenerationEdge && !FogEdge && !SourceEdge && !InteractionEdge) continue;
                if (Edges.Count == 256) throw new InvalidOperationException("Effect zone action journal is full.");
                Edges.Enqueue((i, j, Occupied ? PllEffectZoneTransition.Enter : PllEffectZoneTransition.Exit,
                    (GenerationEdge ? ZoneActions.Generation : 0) | (FogEdge ? ZoneActions.Fog : 0)
                        | (SourceEdge ? ZoneActions.NativeSource : 0) | (InteractionEdge ? ZoneActions.Interaction : 0),
                    InteractionEdge ? Run.PresentationClock : 0d));
            }
            Declaration.ObservedInitialized |= Generation;
            Declaration.FogObservedInitialized |= Fog;
            Declaration.NativeSourceObservedInitialized |= NativeSource;
            Declaration.InteractionObservedInitialized |= Interaction;
        }
        if (Centers is null) { Migrating = false; return; }
        if (!Migrating || Pending is not null) return;
        var Records = (Record?[])Current.Records.Clone();
        for (var i = 0; i < Records.Length; i++)
        {
            var Declaration = Declarations[i];
            if (Records[i] is not { } Record || OccurrenceReleased(Declaration.Occurrence)) continue;
            if (!(Record.Initialized && Declaration.ObservedInitialized
                || Record.FogInitialized && Declaration.FogObservedInitialized
                || Record.NativeSourceInitialized && Declaration.NativeSourceObservedInitialized
                || Record.InteractionInitialized && Declaration.InteractionObservedInitialized)) continue;
            var Updated = Records[i] = Record.Copy();
            for (var j = 0; j < Declaration.Zones.Length; j++)
            {
                if (Record.Initialized && Declaration.ObservedInitialized) Set(Updated.Occupied, j, Declaration.Observed[j]);
                if (Record.FogInitialized && Declaration.FogObservedInitialized) Set(Updated.FogOccupied, j, Declaration.FogObserved[j]);
                if (Record.NativeSourceInitialized && Declaration.NativeSourceObservedInitialized)
                    Set(Updated.NativeSourceOccupied, j, Declaration.NativeSourceObserved[j]);
                if (Record.InteractionInitialized && Declaration.InteractionObservedInitialized)
                    Set(Updated.InteractionOccupied, j, Declaration.InteractionObserved[j]);
            }
        }
        if (Publish(new Ledger(checked(Current.Revision + 1), Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock, Current.Preparing))) Migrating = false;
    }
    void PublishEdge()
    {
        var Edge = Edges.Peek(); var Declaration = Declarations[Edge.Record];
        if (Current!.Records[Edge.Record] is not { } Active) { Edges.Dequeue(); return; }
        var Records = (Record?[])Current.Records.Clone();
        var Generation = (Edge.Actions & ZoneActions.Generation) != 0;
        var Fog = (Edge.Actions & ZoneActions.Fog) != 0;
        var NativeSource = (Edge.Actions & ZoneActions.NativeSource) != 0;
        var Interaction = (Edge.Actions & ZoneActions.Interaction) != 0;
        var Record = Records[Edge.Record] = Active.Copy(Active.Initialized || Generation,
            Active.FogInitialized || Fog, Active.NativeSourceInitialized || NativeSource, Active.InteractionInitialized || Interaction);
        if (Generation) Set(Record.Occupied, Edge.Zone, Edge.Transition == PllEffectZoneTransition.Enter);
        if (Fog) Set(Record.FogOccupied, Edge.Zone, Edge.Transition == PllEffectZoneTransition.Enter);
        if (NativeSource) Set(Record.NativeSourceOccupied, Edge.Zone, Edge.Transition == PllEffectZoneTransition.Enter);
        if (Interaction) Set(Record.InteractionOccupied, Edge.Zone, Edge.Transition == PllEffectZoneTransition.Enter);
        var Offset = 0; var FogOffset = 0; var SourceOffset = 0; var InteractionOffset = 0;
        for (var i = 0; i < Edge.Zone; i++)
        {
            Offset += Declaration.Zones[i].PropTriggers.Count;
            FogOffset += Declaration.Zones[i].FogTriggers.Count;
            SourceOffset += Declaration.Zones[i].NativeSourceTriggers.Count;
            InteractionOffset += Declaration.Zones[i].InteractionTriggers.Count;
        }
        var Zone = Declaration.Zones[Edge.Zone];
        for (var i = 0; Generation && i < Zone.PropTriggers.Count; i++)
        {
            var Action = Zone.PropTriggers[i];
            if (Action.Transition != Edge.Transition || !Zone.Looped && Get(Record.Consumed, Offset + i)) continue;
            if (!ReferenceEquals(Run.ReadyCurrentSlot?.Occurrence, Declaration.Occurrence)) continue;
            if (Record.Desired[Action.TargetIndex] == int.MaxValue
                || Record.Desired[Action.TargetIndex] - Record.Committed[Action.TargetIndex] >= 256)
                throw new InvalidOperationException("Effect zone generation exceeds the target revision backlog.");
            Record.Desired[Action.TargetIndex]++;
            if (!Zone.Looped) Set(Record.Consumed, Offset + i, true);
        }
        var Revision = checked(Current.Revision + 1);
        if (Fog && Run.IsOccurrenceReady(Declaration.Occurrence))
        {
            var Now = Run.PresentationClock;
            for (var i = 0; i < Zone.FogTriggers.Count; i++)
            {
                var Action = Zone.FogTriggers[i];
                if (Action.Transition != Edge.Transition || !Zone.Looped && Get(Record.FogConsumed, FogOffset + i)) continue;
                var Command = Record.FogCommands[Action.TargetIndex];
                if (Command.Revision == 0) continue;
                var Target = Action.Operation == PllFogZoneOperation.Activate;
                if (Command.Target != Target)
                {
                    var Coverage = new PllPresentationCoverage();
                    Coverage.Adopt(Command.Target, Command.StartCoverage, Command.StartClock, Command.CoverDelaySeconds, Now);
                    Record.FogCommands[Action.TargetIndex] = new FogCommand(Target, Coverage.Coverage,
                        Now, Command.CoverDelaySeconds, Revision);
                }
                if (!Zone.Looped) Set(Record.FogConsumed, FogOffset + i, true);
            }
        }
        if (NativeSource && Run.IsOccurrenceReady(Declaration.Occurrence) && FlowLightingReady(Declaration.Occurrence))
        {
            var Now = Run.PresentationClock;
            for (var i = 0; i < Zone.NativeSourceTriggers.Count; i++)
            {
                var Action = Zone.NativeSourceTriggers[i];
                if (Action.Transition != Edge.Transition || !Zone.Looped && Get(Record.NativeSourceConsumed, SourceOffset + i)) continue;
                var SourceIndex = Declaration.NativeSourceIndices[Action.TargetIndex];
                var Previous = Record.NativeSourceCommands[SourceIndex];
                var Target = Action.Operation == PllNativeSourceOperation.Activate;
                if ((Previous?.Target ?? Declaration.SourceTargets[SourceIndex].InitiallyEnabled) != Target)
                {
                    var Generations = new int[Record.FlowStarts.Length];
                    var Coverages = new float[Generations.Length];
                    var Duration = Declaration.SourceTargets[SourceIndex].CoverDelaySeconds;
                    for (var j = 0; j < Generations.Length; j++)
                    {
                        var Birth = Record.FlowStarts[j]; Generations[j] = Birth.GenerationRevision;
                        if (Birth.Revision == 0) continue;
                        var Coverage = new PllPresentationCoverage();
                        if (Previous is null)
                            Coverage.Adopt(true, Birth.RestoreEndpoint ? 1f : 0f, Birth.StartClock, Duration, Now);
                        else Previous.Adopt(ref Coverage, Birth, j, Duration, Now);
                        Coverages[j] = Coverage.Coverage;
                    }
                    Record.NativeSourceCommands[SourceIndex] = new NativeSourceCommand(Target, Now, Revision, Generations, Coverages);
                }
                if (!Zone.Looped) Set(Record.NativeSourceConsumed, SourceOffset + i, true);
            }
        }
        if (Interaction && Run.IsOccurrenceReady(Declaration.Occurrence))
        {
            var Now = Edge.Clock;
            for (var i = 0; i < Zone.InteractionTriggers.Count; i++)
            {
                var Action = Zone.InteractionTriggers[i];
                if (Action.Transition != Edge.Transition || !Zone.Looped && Get(Record.InteractionConsumed, InteractionOffset + i)) continue;
                var Definition = Declaration.Authoring.PropInteractionContracts[Action.TargetIndex];
                var Target = Declaration.PropIndices[Definition];
                var Intent = PropInteractionIntent.Apply(Record.PropIntents[Target], Action.Operation, Action.Enabled,
                    Definition.InitiallyEnabled, Now, Revision);
                if (Intent is null) continue;
                Record.PropIntents[Target] = Intent;
                if (!Zone.Looped) Set(Record.InteractionConsumed, InteractionOffset + i, true);
            }
        }
        if (!Publish(new Ledger(Revision, Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock), JournalHead: true))
            throw new InvalidOperationException("Effect zone generation admission was rejected.");
    }

    bool OrdinaryReady(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots) if (Slot.Released || !Slot.FeaturesReady || !Slot.ViewBatchReady) return false;
        return true;
    }

    bool EffectivelySelected(FeatureOccurrence Occurrence) =>
        ReferenceEquals((Run.Publishing ? Run.Slots[0] : Run.SelectedActiveSlot)?.Occurrence, Occurrence);

    bool AllCommittedLocallyReady()
    {
        foreach (var Declaration in Declarations)
        {
            if (!EffectivelySelected(Declaration.Occurrence) || !OrdinaryReady(Declaration.Occurrence)
                || Current?.Records[RecordIndex(Declaration.Occurrence.RequestIndex)] is not { } Record) continue;
            if (!CommittedOutputsReady(Declaration.Occurrence)) return false;
            for (var i = 0; i < Record.Desired.Length; i++)
                if (Declaration.Occurrence.HookedTargets[i].Grouper.gameObject.activeInHierarchy
                    && Record.Desired[i] != Record.Committed[i]) return false;
        }
        return true;
    }

    static bool OccurrenceReleased(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots) if (!Slot.Released) return false;
        return true;
    }

    bool RestorationComplete()
    {
        if (Current is null || Pending is not null || Current.Preparing is not null || Routine is not null) return false;
        foreach (var Declaration in Declarations)
        {
            if (!OrdinaryReady(Declaration.Occurrence)) continue;
            if (Current.Records[RecordIndex(Declaration.Occurrence.RequestIndex)] is not { } Record) continue;
            for (var i = 0; i < Record.Desired.Length; i++)
            {
                var Target = Declaration.Occurrence.HookedTargets[i];
                if (!Target.Grouper.gameObject.activeInHierarchy) continue;
                if (Target.Contract.GenerateOnLoad && Record.Committed[i] == 0 && !EffectivelySelected(Declaration.Occurrence)) continue;
                if (Record.Desired[i] != Record.Committed[i] || Record.Committed[i] > 0
                    && (!Outputs.TryGetValue(Target.BatchKey, out var Output) || !Output.Initialized)) return false;
            }
        }
        return true;
    }

    int RecordIndex(int RequestIndex)
    {
        if (RecordIndices.TryGetValue(RequestIndex, out var Index)) return Index;
        throw new InvalidOperationException("Hooked generation references a foreign occurrence.");
    }

    object[] EncodeLedger(Ledger Ledger)
    {
        var Records = new List<object>(); var Retired = new List<int>();
        for (var i = 0; i < Ledger.Records.Length; i++)
        {
            if (Ledger.Records[i] is not { } Record) { Retired.Add(Declarations[i].Occurrence.RequestIndex); continue; }
            Records.Add(new object[] { Declarations[i].Occurrence.RequestIndex,
                Record.Desired, Record.Committed, Record.Occupied, Record.Consumed, Record.Initialized,
                EncodeTrackerIds(Record.TrackerIds), Record.FogConsumed, EncodeFogCommands(Record.FogCommands),
                Record.FogOccupied, Record.FogInitialized, EncodeFlowStarts(Record.FlowStarts), Record.NativeSourceConsumed,
                EncodeNativeSourceCommands(Record.NativeSourceCommands), Record.NativeSourceOccupied, Record.NativeSourceInitialized,
                Record.InteractionConsumed, EncodePropIntents(Record.PropIntents), Record.InteractionOccupied, Record.InteractionInitialized });
        }
        return new object[] { ControlProtocol, Run.GenerationEpoch, Run.LayoutRevision, Ledger.Revision,
            Ledger.Preparing is { } Preparing ? new object[] { Declarations[Preparing.RecordIndex].Occurrence.RequestIndex,
                Preparing.TargetIndex, Preparing.Revision } : Array.Empty<object>(), Records.ToArray(), Retired.ToArray(),
            Ledger.ClockEpoch, unchecked((int)Ledger.ClockAnchor), Ledger.CheckpointRestoreClock is { } Clock
                ? new object[] { Clock } : Array.Empty<object>() };
    }

    Ledger ParseLedger(object Value)
    {
        if (Value is not object[] { Length: 10 } Fields || !Header(Fields, ControlProtocol)
            || Fields[3] is not long Revision || Revision < 0 || Fields[4] is not object[] Preparing
            || Fields[5] is not object[] Records || Fields[6] is not int[] Retired
            || Records.Length + Retired.Length != Declarations.Length
            || Fields[7] is not long ClockEpoch || ClockEpoch < 0 || Fields[8] is not int ClockAnchor
            || Fields[9] is not object[] { Length: <= 1 } RestoreClock)
            throw new InvalidOperationException("Hooked generation control is malformed.");
        double? CheckpointRestoreClock = null;
        if (RestoreClock.Length != 0)
        {
            if (RestoreClock[0] is not double Clock || !double.IsFinite(Clock) || Clock < 0d
                || Clock > ClockEpoch * ClockCycleSeconds + unchecked((uint)ClockAnchor) * .001d)
                throw new InvalidOperationException("The checkpoint restoration clock is invalid.");
            CheckpointRestoreClock = Clock;
        }
        var Parsed = new Record?[Declarations.Length]; var Seen = new bool[Declarations.Length];
        var TrackerOwners = new HashSet<string>(StringComparer.Ordinal);
        var Previous = -1;
        for (var i = 0; i < Records.Length; i++)
        {
            if (Records[i] is not object[] { Length: 20 } Record
                || Record[0] is not int Request || Request <= Previous
                || Record[1] is not int[] Desired || Record[2] is not int[] Committed
                || Record[3] is not byte[] Occupied || Record[4] is not byte[] Consumed || Record[5] is not bool Initialized
                || Record[6] is not object[] TrackerValues || TrackerValues.Length != Desired.Length
                || Record[7] is not byte[] FogConsumed || Record[8] is not object[] FogValues
                || Record[9] is not byte[] FogOccupied || Record[10] is not bool FogInitialized
                || Record[11] is not object[] FlowValues || Record[12] is not byte[] SourceConsumed
                || Record[13] is not object[] SourceValues || Record[14] is not byte[] SourceOccupied
                || Record[15] is not bool SourceInitialized || Record[16] is not byte[] InteractionConsumed
                || Record[17] is not object[] PropValues || Record[18] is not byte[] InteractionOccupied
                || Record[19] is not bool InteractionInitialized)
                throw new InvalidOperationException("Hooked generation occurrence state is malformed.");
            var Index = RecordIndex(Request); Previous = Request; Seen[Index] = true;
            if (FogValues.Length != Declarations[Index].FogZones.Count
                || FlowValues.Length != Declarations[Index].FlowPublicationCount
                || SourceValues.Length != Declarations[Index].SourceTargets.Length || PropValues.Length != Declarations[Index].PropTargets.Length)
                throw new InvalidOperationException("World presentation has a different declaration shape.");
            var TrackerIds = new string[TrackerValues.Length][];
            for (var j = 0; j < TrackerIds.Length; j++)
                TrackerIds[j] = TrackerValues[j] is string[] Ids ? Ids
                    : throw new InvalidOperationException("Hooked tracker identity inventory is malformed.");
            ValidateRecord(Declarations[Index], Desired, Committed, Occupied, Consumed, TrackerIds, Initialized, TrackerOwners);
            var FogCommands = new FogCommand[FogValues.Length];
            for (var j = 0; j < FogCommands.Length; j++)
            {
                if (FogValues[j] is not object[] { Length: 5 } Command || Command[0] is not bool Target
                    || Command[1] is not float StartCoverage || Command[2] is not double StartClock
                    || Command[3] is not float CoverDelaySeconds || Command[4] is not long CommandRevision)
                    throw new InvalidOperationException("Fog command is malformed.");
                FogCommands[j] = new FogCommand(Target, StartCoverage, StartClock, CoverDelaySeconds, CommandRevision);
            }
            ValidateFogRecord(Declarations[Index], FogConsumed, FogCommands, FogOccupied, FogInitialized, Revision);
            var FlowStarts = new FlowPresentationStart[FlowValues.Length];
            for (var j = 0; j < FlowStarts.Length; j++)
            {
                if (FlowValues[j] is not object[] { Length: 4 } Start
                    || Start[0] is not int GenerationRevision || GenerationRevision < 0
                    || GenerationRevision > (j == 0 ? 0 : Committed[j - 1])
                    || Start[1] is not double StartClock || !double.IsFinite(StartClock) || StartClock < 0d
                    || Start[2] is not long StartRevision || StartRevision < 0 || StartRevision > Revision
                    || Start[3] is not bool RestoreEndpoint || StartRevision == 0 && StartClock != 0d
                    || j != 0 && GenerationRevision == 0 && (StartRevision != 0 || RestoreEndpoint))
                    throw new InvalidOperationException("Flow publication violates its generation or clock contract.");
                FlowStarts[j] = new FlowPresentationStart(GenerationRevision, StartClock, StartRevision, RestoreEndpoint);
            }
            for (var j = 0; j < TrackerIds.Length; j++) TrackerIds[j] = (string[])TrackerIds[j].Clone();
            var SourceCommands = new NativeSourceCommand?[SourceValues.Length];
            for (var j = 0; j < SourceCommands.Length; j++)
            {
                if (SourceValues[j] is not object[] Command)
                    throw new InvalidOperationException("Native source command is malformed.");
                if (Command.Length == 0) continue;
                if (Command is not { Length: 5 } || Command[0] is not bool Target || Command[1] is not double StartClock
                    || Command[2] is not long CommandRevision || Command[3] is not int[] Generations
                    || Command[4] is not float[] StartCoverages)
                    throw new InvalidOperationException("Native source command is malformed.");
                ValidateNativeSourceFields(StartClock, CommandRevision, Generations, StartCoverages,
                    Declarations[Index].FlowPublicationCount, Committed, Revision);
                SourceCommands[j] = new NativeSourceCommand(Target, StartClock, CommandRevision,
                    (int[])Generations.Clone(), (float[])StartCoverages.Clone());
            }
            ValidateNativeSourceRecord(Declarations[Index], SourceConsumed, SourceCommands, SourceOccupied, SourceInitialized);
            var PropIntents = new PropInteractionIntent?[PropValues.Length];
            for (var j = 0; j < PropIntents.Length; j++)
            {
                if (PropValues[j] is not byte[] Bytes) throw new InvalidOperationException("Prop intent is malformed.");
                if (Bytes.Length != 0) PropIntents[j] = PropInteractionIntent.Decode(Bytes);
            }
            ValidateInteractionRecord(Declarations[Index], InteractionConsumed, PropIntents, InteractionOccupied, InteractionInitialized, Revision);
            Parsed[Index] = new Record((int[])Desired.Clone(), (int[])Committed.Clone(), (byte[])Occupied.Clone(),
                (byte[])Consumed.Clone(), TrackerIds, (byte[])FogConsumed.Clone(), FogCommands,
                (byte[])FogOccupied.Clone(), FlowStarts, (byte[])SourceConsumed.Clone(), SourceCommands,
                (byte[])SourceOccupied.Clone(), SourceInitialized,
                (byte[])InteractionConsumed.Clone(), PropIntents, (byte[])InteractionOccupied.Clone(), InteractionInitialized, Initialized, FogInitialized);
        }
        Previous = -1;
        foreach (var Request in Retired)
        {
            var Index = RecordIndex(Request);
            if (Request <= Previous || Seen[Index]) throw new InvalidOperationException("Hooked retirement identities are duplicate or overlapping.");
            Previous = Request; Seen[Index] = true;
        }
        Preparation? Intent = null;
        if (Preparing.Length != 0)
        {
            if (Preparing is not { Length: 3 } || Preparing[0] is not int Request || Preparing[1] is not int Target
                || Preparing[2] is not int PayloadRevision) throw new InvalidOperationException("Hooked generation preparation is malformed.");
            var Index = RecordIndex(Request);
            if (Parsed[Index] is not { } Record || Target < 0 || Target >= Record.Desired.Length || PayloadRevision <= 0
                || PayloadRevision != (long)Record.Committed[Target] + 1 || PayloadRevision > Record.Desired[Target])
                throw new InvalidOperationException("Hooked generation preparation violates its revision order.");
            Intent = new Preparation(Index, Target, PayloadRevision);
        }
        return new Ledger(Revision, Parsed, ClockEpoch, unchecked((uint)ClockAnchor), CheckpointRestoreClock, Intent);
    }

    static void ValidateRecord(Declaration Declaration, int[] Desired, int[] Committed, byte[] Occupied, byte[] Consumed,
        string[][] TrackerIds, bool Initialized, HashSet<string> TrackerOwners)
    {
        if (Desired.Length != Declaration.Occurrence.HookedTargets.Length || Committed.Length != Desired.Length
            || TrackerIds.Length != Desired.Length || !ValidBits(Occupied, Declaration.Zones.Length) || !ValidBits(Consumed, Declaration.ActionCount))
            throw new InvalidOperationException("Hooked generation record has a different declaration shape.");
        if (Initialized && Declaration.ActionCount == 0)
            throw new InvalidOperationException("A record without generation actions cannot be initialized.");
        if (!Initialized)
        {
            foreach (var Value in Occupied) if (Value != 0) throw new InvalidOperationException("Dormant generation zones have occupied bits.");
            foreach (var Value in Consumed) if (Value != 0) throw new InvalidOperationException("Dormant generation zones have consumed actions.");
        }
        for (var i = 0; i < Desired.Length; i++)
        {
            if (Committed[i] < 0 || Desired[i] < Committed[i] || (long)Desired[i] - Committed[i] > 256)
                throw new InvalidOperationException("Hooked generation record has invalid revisions.");
            if (TrackerIdentityFailure(TrackerIds[i], Committed[i], TrackerOwners) is { } Failure)
                throw new InvalidOperationException(Failure);
        }
        var Offset = 0;
        foreach (var Zone in Declaration.Zones)
        {
            for (var i = 0; i < Zone.PropTriggers.Count; i++)
                if (Zone.Looped && Get(Consumed, Offset + i)) throw new InvalidOperationException("Looped generation action has a consumed bit.");
            Offset += Zone.PropTriggers.Count;
        }
    }

    static void ValidateFogRecord(Declaration Declaration, byte[] Consumed, FogCommand[] Commands,
        byte[] Occupied, bool Initialized, long Revision)
    {
        if (!ValidBits(Consumed, Declaration.FogActionCount) || !ValidBits(Occupied, Declaration.Zones.Length))
            throw new InvalidOperationException("Fog state has a different declaration shape.");
        if (Initialized && Declaration.FogActionCount == 0)
            throw new InvalidOperationException("A record without fog actions cannot be initialized.");
        if (!Initialized)
        {
            foreach (var Value in Occupied)
                if (Value != 0) throw new InvalidOperationException("Dormant world zones have occupied fog bits.");
            foreach (var Value in Consumed)
                if (Value != 0) throw new InvalidOperationException("Dormant world zones have consumed fog actions.");
        }
        for (var i = 0; i < Commands.Length; i++)
        {
            var Command = Commands[i]; var Settings = Declaration.FogZones[i].Settings;
            if (!float.IsFinite(Command.StartCoverage) || Command.StartCoverage < 0f || Command.StartCoverage > 1f
                || !double.IsFinite(Command.StartClock) || Command.StartClock < 0d
                || Command.CoverDelaySeconds != Settings.Transition.CoverDelaySeconds
                || Command.Revision < 0 || Command.Revision > Revision
                || Command.Revision == 0 && (Command.Target != Settings.InitiallyEnabled
                    || Command.StartCoverage != 0f || Command.StartClock != 0d))
                throw new InvalidOperationException("Fog command violates its coverage, clock, duration, or revision contract.");
        }
        var Offset = 0;
        foreach (var Zone in Declaration.Zones)
        {
            for (var i = 0; i < Zone.FogTriggers.Count; i++)
                if (Get(Consumed, Offset + i)
                    && (Zone.Looped || Commands[Zone.FogTriggers[i].TargetIndex].Revision == 0))
                    throw new InvalidOperationException("Consumed fog action is looped or targets an unpublished declaration.");
            Offset += Zone.FogTriggers.Count;
        }
    }

    static object[] EncodeFogCommands(FogCommand[] Commands)
    {
        var Values = new object[Commands.Length];
        for (var i = 0; i < Values.Length; i++)
        {
            var Command = Commands[i];
            Values[i] = new object[] { Command.Target, Command.StartCoverage, Command.StartClock,
                Command.CoverDelaySeconds, Command.Revision };
        }
        return Values;
    }

    static object[] EncodeFlowStarts(FlowPresentationStart[] Starts)
    {
        var Values = new object[Starts.Length];
        for (var i = 0; i < Values.Length; i++)
        {
            var Start = Starts[i];
            Values[i] = new object[] { Start.GenerationRevision, Start.StartClock, Start.Revision, Start.RestoreEndpoint };
        }
        return Values;
    }

    static void ValidateNativeSourceFields(double StartClock, long Revision, int[] Generations, float[] Coverages,
        int GroupCount, int[] Committed, long LedgerRevision)
    {
        if (!double.IsFinite(StartClock) || StartClock < 0d || Revision <= 0 || Revision > LedgerRevision
            || Generations.Length != GroupCount || Coverages.Length != GroupCount)
            throw new InvalidOperationException("Native source command violates its clock, revision, or publication shape.");
        for (var i = 0; i < Generations.Length; i++)
            if (Generations[i] < 0 || Generations[i] > (i == 0 ? 0 : Committed[i - 1])
                || !float.IsFinite(Coverages[i]) || Coverages[i] < 0f || Coverages[i] > 1f)
                throw new InvalidOperationException("Native source command violates its generation or coverage contract.");
    }

    static void ValidateNativeSourceRecord(Declaration Declaration, byte[] Consumed, NativeSourceCommand?[] Commands,
        byte[] Occupied, bool Initialized)
    {
        if (!ValidBits(Consumed, Declaration.NativeSourceActionCount)
            || !ValidBits(Occupied, Declaration.NativeSourceActionCount == 0 ? 0 : Declaration.Zones.Length)
            || Commands.Length != Declaration.SourceTargets.Length)
            throw new InvalidOperationException("Native source state has a different declaration shape.");
        if (Initialized && Declaration.NativeSourceActionCount == 0)
            throw new InvalidOperationException("A record without native source actions cannot be initialized.");
        if (!Initialized)
        {
            foreach (var Value in Occupied)
                if (Value != 0) throw new InvalidOperationException("Dormant world zones have occupied native source bits.");
            foreach (var Value in Consumed)
                if (Value != 0) throw new InvalidOperationException("Dormant world zones have consumed native source actions.");
            foreach (var Command in Commands)
                if (Command is not null) throw new InvalidOperationException("Dormant world zones have native source commands.");
        }
        var Offset = 0;
        foreach (var Zone in Declaration.Zones)
        {
            for (var i = 0; i < Zone.NativeSourceTriggers.Count; i++)
            {
                if (!Get(Consumed, Offset + i)) continue;
                if (Zone.Looped)
                    throw new InvalidOperationException("A looped native source action cannot be consumed.");
                var Action = Zone.NativeSourceTriggers[i];
                if (Action.Operation == PllNativeSourceOperation.Deactivate
                    && Commands[Declaration.NativeSourceIndices[Action.TargetIndex]] is null
                    && Declaration.SourceTargets[Declaration.NativeSourceIndices[Action.TargetIndex]].InitiallyEnabled)
                    throw new InvalidOperationException("A consumed native source deactivation has no command.");
            }
            Offset += Zone.NativeSourceTriggers.Count;
        }
    }

    static object[] EncodeNativeSourceCommands(NativeSourceCommand?[] Commands)
    {
        var Values = new object[Commands.Length];
        for (var i = 0; i < Values.Length; i++)
            Values[i] = Commands[i] is { } Command
                ? Command.Encode()
                : Array.Empty<object>();
        return Values;
    }

    static object[] EncodePropIntents(PropInteractionIntent?[] Intents)
    {
        var Values = new object[Intents.Length];
        for (var i = 0; i < Values.Length; i++) Values[i] = Intents[i]?.Encode() ?? Array.Empty<byte>();
        return Values;
    }

    static PropInteractionIntent?[] MaximumPropIntents(Declaration Declaration)
    {
        var Intents = new PropInteractionIntent?[Declaration.PropTargets.Length];
        foreach (var Zone in Declaration.Zones)
            foreach (var Action in Zone.InteractionTriggers)
            {
                var Target = Declaration.PropIndices[Declaration.Authoring.PropInteractionContracts[Action.TargetIndex]];
                Intents[Target] = PropInteractionIntent.Apply(Intents[Target], Action.Operation, true, true, 0d, 1);
            }
        return Intents;
    }

    static void ValidateInteractionRecord(Declaration Declaration, byte[] Consumed, PropInteractionIntent?[] Intents,
        byte[] Occupied, bool Initialized, long Revision)
    {
        if (!ValidBits(Consumed, Declaration.InteractionActionCount)
            || !ValidBits(Occupied, Declaration.InteractionActionCount == 0 ? 0 : Declaration.Zones.Length)
            || Intents.Length != Declaration.PropTargets.Length || Initialized && Declaration.InteractionActionCount == 0)
            throw new InvalidOperationException("Prop interaction state has a different declaration shape.");
        if (!Initialized)
        {
            foreach (var Value in Occupied) if (Value != 0) throw new InvalidOperationException("Dormant prop interaction zones are occupied.");
            foreach (var Value in Consumed) if (Value != 0) throw new InvalidOperationException("Dormant prop interaction zones have consumed actions.");
            foreach (var Intent in Intents) if (Intent is not null) throw new InvalidOperationException("Dormant prop interaction zones have retained intent.");
        }
        for (var i = 0; i < Intents.Length; i++)
            if (Intents[i] is { } Intent && (Intent.Revision > Revision
                || (Intent.BreakRequested || Intent.BreakEnabled.HasValue) && !Declaration.PropTargets[i].HasBreak
                || Intent.Motion.HasValue && (!Declaration.PropTargets[i].HasMotion || Declaration.PropTargets[i].Motion == PllPropMotion.NativeBridgeResponse)))
                throw new InvalidOperationException("Prop intent violates its revision or target capability.");
        var Offset = 0;
        foreach (var Zone in Declaration.Zones)
        {
            for (var i = 0; i < Zone.InteractionTriggers.Count; i++)
                if (Get(Consumed, Offset + i) && (Zone.Looped
                    || Intents[Declaration.PropIndices[Declaration.Authoring.PropInteractionContracts[Zone.InteractionTriggers[i].TargetIndex]]] is null))
                    throw new InvalidOperationException("Consumed prop action is looped or has no confirmed retained intent.");
            Offset += Zone.InteractionTriggers.Count;
        }
    }

    object[] EncodeTarget(GeneratedBatchKey Key, int Revision, int[] Actors, byte[] Raw, byte[] Trackers, object[] Envelope) =>
        new object[] { TargetProtocol, Run.GenerationEpoch, Run.LayoutRevision, Key.RequestIndex, Key.TargetIndex,
            Revision, Actors, Raw, Trackers, Envelope,
            Declarations[RecordIndex(Key.RequestIndex)].Occurrence.HookedTargets[Key.TargetIndex].Contract.TargetKeyHash.ToArray() };

    object[]? ReadTarget(GeneratedBatchKey Key, int RequiredRevision, out ViewBatchEnvelope Envelope,
        out int[] Actors, out byte[] Raw, out byte[] Trackers)
    {
        Envelope = default; Actors = Array.Empty<int>(); Raw = Trackers = Array.Empty<byte>();
        if (Room is null || Room.CustomProperties[TargetKey(Key)] is null) return null;
        var Declaration = Declarations[RecordIndex(Key.RequestIndex)];
        var Target = Declaration.Occurrence.HookedTargets[Key.TargetIndex];
        if (Room.CustomProperties[TargetKey(Key)] is not object[] { Length: 11 } Fields || !Header(Fields, TargetProtocol)
            || Fields[3] is not int Request || Request != Key.RequestIndex || Fields[4] is not int Index || Index != Key.TargetIndex
            || Fields[5] is not int Revision || Revision <= 0
            || Fields[6] is not int[] Required || !ValidActors(Required)
            || Fields[7] is not byte[] { Length: 32 } RawHash || Fields[8] is not byte[] { Length: 32 } TrackerHash
            || Fields[9] is not object[] ViewValue || Fields[10] is not byte[] { Length: 32 } TargetHash
            || !TargetHash.AsSpan().SequenceEqual(Target.Contract.TargetKeyHash)
            || !ViewBatchEnvelope.TryParse(ViewValue, Run.GenerationEpoch, Run.LayoutRevision,
                SeedFor(Target, Declaration.Occurrence, Revision), out Envelope, out _) || Envelope.ReplaceFakeItems)
            throw new InvalidOperationException("Hooked generation target state is malformed or divergent.");
        if (Revision != RequiredRevision) return null;
        Actors = Required; Raw = RawHash; Trackers = TrackerHash; return Fields;
    }

    object[] EncodeRpc(GeneratedBatchKey Key, int Revision, byte[] Hash, GeneratedRpcDeferral.BatchCatalog Catalog,
        int[] Delivered, int PublicationRevision, int MaximumCatalogBytes) => new object[] { RpcProtocol, Run.GenerationEpoch, Run.LayoutRevision,
            Key.RequestIndex, Key.TargetIndex, Revision, Hash, Catalog.InitializationActor,
            Catalog.HasBeehiveInitialization, Catalog.BeehiveEffectOwnerActor, Catalog.Fingerprint, Catalog.PropertyValue,
            PublicationRevision, Delivered, MaximumCatalogBytes };

    GeneratedRpcDeferral.BatchCatalog ReadRpc(GeneratedBatchKey Key, int RequiredRevision, byte[] RequiredHash,
        out int[] Delivered, out object[] Source)
    {
        var Name = RpcKey(Key);
        RequireBudget(new Hashtable { [Name] = Room!.CustomProperties[Name] }, Confirmed: true);
        if (!RetainedPropertyBytes.TryGetValue(Name, out var Reservation) || Reservation.Rpc is not { } Rpc
            || Rpc.Key != Key || Rpc.Revision != RequiredRevision || !Rpc.Hash.AsSpan().SequenceEqual(RequiredHash))
            throw new InvalidOperationException("Hooked generation RPC state is malformed or divergent.");
        Delivered = Rpc.Delivered; Source = Rpc.Source; return Rpc.Catalog;
    }

    RpcSnapshot ParseRpc(object Value, string Name, int Bytes)
    {
        if (Value is not object[] { Length: 15 } Fields || !Header(Fields, RpcProtocol)
            || Fields[3] is not int Request || Fields[4] is not int Target
            || Fields[5] is not int Revision || Revision <= 0 || Fields[6] is not byte[] { Length: 32 } Hash
            || Fields[14] is not int MaximumCatalogBytes || MaximumCatalogBytes <= 0
            || MaximumCatalogBytes > GeneratedRpcDeferral.MaximumPropertyBytes || Bytes > MaximumCatalogBytes
            || Fields[7] is not int InitializationActor || InitializationActor <= 0 || Fields[8] is not bool Beehive
            || Fields[9] is not int EffectOwner || EffectOwner < 0 || Fields[10] is not byte[] Fingerprint
            || !GeneratedRpcDeferral.BatchCatalog.TryParsePropertyCalls(Fields[11], Fingerprint, InitializationActor,
                Beehive, EffectOwner, out var Catalog) || Catalog.HasKind(GeneratedRpcDeferral.ReplayKind.Beehive) != Beehive
            || Fields[12] is not int PublicationRevision || PublicationRevision < 0
            || Fields[13] is not int[] Actors || !ValidActors(Actors))
            throw new InvalidOperationException("Hooked generation RPC state is malformed or divergent.");
        var Key = GeneratedBatchKey.Hooked(Request, Target);
        if (Name != RpcKey(Key)) throw new InvalidOperationException("Hooked generation RPC key differs from its payload.");
        return new RpcSnapshot((object[])PhotonPropertyProtocol.Snapshot(Fields), Key, Revision, Hash, Catalog, Actors, MaximumCatalogBytes);
    }

    GeneratedRpcDeferral.BatchCatalog ObserveRpc(LocalOutput Output, out int[] Delivered, out object[] Source)
    {
        var Value = Room!.CustomProperties[RpcKey(Output.Key)];
        if (!RetainedPropertyBytes.TryGetValue(RpcKey(Output.Key), out var Reservation)
            || !ReferenceEquals(Value, Reservation.SourceVersion) || !ReferenceEquals(Output.RpcSource, Reservation.Rpc?.Source))
        {
            var Catalog = ReadRpc(Output.Key, Output.Revision, Output.Hash, out var Actors, out var Fields);
            if (!Catalog.PreservesImmutableFacts(Output.Seed, Output.ViewIds))
                throw new InvalidOperationException("Hooked generation changed its immutable replay facts.");
            Output.RpcSource = Fields; Output.RpcCatalog = Catalog; Output.Delivered = Actors;
        }
        Delivered = Output.Delivered; Source = Output.RpcSource!; return Output.RpcCatalog!;
    }

    bool Header(object[] Fields, string Protocol) => Fields[0] is string Value && Value == Protocol
        && Fields[1] is string Epoch && Epoch == Run.GenerationEpoch && Fields[2] is int Layout && Layout == Run.LayoutRevision;

    int[] RequiredActors()
    {
        if (Room is null) return Array.Empty<int>();
        var Actors = new List<int>();
        if (Run.Publishing && Run.InitialGenerationCohort is { } Cohort)
        {
            for (var i = 0; i < Cohort.Count; i++) if (Room.Players.ContainsKey(Cohort.ActorAt(i))) Actors.Add(Cohort.ActorAt(i));
        }
        else foreach (var Actor in Room.Players.Keys) Actors.Add(Actor);
        Actors.Sort();
        if (Actors.Count > GeneratedRpcDeferral.MaximumReplayActors)
            throw new InvalidOperationException("Hooked generation exceeds its supported Photon actor count.");
        return Actors.ToArray();
    }

    static bool ValidActors(int[] Actors)
    {
        if (Actors.Length > GeneratedRpcDeferral.MaximumReplayActors) return false;
        for (var i = 0; i < Actors.Length; i++) if (Actors[i] <= 0 || i > 0 && Actors[i - 1] >= Actors[i]) return false;
        return true;
    }

    void ReserveDeclarationShape(Room? Room, Preparation? Candidate = null, string[]? CandidateTrackers = null)
    {
        var Maximum = new Record?[Declarations.Length]; var Entries = new List<object>();
        Preparation? First = null;
        for (var i = 0; i < Maximum.Length; i++)
        {
            var Declaration = Declarations[i];
            if (Current!.Records[i] is not { } Record) continue;
            var Desired = new int[Declaration.Occurrence.HookedTargets.Length]; Array.Fill(Desired, int.MaxValue);
            var TrackerIds = Record.TrackerIds;
            if (Candidate is { } Pending && Pending.RecordIndex == i)
            {
                TrackerIds = (string[][])TrackerIds.Clone(); TrackerIds[Pending.TargetIndex] = CandidateTrackers!;
            }
            var SourceCommands = (NativeSourceCommand?[])Record.NativeSourceCommands.Clone();
            foreach (var Zone in Declaration.Zones)
                foreach (var Action in Zone.NativeSourceTriggers)
                    if (Action.Operation == PllNativeSourceOperation.Deactivate)
                    {
                        var SourceIndex = Declaration.NativeSourceIndices[Action.TargetIndex];
                        if (SourceCommands[SourceIndex] is not null) continue;
                        SourceCommands[SourceIndex] = new NativeSourceCommand(false, 0d, 1,
                            new int[Declaration.FlowPublicationCount], new float[Declaration.FlowPublicationCount]);
                    }
            Maximum[i] = new Record(Desired, Desired, Bits(Declaration.Zones.Length), Bits(Declaration.ActionCount),
                TrackerIds, Bits(Declaration.FogActionCount), Record.FogCommands, Bits(Declaration.Zones.Length),
                Record.FlowStarts, Bits(Declaration.NativeSourceActionCount), SourceCommands,
                Declaration.NativeSourceActionCount == 0 ? Array.Empty<byte>() : Bits(Declaration.Zones.Length),
                Declaration.NativeSourceActionCount != 0, Bits(Declaration.InteractionActionCount), MaximumPropIntents(Declaration),
                Declaration.InteractionActionCount == 0 ? Array.Empty<byte>() : Bits(Declaration.Zones.Length),
                Declaration.InteractionActionCount != 0, Declaration.ActionCount != 0, Declaration.FogActionCount != 0);
            if (Desired.Length != 0) First ??= new Preparation(i, 0, int.MaxValue);
            for (var j = 0; j < Desired.Length; j++) Entries.Add(new object[]
                { Declaration.Occurrence.RequestIndex, j, int.MaxValue, new byte[32], (byte)1 });
        }
        var ControlBytes = GeneratedRpcDeferral.SerializePhotonValue(EncodeLedger(new Ledger(long.MaxValue,
            Maximum, long.MaxValue, uint.MaxValue, 0d, First))).Length;
        var AckBytes = GeneratedRpcDeferral.SerializePhotonValue(new object[]
            { AckProtocol, Run.GenerationEpoch, Run.LayoutRevision, Entries.ToArray() }).Length;
        if (ControlBytes > GeneratedRpcDeferral.MaximumPropertyBytes || AckBytes > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("Hooked declarations exceed the Photon control or acknowledgement budget.");
        FixedNetworkBytes = checked(ControlBytes + AckBytes * (Room is not { IsOffline: false } || Entries.Count == 0 ? 0
            : GeneratedRpcDeferral.MaximumReplayActors));
        var Total = FixedNetworkBytes;
        foreach (var Reservation in RetainedPropertyBytes.Values) Total = checked(Total + Reservation.HeldBytes);
        Run.ReplayBudget.Reserve(this, Total);
    }

    void RequireBudget(Hashtable Candidate, object? Transfer = null, bool Confirmed = false, RpcSnapshot? CandidateRpc = null)
    {
        var Updated = new Dictionary<string, PropertyReservation>(StringComparer.Ordinal);
        foreach (var Pair in Candidate)
        {
            if (Pair.Key is not string Name) continue;
            var Rpc = Name.StartsWith(PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-rpc/", StringComparison.Ordinal);
            var Target = Name.StartsWith(PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-target/", StringComparison.Ordinal);
            RetainedPropertyBytes.TryGetValue(Name, out var Retained);
            var Count = Pair.Value is null ? 0 : ReferenceEquals(Retained.SourceVersion, Pair.Value)
                ? Retained.ActualBytes : GeneratedRpcDeferral.SerializePhotonValue(Pair.Value).Length;
            if (Count > GeneratedRpcDeferral.MaximumPropertyBytes)
                throw new InvalidOperationException("Hooked generation property exceeds the Photon payload budget.");
            if (!Rpc && !Target) continue;
            var Constructed = CandidateRpc is not null && Name == RpcKey(CandidateRpc.Key) && ReferenceEquals(CandidateRpc.Source, Pair.Value);
            var Snapshot = Rpc && Pair.Value is not null
                ? Constructed ? CandidateRpc : ReferenceEquals(Retained.SourceVersion, Pair.Value) ? Retained.Rpc! : ParseRpc(Pair.Value, Name, Count)
                : null;
            if (Constructed && Count > Snapshot!.MaximumCatalogBytes)
                throw new InvalidOperationException("Hooked RPC publication exceeds its reserved mutation capacity.");
            if (Snapshot is not null && Retained.ConfirmedRpc is { } Previous && Snapshot.Revision == Previous.Revision
                && (Snapshot.MaximumCatalogBytes != Previous.MaximumCatalogBytes
                    || (int)Snapshot.Source[12] < (int)Previous.Source[12]
                    || (int)Snapshot.Source[12] == (int)Previous.Source[12]
                        && !PhotonPropertyProtocol.SameValue(Snapshot.Source, Previous.Source)))
                throw new InvalidOperationException("Hooked RPC state changed or moved backwards during one source lifetime.");
            if (Snapshot is not null && Candidate.TryGetValue(TargetKey(Snapshot.Key), out var Sibling)
                && (Sibling is not object[] TargetValue || (int)TargetValue[5] != Snapshot.Revision
                    || !Snapshot.Hash.AsSpan().SequenceEqual(Hash(TargetValue))))
                throw new InvalidOperationException("Hooked generation RPC has divergent target siblings.");
            var Held = Math.Max(Count, Snapshot?.MaximumCatalogBytes ?? 0);
            if (!Confirmed && Room is not null) Held = Math.Max(Held, Retained.HeldBytes);
            if (!Confirmed && Pair.Value is null) Snapshot = Retained.Rpc;
            Updated[Name] = new PropertyReservation(Held, Count, Pair.Value, Snapshot,
                Confirmed ? Snapshot : Retained.ConfirmedRpc);
        }
        var Total = FixedNetworkBytes;
        foreach (var Pair in RetainedPropertyBytes) if (!Updated.ContainsKey(Pair.Key)) Total = checked(Total + Pair.Value.HeldBytes);
        foreach (var Reservation in Updated.Values) Total = checked(Total + Reservation.HeldBytes);
        Run.ReplayBudget.Reserve(this, Total, Transfer);
        foreach (var Pair in Updated)
            if (Pair.Value.HeldBytes == 0) RetainedPropertyBytes.Remove(Pair.Key);
            else RetainedPropertyBytes[Pair.Key] = Pair.Value;
    }

    int MaximumRpcBytes(GeneratedBatchKey Key, int Revision, PhotonView[] Views, bool Online)
    {
        var Actors = new int[Online ? GeneratedRpcDeferral.MaximumReplayActors : 0];
        for (var i = 0; i < Actors.Length; i++) Actors[i] = int.MaxValue - Actors.Length + i + 1;
        var Bytes = GeneratedRpcDeferral.SerializePhotonValue(EncodeRpc(Key, Revision, new byte[32],
            GeneratedRpcDeferral.MaximumBatchCatalog(Views), Actors, int.MaxValue, GeneratedRpcDeferral.MaximumPropertyBytes)).Length;
        if (Bytes > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("Hooked replay capacity exceeds its Photon property budget.");
        return Bytes;
    }

    void ObserveRetainedTargets(Ledger Ledger)
    {
        var Values = new Hashtable();
        for (var i = 0; i < Declarations.Length; i++)
            for (var j = 0; j < Declarations[i].Occurrence.HookedTargets.Length; j++)
            {
                var Key = GeneratedBatchKey.Hooked(Declarations[i].Occurrence.RequestIndex, j);
                var Target = Room!.CustomProperties[TargetKey(Key)];
                var Rpc = Room.CustomProperties[RpcKey(Key)];
                var Record = Ledger.Records[i];
                if (Target is null && Rpc is null)
                {
                    if (Record is not null && Record.Committed[j] > 0 && !MissingSavedTransport(Key, Record.Committed[j]))
                        throw new InvalidOperationException("Committed Hooked generation transport is missing.");
                }
                else
                {
                    if (Record is null || Target is not object[] { Length: 11 } Fields || Rpc is null
                        || Fields[5] is not int Revision || Revision <= 0
                        || Revision != Record.Committed[j] && !(Ledger.Preparing is { } Preparing
                            && Preparing.RecordIndex == i && Preparing.TargetIndex == j && Preparing.Revision == Revision))
                        throw new InvalidOperationException("Hooked generation transport has incomplete or foreign siblings.");
                    _ = ReadTarget(Key, Revision, out _, out _, out _, out _);
                }
                Values[TargetKey(Key)] = Target; Values[RpcKey(Key)] = Rpc;
            }
        RequireBudget(Values, Confirmed: true);
    }

    bool MissingSavedTransport(GeneratedBatchKey Key, int Revision)
    {
        if (RetainedPropertyBytes.ContainsKey(RpcKey(Key))) return false;
        if (Run.Ingress.LoadedQuicksaveLayout?.hookedGeneration is not { } Saved) return false;
        foreach (var Record in Saved)
            if (Record.requestIndex == Key.RequestIndex) return Record.committed[Key.TargetIndex] == Revision;
        return false;
    }

    internal MapQuicksave.HookedGenerationSave[] CaptureQuicksave(double Clock, out int[] Retired)
    {
        if (!Stable) throw new InvalidOperationException("Hooked generation has no stable logical save snapshot.");
        var Saved = new List<MapQuicksave.HookedGenerationSave>(); var RetiredValues = new List<int>();
        for (var i = 0; i < Declarations.Length; i++)
        {
            if (Declarations[i].Occurrence.HookedTargets.Length == 0 && Declarations[i].FogZones.Count == 0
                && Declarations[i].SourceTargets.Length == 0 && Declarations[i].PropTargets.Length == 0) continue;
            if (Current!.Records[i] is not { } Record) { RetiredValues.Add(Declarations[i].Occurrence.RequestIndex); continue; }
            var TrackerIds = new MapQuicksave.HookedTrackerIdsSave[Record.TrackerIds.Length];
            for (var j = 0; j < TrackerIds.Length; j++)
                TrackerIds[j] = new MapQuicksave.HookedTrackerIdsSave { ids = (string[])Record.TrackerIds[j].Clone() };
            var FogTargets = new byte[Record.FogCommands.Length];
            for (var j = 0; j < FogTargets.Length; j++)
                FogTargets[j] = Record.FogCommands[j].Revision == 0 ? (byte)0
                    : Record.FogCommands[j].Target ? (byte)2 : (byte)1;
            var SourceTargets = new byte[Record.NativeSourceCommands.Length];
            for (var j = 0; j < SourceTargets.Length; j++)
                SourceTargets[j] = Record.NativeSourceCommands[j] is { } Command ? Command.Target ? (byte)2 : (byte)1 : (byte)0;
            var PropIntents = new MapQuicksave.PropIntentSave[Record.PropIntents.Length];
            for (var j = 0; j < PropIntents.Length; j++)
                PropIntents[j] = new MapQuicksave.PropIntentSave { payload = Record.PropIntents[j]?.Rebase(-Clock).Encode() ?? Array.Empty<byte>() };
            Saved.Add(new MapQuicksave.HookedGenerationSave { requestIndex = Declarations[i].Occurrence.RequestIndex,
                desired = (int[])Record.Desired.Clone(), committed = (int[])Record.Committed.Clone(),
                consumed = (byte[])Record.Consumed.Clone(), initialized = Record.Initialized, trackerIds = TrackerIds,
                fogConsumed = (byte[])Record.FogConsumed.Clone(), fogTargets = FogTargets,
                fogInitialized = Record.FogInitialized, sourceConsumed = (byte[])Record.NativeSourceConsumed.Clone(),
                sourceTargets = SourceTargets, sourceInitialized = Record.NativeSourceInitialized,
                interactionConsumed = (byte[])Record.InteractionConsumed.Clone(), propIntents = PropIntents,
                interactionInitialized = Record.InteractionInitialized });
        }
        Retired = RetiredValues.ToArray(); return Saved.ToArray();
    }

    internal bool SaveNow()
    {
        if (Saving) return true;
        if (Failed || Released || !Run.Network.OwnsCurrentRun) throw MapRun.FeatureWriter.Cancelled();
        if (SavePending) return false;
        if (Stable) return true;
        SavePending = true; return false;
    }

    internal void SaveOrDefer(global::Campfire Campfire)
    {
        if (SaveCampfire && SaveCampfire != Campfire)
            throw new InvalidOperationException("A pending quicksave belongs to another campfire transition.");
        SaveCampfire = Campfire; SavePending = true;
        CompleteSave();
    }

    void CompleteSave()
    {
        if (!SavePending || !Stable) return;
        Saving = true;
        try
        {
            Peak.Quicksave.SaveNow();
            if (!Run.Network.OwnsCurrentRun) throw MapRun.FeatureWriter.Cancelled();
            if (SaveCampfire) GUIManager.instance.Quicksave();
        }
        finally { Saving = false; SavePending = false; SaveCampfire = null; }
    }

    void AddViews(LocalOutput Output)
    {
        AckValue = null;
        foreach (var View in Output.Batch.Batch.Views) Views.Add(View, Output);
    }

    void RetireLocal(GeneratedBatchKey Key, bool EndSourceLifetime = true)
    {
        if (!Outputs.Remove(Key, out var Output)) return;
        AckValue = null;
        var Failures = new List<Exception>();
        void Cleanup(Action Action)
        {
            try { Action(); }
            catch (Exception Failure) { Failures.Add(Failure); }
        }
        var Target = Declarations[RecordIndex(Key.RequestIndex)].Occurrence.HookedTargets[Key.TargetIndex];
        if (!Run.SceneGone)
        {
            Cleanup(() => Run.PropInteractions.Retire(Target.Root.gameObject));
            Cleanup(() => Run.SurfaceTextures.Retire(Target.Root.gameObject));
            Cleanup(() => Run.NativeMaterials.Retire(Target.Root.gameObject));
            Cleanup(() => Run.NativeSpawners.Retire(Target.Root.gameObject));
        }
        if (EndSourceLifetime)
        {
            Cleanup(() => RetireSavedHistory(Output.TrackerIds));
            Cleanup(() => Run.WorldInteractions.EndSourceLifetime(Target, Output.Revision));
        }
        Cleanup(() => Run.NativeSpawns.RetireHookedSources(Target, Output.Revision));
        foreach (var View in Output.Batch.Batch.Views)
        {
            Views.Remove(View);
            Cleanup(() =>
            {
                if (View && View.ViewID != 0) { Run.Retirement.RetiredViewIds.Add(View.ViewID); View.ViewID = 0; }
            });
        }
        Cleanup(() => GeneratedRpcDeferral.ReleaseRegisteredBatch(Output.Batch.Batch.Views));
        Cleanup(() => GeneratedRpcDeferral.ReleaseReplayBatch(Key));
        if (Output.FakeRegistry is { } Manager && Manager.allFakeItems is { } Items)
        {
            foreach (var Item in Output.Batch.Batch.FakeItems) Items.Remove(Item);
            if (ReferenceEquals(Map.CurrentRun, Run) && Output.FakeIds.Length != 0 && Manager.fakeItemData.hiddenItems is { } Hidden)
            {
                var Retired = new HashSet<int>(Output.FakeIds); Hidden.RemoveAll(Retired.Contains);
            }
        }
        if (Failures.Count != 0) throw new AggregateException("Hooked output retirement failed.", Failures);
    }

    internal void RetireRoot(GameObject Root)
    {
        var Retired = new List<Transform>();
        foreach (var Pair in TargetRoots)
            if (TransformOwnership.Contains(Root.transform, Pair.Key)) Retired.Add(Pair.Key);
        var Failures = new List<Exception>();
        foreach (var Source in Retired)
        {
            var Target = TargetRoots[Source];
            try { RetireLocal(Target.BatchKey); }
            catch (Exception Failure) { Failures.Add(Failure); }
            try
            {
                NativeFeatureGeneration.Clear(Target.Grouper);
                Target.Occurrence.GeometryBudget.ReleaseNativeOutput(Target);
            }
            catch (Exception Failure) { Failures.Add(Failure); }
            TargetRoots.Remove(Source);
        }
        if (Failures.Count != 0) throw new AggregateException("Hooked source retirement failed.", Failures);
    }

    bool RetireCompletedOccurrences()
    {
        if (Current is null || Pending is not null || Current.Preparing is not null || Routine is not null) return false;
        Record?[]? Records = null; var Properties = new Hashtable();
        for (var i = 0; i < Declarations.Length; i++)
        {
            if (Current.Records[i] is null || !OccurrenceReleased(Declarations[i].Occurrence)) continue;
            Records ??= (Record?[])Current.Records.Clone(); Records[i] = null;
            foreach (var Target in Declarations[i].Occurrence.HookedTargets)
            {
                Properties[TargetKey(Target.BatchKey)] = null; Properties[RpcKey(Target.BatchKey)] = null;
            }
        }
        return Records is not null && Publish(new Ledger(checked(Current.Revision + 1), Records, Current.ClockEpoch, Current.ClockAnchor, Current.CheckpointRestoreClock), Properties);
    }

    internal void Release()
    {
        Cancel();
        var Failures = new List<Exception>();
        if (CancellationFailure is { } Cancellation) Failures.Add(Cancellation);
        foreach (var Key in new List<GeneratedBatchKey>(Outputs.Keys))
        {
            try { RetireLocal(Key); }
            catch (Exception Failure) { Failures.Add(Failure); }
        }
        if (Room is not null && PhotonNetwork.IsMasterClient && PhotonNetwork.NetworkClientState == ClientState.Joined
            && ReferenceEquals(Room, PhotonNetwork.CurrentRoom)
            && Room.CustomProperties[ControlKey] is object[] Control && Header(Control, ControlProtocol))
        {
            var Properties = new Hashtable { [ControlKey] = null };
            foreach (var Declaration in Declarations)
                foreach (var Target in Declaration.Occurrence.HookedTargets)
                { Properties[TargetKey(Target.BatchKey)] = null; Properties[RpcKey(Target.BatchKey)] = null; }
            foreach (var Pair in Room.CustomProperties)
                if (Pair.Key is string Name && Name.StartsWith(PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-target-ack/", StringComparison.Ordinal)
                    && Pair.Value is object[] Value && Header(Value, AckProtocol)) Properties[Name] = null;
            if (!Room.SetCustomProperties(Properties, new Hashtable { [ControlKey] = Control }))
                Failures.Add(new InvalidOperationException("Hooked room property cleanup was rejected."));
        }
        TargetRoots.Clear(); ActiveCapture = null;
        RecordIndices.Clear(); Declarations = Array.Empty<Declaration>();
        RetainedPropertyBytes.Clear(); Run.ReplayBudget.Reserve(this, 0);
        ActorAcknowledgements.Clear(); AckValue = null; Current = null; CurrentValue = null;
        if (Failures.Count != 0) throw new AggregateException("Hooked generation cleanup failed.", Failures);
    }

    void CancelRoutine()
    {
        var CurrentRoutine = Routine; Routine = null;
        try { (CurrentRoutine as IDisposable)?.Dispose(); }
        finally { Writer?.Dispose(); Writer = null; }
    }

    internal void Cancel()
    {
        Released = true; SavePending = false; SaveCampfire = null; Edges.Clear(); Pending = null; AckPublication.Reset();
        foreach (var Declaration in Declarations)
            for (var i = 0; i < Declaration.ResolvedZones.Length; i++)
                Declaration.ResolvedZones[i] = Array.Empty<ResolvedEffectZone>();
        try { CancelRoutine(); }
        catch (Exception Failure)
        {
            CancellationFailure = CancellationFailure is null ? Failure
                : new AggregateException("Hooked generation cancellation and support cleanup failed.", CancellationFailure, Failure);
        }
    }

    OutputEffects CaptureEffects(HookedPropTarget Target)
    {
        var Weather = false; var SpecialDay = false; var Geometry = false; var Lighting = false;
        foreach (var Component in Target.Root.GetComponentsInChildren<Component>(true))
        {
            if (!NativeCollision.IsGeneratedOutput(Target.Root, Component.transform)) continue;
            Geometry = true;
            Weather |= Component is StormVisual or RootsWind or DisableIfWindActive or WindAffectedStatusEmitter;
            SpecialDay |= Component is SpecialDayZone;
            if (Component is MeshRenderer Renderer) Lighting |= Run.LightVolumes.OwnedVolume.CheckRendererValid(Renderer);
        }
        return new OutputEffects(Weather, SpecialDay, Geometry, Lighting);
    }

    internal bool InitialOutputsReady()
    {
        return Run.PresentationClockReady && Routine is null && !Run.FeatureMutationActive
            && CommittedOutputsReady(Run.Slots[0].Occurrence);
    }

    static int SeedFor(HookedPropTarget Target, FeatureOccurrence Occurrence, int Revision) =>
        FeatureGeneration.FeatureSeed(Occurrence.GenerationSeed, Revision, "hooked-prop-generation/v1/" + Target.Contract.StableTargetKey);
    static byte[] Hash(object[] Value)
    {
        using var Sha = SHA256.Create(); return Sha.ComputeHash(GeneratedRpcDeferral.SerializePhotonValue(Value));
    }
    static byte[] Bits(int Count) => new byte[(Count + 7) / 8];
    static string[][] EmptyTrackerIds(int Count)
    {
        var Values = new string[Count][];
        for (var i = 0; i < Values.Length; i++) Values[i] = Array.Empty<string>();
        return Values;
    }
    static object[] EncodeTrackerIds(string[][] Values)
    {
        var Result = new object[Values.Length];
        for (var i = 0; i < Values.Length; i++) Result[i] = Values[i];
        return Result;
    }
    internal static string? TrackerIdentityFailure(string[] Ids, int Revision, HashSet<string> Owners)
    {
        if (Ids is null || Revision == 0 && Ids.Length != 0
            || Ids.Length > GeneratedRpcDeferral.MaximumPropertyBytes / 32)
            return "Hooked tracker identities have an invalid revision or count.";
        string? Previous = null;
        foreach (var Id in Ids)
        {
            if (Id is not { Length: 32 } || Previous is not null && StringComparer.Ordinal.Compare(Previous, Id) >= 0)
                return "Hooked tracker identities are missing, duplicate, or unordered.";
            foreach (var Character in Id)
                if (!(Character is >= '0' and <= '9' or >= 'a' and <= 'f'))
                    return "Hooked tracker identity is not a canonical Hash128.";
            if (!Owners.Add(Id)) return "Hooked tracker identity has multiple target owners.";
            Previous = Id;
        }
        return null;
    }
    static bool SameTrackerIds(string[] Left, string[] Right)
    {
        if (Left.Length != Right.Length) return false;
        for (var i = 0; i < Left.Length; i++) if (Left[i] != Right[i]) return false;
        return true;
    }
    void RetireSavedHistory(string[] Ids)
    {
        if (Run.Ingress.SavedSpawnHistories is not { } Histories) return;
        foreach (var Id in Ids) Histories.Remove(Hash128.Parse(Id));
    }
    internal void TrackerMaterialized(Peak.SpawnedItemTracker Tracker)
    {
        if (Current is null || !TryGetSourceLifetime(Tracker.transform, out var Target, out var Revision)) return;
        var Record = Current.Records[RecordIndex(Target.Occurrence.RequestIndex)];
        if (Record is not null && Record.Committed[Target.TargetIndex] == Revision
            && Array.BinarySearch(Record.TrackerIds[Target.TargetIndex], Tracker.SpawnerId.ToString(), StringComparer.Ordinal) >= 0)
            Run.Ingress.SavedSpawnHistories?.Remove(Tracker.SpawnerId);
    }
    internal void CarryUnmaterializedHistories(ref Peak.Quicksave.RunProgress Progress)
    {
        if (Current is null || Run.Ingress.SavedSpawnHistories is not { } Histories) return;
        Dictionary<Hash128, int>? Indices = null;
        foreach (var Record in Current.Records)
        {
            if (Record is null) continue;
            foreach (var Target in Record.TrackerIds)
                foreach (var Id in Target)
                {
                    var Key = Hash128.Parse(Id);
                    if (!Histories.TryGetValue(Key, out var Records)) continue;
                    if (Indices is null)
                    {
                        Indices = new Dictionary<Hash128, int>(Progress.spawnHistory.Count);
                        for (var i = 0; i < Progress.spawnHistory.Count; i++) Indices.Add(Progress.spawnHistory[i].spawnerId, i);
                    }
                    var History = new Peak.Quicksave.RunProgress.SpawnHistory { spawnerId = Key, spawnedItems = Records.ToArray() };
                    if (Indices.TryGetValue(Key, out var Index)) Progress.spawnHistory[Index] = History;
                    else { Indices.Add(Key, Progress.spawnHistory.Count); Progress.spawnHistory.Add(History); }
                }
        }
        if (Indices is not null) Progress._spawnHistoryLookup = null;
    }

    internal void PlayerLeft(int Actor)
    {
        ActorAcknowledgements.Remove(Actor);
        foreach (var Output in Outputs.Values)
        {
            Output.ReplayedRegistrations.Remove(Actor); Output.MobActors.Remove(Actor);
        }
        if (Room is not null && PhotonNetwork.IsMasterClient && Room.CustomProperties[AckKey(Actor)] is object[] Value
            && Header(Value, AckProtocol) && !Room.SetCustomProperties(new Hashtable { [AckKey(Actor)] = null },
                new Hashtable { [AckKey(Actor)] = Value }))
            throw new InvalidOperationException("Hooked departed-actor acknowledgement cleanup was rejected.");
    }
    static bool Get(byte[] Values, int Index) => (Values[Index / 8] & 1 << Index % 8) != 0;
    static void Set(byte[] Values, int Index, bool Value)
    {
        if (Value) Values[Index / 8] |= (byte)(1 << Index % 8);
        else Values[Index / 8] &= (byte)~(1 << Index % 8);
    }
    static bool ValidBits(byte[] Values, int Count) => Values.Length == (Count + 7) / 8
        && (Count % 8 == 0 || (Values[Values.Length - 1] >> Count % 8) == 0);
    static string Suffix(GeneratedBatchKey Key) => Key.RequestIndex.ToString("D3", CultureInfo.InvariantCulture)
        + "/" + Key.TargetIndex.ToString("D3", CultureInfo.InvariantCulture);
    static string TargetKey(GeneratedBatchKey Key) => PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-target/" + Suffix(Key);
    static string RpcKey(GeneratedBatchKey Key) => PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-rpc/" + Suffix(Key);
    static string AckKey(int Actor) => PhotonPropertyProtocol.StatePrefix + "hooked-prop-generation-target-ack/" + Actor.ToString(CultureInfo.InvariantCulture);

    [HarmonyPatch(typeof(Peak.Quicksave), nameof(Peak.Quicksave.SaveNow))]
    static class SavePatch
    {
        [HarmonyPrefix]
        static bool Prefix() => Map.CurrentRun is not { Active: true } Run || Run.HookedGeneration.SaveNow();
    }

    [HarmonyPatch(typeof(FakeItem), nameof(FakeItem.IsInteractible))]
    static class FakeItemInteractionPatch
    {
        [HarmonyPostfix]
        static void Postfix(FakeItem __instance, ref bool __result) =>
            __result &= Map.CurrentRun?.HookedGeneration.InteractionReady(__instance.transform) ?? true;
    }

    [HarmonyPatch(typeof(FakeItem), nameof(FakeItem.Interact))]
    static class FakeItemPickupPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FakeItem __instance) =>
            Map.CurrentRun?.HookedGeneration.InteractionReady(__instance.transform) ?? true;
    }
}
