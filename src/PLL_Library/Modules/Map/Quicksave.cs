using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal sealed class MapQuicksave
{
    readonly Dictionary<int, string> ConsumedAmuletSources;
    readonly Dictionary<string, int> AllowedAmuletSources = new(StringComparer.Ordinal);
    readonly Dictionary<Peak.PropSpawner_AmuletStatues, string> PlacedAmuletSources = new();
    readonly Dictionary<FakeItem, string> AmuletSources = new();
    readonly List<string> MaximumAmuletSnapshot = new();
    int BoundAmuletSlots;
    int MaximumAmuletKeyLength;

    internal MapQuicksave(QuicksaveLayout? Saved = null) => ConsumedAmuletSources = Saved is null
        ? new Dictionary<int, string>()
        : new Dictionary<int, string>(Saved.ParsedAmuletConsumption
            ?? throw new InvalidOperationException("The saved amulet source state was not parsed before run construction."));

    internal void BindAmuletSources(MapSlot[] Slots)
    {
        if (BoundAmuletSlots == Slots.Length) return;
        var InitialBinding = BoundAmuletSlots == 0;
        for (var Index = BoundAmuletSlots; Index < Slots.Length; Index++)
        {
            var Slot = Slots[Index];
            if (Slot.Occurrence.BatchIndex != Slot.Index) continue;
            string? Longest = null;
            foreach (var Declaration in Slot.Occurrence.Slots)
            foreach (var Binding in Declaration.Source.AmuletSourceBindings)
            {
                var Key = AmuletSourceKey(Slot, Binding);
                AllowedAmuletSources.TryAdd(Key, Slot.Index);
                MaximumAmuletKeyLength = Math.Max(MaximumAmuletKeyLength, Key.Length);
                if (Longest is null || Key.Length > Longest.Length) Longest = Key;
            }
            if (Longest is not null) MaximumAmuletSnapshot.Add(Longest);
        }
        BoundAmuletSlots = Slots.Length;
        if (InitialBinding)
            foreach (var Pair in ConsumedAmuletSources)
                if (!AllowedAmuletSources.TryGetValue(Pair.Value, out var Owner) || Owner != Pair.Key)
                    throw RejectQuicksave("a consumed amulet source does not belong to this layout.");
    }

    internal string[] CaptureMaximumAmuletSnapshot() => MaximumAmuletSnapshot.ToArray();

    static string AmuletSourceKey(MapSlot Owner, string Binding) =>
        Owner.Index.ToString("X8", CultureInfo.InvariantCulture)
        + ":" + Owner.Source.StableId + "\n" + Binding;

    internal void EnrollAmuletSource(MapSlot Owner,
        Peak.PropSpawner_AmuletStatues Generator, string Binding)
    {
        var Key = AmuletSourceKey(Owner, Binding);
        if (PlacedAmuletSources.TryGetValue(Generator, out var Current))
        {
            if (Current != Key)
                throw new InvalidOperationException("A placed amulet generator received different logical source bindings.");
            return;
        }
        PlacedAmuletSources.Add(Generator, Key);
    }

    internal void ApplyAmuletConsumption(FakeItem[] Items, MapSlot[] Slots)
    {
        var Retired = new List<FakeItem>();
        foreach (var Pair in AmuletSources)
            if (!Pair.Key) Retired.Add(Pair.Key!);
        foreach (var Item in Retired) AmuletSources.Remove(Item);
        foreach (var Item in Items)
        {
            var Generator = Item.GetComponentInParent<Peak.PropSpawner_AmuletStatues>(true);
            if (!Generator) continue;
            if (!PlacedAmuletSources.TryGetValue(Generator, out var Key))
            {
                foreach (var Slot in Slots)
                    if (!Slot.Released && Slot.Container
                        && TransformOwnership.Contains(Slot.Container, Generator.transform))
                        throw new InvalidOperationException("A generated amulet has no enrolled logical source binding.");
                continue;
            }
            if (!AllowedAmuletSources.TryGetValue(Key, out var Owner))
                throw new InvalidOperationException("A generated amulet has no sealed logical source binding.");
            foreach (var Pair in AmuletSources)
                if (Pair.Key != Item && Pair.Value == Key)
                    throw new InvalidOperationException("An amulet source published more than one collectible.");
            AmuletSources[Item] = Key;
            if (Item.pickedUp) AddConsumedAmulet(Key);
            else if (ConsumedAmuletSources.TryGetValue(Owner, out var Consumed) && Consumed == Key)
                Item.PickUpVisibly();
        }
    }

    void RecordAmuletPickup(FakeItem Item)
    {
        if (AmuletSources.TryGetValue(Item, out var Key) && AddConsumedAmulet(Key))
            Map.CurrentRun!.Network.ViewBatches.QueueAmuletConsumption();
    }

    bool AddConsumedAmulet(string Key)
    {
        var Owner = AllowedAmuletSources[Key];
        if (ConsumedAmuletSources.TryGetValue(Owner, out var Current))
        {
            if (Current != Key)
                throw new InvalidOperationException("An occurrence changed its consumed amulet source binding.");
            return false;
        }
        ConsumedAmuletSources.Add(Owner, Key);
        return true;
    }

    internal string[] CaptureConsumedAmuletSources()
    {
        foreach (var Pair in AmuletSources)
            if (Pair.Key && Pair.Key.pickedUp) AddConsumedAmulet(Pair.Value);
        var Values = new string[ConsumedAmuletSources.Count];
        ConsumedAmuletSources.Values.CopyTo(Values, 0);
        Array.Sort(Values, StringComparer.Ordinal);
        return Values;
    }

    internal void AdoptConsumedAmuletSources(string[] Keys)
    {
        if (Keys.Length > PllLayoutCapacityContract.MaximumOrdinarySegments)
            throw new InvalidOperationException("The synchronized amulet source count exceeds the layout capacity.");
        string? Previous = null;
        var Occurrences = new HashSet<int>();
        foreach (var Key in Keys)
        {
            if (Key is null || Key.Length > MaximumAmuletKeyLength
                || !AllowedAmuletSources.TryGetValue(Key, out var Occurrence) || !Occurrences.Add(Occurrence)
                || ConsumedAmuletSources.TryGetValue(Occurrence, out var Current) && Current != Key
                || Previous is not null && StringComparer.Ordinal.Compare(Previous, Key) >= 0)
                throw new InvalidOperationException("The synchronized amulet source state has an unknown or noncanonical binding.");
            Previous = Key;
        }
        foreach (var Key in Keys) AddConsumedAmulet(Key);
        foreach (var Pair in AmuletSources)
            if (Pair.Key && !Pair.Key.pickedUp
                && ConsumedAmuletSources.TryGetValue(AllowedAmuletSources[Pair.Value], out var Current)
                && Current == Pair.Value)
                Pair.Key.PickUpVisibly();
    }

    static bool ValidAmuletSourceKey(string? Key, int SlotCount, out int Slot)
    {
        Slot = -1;
        if (Key is null || Key.Length < 20 || Key[8] != ':'
            || !int.TryParse(Key.AsSpan(0, 8), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out Slot)
            || Slot < 0 || Slot >= SlotCount) return false;
        var Separator = Key.IndexOf('\n', 9);
        if (Separator < 0 || !SegmentId.TryParse(Key.Substring(9, Separator - 9), out _)) return false;
        var Path = Key.AsSpan(Separator + 1);
        if (Path.StartsWith("authored:".AsSpan(), StringComparison.Ordinal)) Path = Path.Slice(9);
        else if (Path.StartsWith("native:".AsSpan(), StringComparison.Ordinal)) Path = Path.Slice(7);
        else return false;
        if (Path.Length < 9 || Path.Length % 9 != 0) return false;
        for (var Index = 0; Index < Path.Length - 9; Index += 9)
            if (Path[Index + 8] != '/' || !Hex(Path.Slice(Index, 8))) return false;
        return Path[Path.Length - 9] == '#' && Hex(Path.Slice(Path.Length - 8));

        static bool Hex(ReadOnlySpan<char> Value)
        {
            foreach (var Character in Value)
                if (Character is not (>= '0' and <= '9' or >= 'A' and <= 'F')) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(FakeItem), nameof(FakeItem.PickUpVisibly))]
    static class AmuletPickupPatch
    {
        static void Postfix(FakeItem __instance) => Map.CurrentRun?.Quicksave.RecordAmuletPickup(__instance);
    }

    sealed class HostCampfireRestoration
    {
        enum RetentionState : byte { AwaitingSpawn, Retained, Released }

        internal HostCampfireRestoration(
            int CampfireSlot,
            int TargetSlot,
            GameObject CheckpointRoot,
            global::Campfire? Campfire)
        {
            this.CampfireSlot = CampfireSlot;
            this.TargetSlot = TargetSlot;
            this.CheckpointRoot = CheckpointRoot;
            this.Campfire = Campfire;
        }

        internal int CampfireSlot { get; }
        internal int TargetSlot { get; }
        internal GameObject CheckpointRoot { get; }
        internal global::Campfire? Campfire { get; private set; }
        internal bool Retained => State != RetentionState.Released;
        RetentionState State { get; set; }

        internal global::Campfire? ResolveCampfire()
        {
            if (Campfire) return Campfire;
            if (!CheckpointRoot) return null;
            Campfire = CheckpointRoot.GetComponentInChildren<global::Campfire>(true);
            return Campfire;
        }

        internal void MarkConsumed()
        {
            if (State == RetentionState.AwaitingSpawn) State = RetentionState.Retained;
        }

        internal void Advance(int CurrentSlot)
        {
            if (State == RetentionState.Retained && CurrentSlot != TargetSlot)
                State = RetentionState.Released;
        }
    }

    HostCampfireRestoration? RestoredHostCampfire;

    internal int PendingCampfireSaveTarget { get; set; } = -1;
    internal void ReleaseState()
    {
        PendingCampfireSaveTarget = -1;
        RestoredHostCampfire = null;
        AmuletSources.Clear();
        PlacedAmuletSources.Clear();
        ConsumedAmuletSources.Clear();
        AllowedAmuletSources.Clear();
        MaximumAmuletSnapshot.Clear();
        BoundAmuletSlots = 0;
        MaximumAmuletKeyLength = 0;
    }

    internal const string ProcessingSceneName = "WilIsland";
    internal const int PropIntentMetadataVersion = 28;
    const int CurrentQuicksaveMetadataVersion = 29;
    const string NativeProcessingMismatch =
        "the native quicksave cannot restore the current map processing configuration.";
    const string QuicksaveCommitMutexName = @"Local\AF.PLL.Library.Quicksave.Commit";
    internal enum QuicksaveRejection : byte { IncompatibleConfiguration, ReadFailure, InvalidSave }
    static QuicksaveRejection? RejectedRestoredQuicksave;
    static readonly UTF8Encoding StrictSeedUtf8 = new(false, true);

    [Serializable, DataContract]
    sealed class QuicksaveEnvelope
    {
        static readonly QuicksaveLayout MissingPll = new();

        [SerializeField, DataMember(Name = "pll")] internal QuicksaveLayout? pll = null;
        [SerializeField, DataMember(Name = "run", IsRequired = true)] internal QuicksaveRun? run = null;

        [OnDeserializing]
        void OnDeserializing(StreamingContext _) => pll = MissingPll;

        internal bool PllMissing => ReferenceEquals(pll, MissingPll);
    }

    [Serializable, DataContract]
    internal sealed class QuicksaveLayout
    {
        internal static readonly HookedGenerationSave[] MissingHookedGeneration = new HookedGenerationSave[1];
        internal static readonly int[] MissingRetiredHookedGeneration = new int[1];
        [SerializeField, DataMember(Name = "version", IsRequired = true)] internal int version;
        [SerializeField, DataMember(Name = "layoutIdentity", IsRequired = true)] internal string layoutIdentity = string.Empty;
        [SerializeField, DataMember(Name = "biomeConfiguration", IsRequired = true)] internal string biomeConfiguration = string.Empty;
        [NonSerialized] internal BiomeLayoutConfigSnapshot? ParsedBiomes;
        [SerializeField, DataMember(Name = "layoutId", IsRequired = true)] internal string layoutId = string.Empty;
        [SerializeField, DataMember(Name = "libraryBase", IsRequired = true)] internal bool libraryBase;
        [SerializeField, DataMember(Name = "layoutRevision", IsRequired = true)] internal int layoutRevision;
        [SerializeField, DataMember(Name = "seed", IsRequired = true)] internal int seed;
        [SerializeField, DataMember(Name = "slotCount", IsRequired = true)] internal int slotCount;
        [SerializeField, DataMember(Name = "mode", IsRequired = true)] internal string mode = string.Empty;
        [SerializeField, DataMember(Name = "orderEnabled", IsRequired = true)] internal bool orderEnabled;
        [SerializeField, DataMember(Name = "peakCheckpointStates", IsRequired = true)] internal byte[] peakCheckpointStates = Array.Empty<byte>();
        [SerializeField, DataMember(Name = "peakCheckpointOutputs", IsRequired = true)] internal byte[] peakCheckpointOutputs = Array.Empty<byte>();
        [SerializeField, DataMember(Name = "consumedAmuletSources")] internal string[]? consumedAmuletSources = null;
        [NonSerialized] internal Dictionary<int, string>? ParsedAmuletConsumption;
        [SerializeField, DataMember(Name = "consumedZombieSources")] internal string[]? consumedZombieSources = null;
        [NonSerialized] internal HashSet<string>? ParsedZombieConsumption;
        [SerializeField, DataMember(Name = "hookedGeneration")] internal HookedGenerationSave[]? hookedGeneration;
        [SerializeField, DataMember(Name = "retiredHookedGeneration")] internal int[]? retiredHookedGeneration;
        [SerializeField, DataMember(Name = "worldInteractions")] internal WorldInteractionSave[]? worldInteractions;
        [OnDeserializing]
        void PrepareHookedGeneration(StreamingContext Context)
        {
            hookedGeneration = MissingHookedGeneration;
            retiredHookedGeneration = MissingRetiredHookedGeneration;
        }
    }

    [Serializable, DataContract]
    internal sealed class WorldInteractionSave
    {
        [SerializeField, DataMember(Name = "key", IsRequired = true)] internal string key = string.Empty;
        [SerializeField, DataMember(Name = "kind", IsRequired = true)] internal byte kind;
        [SerializeField, DataMember(Name = "payload", IsRequired = true)] internal byte[] payload = Array.Empty<byte>();
        [NonSerialized] internal int ReservedBytes;
    }

    [Serializable, DataContract]
    internal sealed class HookedGenerationSave
    {
        [SerializeField, DataMember(Name = "requestIndex", IsRequired = true)] internal int requestIndex;
        [SerializeField, DataMember(Name = "desired", IsRequired = true)] internal int[] desired = Array.Empty<int>();
        [SerializeField, DataMember(Name = "committed", IsRequired = true)] internal int[] committed = Array.Empty<int>();
        [SerializeField, DataMember(Name = "consumed", IsRequired = true)] internal byte[] consumed = Array.Empty<byte>();
        [SerializeField, DataMember(Name = "initialized", IsRequired = true)] internal bool initialized;
        [SerializeField, DataMember(Name = "trackerIds", IsRequired = true)] internal HookedTrackerIdsSave[] trackerIds = Array.Empty<HookedTrackerIdsSave>();
        [SerializeField, DataMember(Name = "fogConsumed")] internal byte[]? fogConsumed;
        [SerializeField, DataMember(Name = "fogTargets")] internal byte[]? fogTargets;
        [SerializeField, DataMember(Name = "fogInitialized")] internal bool? fogInitialized;
        [SerializeField, DataMember(Name = "sourceConsumed")] internal byte[]? sourceConsumed;
        [SerializeField, DataMember(Name = "sourceTargets")] internal byte[]? sourceTargets;
        [SerializeField, DataMember(Name = "sourceInitialized")] internal bool? sourceInitialized;
        [SerializeField, DataMember(Name = "interactionConsumed")] internal byte[]? interactionConsumed;
        [SerializeField, DataMember(Name = "propIntents")] internal PropIntentSave[]? propIntents;
        [SerializeField, DataMember(Name = "interactionInitialized")] internal bool? interactionInitialized;
    }

    [Serializable, DataContract]
    internal sealed class PropIntentSave
    {
        [SerializeField, DataMember(Name = "payload", IsRequired = true)] internal byte[] payload = Array.Empty<byte>();
    }

    [Serializable, DataContract]
    internal sealed class HookedTrackerIdsSave
    {
        [SerializeField, DataMember(Name = "ids", IsRequired = true)] internal string[] ids = Array.Empty<string>();
    }

    [Serializable, DataContract]
    sealed class QuicksaveRun
    {
        [SerializeField, DataMember(Name = "biomeReached", IsRequired = true)] internal int biomeReached = -1;
        [SerializeField, DataMember(Name = "levelName", IsRequired = true)] internal string levelName = string.Empty;
    }

    internal static bool RuntimeLayoutPublicationRequired(
        ResolvedRunConfiguration Configuration) => LayoutPublicationRequired(
        Configuration.Layout.SelectedLayoutId,
        Configuration.Layout.IsLibraryBase,
        Configuration.Layout.ReplaceGenerated,
        Configuration.Layout.OrderEnabled,
        Configuration.Layout.Generative,
        Configuration.Layout.Infinite,
        Configuration.Terminal.PeakAtEnd) || Configuration.Biomes.HasForcedNativeVariation;
    internal static bool RuntimeMapProcessingRequired(ResolvedRunConfiguration Configuration) =>
        RuntimeLayoutPublicationRequired(Configuration) || NativeBaseMap.RequiresProcessing(Configuration);

    static bool TryLocalSceneCompatibility(
        CapturedLayoutSelection Selection,
        bool SavedActive,
        string? SceneName,
        out string Failure,
        BiomeLayoutConfigSnapshot? SavedBiomes = null)
    {
        if (string.IsNullOrEmpty(SceneName))
        {
            Failure = "the saved scene name is unavailable.";
            return false;
        }
        var PublicationRequired = LocalLayoutPublicationRequired(Selection, SavedBiomes);
        if (SavedActive != PublicationRequired)
        {
            Failure = SavedActive
                ? "the saved layout is disabled. Restore its configuration before using Continue."
                : "the save uses the native layout. Restore compatible layout settings before using Continue.";
            return false;
        }
        if ((SavedActive || NativeBaseMap.RequiresLocalProcessing())
            && !string.Equals(SceneName, ProcessingSceneName, StringComparison.Ordinal))
        {
            Failure = SavedActive
                ? "the saved PLL map scene is not WilIsland."
                : NativeProcessingMismatch;
            return false;
        }
        Failure = string.Empty;
        return true;
    }

    static bool LocalLayoutPublicationRequired(
        CapturedLayoutSelection Selection, BiomeLayoutConfigSnapshot? SavedBiomes = null) =>
        LayoutPublicationRequired(
            Selection.Id,
            Selection.IsLibraryBase,
            FileManager.ReplaceGenerated.Value,
            Selection.DoOrder,
            FileManager.GenerativeMap.Value,
            FileManager.InfiniteMap.Value,
            FileManager.PeakAtEnd.Value) || (SavedBiomes?.HasForcedNativeVariation ?? HasLocalForcedNativeVariation());
    static bool HasLocalForcedNativeVariation()
    {
        foreach (var Entry in FileManager.NativeVariations.Values) if (Entry.Value != "Random") return true;
        return false;
    }
    static bool LayoutPublicationRequired(
        LayoutId SelectedLayout,
        bool IsLibraryBase,
        bool ReplaceGenerated,
        bool OrderEnabled,
        bool Generative,
        bool Infinite,
        bool PeakAtEnd) => LayoutRegistry.HasDefaultModification()
        || IsLibraryBase || !SelectedLayout.IsDefault || ReplaceGenerated || OrderEnabled
        || Generative || Infinite || PeakAtEnd;
    internal static int ResolveConfiguredSeed(string value)
    {
        if (Peak.Quicksave.ShouldUseSaveData
            && Map.Ingress.LoadedQuicksaveLayout is { } saved
            && ActiveQuicksaveMode(saved.mode))
        {
            if (!SeedMatchesConfiguration(saved.seed, value))
                throw RejectQuicksave("the saved and current map seeds differ.");
            return saved.seed;
        }
        if (!string.IsNullOrEmpty(value)) return SeedFromText(value);
        var bytes = new byte[sizeof(int)];
        using var algorithm = RandomNumberGenerator.Create();
        algorithm.GetBytes(bytes);
        return unchecked((int)((uint)bytes[0] << 24
            | (uint)bytes[1] << 16
            | (uint)bytes[2] << 8
            | bytes[3]));
    }

    static bool SeedMatchesConfiguration(int seed) =>
        SeedMatchesConfiguration(seed, FileManager.Seed.Value);

    static bool SeedMatchesConfiguration(int seed, string value) =>
        string.IsNullOrEmpty(value) || seed == SeedFromText(value);

    internal static int SeedFromText(string value)
    {
        using var algorithm = SHA256.Create();
        var hash = algorithm.ComputeHash(StrictSeedUtf8.GetBytes(value));
        return unchecked((int)((uint)hash[0] << 24
            | (uint)hash[1] << 16
            | (uint)hash[2] << 8
            | hash[3]));
    }

    internal static bool ClearBufferedCampfireRpcs()
    {
        if (PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom is null) return true;
        if (!PhotonNetwork.IsMasterClient) return false;
        var light = PhotonNetwork.RemoveBufferedRPCs(0, "Light_Rpc");
        var extinguish = PhotonNetwork.RemoveBufferedRPCs(0, "Extinguish_Rpc");
        if (light || extinguish) PhotonNetwork.SendAllOutgoingCommands();
        return light && extinguish;
    }
    internal static bool RestoringPllQuicksave() =>
        Peak.Quicksave.ShouldUseSaveData && Map.Ingress.LoadedQuicksaveLayout is not null;
    internal static bool RestoringActivePllQuicksave() =>
        RestoringPllQuicksave()
        && ActiveQuicksaveMode(Map.Ingress.LoadedQuicksaveLayout!.mode);

    internal void ValidateRestoredHostCampfire()
    {
        if (!RestoringActivePllQuicksave()) return;
        var Saved = Peak.Quicksave.SavedRun;
        var TargetSlot = SpawnTrackerTransaction.SpawnReadinessIndex(Saved.biomeReached);
        if (!SpawnTrackerTransaction.TryResolveBaseCheckpoint(
                TargetSlot,
                Map.Ingress.LoadedQuicksaveLayout!.peakCheckpointStates,
                out var CampfireSlot,
                out var CheckpointRoot,
                out var Campfire))
            throw RejectQuicksave(
                $"logical segment {Saved.biomeReached} has no prior campfire root for the restored host spawn.");
        RestoredHostCampfire = new HostCampfireRestoration(
            CampfireSlot, TargetSlot, CheckpointRoot, Campfire);
    }


    internal bool TryGetRetainedRestoredHostSlots(out int CampfireSlot, out int TargetSlot)
    {
        if (RestoredHostCampfire is { Retained: true } Restoration)
        {
            CampfireSlot = Restoration.CampfireSlot;
            TargetSlot = Restoration.TargetSlot;
            return true;
        }
        CampfireSlot = -1;
        TargetSlot = -1;
        return false;
    }

    internal global::Campfire? ResolveRestoredHostCampfire() =>
        RestoredHostCampfire?.ResolveCampfire();

    internal int? RetainedRestoredHostCampfireSlot =>
        RestoredHostCampfire is { Retained: true } Restoration
            ? Restoration.CampfireSlot
            : null;

    internal global::Campfire? RetainedRestoredHostCampfire =>
        RestoredHostCampfire is { Retained: true } Restoration
            ? Restoration.Campfire
            : null;

    internal void MarkRestoredHostCampfireConsumed() => RestoredHostCampfire?.MarkConsumed();

    internal void AdvanceRestoredHostCampfireRetention(int CurrentSlot) =>
        RestoredHostCampfire?.Advance(CurrentSlot);


    internal void AbortRestoredHostCampfire(string Problem)
    {
        var Restoration = RestoredHostCampfire
            ?? throw new InvalidOperationException("The restored host campfire was not validated before map activation.");
        var Failure = new InvalidDataException(
            $"PLL Library rejected the quicksave: target slot {Restoration.TargetSlot} {Problem}");
        RejectedRestoredQuicksave = QuicksaveRejection.IncompatibleConfiguration;
        try { ClearRejectedQuicksaveState(); }
        finally
        {
            LazySlotGenerator.AbortLazyGeneration(
                Restoration.CampfireSlot, "Quicksave map restoration failed", Failure);
        }
    }

    static bool ActiveQuicksaveMode(string mode) =>
        mode is Map.LayoutQuicksaveMode or Map.GenerativeQuicksaveMode or Map.InfiniteQuicksaveMode;
    internal static void ValidateQuicksaveSlotCount(int SlotCount)
    {
        if (!RestoringPllQuicksave() || Map.Ingress.LoadedQuicksaveLayout!.slotCount == SlotCount) return;
        throw RejectQuicksave(
            $"the rebuilt logical slot count {SlotCount} does not match {Map.Ingress.LoadedQuicksaveLayout.slotCount} saved slots.");
    }

    internal static void PrepareCampfireQuicksave(int completedIndex)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true || completedIndex < 0 || completedIndex >= Map.Slots.Length - 1
            || completedIndex != Map.Index || Map.Slots[completedIndex].Released || Map.Slots[completedIndex + 1].Released)
            throw new InvalidOperationException("The campfire quicksave was prepared outside its map transition.");
        var target = completedIndex + 1;
        if (Run.Quicksave.PendingCampfireSaveTarget == target) return;
        if (Run.Quicksave.PendingCampfireSaveTarget >= 0)
            throw new InvalidOperationException("A map run already has a pending campfire quicksave.");
        Run.Quicksave.PendingCampfireSaveTarget = target;
    }

    internal static void CancelCampfireQuicksave(int completedIndex)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true || completedIndex < 0 || completedIndex >= Map.Slots.Length - 1
            || completedIndex != Map.Index || Run.Quicksave.PendingCampfireSaveTarget != completedIndex + 1) return;
        Run.Quicksave.PendingCampfireSaveTarget = -1;
    }

    internal static void ValidateQuicksaveLayoutRevision(int Revision)
    {
        if (!RestoringActivePllQuicksave()) return;
        var Saved = Map.Ingress.LoadedQuicksaveLayout!;
        if (Saved.layoutRevision != Revision)
            throw RejectQuicksave(
                $"the rebuilt layout revision {Revision} does not match saved revision {Saved.layoutRevision}.");
    }
    internal static InvalidDataException RejectQuicksave(
        string Message, bool MarkRestoredSave = true,
        QuicksaveRejection Rejection = QuicksaveRejection.IncompatibleConfiguration)
    {
        if (MarkRestoredSave) RejectedRestoredQuicksave = Rejection;
        return new InvalidDataException($"PLL Library rejected the quicksave: {Message}");
    }
    internal static void ClearRejectedQuicksaveState()
    {
        if (RejectedRestoredQuicksave is null) return;
        RejectedRestoredQuicksave = null;
        Peak.Quicksave.ShouldUseSaveData = false;
        Peak.Quicksave._loadedData = default;
        Peak.Quicksave._hasLoadedData = false;
        Map.Ingress.LoadedQuicksaveLayout = null;
        Map.Ingress.LoadedQuicksaveSceneName = null;
    }

    static void LoadCompatibleQuicksave(Action<bool> NativeResult, Action<bool> CallerResult)
    {
        Peak.Quicksave.LoadSaveData(Success =>
        {
            if (RejectedRestoredQuicksave is QuicksaveRejection.IncompatibleConfiguration or QuicksaveRejection.ReadFailure)
            {
                ((QuicksaveReadNotification)CallerResult.Target!).Reject();
                return;
            }
            RejectedRestoredQuicksave = null;
            NativeResult(Success);
        });
    }

    sealed class QuicksaveReadNotification
    {
        readonly Action<bool>? Caller;
        bool Notified;

        internal QuicksaveReadNotification(Action<bool>? Caller) => this.Caller = Caller;
        internal void NativeResult(bool Success)
        {
            if (Notified) return;
            Notified = true;
            Caller?.Invoke(Success);
        }
        internal void Reject()
        {
            ClearRejectedQuicksaveState();
            NativeResult(false);
        }
    }

    static void WriteQuicksave(string path, string contents)
    {
        var Active = Map.Active;
        var Configuration = Active ? Map.CurrentRun?.Configuration
            ?? throw new InvalidOperationException("The active map run has no resolved configuration.") : null;
        var Biomes = Configuration?.Biomes ?? RunConfigurationResolution.CurrentBiomes;
        if (Biomes is not null)
        {
            var handler = Singleton<MapHandler>.Instance;
            var LayoutIdentity = Configuration is not null
                ? QuicksaveLayoutIdentity.RuntimeLayoutIdentity(Configuration)
                : string.Empty;
            var SlotCount = Active ? Map.Slots.Length : handler && handler.segments is not null ? handler.segments.Length : 0;
            if (SlotCount < 1 || SlotCount > byte.MaxValue)
                throw RejectQuicksave($"the logical slot count {SlotCount} is invalid.", MarkRestoredSave: false);
            var PeakState = Active
                ? Map.CurrentRun!.Peak.CaptureQuicksaveState()
                : PeakQuicksaveState.Empty(SlotCount);
            var RetiredHooked = Array.Empty<int>();
            var Clock = Active ? Map.CurrentRun!.PresentationClock : 0d;
            var Hooked = Active ? Map.CurrentRun!.HookedGeneration.CaptureQuicksave(Clock, out RetiredHooked)
                : Array.Empty<HookedGenerationSave>();

            var Metadata = new QuicksaveLayout
            {
                version = CurrentQuicksaveMetadataVersion,
                layoutIdentity = LayoutIdentity,
                biomeConfiguration = Biomes.Serialized,
                layoutId = Configuration?.Layout.SelectedLayoutId.Value ?? LayoutId.Default.Value,
                libraryBase = Configuration?.Layout.IsLibraryBase == true,
                layoutRevision = Active ? Map.LayoutRevision : 0,
                seed = Active ? FeatureGeneration.MapSeed() : 0,
                slotCount = SlotCount,
                mode = Configuration is not null ? Configuration.Mode switch
                {
                    MapLayoutMode.Generative => Map.GenerativeQuicksaveMode,
                    MapLayoutMode.Infinite => Map.InfiniteQuicksaveMode,
                    _ => Map.LayoutQuicksaveMode
                } : Map.InactiveQuicksaveMode,
                orderEnabled = Configuration?.Mode == MapLayoutMode.Ordered,
                peakCheckpointStates = PeakState.States,
                peakCheckpointOutputs = PeakState.Outputs,
                consumedAmuletSources = Active
                    ? Map.CurrentRun!.Quicksave.CaptureConsumedAmuletSources() : Array.Empty<string>(),
                consumedZombieSources = Active
                    ? Map.CurrentRun!.NativeSpawns.CaptureConsumedZombieSources() : Array.Empty<string>(),
                hookedGeneration = Hooked,
                retiredHookedGeneration = RetiredHooked,
                worldInteractions = Active ? Map.CurrentRun!.WorldInteractions.CaptureQuicksave(Clock)
                    : Array.Empty<WorldInteractionSave>()
            };
            using var MetadataStream = new MemoryStream();
            new DataContractJsonSerializer(typeof(QuicksaveLayout)).WriteObject(MetadataStream, Metadata);
            var metadata = Encoding.UTF8.GetString(MetadataStream.GetBuffer(), 0, checked((int)MetadataStream.Length));
            var end = contents.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(contents[end])) end--;
            if (end < 0 || contents[end] != '}')
                throw RejectQuicksave("PEAK produced malformed JSON.", MarkRestoredSave: false);

            contents = contents.Substring(0, end) + ",\"pll\":" + metadata + "}" + contents.Substring(end + 1);
        }
        var finalPath = Path.GetFullPath(path);
        var stagePath = Path.Combine(
            Path.GetDirectoryName(finalPath)!,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(stagePath, contents);
            using var CommitMutex = new Mutex(false, QuicksaveCommitMutexName);
            var CommitOwned = false;
            try
            {
                try
                {
                    CommitOwned = CommitMutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    CommitOwned = true;
                }
                if (!CommitOwned)
                    throw new IOException("Timed out after five seconds waiting to commit the quicksave.");
                if (File.Exists(finalPath)) File.Replace(stagePath, finalPath, null);
                else File.Move(stagePath, finalPath);
            }
            finally
            {
                if (CommitOwned) CommitMutex.ReleaseMutex();
            }
        }
        finally
        {
            File.Delete(stagePath);
        }
    }

    static bool TryResolveSavedLayoutSelection(
        QuicksaveLayout Saved,
        LayoutId LayoutIdValue,
        out CapturedLayoutSelection Selection)
    {
        Selection = TerminalLayoutSelection.Resolve(
            LayoutIdValue,
            FileManager.LayoutOrder.Value,
            Saved.orderEnabled,
            Saved.libraryBase);
        if (Saved.ParsedBiomes is null)
        {
            if (!CustomBiomeConfigRegistry.TryParseSnapshot(Saved.biomeConfiguration, out var Biomes)) return false;
            Saved.ParsedBiomes = Biomes;
        }
        return QuicksaveLayoutIdentity.Matches(Saved.layoutIdentity, Selection, Saved.seed, Saved.ParsedBiomes);
    }

    static bool SavedModeMatchesLocal(QuicksaveLayout Saved, CapturedLayoutSelection Selection)
    {
        var Mode = QuicksaveLayoutIdentity.LocalMode(Selection.Id, Selection.DoOrder);
        return Saved.orderEnabled == (Mode == MapLayoutMode.Ordered)
            && (Saved.mode == Map.GenerativeQuicksaveMode) == (Mode == MapLayoutMode.Generative)
            && (Saved.mode == Map.InfiniteQuicksaveMode) == (Mode == MapLayoutMode.Infinite);
    }

    static InvalidDataException InvalidQuicksave(string Message) =>
        RejectQuicksave(Message, Rejection: QuicksaveRejection.InvalidSave);

    static bool TryReadQuicksave(out Peak.Quicksave.SaveData Saved)
    {
        Saved = default;
        string Contents;
        try { Contents = ReadQuicksave(Peak.Quicksave.SaveFile.FullName); }
        catch (InvalidDataException Failure) when (
            RejectedRestoredQuicksave is QuicksaveRejection.IncompatibleConfiguration or QuicksaveRejection.InvalidSave)
        {
            if (RejectedRestoredQuicksave == QuicksaveRejection.InvalidSave) Console.WriteLine(Failure);
            return false;
        }
        catch (Exception Failure) when (Failure is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine(Failure);
            return false;
        }
        try
        {
            Saved = JsonUtility.FromJson<Peak.Quicksave.SaveData>(Contents);
            return true;
        }
        catch (ArgumentException Failure)
        {
            RejectedRestoredQuicksave = QuicksaveRejection.InvalidSave;
            Console.WriteLine(Failure);
            return false;
        }
    }

    static string ReadQuicksave(string PathValue)
    {
        RejectedRestoredQuicksave = QuicksaveRejection.ReadFailure;
        Map.Ingress.LoadedQuicksaveLayout = null;
        Map.Ingress.LoadedQuicksaveSceneName = null;
        var Contents = File.ReadAllText(PathValue);
        QuicksaveEnvelope? Envelope;
        try
        {
            using var Stream = new MemoryStream(Encoding.UTF8.GetBytes(Contents), false);
            Envelope = (QuicksaveEnvelope?)new DataContractJsonSerializer(
                typeof(QuicksaveEnvelope)).ReadObject(Stream);
        }
        catch (Exception ExceptionValue) when (ExceptionValue is SerializationException or XmlException)
        {
            throw InvalidQuicksave($"metadata is malformed ({ExceptionValue.GetType().Name}).");
        }
        if (Envelope is null)
            throw InvalidQuicksave("the saved run metadata is missing or malformed.");
        var Run = Envelope.run ?? throw InvalidQuicksave("the saved run metadata is missing or malformed.");
        var SceneName = Run.levelName;
        if (Envelope.PllMissing)
        {
            var Selection = TerminalLayoutSelection.Resolve(
                LayoutId.Default,
                FileManager.LayoutOrder.Value,
                FileManager.DoOrder.Value);
            if (!TryLocalSceneCompatibility(Selection, false, SceneName, out var Failure))
                throw RejectQuicksave(Failure);
            Map.Ingress.LoadedQuicksaveSceneName = SceneName;
            RejectedRestoredQuicksave = null;
            return Contents;
        }

        var Saved = Envelope.pll ?? throw InvalidQuicksave("PLL metadata is malformed.");
        if (Saved.version == 22)
        {
            if (Saved.consumedAmuletSources is not null)
                throw RejectQuicksave("metadata version 22 cannot contain consumed amulet source state.");
            Saved.consumedAmuletSources = Array.Empty<string>();
        }
        else if (Saved.version != 23 && Saved.version != 24 && Saved.version != 25 && Saved.version != 26 && Saved.version != 27
            && Saved.version != PropIntentMetadataVersion && Saved.version != CurrentQuicksaveMetadataVersion)
            throw RejectQuicksave($"metadata version {Saved.version} is unsupported.");
        if (Saved.version < 27)
        {
            if (Saved.worldInteractions is { Length: > 0 })
                throw InvalidQuicksave("the saved metadata version cannot contain world interaction state.");
            Saved.worldInteractions = Array.Empty<WorldInteractionSave>();
        }
        else if (Saved.worldInteractions is null)
            throw InvalidQuicksave("world interaction state is missing.");
        var InteractionKeys = new HashSet<string>(StringComparer.Ordinal);
        var InteractionBytes = 0;
        foreach (var Interaction in Saved.worldInteractions)
        {
            if (Interaction is null || string.IsNullOrEmpty(Interaction.key)
                || Interaction.kind > WorldInteractionLedger.FixtureKind || Interaction.payload is not { Length: > 0 }
                || Interaction.payload.Length > GeneratedRpcDeferral.MaximumPropertyBytes
                || !InteractionKeys.Add(Interaction.key))
                throw InvalidQuicksave("world interaction state is malformed or duplicated.");
            Interaction.ReservedBytes = GeneratedRpcDeferral.SerializePhotonValue(
                new object[] { Interaction.key, Interaction.kind, Interaction.payload }).Length;
            InteractionBytes = checked(InteractionBytes + Interaction.ReservedBytes);
            if (InteractionBytes > GeneratedRpcDeferral.MaximumRetainedCatalogBytes)
                throw InvalidQuicksave("world interaction state exceeds the synchronized capacity.");
        }
        if (Saved.consumedAmuletSources is null)
            throw RejectQuicksave("the save has no consumed amulet source state.");
        var Reached = (Segment)Run.biomeReached;
        var ReachedValid = NavigationRoles.TryDecode(Reached, out var ReachedIndex)
            && ReachedIndex < Saved.slotCount
            || Reached == Segment.Void;
        if (Saved.slotCount < 1 || Saved.slotCount > byte.MaxValue || !ReachedValid)
            throw InvalidQuicksave($"logical segment {Run.biomeReached} is outside {Saved.slotCount} saved slots.");
        if (Saved.consumedAmuletSources.Length > PllLayoutCapacityContract.MaximumOrdinarySegments)
            throw InvalidQuicksave("the consumed amulet source count exceeds the layout capacity.");
        var Consumed = new Dictionary<int, string>();
        foreach (var Key in Saved.consumedAmuletSources)
            if (!ValidAmuletSourceKey(Key, Saved.slotCount, out var Occurrence)
                || !Consumed.TryAdd(Occurrence, Key))
                throw InvalidQuicksave("the consumed amulet source state is malformed.");
        Saved.ParsedAmuletConsumption = Consumed;
        if (Saved.version < 25)
        {
            if (Saved.consumedZombieSources is not null)
                throw RejectQuicksave("the saved metadata version cannot contain zombie marker state.");
            Saved.consumedZombieSources = Array.Empty<string>();
        }
        else if (Saved.consumedZombieSources is null)
            throw RejectQuicksave("the save has no consumed zombie marker state.");
        if (Saved.consumedZombieSources.Length > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw InvalidQuicksave("the consumed zombie markers exceed the synchronized source capacity.");
        var Zombies = new HashSet<string>(StringComparer.Ordinal);
        string? PreviousZombie = null;
        foreach (var Key in Saved.consumedZombieSources)
        {
            if (!NativeSpawnAdmission.ValidZombieSourceKey(Key, Saved.slotCount)
                || PreviousZombie is not null && StringComparer.Ordinal.Compare(PreviousZombie, Key) >= 0)
                throw InvalidQuicksave("the consumed zombie marker state is malformed.");
            Zombies.Add(Key); PreviousZombie = Key;
        }
        Saved.ParsedZombieConsumption = Zombies;
        if (GeneratedRpcDeferral.SerializePhotonValue(new object[]
            { Guid.Empty.ToString("N"), Saved.layoutRevision, Saved.seed, Saved.consumedZombieSources }).Length
            > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw InvalidQuicksave("the consumed zombie markers exceed the synchronized source capacity.");

        if (Saved.version < 25)
        {
            if (!ReferenceEquals(Saved.hookedGeneration, QuicksaveLayout.MissingHookedGeneration))
                throw RejectQuicksave("legacy metadata cannot contain Hooked generation state.");
            if (!ReferenceEquals(Saved.retiredHookedGeneration, QuicksaveLayout.MissingRetiredHookedGeneration))
                throw RejectQuicksave("legacy metadata cannot contain Hooked retirement state.");
            Saved.hookedGeneration = Array.Empty<HookedGenerationSave>();
            Saved.retiredHookedGeneration = Array.Empty<int>();
        }
        else if (Saved.hookedGeneration is null
            || ReferenceEquals(Saved.hookedGeneration, QuicksaveLayout.MissingHookedGeneration))
            throw RejectQuicksave("the save has no Hooked generation state.");
        else if (Saved.retiredHookedGeneration is null
            || ReferenceEquals(Saved.retiredHookedGeneration, QuicksaveLayout.MissingRetiredHookedGeneration))
            throw RejectQuicksave("the save has no Hooked retirement state.");
        if (Saved.hookedGeneration.Length + Saved.retiredHookedGeneration!.Length > Saved.slotCount)
            throw InvalidQuicksave("the Hooked occurrence count exceeds the saved slots.");
        var PreviousRetired = -1;
        foreach (var Request in Saved.retiredHookedGeneration)
        {
            if (Request <= PreviousRetired || Request >= Saved.slotCount)
                throw InvalidQuicksave("the Hooked retirement identities are malformed.");
            PreviousRetired = Request;
        }
        var PreviousRequest = -1; var HookedTargets = 0; var FogTargets = 0; var SourceTargets = 0; var PropTargets = 0;
        var TrackerOwners = new HashSet<string>(StringComparer.Ordinal);
        var HookedValues = new object[Saved.hookedGeneration.Length];
        for (var i = 0; i < Saved.hookedGeneration.Length; i++)
        {
            var Record = Saved.hookedGeneration[i];
            if (Record is null || Record.requestIndex <= PreviousRequest || Record.requestIndex >= Saved.slotCount
                || Record.desired is not { Length: <= 256 } || Record.committed is null
                || Record.committed.Length != Record.desired.Length || Record.consumed is null || Record.consumed.Length > 32
                || Record.trackerIds is null || Record.trackerIds.Length != Record.desired.Length)
                throw InvalidQuicksave("the Hooked generation record is malformed.");
            PreviousRequest = Record.requestIndex; HookedTargets += Record.desired.Length;
            if (Saved.version < 26)
            {
                if (Record.desired.Length == 0 || Record.fogConsumed is not null || Record.fogTargets is not null
                    || Record.fogInitialized is not null || Record.sourceConsumed is not null
                    || Record.sourceTargets is not null || Record.sourceInitialized is not null)
                    throw InvalidQuicksave("legacy Hooked metadata contains world presentation state or has no generation target.");
                Record.fogConsumed = Array.Empty<byte>(); Record.fogTargets = Array.Empty<byte>();
                Record.fogInitialized = false;
            }
            else if (Record.fogInitialized is null || Record.fogConsumed is null || Record.fogConsumed.Length > 32
                || Record.fogTargets is not { Length: <= 256 })
                throw InvalidQuicksave("the fog action or target state is missing or exceeds its capacity.");
            if (Record.sourceConsumed is null && Record.sourceTargets is null && Record.sourceInitialized is null)
            {
                Record.sourceConsumed = Array.Empty<byte>(); Record.sourceTargets = Array.Empty<byte>();
                Record.sourceInitialized = false;
            }
            else if (Record.sourceInitialized is null || Record.sourceConsumed is not { Length: <= 32 }
                || Record.sourceTargets is not { Length: <= 256 })
                throw InvalidQuicksave("the native source action or target state is missing or exceeds its capacity.");
            if (Saved.version < PropIntentMetadataVersion)
            {
                if (Record.interactionConsumed is not null || Record.propIntents is not null || Record.interactionInitialized is not null)
                    throw InvalidQuicksave("legacy Hooked metadata contains prop interaction intent.");
                Record.interactionConsumed = Array.Empty<byte>(); Record.propIntents = Array.Empty<PropIntentSave>();
                Record.interactionInitialized = false;
            }
            else if (Record.interactionInitialized is null || Record.interactionConsumed is not { Length: <= 32 }
                || Record.propIntents is not { Length: <= 256 })
                throw InvalidQuicksave("the prop interaction action or intent state is missing or exceeds its capacity.");
            var PropValues = new object[Record.propIntents.Length];
            for (var j = 0; j < Record.propIntents.Length; j++)
            {
                if (Record.propIntents[j] is not { payload: { } Payload }) throw InvalidQuicksave("the prop intent payload is missing.");
                if (Payload.Length != 0) _ = PropInteractionIntent.Decode(Payload);
                PropValues[j] = Payload;
            }
            PropTargets += Record.propIntents.Length;
            if (Record.desired.Length == 0 && Record.fogTargets.Length == 0 && Record.sourceTargets.Length == 0 && Record.propIntents.Length == 0)
                throw InvalidQuicksave("a world record has no generation or presentation target.");
            FogTargets += Record.fogTargets.Length;
            foreach (var Target in Record.fogTargets)
                if (Target > 2) throw InvalidQuicksave("the fog target state is invalid.");
            SourceTargets += Record.sourceTargets.Length;
            foreach (var Target in Record.sourceTargets)
                if (Target > 2) throw InvalidQuicksave("the native source target state is invalid.");
            if (Array.BinarySearch(Saved.retiredHookedGeneration, Record.requestIndex) >= 0)
                throw InvalidQuicksave("a Hooked occurrence is both active and retired.");
            if (Record.initialized && Record.consumed.Length == 0)
                throw InvalidQuicksave("the initialized Hooked record has no generation actions.");
            if (!Record.initialized)
            {
                foreach (var Value in Record.consumed)
                    if (Value != 0) throw InvalidQuicksave("a dormant Hooked record has consumed actions.");
            }
            if (Record.fogInitialized.Value && Record.fogConsumed.Length == 0)
                throw InvalidQuicksave("the initialized fog record has no fog actions.");
            if (!Record.fogInitialized.Value)
            {
                foreach (var Value in Record.fogConsumed)
                    if (Value != 0) throw InvalidQuicksave("a dormant world record has consumed fog actions.");
            }
            if (Record.sourceInitialized.Value && Record.sourceConsumed.Length == 0)
                throw InvalidQuicksave("the initialized native source record has no source actions.");
            if (!Record.sourceInitialized.Value)
            {
                foreach (var Value in Record.sourceConsumed)
                    if (Value != 0) throw InvalidQuicksave("a dormant world record has consumed native source actions.");
                foreach (var Target in Record.sourceTargets)
                    if (Target != 0) throw InvalidQuicksave("a dormant world record has native source commands.");
            }
            if (Record.interactionInitialized.Value && Record.interactionConsumed.Length == 0)
                throw InvalidQuicksave("the initialized prop interaction record has no actions.");
            if (!Record.interactionInitialized.Value)
            {
                foreach (var Value in Record.interactionConsumed)
                    if (Value != 0) throw InvalidQuicksave("a dormant world record has consumed prop interaction actions.");
                foreach (var Intent in Record.propIntents)
                    if (Intent.payload.Length != 0) throw InvalidQuicksave("a dormant world record has retained prop intent.");
            }
            if (HookedTargets + FogTargets + SourceTargets + PropTargets > 256) throw InvalidQuicksave("the world target capacity is exceeded.");
            for (var j = 0; j < Record.desired.Length; j++)
            {
                if (Record.committed[j] < 0 || Record.desired[j] < Record.committed[j]
                    || (long)Record.desired[j] - Record.committed[j] > 256)
                    throw InvalidQuicksave("the Hooked generation revisions are invalid.");
                if (Record.trackerIds[j] is not { ids: { } Ids })
                    throw InvalidQuicksave("the Hooked tracker inventory is missing.");
                if (HookedPropGeneration.TrackerIdentityFailure(Ids, Record.committed[j], TrackerOwners) is { } Failure)
                    throw InvalidQuicksave(Failure);
            }
            var TrackerValues = new object[Record.trackerIds.Length];
            for (var j = 0; j < TrackerValues.Length; j++) TrackerValues[j] = Record.trackerIds[j].ids;
            HookedValues[i] = new object[] { Record.requestIndex, Record.desired, Record.committed,
                Record.consumed, Record.initialized, TrackerValues, Record.fogConsumed, Record.fogTargets,
                Record.fogInitialized.Value, Record.sourceConsumed, Record.sourceTargets, Record.sourceInitialized.Value,
                Record.interactionConsumed, PropValues, Record.interactionInitialized.Value };
        }
        if (GeneratedRpcDeferral.SerializePhotonValue(HookedValues).Length > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw InvalidQuicksave("the Hooked generation ledger exceeds its synchronized payload capacity.");

        var SavedActive = ActiveQuicksaveMode(Saved.mode);
        var SavedGenerativeMap = Saved.mode == Map.GenerativeQuicksaveMode;
        var SavedInfiniteMap = Saved.mode == Map.InfiniteQuicksaveMode;
        var SavedInactive = Saved.mode == Map.InactiveQuicksaveMode;
        if (SavedInactive && Saved.worldInteractions.Length != 0)
            throw InvalidQuicksave("an inactive layout cannot contain world interaction state.");
        if (SavedInactive && Saved.consumedAmuletSources.Length != 0)
            throw InvalidQuicksave("an inactive layout cannot contain consumed amulet sources.");
        if (SavedInactive && Zombies.Count != 0)
            throw InvalidQuicksave("an inactive layout cannot contain consumed zombie markers.");
        if (SavedInactive && (Saved.hookedGeneration.Length != 0 || Saved.retiredHookedGeneration.Length != 0))
            throw InvalidQuicksave("an inactive layout cannot contain Hooked generation or retirement state.");
        if (!SavedActive && !SavedInactive)
            throw InvalidQuicksave($"layout mode '{Saved.mode}' is malformed.");
        if (!LayoutId.TryParse(Saved.layoutId, out var SavedLayoutId))
            throw InvalidQuicksave($"layout ID '{Saved.layoutId}' is malformed.");
        if (!SavedActive && Saved.orderEnabled
            || Saved.orderEnabled && (SavedGenerativeMap || SavedInfiniteMap)
            || !SavedLayoutId.IsDefault && !Saved.orderEnabled)
            throw InvalidQuicksave("the saved effective layout mode is malformed.");
        if (Saved.libraryBase && (!SavedActive || !SavedLayoutId.IsDefault || !Saved.orderEnabled))
            throw InvalidQuicksave("the saved Library Base selection is malformed.");
        if (!SavedActive && !SavedLayoutId.IsDefault)
            throw InvalidQuicksave(
                $"inactive layout metadata cannot carry named layout '{SavedLayoutId}'.");
        var PeakStatePresent = false;
        if (Saved.peakCheckpointStates is null || Saved.peakCheckpointStates.Length != Saved.slotCount)
            throw InvalidQuicksave("the Peak checkpoint state does not match the saved logical slot count.");
        if (Saved.peakCheckpointOutputs is null || Saved.peakCheckpointOutputs.Length != Saved.slotCount)
            throw InvalidQuicksave("the Peak checkpoint outputs do not match the saved logical slot count.");
        for (var i = 0; i < Saved.slotCount; i++)
        {
            var State = Saved.peakCheckpointStates[i];
            var Outputs = Saved.peakCheckpointOutputs[i];
            if (!PeakSuccessorRuntime.ValidQuicksaveState(State))
                throw InvalidQuicksave($"the Peak checkpoint state contains invalid value {State}.");
            if (!PeakSuccessorRuntime.ValidQuicksaveOutputs(Outputs))
                throw InvalidQuicksave($"the Peak checkpoint outputs contain invalid value {Outputs}.");
            if (Outputs != 0 && State == 0)
                throw InvalidQuicksave("Peak checkpoint outputs require a fixture.");
            if (Outputs != 0 && Saved.version < 24
                && !PeakSuccessorRuntime.QuicksaveCampfireLit(State))
                throw InvalidQuicksave("Legacy Peak checkpoint outputs require a lit campfire.");
            PeakStatePresent |= State != 0 || Outputs != 0;
        }
        if (!SavedActive && (Saved.layoutIdentity != string.Empty || Saved.layoutRevision != 0
                || Saved.seed != 0 || PeakStatePresent)
            || SavedActive && (string.IsNullOrEmpty(Saved.layoutIdentity) || Saved.layoutRevision == 0))
            throw InvalidQuicksave("the layout metadata fields are malformed.");
        if (Saved.version < CurrentQuicksaveMetadataVersion)
        {
            if (!CustomBiomeConfigRegistry.TryParseSnapshot(Saved.biomeConfiguration, out var Biomes, LegacySpatial: true))
                throw RejectQuicksave("the saved biome configuration is malformed.");
            Saved.ParsedBiomes = Biomes;
            Saved.biomeConfiguration = Biomes.Serialized;
            if (SavedActive)
            {
                if (!QuicksaveLayoutIdentity.TryUpgradeSpatialIdentity(Saved.layoutIdentity, out var Identity))
                    throw RejectQuicksave("the saved layout identity is malformed.");
                Saved.layoutIdentity = Identity;
            }
            Saved.version = CurrentQuicksaveMetadataVersion;
        }
        if (SavedActive)
        {
            if (!SavedLayoutId.IsDefault && !LayoutRegistry.TryGet(SavedLayoutId, out _))
                throw RejectQuicksave($"named layout '{SavedLayoutId}' is unavailable.");
            if (!TryResolveSavedLayoutSelection(Saved, SavedLayoutId, out var Selection))
                throw RejectQuicksave(SavedLayoutId.IsDefault
                    ? "the saved layout configuration does not match the current configuration."
                    : $"named layout '{SavedLayoutId}' or its map configuration changed.");
            if (!TryLocalSceneCompatibility(Selection, true, SceneName, out var Failure, Saved.ParsedBiomes))
                throw RejectQuicksave(Failure);
            if (!SavedModeMatchesLocal(Saved, Selection))
                throw RejectQuicksave("the saved and current effective layout modes differ.");
            if (!SeedMatchesConfiguration(Saved.seed))
                throw RejectQuicksave("the saved and current map seeds differ.");
        }
        else
        {
            var Selection = TerminalLayoutSelection.Resolve(
                SavedLayoutId,
                FileManager.LayoutOrder.Value,
                Saved.orderEnabled,
                Saved.libraryBase);
            var Biomes = Saved.ParsedBiomes;
            if (Biomes is null && !CustomBiomeConfigRegistry.TryParseSnapshot(Saved.biomeConfiguration, out Biomes))
                throw RejectQuicksave("the saved biome configuration is malformed.");
            Saved.ParsedBiomes = Biomes;
            if (!TryLocalSceneCompatibility(Selection, false, SceneName, out var Failure, Biomes))
                throw RejectQuicksave(Failure);
        }

        Map.Ingress.LoadedQuicksaveSceneName = SceneName;
        Map.Ingress.LoadedQuicksaveLayout = Saved;
        RejectedRestoredQuicksave = null;
        return Contents;
    }

    internal static bool TryCaptureQuicksaveLayoutSelection(
        out CapturedLayoutSelection Selection,
        out string Failure)
    {
        Selection = default;
        var Saved = Map.Ingress.LoadedQuicksaveLayout;
        var SceneName = Map.Ingress.LoadedQuicksaveSceneName;
        if (Saved is null && SceneName is null)
        {
            Failure = "validated quicksave metadata is unavailable.";
            return false;
        }

        if (Saved is null)
        {
            Selection = TerminalLayoutSelection.Resolve(
                LayoutId.Default,
                FileManager.LayoutOrder.Value,
                FileManager.DoOrder.Value);
            if (!TryLocalSceneCompatibility(Selection, false, SceneName, out Failure)) return false;
            Failure = string.Empty;
            return true;
        }

        var LayoutIdValue = new LayoutId(Saved.layoutId);
        if (!LayoutIdValue.IsDefault && !LayoutRegistry.TryGet(LayoutIdValue, out _))
        {
            Failure = $"named layout '{LayoutIdValue}' is unavailable.";
            return false;
        }
        if (ActiveQuicksaveMode(Saved.mode))
        {
            if (!TryResolveSavedLayoutSelection(Saved, LayoutIdValue, out Selection)
                || !SavedModeMatchesLocal(Saved, Selection)
                || !SeedMatchesConfiguration(Saved.seed))
            {
                Failure = LayoutIdValue.IsDefault
                    ? "the layout configuration changed after validation."
                    : $"named layout '{LayoutIdValue}' or its map configuration changed after validation.";
                return false;
            }
            if (!TryLocalSceneCompatibility(Selection, true, SceneName, out Failure, Saved.ParsedBiomes)) return false;
        }
        else
        {
            Selection = TerminalLayoutSelection.Resolve(
                LayoutIdValue,
                FileManager.LayoutOrder.Value,
                Saved.orderEnabled,
                Saved.libraryBase);
            if (!TryLocalSceneCompatibility(Selection, false, SceneName, out Failure, Saved.ParsedBiomes)) return false;
        }

        Failure = string.Empty;
        return true;
    }

    static Luggage[] GetBaseCampLuggageForQuicksave(Segment SegmentValue) =>
        Map.Active && Map.HasPublishedLayout ? Array.Empty<Luggage>() : MapHandler.GetBaseCampLuggage(SegmentValue);

    [HarmonyPatch]
    static class LuggageConstructorPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.DeclaredConstructor(
                typeof(Peak.Quicksave.RunProgress),
                new[] { typeof(Guid), typeof(string), typeof(Segment), typeof(bool) })
            ?? throw new MissingMethodException(
                "Peak.Quicksave.RunProgress(Guid, string, Segment, bool) was not found.");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var Source = ((Func<Segment, Luggage[]>)MapHandler.GetBaseCampLuggage).Method;
            var Replacement = ((Func<Segment, Luggage[]>)GetBaseCampLuggageForQuicksave).Method;

            var Replaced = 0;
            foreach (var Instruction in instructions)
            {
                if (Instruction.Calls(Source))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced++;
                }
                yield return Instruction;
            }
            if (Replaced != 1)
                throw new MissingMethodException(
                    $"Peak.Quicksave.RunProgress's base-camp luggage call was found {Replaced} times.");
        }
    }

    [HarmonyPatch]
    static class PersistencePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(ReconnectData), "CreateFromCharacter")
                ?? throw new MissingMethodException("ReconnectData.CreateFromCharacter was not found.");
            yield return AccessTools.DeclaredMethod(
                typeof(Peak.Quicksave.RunProgress), nameof(Peak.Quicksave.RunProgress.Create), Type.EmptyTypes)
                ?? throw new MissingMethodException("Peak.Quicksave.RunProgress.Create was not found.");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var Codes = new List<CodeInstruction>(instructions);
            var CurrentMethod = MapTransitionAdapter.Instance.GetCurrentSegment;
            var Logical = ((Func<MapHandler, Segment>)Map.LogicalSegment).Method;
            var Height = ((Func<float>)Map.ReconnectHeightLimit).Method;
            var PreviousGetter = AccessTools.PropertyGetter(typeof(MapHandler), nameof(MapHandler.PreviousScoutStatue))
                ?? throw new MissingMethodException("MapHandler.PreviousScoutStatue getter was not found.");
            var SpentGetter = AccessTools.PropertyGetter(typeof(RespawnChest), nameof(RespawnChest.IsSpent))
                ?? throw new MissingMethodException("RespawnChest.IsSpent getter was not found.");
            var Reconnect = __originalMethod.DeclaringType == typeof(ReconnectData);
            var Replaced = false;
            var HeightReplaced = false;
            var UnusedStatueStateReplaced = false;
            for (var i = 0; i < Codes.Count; i++)
            {
                var Instruction = Codes[i];
                if (Instruction.Calls(CurrentMethod))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Logical;
                    Replaced = true;
                }
                else if (Reconnect && Instruction.opcode == OpCodes.Ldc_R4
                    && Instruction.operand is float Limit && Limit == 1300f)
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Height;
                    HeightReplaced = true;
                }
                else if (!Reconnect && i + 1 < Codes.Count
                    && Instruction.Calls(PreviousGetter) && Codes[i + 1].Calls(SpentGetter))
                {
                    if (UnusedStatueStateReplaced)
                        throw new AmbiguousMatchException(
                            "Peak.Quicksave.RunProgress.Create's unused Scout Statue state was ambiguous.");
                    Instruction.opcode = OpCodes.Nop;
                    Instruction.operand = null;
                    Codes[i + 1].opcode = OpCodes.Ldc_I4_0;
                    Codes[i + 1].operand = null;
                    UnusedStatueStateReplaced = true;
                }
            }
            if (!Replaced) throw new MissingMethodException("The persisted map segment getter was not found.");
            if (Reconnect && !HeightReplaced)
                throw new MissingMethodException("ReconnectData's upper map-height guard was not found.");
            if (!Reconnect && !UnusedStatueStateReplaced)
                throw new MissingMethodException(
                    "Peak.Quicksave.RunProgress.Create's unused Scout Statue state was not found.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(Peak.Quicksave), nameof(Peak.Quicksave.SaveNow))]
    static class QuicksaveIoPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var Source = ((Action<string, string>)File.WriteAllText).Method;
            var Replacement = ((Action<string, string>)WriteQuicksave).Method;

            var Replaced = 0;
            foreach (var Instruction in instructions)
            {
                if (Instruction.Calls(Source))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced++;
                }
                yield return Instruction;
            }
            if (Replaced != 1)
                throw new MissingMethodException(
                    $"PEAK's quicksave SaveNow file operation was found {Replaced} times.");
        }
    }

    [HarmonyPatch(typeof(Peak.Quicksave), nameof(Peak.Quicksave.LoadSaveData))]
    static class QuicksaveLoadPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Action<bool>? onLoaded)
        {
            var Success = false;
            if (Peak.Quicksave.Exists && TryReadQuicksave(out var Saved))
            {
                Peak.Quicksave._loadedData = Saved;
                Peak.Quicksave._hasLoadedData = true;
                Success = true;
            }
            onLoaded?.Invoke(Success);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.Quicksave), nameof(Peak.Quicksave.TryLoadSave))]
    static class QuicksaveReadResultPatch
    {
        [HarmonyPrefix]
        static void Prefix(ref Action<bool>? onLoaded) => onLoaded = new QuicksaveReadNotification(onLoaded).NativeResult;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Load = ((Action<Action<bool>>)Peak.Quicksave.LoadSaveData).Method;
            var Replacement = ((Action<Action<bool>, Action<bool>>)LoadCompatibleQuicksave).Method;
            var Replaced = 0;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(Load))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(Instruction);
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced++;
                }
                yield return Instruction;
            }
            if (Replaced != 1)
                throw new MissingMethodException($"PEAK's TryLoadSave loader was found {Replaced} times.");
        }
    }

    [HarmonyPatch(typeof(MainMenuMainPage), "ContinueFromQuicksaveClicked")]
    static class QuicksaveContinuePatch
    {
        [HarmonyPrefix]
        static bool Prefix(UnityEngine.UI.Button ___m_continueButton)
        {
            if (TryCaptureQuicksaveLayoutSelection(out _, out var Failure)) return true;
            Plugin.PLLLog.LogError(RejectQuicksave(Failure).Message);
            ClearRejectedQuicksaveState();
            if (___m_continueButton) ___m_continueButton.interactable = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.Quicksave.RunProgress), nameof(Peak.Quicksave.RunProgress.Create))]
    static class LuggageSavePatch
    {
        [HarmonyPostfix]
        static void Postfix(ref Peak.Quicksave.RunProgress __result)
        {
            if (!Map.Active) return;
            Map.CurrentRun!.HookedGeneration.CarryUnmaterializedHistories(ref __result);
            var Open = __result.openLuggageViewIds;
            Open.Clear();
            foreach (var Luggage in MapHandler.GetBaseCampLuggage(__result.biomeReached))
                if (Luggage.IsOpen && Luggage.TryGetComponent<PhotonView>(out var View)) Open.Add(View.ViewID);
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.GetBaseCampLuggage))]
    static class BaseCampLuggagePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Segment segment, ref Luggage[] __result)
        {
            if (!Map.HasPublishedLayout) return true;
            __result = SpawnTrackerTransaction.TryResolveBaseCheckpoint(
                SpawnTrackerTransaction.SpawnReadinessIndex(segment), null,
                out _, out var Root, out _)
                ? Root.GetComponentsInChildren<Luggage>(true)
                : Array.Empty<Luggage>();
            return false;
        }
    }

    static IEnumerable<Item> ItemsAroundCheckpoint()
    {
        if (!SpawnTrackerTransaction.TryResolveBaseCheckpoint(
                Map.Index, null, out _, out var Root, out _)) yield break;
        var Position = Root.transform.position;
        foreach (var Item in global::Item.ALL_ITEMS)
            if (Item && Item.itemState == ItemState.Ground
                && (Item.transform.position - Position).sqrMagnitude <= 40f * 40f)
                yield return Item;
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.ItemsAroundCampfire), MethodType.Getter)]
    static class CheckpointItemsPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref IEnumerable<Item> __result)
        {
            if (!Map.HasPublishedLayout) return true;
            __result = ItemsAroundCheckpoint();
            return false;
        }
    }

    [HarmonyPatch(typeof(LoadingScreenHandler), nameof(LoadingScreenHandler.LoadSceneProcess))]
    static class CampfireRpcCachePatch
    {
        [HarmonyPrefix]
        static void Prefix(string __0, bool __1)
        {
            if (!Map.Active || !__1 || !string.Equals(__0, "Airport", StringComparison.Ordinal)
                || PhotonNetwork.OfflineMode || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom is null) return;
            if (!ClearBufferedCampfireRpcs())
                throw new InvalidOperationException(
                    "PLL Library could not clear PEAK's buffered campfire RPC state before returning to the Airport.");
        }
    }
}
