using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class ViewBatchSynchronization
{
    const int InventoryPreviewCharacterBudget = 1 << 16;
    readonly MapRun Run;
    readonly GeneratedRpcReplaySynchronization RpcReplays;
    readonly Dictionary<int, PendingViewBatch> PendingViewBatches = new();
    readonly Dictionary<PhotonView, int> PublishedViewBatches = new();
    readonly Dictionary<int, int> RetainedViewBytes = new();
    readonly RoomPropertyPublication AmuletPublication = new();
    object[]? AmuletPublicationValue;
    int AmuletCapacitySlots;

    internal ViewBatchSynchronization(MapRun Run)
    {
        this.Run = Run ?? throw new ArgumentNullException(nameof(Run));
        RpcReplays = new GeneratedRpcReplaySynchronization(Run);
    }

    internal bool FakeItemRemapPending { get; private set; } = true;
    internal bool InitialBatchReady { get; private set; } = true;
    internal int FakeItemIdHighWater { get; private set; }
    internal bool HasPendingBatches => PendingViewBatches.Count != 0
        || RpcReplays.HasPendingPublication || AmuletPublicationValue is not null;

    internal enum ViewBatchState { Absent, Waiting, Applied, Invalid }
    enum FailureEvidence { None, ViewInventory, GeneratedNetworkContract }
    internal readonly struct ViewInventoryEntry
    {
        internal ViewInventoryEntry(int RootIndex, string StructuralPath)
        {
            this.RootIndex = RootIndex;
            this.StructuralPath = StructuralPath;
        }

        internal int RootIndex { get; }
        internal string StructuralPath { get; }
    }
    internal readonly struct ViewInventoryEvidence
    {
        internal ViewInventoryEvidence(int TotalCount, int ShownCount, string Preview, byte[] Hash)
        {
            this.TotalCount = TotalCount;
            this.ShownCount = ShownCount;
            this.Preview = Preview;
            this.Hash = Hash;
        }

        internal int TotalCount { get; }
        internal int ShownCount { get; }
        internal string Preview { get; }
        internal byte[] Hash { get; }
    }
    internal sealed class ViewBatch
    {
        internal ViewBatch(
            GameObject[] Roots, PhotonView[] Views, byte[] PathHash,
            FakeItem[] FakeItems, byte[] FakePathHash,
            GeneratedNetworkContract NetworkContract,
            ViewInventoryEvidence? BaselineEvidence, ViewInventoryEvidence? FinalEvidence,
            int Seed, bool ReplaceFakeItems)
        {
            this.Roots = Roots;
            this.Views = Views;
            this.PathHash = PathHash;
            this.FakeItems = FakeItems;
            this.FakePathHash = FakePathHash;
            this.NetworkContract = NetworkContract;
            this.BaselineEvidence = BaselineEvidence;
            this.FinalEvidence = FinalEvidence;
            this.Seed = Seed;
            this.ReplaceFakeItems = ReplaceFakeItems;
        }

        internal GameObject[] Roots { get; }
        internal PhotonView[] Views { get; }
        internal byte[] PathHash { get; }
        internal FakeItem[] FakeItems { get; }
        internal byte[] FakePathHash { get; }
        internal GeneratedNetworkContract NetworkContract { get; }
        internal ViewInventoryEvidence? BaselineEvidence { get; }
        internal ViewInventoryEvidence? FinalEvidence { get; }
        internal int Seed { get; }
        internal bool ReplaceFakeItems { get; }
    }

    internal sealed class PendingViewBatch
    {
        internal PendingViewBatch(ViewBatch Batch) => this.Batch = Batch;

        internal ViewBatch Batch { get; }
        internal bool PublicationPending { get; set; }
        internal bool LocalRegistrationComplete { get; set; }
        internal List<(PhotonView View, int Id)> ProvisionalViewIds { get; } = new();
    }

    internal readonly struct ViewBatchBaseline
    {
        internal ViewBatchBaseline(
            GameObject[] Roots, int[] RootIndices, HashSet<PhotonView> AssignedViews,
            ViewInventoryEvidence? Evidence, FakeItem[] AssignedFakeItems)
        {
            this.Roots = Roots;
            this.RootIndices = RootIndices;
            this.AssignedViews = AssignedViews;
            this.Evidence = Evidence;
            this.AssignedFakeItems = AssignedFakeItems;
        }

        internal GameObject[] Roots { get; }
        internal int[] RootIndices { get; }
        internal HashSet<PhotonView> AssignedViews { get; }
        internal ViewInventoryEvidence? Evidence { get; }
        internal FakeItem[] AssignedFakeItems { get; }
    }

    internal ViewBatchBaseline BeginLazyViewBatch(int Index, bool IncludeMap)
    {
        Run.RequirePublished();
        if (!Run.Network.OwnsCurrentRun)
            throw new InvalidOperationException("A synchronized object batch has no owning map run.");
        SynchronizeAmuletConsumption();
        Run.NativeSpawns.BindZombieDeclarations();
        var Roots = BatchRoots(Index, IncludeMap, out var RootIndices);
        GeneratedRpcDeferral.ReserveRoots(Run, Roots);
        var Assigned = new HashSet<PhotonView>();
        var Evidence = CaptureViewBaseline(
            Roots, RootIndices, Assigned, PhotonNetwork.CurrentRoom is { IsOffline: false });
        var AssignedFakeItems = new List<FakeItem>();
        var Manager = FakeItemManager.Instance;
        if (Manager && Manager.allFakeItems is { } Items)
            foreach (var Item in Items)
                if (Item) AssignedFakeItems.Add(Item);
        return new ViewBatchBaseline(
            Roots, RootIndices, Assigned, Evidence, AssignedFakeItems.ToArray());
    }

    internal ViewBatchBaseline BeginHookedViewBatch(HookedPropTarget Target)
    {
        Run.RequirePublished();
        var Roots = new[] { Target.Root.gameObject };
        var Indices = new[] { 0 };
        GeneratedRpcDeferral.ReserveRoots(Run, Roots);
        var Assigned = new HashSet<PhotonView>();
        var Evidence = CaptureViewBaseline(Roots, Indices, Assigned, PhotonNetwork.CurrentRoom is { IsOffline: false });
        var Items = FakeItemManager.Instance?.allFakeItems;
        return new ViewBatchBaseline(Roots, Indices, Assigned, Evidence,
            Items is null ? Array.Empty<FakeItem>() : Items.ToArray());
    }

    ViewInventoryEvidence? CaptureViewBaseline(
        GameObject[] Roots, int[] RootIndices,
        HashSet<PhotonView> Assigned, bool CaptureInventory)
    {
        List<ViewInventoryEntry>? Inventory = CaptureInventory ? new() : null;
        HashSet<int>? Seen = CaptureInventory ? new() : null;
        for (var i = 0; i < Roots.Length; i++)
        {
            var Root = Roots[i];
            if (!Root) continue;
            foreach (var View in Root.GetComponentsInChildren<PhotonView>(true))
            {
                if (!View || !Run.HookedGeneration.OwnedByOuter(Root.transform, View.transform)) continue;
                if (View.ViewID != 0)
                {
                    Assigned.Add(View);
                    continue;
                }
                if (Inventory is null || !PhotonCloneIdentity.IsBatchOrigin(View)
                    || !Seen!.Add(View.GetInstanceID())) continue;
                Inventory.Add(new ViewInventoryEntry(
                    RootIndices[i],
                    DeterministicObjectIdentity.RelativeComponentStructuralPath(View, Root.transform)));
            }
        }
        if (Inventory is null) return null;
        Inventory.Sort(CompareInventoryEntries);
        var Paths = new string[Inventory.Count];
        for (var i = 0; i < Paths.Length; i++)
            Paths[i] = DeterministicObjectIdentity.CanonicalRelativeComponentPath(
                Inventory[i].RootIndex, Inventory[i].StructuralPath);
        return CaptureInventoryEvidence(
            Inventory, DeterministicObjectIdentity.HashPaths("PhotonView", Paths));
    }

    List<GameObject> LazyBatchRoots(int Index)
    {
        var Result = new List<GameObject>();
        var Seen = new HashSet<int>();
        var Occurrence = Run.Slots[Index].Occurrence;
        foreach (var Slot in Occurrence.Slots)
        {
            Add(Slot.Segment.segmentParent);
            Add(Slot.Segment.segmentCampfire);
            var Peak = ExternalPeakRoot(Slot);
            if (Peak) Add(Peak.gameObject);
        }
        foreach (var Supplemental in Occurrence.SupplementalRoots)
            Add(Supplemental.gameObject);
        return Result;

        void Add(GameObject? Root)
        {
            if (Root && Seen.Add(Root.GetInstanceID())) Result.Add(Root);
        }
    }

    Transform? RetainedEndCutscenePeakRoot =>
        (Run.RollbackOwner ?? Run.PublicationOwner)?.EndCutscenePeakHandler?.transform;
    int RetainedEndCutscenePeakRootIndex => checked(Run.Slots.Length * 2);

    GameObject[] BatchRoots(int Index, bool IncludeMap, out int[] RootIndices)
    {
        if (!IncludeMap)
        {
            var Roots = LazyBatchRoots(Index).ToArray();
            RootIndices = new int[Roots.Length];
            for (var i = 0; i < RootIndices.Length; i++) RootIndices[i] = i;
            return Roots;
        }

        var Result = new List<GameObject>();
        var Indices = new List<int>();
        var Seen = new HashSet<int>();
        for (var i = 0; i < Run.Slots.Length; i++)
        {
            Add(Run.Slots[i].Container.gameObject, i);
            var Peak = ExternalPeakRoot(Run.Slots[i]);
            if (Peak) Add(Peak.gameObject, Run.Slots.Length + i);
        }
        var RetainedPeak = RetainedEndCutscenePeakRoot;
        if (RetainedPeak) Add(RetainedPeak.gameObject, RetainedEndCutscenePeakRootIndex);
        RootIndices = Indices.ToArray();
        return Result.ToArray();

        void Add(GameObject? Root, int RootIndex)
        {
            if (!Root || !Seen.Add(Root.GetInstanceID())) return;
            Result.Add(Root);
            Indices.Add(RootIndex);
        }
    }
    internal ViewBatch FreezeLazyViews(
        int Index,
        ViewBatchBaseline Baseline,
        bool IncludeMap = false, int? GenerationSeed = null, Transform? NativeSourceRoot = null)
    {
        var Entries = CollectGeneratedViews(
            Baseline.Roots, Baseline.RootIndices, Baseline.AssignedViews);
        var Views = new PhotonView[Entries.Count];
        var Paths = new string[Entries.Count];
        var NetworkViews = new (PhotonView View, Transform Root, string Path)[Entries.Count];
        var FinalInventory = Baseline.Evidence is null
            ? null
            : new ViewInventoryEntry[Entries.Count];
        for (var i = 0; i < Entries.Count; i++)
        {
            var Entry = Entries[i];
            var Path = DeterministicObjectIdentity.CanonicalRelativeComponentPath(
                Entry.Inventory.RootIndex, Entry.Inventory.StructuralPath);
            Views[i] = Entry.View;
            Paths[i] = Path;
            NetworkViews[i] = (Entry.View, Entry.Root, Path);
            if (FinalInventory is not null) FinalInventory[i] = Entry.Inventory;
        }
        var FakeEntries = IncludeMap
            ? CollectInitialFakeItems()
            : CollectGeneratedFakeItems(
                Baseline.Roots, Baseline.RootIndices, ExistingFakeItemIds(Baseline.AssignedFakeItems));
        var FakeItems = new FakeItem[FakeEntries.Count];
        var FakePaths = new string[FakeEntries.Count];
        for (var i = 0; i < FakeEntries.Count; i++)
        {
            FakeItems[i] = FakeEntries[i].Item;
            FakePaths[i] = FakeEntries[i].Path;
        }
        var PathHash = DeterministicObjectIdentity.HashPaths("PhotonView", Paths);
        ViewInventoryEvidence? FinalEvidence = FinalInventory is null
            ? null
            : CaptureInventoryEvidence(FinalInventory, PathHash);
        var NetworkContract = GeneratedNetworkContract.Create(
            Run.Slots, Index, IncludeMap, NetworkViews, FakeEntries, Run.NativeSpawns, NativeSourceRoot);
        var Batch = new ViewBatch(
            Baseline.Roots,
            Views,
            PathHash,
            FakeItems,
            DeterministicObjectIdentity.HashPaths(
                IncludeMap ? "FakeItem/Full" : "FakeItem/Append", FakePaths),
            NetworkContract,
            Baseline.Evidence,
            FinalEvidence,
            GenerationSeed ?? FeatureGeneration.MapSeed(), IncludeMap);
        return Batch;
    }
    internal void SynchronizeViewBatch(int Index, ViewBatch Batch)
    {
        if (Index != 0 && !Batch.ReplaceFakeItems && !InitialBatchReady)
            throw new InvalidOperationException("A lazy-generated object batch cannot precede the complete map registry.");
        var LocalBatch = Run.InitialGenerationCohort is null;
        if (LocalBatch)
        {
            if (PhotonNetwork.CurrentRoom is { IsOffline: false })
                throw new InvalidOperationException(
                    "An offline map run cannot publish a local object batch in an online Photon room.");
        }
        else if (PhotonNetwork.CurrentRoom is not { IsOffline: false }
            || PhotonNetwork.NetworkClientState != Photon.Realtime.ClientState.Joined)
            throw new InvalidOperationException(
                "The online map run lost its Photon room before object batch publication.");
        var Pending = new PendingViewBatch(Batch);
        PendingViewBatches[Index] = Pending;

        if (LocalBatch)
        {
            foreach (var View in Batch.Views)
                if (View.ViewID == 0 && !PhotonNetwork.AllocateViewID(View))
                    throw new InvalidOperationException("A lazy-generated PhotonView could not receive a local ViewID.");
            var FakeIds = AllocateFakeItemIds(Batch, out var NextFakeId);
            if (!ApplyFakeItemBatch(Batch, FakeIds, NextFakeId))
                throw new InvalidOperationException("A lazy-generated FakeItem batch could not be registered locally.");
            GeneratedRpcDeferral.HoldRegisteredBatch(Run, Index, Batch.Views);
            _ = GeneratedRpcDeferral.InitializeLocalBatch(Run, Batch.Roots, Batch.Views);
            CommitLocalViewBatch(Index, Pending, false);
            FinalizeViewBatch(Index, Pending);
            return;
        }

        var State = TryApplyViewBatch(Index);
        if (State == ViewBatchState.Invalid)
            throw new InvalidOperationException("The synchronized object batch is invalid.");
        if (State != ViewBatchState.Absent || !PhotonNetwork.IsMasterClient) return;
        PublishViewBatch(Index, Pending);
    }

    static bool HasBeehive(PhotonView[] Views)
    {
        foreach (var View in Views)
            if (View && View.TryGetComponent<Beehive>(out _)) return true;
        return false;
    }

    HashSet<int> ExistingFakeItemIds(FakeItem[] Items)
    {
        var Result = new HashSet<int>();
        foreach (var Item in Items)
            if (Item) Result.Add(Item.GetInstanceID());
        return Result;
    }

    void PublishViewBatch(int Index, PendingViewBatch Pending)
    {
        var Batch = Pending.Batch;
        if (Pending.PublicationPending) return;
        if (Pending.ProvisionalViewIds.Count != 0) ReleaseProvisionalViewIds(Pending);
        var Room = PhotonNetwork.CurrentRoom
            ?? throw new InvalidOperationException("The lazy-generation ViewID batch has no Photon room.");
        try
        {
            ReserveViewProperty(Index, MaximumViewValue(Batch));
            RpcReplays.ReserveMaximumCatalog(Index, Batch.PathHash, Batch.Views);
            var Ids = new int[Batch.Views.Length];
            var FakeIds = AllocateFakeItemIds(Batch, out var NextFakeId);
            var SeenViews = new HashSet<int>();
            foreach (var View in Batch.Views)
            {
                if (!View)
                    throw new InvalidOperationException(
                        "A lazy-generated PhotonView was destroyed before publication.");
                if (View.ViewID != 0)
                    throw new InvalidOperationException(
                        "A lazy-generated PhotonView was assigned before batch publication.");
                if (!SeenViews.Add(View.GetInstanceID()))
                    throw new InvalidOperationException(
                        "A lazy-generated PhotonView appeared more than once in its object batch.");
            }
            if (!HasRoomViewCapacity(Batch.Views.Length))
                throw new InvalidOperationException(
                    "The Photon room ViewID space cannot fit this object batch.");

            var Unique = new HashSet<int>();
            for (var i = 0; i < Batch.Views.Length; i++)
            {
                var View = Batch.Views[i];
                var Allocated = PhotonNetwork.AllocateRoomViewID(View);
                var Id = View.ViewID;
                if (Id > 0) Pending.ProvisionalViewIds.Add((View, Id));
                if (!Allocated || Id <= 0 || Id >= PhotonNetwork.MAX_VIEW_IDS
                    || !Unique.Add(Id) || !ReferenceEquals(PhotonNetwork.GetPhotonView(Id), View))
                    throw new InvalidOperationException(
                        "A lazy-generated PhotonView could not receive a unique room ViewID.");
                Ids[i] = Id;
            }
            GeneratedRpcDeferral.HoldRegisteredBatch(Run, Index, Batch.Views);
            GeneratedRpcDeferral.RunMasterInitialization(Run, Batch.Views);
            var HasBeehiveInitialization = HasBeehive(Batch.Views);
            var InitializationActor = PhotonNetwork.LocalPlayer.ActorNumber;
            var ReplaySeed = GeneratedRpcDeferral.SnapshotBatch(
                Run, Batch.Roots, Batch.Views, InitializationActor,
                HasBeehiveInitialization, 0);
            var Data = new ViewBatchEnvelope(
                Run.GenerationEpoch, Run.LayoutRevision, Batch.Seed, Batch.PathHash, Ids,
                ReplaySeed.ImmutableFingerprint, Batch.FakePathHash, FakeIds,
                Batch.ReplaceFakeItems, Batch.NetworkContract.Hash, NextFakeId)
                .ToPropertyValue();
            if (GeneratedRpcDeferral.SerializePhotonValue(Data).Length
                > GeneratedRpcDeferral.MaximumPropertyBytes)
                throw new InvalidOperationException(
                    "The synchronized object batch exceeds its Photon property budget.");
            var Properties = new Hashtable
            {
                [PropertyKey(Index)] = Data,
                [GeneratedRpcReplaySynchronization.CatalogKey(Index)] =
                    RpcReplays.ReserveInitialCatalogValue(Index, Batch.PathHash, ReplaySeed),
                [GeneratedRpcReplaySynchronization.CatalogRevisionKey(Index)] = 0
            };
            Pending.PublicationPending = true;
            var Key = PropertyKey(Index);
            var Accepted = Room.CustomProperties.ContainsKey(Key)
                ? Room.SetCustomProperties(
                    Properties, new Hashtable { [Key] = Room.CustomProperties[Key] })
                : Room.SetCustomProperties(Properties);
            if (!Accepted)
                throw new InvalidOperationException(
                    "The lazy-generation object batch could not be published.");
        }
        catch (Exception Exception)
        {
            RpcReplays.CancelPreparedCatalog(Index);
            try
            {
                ReleaseProvisionalViewIds(Pending);
            }
            catch (Exception Rollback)
            {
                throw new AggregateException(
                    "Object batch publication and allocation rollback both failed.",
                    Exception, Rollback);
            }
            throw;
        }
    }
    internal void ReleaseProvisionalViewIds(PendingViewBatch Pending)
    {
        Pending.PublicationPending = false;
        Exception? Failure = null;
        for (var i = Pending.ProvisionalViewIds.Count - 1; i >= 0; i--)
        {
            var (View, Id) = Pending.ProvisionalViewIds[i];
            try
            {
                var Registered = PhotonNetwork.GetPhotonView(Id);
                if (!View)
                {
                    if (Registered is not null)
                        throw new InvalidOperationException($"Provisional PhotonView {Id} was destroyed without unregistering.");
                }
                else if (View.ViewID != 0 || Registered is not null)
                {
                    if (View.ViewID != Id || !ReferenceEquals(Registered, View))
                        throw new InvalidOperationException($"Provisional PhotonView {Id} changed before rollback.");
                    View.ViewID = 0;
                    if (View.ViewID != 0 || PhotonNetwork.GetPhotonView(Id) is not null)
                        throw new InvalidOperationException($"Provisional PhotonView {Id} did not unregister during rollback.");
                }
                Pending.ProvisionalViewIds.RemoveAt(i);
            }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("Multiple provisional PhotonView rollbacks failed.", Failure, Exception);
            }
        }
        try
        {
            GeneratedRpcDeferral.ReleaseRegisteredBatch(Pending.Batch.Views);
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException(
                    "Provisional PhotonView and generated replay cleanup both failed.", Failure, Exception);
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }
    internal void ReleaseAllProvisionalViewIds()
    {
        Exception? Failure = null;
        foreach (var Pending in PendingViewBatches.Values)
            try
            {
                ReleaseProvisionalViewIds(Pending);
            }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("Multiple ViewID batch rollbacks failed.", Failure, Exception);
            }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }
    internal void CommitProvisionalViewIds(PendingViewBatch Pending)
    {
        Pending.PublicationPending = false;
        Pending.ProvisionalViewIds.Clear();
    }

    internal bool HasRoomViewCapacity(int Required)
    {
        var Remaining = Required;
        if (Remaining == 0) return true;
        var RetiredViewIds = Run.Retirement.RetiredViewIds;
        for (var Id = 1; Id < PhotonNetwork.MAX_VIEW_IDS; Id++)
        {
            if (PhotonNetwork.GetPhotonView(Id) is not null || RetiredViewIds.Contains(Id)) continue;
            if (--Remaining == 0) return true;
        }
        return false;
    }

    List<(PhotonView View, Transform Root, ViewInventoryEntry Inventory)> CollectGeneratedViews(
        GameObject[] Roots,
        int[] RootIndices,
        HashSet<PhotonView> AssignedBefore)
    {
        var Result = new List<(PhotonView View, Transform Root, ViewInventoryEntry Inventory)>();
        var Seen = new HashSet<int>();
        for (var i = 0; i < Roots.Length; i++)
            AddViews(Roots[i], RootIndices[i], AssignedBefore, Result, Seen);
        Result.Sort((Left, Right) => CompareInventoryEntries(Left.Inventory, Right.Inventory));
        return Result;
    }

    void AddViews(
        GameObject? Root,
        int RootIndex,
        HashSet<PhotonView> AssignedBefore,
        List<(PhotonView View, Transform Root, ViewInventoryEntry Inventory)> Result,
        HashSet<int> Seen)
    {
        if (!Root) return;
        foreach (var View in Root.GetComponentsInChildren<PhotonView>(true))
        {
            if (!View || !Run.HookedGeneration.OwnedByOuter(Root.transform, View.transform)
                || !PhotonCloneIdentity.IsBatchOrigin(View)
                || AssignedBefore.Contains(View) || !Seen.Add(View.GetInstanceID())) continue;
            Result.Add((View, Root.transform, new ViewInventoryEntry(
                RootIndex,
                DeterministicObjectIdentity.RelativeComponentStructuralPath(View, Root.transform))));
        }
    }

    static int CompareInventoryEntries(ViewInventoryEntry Left, ViewInventoryEntry Right)
    {
        var Order = Left.RootIndex.CompareTo(Right.RootIndex);
        return Order != 0 ? Order : string.CompareOrdinal(Left.StructuralPath, Right.StructuralPath);
    }

    static ViewInventoryEvidence CaptureInventoryEvidence(
        IReadOnlyList<ViewInventoryEntry> Inventory, byte[] Hash)
    {
        var Preview = new StringBuilder();
        var Shown = 0;
        for (var i = 0; i < Inventory.Count; i++)
        {
            var Entry = Inventory[i];
            var Root = Entry.RootIndex.ToString(CultureInfo.InvariantCulture);
            var Available = InventoryPreviewCharacterBudget - Preview.Length;
            var Framing = (Shown == 0 ? 0 : 1) + Root.Length + 3;
            if (Framing > Available || Entry.StructuralPath.Length > Available - Framing) break;
            if (Shown != 0) Preview.Append(';');
            Preview.Append('(').Append(Root).Append(',').Append(Entry.StructuralPath).Append(')');
            Shown++;
        }
        return new ViewInventoryEvidence(Inventory.Count, Shown, Preview.ToString(), Hash);
    }

    static string AppendInventory(
        string Failure, string Peer,
        ViewInventoryEvidence? Baseline, ViewInventoryEvidence? Final)
    {
        if (Baseline is not { } BaselineValue || Final is not { } FinalValue) return Failure;
        return $"{Failure}. {Peer} PhotonView inventory baseline {FormatInventory(BaselineValue)}; final {FormatInventory(FinalValue)}";
    }

    static string AppendGeneratedNetworkContract(string Failure, string Peer, GeneratedNetworkContract Evidence) =>
        $"{Failure}. {Peer} generated network contract {FormatGeneratedNetworkContract(Evidence)}";

    static string FormatInventory(ViewInventoryEvidence Evidence)
    {
        var Result = new StringBuilder().Append("total=").Append(Evidence.TotalCount)
            .Append(" entries=[").Append(Evidence.Preview).Append(']');
        if (Evidence.ShownCount == Evidence.TotalCount) return Result.ToString();
        return Result.Append(" shown=").Append(Evidence.ShownCount).Append(" sha256=")
            .Append(Hex(Evidence.Hash)).ToString();
    }

    static string FormatGeneratedNetworkContract(GeneratedNetworkContract Evidence) =>
        new StringBuilder().Append("records=").Append(Evidence.RecordCount)
            .Append(" characters=").Append(Evidence.TotalCharacters)
            .Append(" previewCharacters=").Append(Evidence.Preview.Length)
            .Append(" entries=[").Append(Evidence.Preview).Append("] sha256=")
            .Append(Hex(Evidence.Hash)).ToString();
    List<(FakeItem Item, string Path)> CollectGeneratedFakeItems(
        GameObject[] Roots,
        int[] RootIndices,
        HashSet<int> AssignedBefore)
    {
        var Result = new List<(FakeItem Item, string Path)>();
        var Seen = new HashSet<int>();
        for (var i = 0; i < Roots.Length; i++)
            AddFakeItems(Roots[i], RootIndices[i], AssignedBefore, Result, Seen);
        Result.Sort((Left, Right) => string.CompareOrdinal(Left.Path, Right.Path));
        return Result;
    }

    List<(FakeItem Item, string Path)> CollectInitialFakeItems()
    {
        var Result = new List<(FakeItem Item, string Path)>();
        foreach (var Item in NativeAmuletRecipe.FindRuntimeFakeItems(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (Item) Result.Add((Item, DeterministicObjectIdentity.GlobalComponentPath(Item)));
        Result.Sort((Left, Right) => string.CompareOrdinal(Left.Path, Right.Path));
        for (var i = 1; i < Result.Count; i++)
            if (Result[i - 1].Path == Result[i].Path)
                throw new InvalidOperationException("The complete FakeItem registry contains a duplicate structural path.");
        return Result;
    }

    void AddFakeItems(
        GameObject? Root,
        int RootIndex,
        HashSet<int> AssignedBefore,
        List<(FakeItem Item, string Path)> Result,
        HashSet<int> Seen)
    {
        if (!Root) return;
        foreach (var Item in Root.GetComponentsInChildren<FakeItem>(true))
        {
            if (!Item || !Run.HookedGeneration.OwnedByOuter(Root.transform, Item.transform)
                || AssignedBefore.Contains(Item.GetInstanceID()) || !Seen.Add(Item.GetInstanceID())) continue;
            Result.Add((Item, DeterministicObjectIdentity.RelativeComponentPath(Item, Root.transform, RootIndex)));
        }
    }
    internal Transform? ExternalPeakRoot(MapSlot Slot) =>
        Slot.PeakAttachment && (!Slot.Container || !TransformOwnership.Contains(Slot.Container, Slot.PeakAttachment))
            ? Slot.PeakAttachment
            : null;

    internal int[] AllocateFakeItemIds(ViewBatch Batch, out int NextFakeId)
    {
        var Count = Batch.FakeItems.Length;
        if (Batch.ReplaceFakeItems)
        {
            var Replacement = new int[Count];
            for (var i = 0; i < Replacement.Length; i++) Replacement[i] = i;
            NextFakeId = Count;
            return Replacement;
        }
        NextFakeId = FakeItemHighWater();
        var Ids = new int[Count];
        for (var i = 0; i < Ids.Length; i++)
        {
            if (NextFakeId == int.MaxValue)
                throw new InvalidOperationException("The synchronized FakeItem index space is exhausted.");
            Ids[i] = NextFakeId++;
        }
        return Ids;
    }

    internal object[] MaximumViewValue(ViewBatch Batch)
    {
        var ViewIds = new int[Batch.Views.Length]; Array.Fill(ViewIds, PhotonNetwork.MAX_VIEW_IDS - 1);
        var FakeIds = new int[Batch.FakeItems.Length]; Array.Fill(FakeIds, int.MaxValue);
        return new ViewBatchEnvelope(Run.GenerationEpoch, Run.LayoutRevision, Batch.Seed, Batch.PathHash,
            ViewIds, new byte[32], Batch.FakePathHash, FakeIds, Batch.ReplaceFakeItems,
            Batch.NetworkContract.Hash, int.MaxValue).ToPropertyValue();
    }

    void ReserveViewProperty(int Index, object[] Value)
    {
        var Bytes = GeneratedRpcDeferral.SerializePhotonValue(Value).Length;
        if (Bytes > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("The synchronized object batch exceeds its Photon property budget.");
        var Total = Bytes;
        foreach (var Pair in RetainedViewBytes) if (Pair.Key != Index) Total = checked(Total + Pair.Value);
        Run.ReplayBudget.Reserve(this, Total); RetainedViewBytes[Index] = Bytes;
    }

    int FakeItemHighWater()
    {
        var Next = FakeItemIdHighWater;
        var Manager = FakeItemManager.Instance;
        if (Manager)
        {
            var Items = Manager.allFakeItems;
            if (Items is not null)
                foreach (var Item in Items)
                {
                    if (!Item) continue;
                    if (Item.index < 0 || Item.index == int.MaxValue)
                        throw new InvalidOperationException("The FakeItem registry contains an invalid index.");
                    Next = Math.Max(Next, Item.index + 1);
                }
        }

        var Room = PhotonNetwork.CurrentRoom;
        if (Room is null) return Next;
        for (var i = 0; i < Run.Slots.Length; i++)
        {
            var Value = Room.CustomProperties[PropertyKey(i)];
            if (Value is not object[] { Length: > 0 } Fields
                || Fields[0] is not string Epoch || Epoch != Run.GenerationEpoch) continue;
            if (!ViewBatchEnvelope.TryParse(
                    Value, Run.GenerationEpoch, Run.LayoutRevision, FeatureGeneration.MapSeed(), out var Envelope, out _))
                throw new InvalidOperationException("A cached lazy-generation object batch is invalid.");
            var UniqueViews = new HashSet<int>();
            foreach (var Id in Envelope.ViewIds)
                if (Id <= 0 || Id >= PhotonNetwork.MAX_VIEW_IDS || !UniqueViews.Add(Id))
                    throw new InvalidOperationException("A cached lazy-generation PhotonView batch is invalid.");
            var UniqueItems = new HashSet<int>();
            foreach (var Id in Envelope.FakeIds)
                if (Id < 0 || Id >= Envelope.NextFakeId || !UniqueItems.Add(Id))
                    throw new InvalidOperationException("A cached lazy-generation FakeItem batch is invalid.");
            if (Envelope.ReplaceFakeItems && (i != 0 || Envelope.NextFakeId != Envelope.FakeIds.Length))
                throw new InvalidOperationException("A cached complete FakeItem registry is invalid.");
            Next = Math.Max(Next, Envelope.NextFakeId);
        }
        return Run.HookedGeneration.IncludeFakeItemHighWater(Next);
    }

    internal bool ApplyFakeItemBatch(ViewBatch Batch, int[] Ids, int NextFakeId)
    {
        if (Ids.Length != Batch.FakeItems.Length || NextFakeId < 0) return false;
        var Targets = new Dictionary<int, int>(Batch.FakeItems.Length);
        var Used = new HashSet<int>();
        for (var i = 0; i < Batch.FakeItems.Length; i++)
        {
            var Item = Batch.FakeItems[i];
            if (!Item || Ids[i] < 0 || Ids[i] >= NextFakeId || !Used.Add(Ids[i])
                || !Targets.TryAdd(Item.GetInstanceID(), Ids[i])) return false;
        }

        var Manager = FakeItemManager.Instance;
        if (!Manager) return Batch.FakeItems.Length == 0;
        ref var Items = ref Manager.allFakeItems;
        Items ??= new List<FakeItem>();
        ref var Data = ref Manager.fakeItemData;
        if (Batch.ReplaceFakeItems)
        {
            if (NextFakeId != Ids.Length) return false;
            var HiddenSet = Data.hiddenItems is { Count: > 0 }
                ? new HashSet<int>(Data.hiddenItems)
                : null;
            var HiddenObjects = new HashSet<int>();
            foreach (var Item in Items)
                if (Item && (Item.pickedUp || HiddenSet is not null && HiddenSet.Contains(Item.index)))
                    HiddenObjects.Add(Item.GetInstanceID());
            var Replacement = new List<FakeItem>(Batch.FakeItems.Length);
            var ReplacementHidden = new List<int>();
            for (var i = 0; i < Batch.FakeItems.Length; i++)
            {
                if (Ids[i] != i) return false;
                var Item = Batch.FakeItems[i];
                Item.index = i;
                Replacement.Add(Item);
                if (!Item.pickedUp && !HiddenObjects.Contains(Item.GetInstanceID())) continue;
                Item.pickedUp = true;
                Item.gameObject.SetActive(false);
                ReplacementHidden.Add(i);
            }
            Items = Replacement;
            Data.hiddenItems = ReplacementHidden;
            FakeItemIdHighWater = NextFakeId;
            Run.Quicksave.ApplyAmuletConsumption(Batch.FakeItems, Run.Slots);
            return true;
        }

        var Retained = new List<FakeItem>(Items.Count);
        Used.Clear();
        foreach (var Item in Items)
        {
            if (!Item) continue;
            if (Targets.TryGetValue(Item.GetInstanceID(), out var Expected))
            {
                if (Item.index != Expected) return false;
                continue;
            }
            if (Item.index < 0 || !Used.Add(Item.index)) return false;
            Retained.Add(Item);
        }
        foreach (var Id in Ids)
            if (!Used.Add(Id)) return false;

        Items.Clear();
        Items.AddRange(Retained);
        var Hidden = Data.hiddenItems is { Count: > 0 }
            ? new HashSet<int>(Data.hiddenItems)
            : null;
        for (var i = 0; i < Batch.FakeItems.Length; i++)
        {
            var Item = Batch.FakeItems[i];
            Item.index = Ids[i];
            Items.Add(Item);
            if (Hidden is null || !Hidden.Contains(Ids[i])) continue;
            Item.gameObject.SetActive(false);
            Item.pickedUp = true;
        }
        Items.Sort((Left, Right) => Left.index.CompareTo(Right.index));
        FakeItemIdHighWater = Math.Max(FakeItemIdHighWater, NextFakeId);
        Run.Quicksave.ApplyAmuletConsumption(Batch.FakeItems, Run.Slots);
        return true;
    }

    bool TryReadViewBatch(
        ViewBatch Batch,
        object[] Data,
        out int[] ViewIds,
        out int[] FakeIds,
        out int NextFakeId,
        out byte[] ReplaySeedFingerprint,
        out string Failure,
        out FailureEvidence Evidence)
    {
        ViewIds = Array.Empty<int>();
        FakeIds = Array.Empty<int>();
        NextFakeId = -1;
        ReplaySeedFingerprint = Array.Empty<byte>();
        Failure = string.Empty;
        Evidence = FailureEvidence.None;
        if (!ViewBatchEnvelope.TryParse(
                Data, Run.GenerationEpoch, Run.LayoutRevision, Batch.Seed, out var Envelope, out var ParseFailure))
        {
            Evidence = ParseFailure switch
            {
                ViewBatchEnvelope.ParseFailure.ViewIds or ViewBatchEnvelope.ParseFailure.ViewPathHash =>
                    FailureEvidence.ViewInventory,
                ViewBatchEnvelope.ParseFailure.GeneratedNetworkContractHash =>
                    FailureEvidence.GeneratedNetworkContract,
                _ => FailureEvidence.None
            };
            Failure = ParseFailure switch
            {
                ViewBatchEnvelope.ParseFailure.Shape =>
                    $"Lazy-generation object batch shape differed: expected {ViewBatchEnvelope.FieldCount} fields, received {Data.Length}",
                ViewBatchEnvelope.ParseFailure.GenerationEpoch => "Lazy-generation object batch generation epoch differed",
                ViewBatchEnvelope.ParseFailure.Protocol => "Lazy-generation object batch protocol differed",
                ViewBatchEnvelope.ParseFailure.LayoutRevision => "Lazy-generation object batch layout revision differed",
                ViewBatchEnvelope.ParseFailure.Seed => "Lazy-generation object batch seed differed",
                ViewBatchEnvelope.ParseFailure.ViewIds => "Lazy-generation PhotonView path count was malformed",
                ViewBatchEnvelope.ParseFailure.ViewPathHash => "Lazy-generation PhotonView path hash differed",
                ViewBatchEnvelope.ParseFailure.FakeIds => "Lazy-generation FakeItem path count was malformed",
                ViewBatchEnvelope.ParseFailure.FakePathHash => "Lazy-generation FakeItem path hash was malformed",
                ViewBatchEnvelope.ParseFailure.ReplaceFakeItems => "Lazy-generation FakeItem replacement mode was malformed",
                ViewBatchEnvelope.ParseFailure.ReplayCatalogFingerprint =>
                    "Generated RPC replay seed fingerprint was malformed",
                ViewBatchEnvelope.ParseFailure.GeneratedNetworkContractHash =>
                    "Generated network contract hash was malformed",
                ViewBatchEnvelope.ParseFailure.NextFakeId => "Lazy-generation FakeItem next ID was malformed",
                _ => throw new InvalidOperationException("ViewBatch parsing failed without a field failure.")
            };
            return false;
        }
        ReplaySeedFingerprint = Envelope.ReplayCatalogFingerprint;
        if (Envelope.ViewIds.Length != Batch.Views.Length)
        {
            Failure =
                $"Lazy-generation PhotonView path count differed: expected {Batch.Views.Length}, received {Envelope.ViewIds.Length}";
            Evidence = FailureEvidence.ViewInventory;
            return false;
        }
        if (!DeterministicObjectIdentity.SameHash(Envelope.ViewPathHash, Batch.PathHash))
        {
            Failure = "Lazy-generation PhotonView path hash differed";
            Evidence = FailureEvidence.ViewInventory;
            return false;
        }
        if (Envelope.FakeIds.Length != Batch.FakeItems.Length)
        {
            Failure =
                $"Lazy-generation FakeItem path count differed: expected {Batch.FakeItems.Length}, received {Envelope.FakeIds.Length}";
            return false;
        }
        if (!DeterministicObjectIdentity.SameHash(Envelope.FakePathHash, Batch.FakePathHash))
        {
            Failure =
                $"Lazy-generation FakeItem path hash differed: master={Hex(Envelope.FakePathHash)}, local={Hex(Batch.FakePathHash)}";
            return false;
        }
        if (Envelope.ReplaceFakeItems != Batch.ReplaceFakeItems)
        {
            Failure =
                $"Lazy-generation FakeItem replacement mode differed: expected {Batch.ReplaceFakeItems}, received {Envelope.ReplaceFakeItems}";
            return false;
        }
        if (!DeterministicObjectIdentity.SameHash(
                Envelope.GeneratedNetworkContractHash, Batch.NetworkContract.Hash))
        {
            Failure =
                $"Generated network contract hash differed: master={Hex(Envelope.GeneratedNetworkContractHash)}, local={Hex(Batch.NetworkContract.Hash)}";
            Evidence = FailureEvidence.GeneratedNetworkContract;
            return false;
        }
        ViewIds = Envelope.ViewIds;
        FakeIds = Envelope.FakeIds;
        NextFakeId = Envelope.NextFakeId;
        return true;
    }

    internal void RegisterHookedBatch(GeneratedBatchKey Key, PendingViewBatch Pending, object[] Value)
    {
        var Batch = Pending.Batch;
        if (!TryReadViewBatch(Batch, Value, out var Ids, out var FakeIds, out var NextFakeId,
                out _, out var Failure, out _)) throw new InvalidOperationException(Failure);
        var Unique = new HashSet<int>();
        for (var i = 0; i < Batch.Views.Length; i++)
        {
            var View = Batch.Views[i]; var Id = Ids[i];
            var Registered = PhotonNetwork.GetPhotonView(Id);
            if (!View || Id <= 0 || Id >= PhotonNetwork.MAX_VIEW_IDS || !Unique.Add(Id)
                || Run.Retirement.RetiredViewIds.Contains(Id)
                || Registered is not null && !ReferenceEquals(Registered, View)
                || View.ViewID != 0 && View.ViewID != Id)
                throw new InvalidOperationException("Hooked generation PhotonView ordering diverged.");
        }
        for (var i = 0; i < Batch.Views.Length; i++)
            if (Batch.Views[i].ViewID == 0) Batch.Views[i].ViewID = Ids[i];
        GeneratedRpcDeferral.HoldRegisteredBatch(Run, Key, Batch.Views);
        if (!ApplyFakeItemBatch(Batch, FakeIds, NextFakeId))
            throw new InvalidOperationException("Hooked generation FakeItem registration diverged.");
        CommitProvisionalViewIds(Pending); Pending.LocalRegistrationComplete = true;
    }

    static string Hex(byte[] Value) => BitConverter.ToString(Value).Replace("-", string.Empty);
    internal ViewBatchState TryApplyViewBatch(int Index, bool AdvanceReplay = true)
    {
        if (!PendingViewBatches.TryGetValue(Index, out var Pending) || PhotonNetwork.CurrentRoom is not { } Room)
            return ViewBatchState.Absent;
        if (Pending.LocalRegistrationComplete)
        {
            if (!AdvanceReplay || !RpcReplays.AdvanceBatch(Index)) return ViewBatchState.Waiting;
            FinalizeViewBatch(Index, Pending);
            return ViewBatchState.Applied;
        }
        var Batch = Pending.Batch;
        var Properties = Room.CustomProperties;
        if (Properties[PropertyKey(Index)] is not object[] Data) return ViewBatchState.Absent;
        if (Data.Length == 0) return ViewBatchState.Absent;
        if (Data[0] is string Epoch && Epoch != Run.GenerationEpoch) return ViewBatchState.Absent;
        if (!TryReadViewBatch(
                Batch, Data, out var Ids, out var FakeIds, out var NextFakeId,
                out var ReplaySeedFingerprint, out var Failure, out var Evidence))
        {
            Failure = Evidence switch
            {
                FailureEvidence.ViewInventory =>
                    AppendInventory(Failure, "Local", Batch.BaselineEvidence, Batch.FinalEvidence),
                FailureEvidence.GeneratedNetworkContract =>
                    AppendGeneratedNetworkContract(Failure, "Local", Batch.NetworkContract),
                _ => Failure
            };
            LazySlotGenerator.AbortLazyGeneration(Index, Failure);
            return ViewBatchState.Invalid;
        }
        RpcReplays.RequirePublishedCatalogCapacity(Index, Batch.PathHash, Batch.Views);
        ReserveViewProperty(Index, Data);

        if (Pending.ProvisionalViewIds.Count != 0)
        {
            var MatchesEnvelope = Pending.ProvisionalViewIds.Count == Ids.Length;
            for (var Offset = 0; MatchesEnvelope && Offset < Ids.Length; Offset++)
            {
                var Provisional = Pending.ProvisionalViewIds[Offset];
                MatchesEnvelope = ReferenceEquals(Provisional.View, Batch.Views[Offset])
                    && Provisional.Id == Ids[Offset];
            }
            if (!MatchesEnvelope) ReleaseProvisionalViewIds(Pending);
        }

        var Unique = new HashSet<int>();
        for (var i = 0; i < Batch.Views.Length; i++)
        {
            var View = Batch.Views[i];
            var Id = Ids[i];
            var Registered = Id > 0 ? PhotonNetwork.GetPhotonView(Id) : null;
            if (Id <= 0 || Id >= PhotonNetwork.MAX_VIEW_IDS || !Unique.Add(Id) || !View
                || Run.Retirement.RetiredViewIds.Contains(Id)
                || Registered is not null && !ReferenceEquals(Registered, View)
                || View.ViewID != 0 && View.ViewID != Id)
            {
                LazySlotGenerator.AbortLazyGeneration(Index, "Lazy-generation PhotonView ordering diverged");
                return ViewBatchState.Invalid;
            }
        }
        for (var i = 0; i < Batch.Views.Length; i++)
            if (Batch.Views[i].ViewID == 0) Batch.Views[i].ViewID = Ids[i];
        for (var i = 0; i < Batch.Views.Length; i++)
            if (!ReferenceEquals(PhotonNetwork.GetPhotonView(Ids[i]), Batch.Views[i]))
            {
                LazySlotGenerator.AbortLazyGeneration(Index, "Lazy-generation PhotonView registration diverged");
                return ViewBatchState.Invalid;
            }
        GeneratedRpcDeferral.HoldRegisteredBatch(Run, Index, Batch.Views);
        if (!ApplyFakeItemBatch(Batch, FakeIds, NextFakeId))
        {
            LazySlotGenerator.AbortLazyGeneration(Index, "Lazy-generation FakeItem registration diverged");
            return ViewBatchState.Invalid;
        }
        CommitLocalViewBatch(
            Index, Pending, true, ReplaySeedFingerprint);
        if (!AdvanceReplay || !RpcReplays.AdvanceBatch(Index)) return ViewBatchState.Waiting;
        FinalizeViewBatch(Index, Pending);
        return ViewBatchState.Applied;
    }

    void CommitLocalViewBatch(
        int Index, PendingViewBatch Pending, bool SynchronizeReplay,
        byte[]? ReplaySeedFingerprint = null)
    {
        if (Pending.LocalRegistrationComplete)
            throw new InvalidOperationException("An object batch completed local registration more than once.");
        var Batch = Pending.Batch;
        if (Index == 0) Run.Network.GenerationBarrier.RecordGeneratedNetworkContractHash(Batch.NetworkContract.Hash);
        CommitProvisionalViewIds(Pending);
        foreach (var View in Batch.Views)
            if (View) PublishedViewBatches.Add(View, Index);
        if (SynchronizeReplay)
        {
            var LocalCatalog = GeneratedRpcDeferral.SealBatch(
                Run, Batch.Roots, Batch.Views);
            RpcReplays.RegisterBatch(
                Index, Batch.PathHash, Batch.Views,
                ReplaySeedFingerprint ?? throw new InvalidOperationException(
                    "A synchronized object batch has no authoritative replay seed."),
                LocalCatalog);
        }
        Pending.LocalRegistrationComplete = true;
    }

    void FinalizeViewBatch(int Index, PendingViewBatch Pending)
    {
        if (!PendingViewBatches.Remove(Index, out var Current) || !ReferenceEquals(Current, Pending))
            throw new InvalidOperationException("A completed object batch was not pending.");
        if (Index == 0)
            for (var i = 0; i < Run.InitialGenerationSlotCount; i++) Run.Slots[i].MarkViewBatchReady();
        else if (Index > 0 && Index < Run.Slots.Length)
            foreach (var Slot in Run.Slots[Index].Occurrence.Slots) Slot.MarkViewBatchReady();
        Run.NativeSpawns.BindPendingOutputs();
        if (Index == 0)
        {
            var Occurrences = new HashSet<FeatureOccurrence>();
            for (var i = 0; i < Run.InitialGenerationSlotCount; i++)
                if (Occurrences.Add(Run.Slots[i].Occurrence))
                {
                    Run.ClimbingFixtures.Admit(Run.Slots[i].Occurrence);
                    Run.PropInteractions.Admit(Run.Slots[i].Occurrence);
                    Run.Slots[i].Occurrence.PublishActors();
                }
        }
        else if (Index > 0 && Index < Run.Slots.Length)
        {
            Run.ClimbingFixtures.Admit(Run.Slots[Index].Occurrence);
            Run.PropInteractions.Admit(Run.Slots[Index].Occurrence);
            Run.Slots[Index].Occurrence.PublishActors();
        }
        Run.Peak.Campfire.ReconcileCurrentOutputs();
        if (Index == 0 && !InitialBatchReady)
        {
            InitialBatchReady = true;
            if (!ReplayDeferredFakeItemSync()) return;
            ReleasedSlotRetirement.TryApplyReleasedPrefix();
        }
        Run.Network.DeferredOperations.Replay();
        if (!Run.Network.DeferredOperations.HasPending
            && Run.Transitions.Lease.Current == MapTransitionCoordinator.TransitionLease.State.Idle)
            Run.Network.DeferredOperations.ReplayJump(Index);
    }

    internal bool TryFindPendingViewBatch(int ViewId, out int Index)
    {
        Index = -1;
        if (PhotonNetwork.CurrentRoom is not { } Room) return false;
        for (var i = 0; i < Run.Slots.Length; i++)
        {
            if (Run.Slots[i].Released || Run.Slots[i].FeaturesReady && Run.Slots[i].ViewBatchReady
                || !ViewBatchEnvelope.TryParse(
                    Room.CustomProperties[PropertyKey(i)], Run.GenerationEpoch, Run.LayoutRevision, FeatureGeneration.MapSeed(),
                    out var Envelope, out _)) continue;
            foreach (var Id in Envelope.ViewIds)
            {
                if (Id != ViewId) continue;
                if (Index >= 0)
                    throw new InvalidOperationException(
                        $"Synchronized PhotonView {ViewId} belongs to multiple object batches.");
                Index = i;
                break;
            }
        }
        return Index >= 0;
    }

    internal bool TryFindPendingBatch(int ViewId, out GeneratedBatchKey Key)
    {
        if (TryFindPendingViewBatch(ViewId, out var Index)) { Key = GeneratedBatchKey.Slot(Index); return true; }
        return Run.HookedGeneration.TryFindPendingView(ViewId, out Key);
    }
    internal void ClearDeferredFakeItemSync()
    {
        FakeItemRemapPending = false;
        Run.Ingress.ClearFakeItemSync();
    }
    internal readonly struct PendingBatchRetirement
    {
        internal PendingBatchRetirement(int Index, PendingViewBatch? Pending)
        {
            this.Index = Index;
            this.Pending = Pending;
        }

        internal int Index { get; }
        internal PendingViewBatch? Pending { get; }
    }

    internal PendingBatchRetirement CapturePendingBatchRetirement(int Index)
    {
        PendingViewBatches.TryGetValue(Index, out var Pending);
        return new PendingBatchRetirement(Index, Pending);
    }

    internal void CommitPendingBatchRetirement(PendingBatchRetirement Retirement)
    {
        if (Retirement.Pending is { } Pending)
        {
            if (!PendingViewBatches.TryGetValue(Retirement.Index, out var Current)
                || !ReferenceEquals(Current, Pending))
                throw new InvalidOperationException(
                    "A retiring object batch changed before its provisional ViewIDs were committed.");
            CommitProvisionalViewIds(Pending);
        }
        PendingViewBatches.Remove(Retirement.Index);
    }

    internal void RestorePendingBatchRetirement(PendingBatchRetirement Retirement)
    {
        if (Retirement.Pending is { } Pending) PendingViewBatches[Retirement.Index] = Pending;
        else PendingViewBatches.Remove(Retirement.Index);
    }
    internal string PropertyKey(int Index) => PhotonPropertyProtocol.StatePrefix + "view/"
        + Index.ToString("D3", CultureInfo.InvariantCulture);

    static string AmuletPropertyKey => PhotonPropertyProtocol.StatePrefix + "amulets";

    void ValidateAmuletCapacity()
    {
        if (AmuletCapacitySlots == Run.Slots.Length) return;
        var Maximum = new object[]
        {
            1, Run.GenerationEpoch, Run.LayoutRevision, Run.Configuration.Layout.Seed,
            Run.Quicksave.CaptureMaximumAmuletSnapshot()
        };
        if (GeneratedRpcDeferral.SerializePhotonValue(Maximum).Length > GeneratedRpcDeferral.MaximumPropertyBytes)
            throw new InvalidOperationException("The declared amulet sources exceed the room property capacity.");
        AmuletCapacitySlots = Run.Slots.Length;
    }

    internal void QueueAmuletConsumption()
    {
        var Room = PhotonNetwork.CurrentRoom;
        if (PhotonNetwork.OfflineMode || Room is null || !PhotonNetwork.IsMasterClient) return;
        var Value = new object[]
        {
            1, Run.GenerationEpoch, Run.LayoutRevision, Run.Configuration.Layout.Seed,
            Run.Quicksave.CaptureConsumedAmuletSources()
        };
        AmuletPublicationValue = Value;
        PublishAmuletConsumption();
    }

    void PublishAmuletConsumption()
    {
        var Room = PhotonNetwork.CurrentRoom;
        if (Room is null || !PhotonNetwork.IsMasterClient)
        {
            AmuletPublicationValue = null;
            AmuletPublication.Reset();
            return;
        }
        if (AmuletPublication.AwaitEcho(Room, AmuletPropertyKey, AmuletPublicationValue!,
                "The consumed amulet source state could not be published."))
            AmuletPublicationValue = null;
        else if (AmuletPublication.Failure is { } Failure)
            throw new InvalidOperationException(Failure);
    }

    void SynchronizeAmuletConsumption()
    {
        if (Run.Slots.Length == 0) return;
        Run.Quicksave.BindAmuletSources(Run.Slots);
        ValidateAmuletCapacity();
        var Room = PhotonNetwork.CurrentRoom;
        if (PhotonNetwork.OfflineMode || Room is null) return;
        var Raw = Room.CustomProperties[AmuletPropertyKey];
        if (Raw is object[] { Length: 5 } Fields && Fields[0] is int Version && Version == 1
            && Fields[1] is string Epoch && Fields[2] is int Revision && Fields[3] is int Seed
            && Fields[4] is string[] Keys)
        {
            if (Epoch == Run.GenerationEpoch && Revision == Run.LayoutRevision)
            {
                if (Seed != Run.Configuration.Layout.Seed)
                    throw new InvalidOperationException("The consumed amulet source state has invalid run provenance.");
                Run.Quicksave.AdoptConsumedAmuletSources(Keys);
            }
        }
        else if (Raw is not null && Raw is not object[] { Length: 0 })
            throw new InvalidOperationException("The consumed amulet source state has an unsupported room schema.");
        if (PhotonNetwork.IsMasterClient) QueueAmuletConsumption();
    }

    internal bool HasPendingBatch(int Index) => PendingViewBatches.ContainsKey(Index);

    internal static bool GeneratedBatchOriginReady(PhotonView View, bool Simulating = false)
    {
        if (!View) return true;
        if (Simulating && !NativeSpawnAdmission.Ready(View)) return false;
        if (!PhotonCloneIdentity.IsBatchOrigin(View)) return true;
        if (View.ViewID <= 0 || !Map.TryGetPublishedRun(out var Run)
            || Simulating && (!Run.Active || !Run.NativeSpawns.Started)) return false;
        var Batches = Run.Network.ViewBatches;
        return Batches.PublishedViewBatches.TryGetValue(View, out var Index)
            ? !Batches.PendingViewBatches.ContainsKey(Index)
            : Run.HookedGeneration.OriginReady(View);
    }

    internal bool TryRefreshRetainedMobState(
        PhotonView View, int SenderActor, int TargetActor, int State)
    {
        if (!PublishedViewBatches.TryGetValue(View, out var Index))
            return Run.HookedGeneration.TryRefreshMobState(View, SenderActor, TargetActor, State);
        return RpcReplays.TryRefreshMobState(
            Index, View.ViewID, SenderActor, TargetActor, State);
    }

    internal int EarliestPendingIndex(int Fallback)
    {
        foreach (var Index in PendingViewBatches.Keys)
            if (Index < Fallback) Fallback = Index;
        return Fallback;
    }

    internal void RoomPropertiesUpdated(Hashtable Changed)
    {
        Run.NativeSpawns.RoomPropertiesUpdated(Changed);
        if (Changed.ContainsKey(AmuletPropertyKey)) SynchronizeAmuletConsumption();
        RpcReplays.RoomPropertiesUpdated(Changed);
        foreach (var Index in new List<int>(PendingViewBatches.Keys))
        {
            if (!PendingViewBatches.TryGetValue(Index, out var Pending)) continue;
            var BatchChanged = Changed.ContainsKey(PropertyKey(Index));
            if (!BatchChanged && !Pending.LocalRegistrationComplete) continue;
            if (BatchChanged) Pending.PublicationPending = false;
            var State = TryApplyViewBatch(Index);
            if (!Run.Network.OwnsCurrentRun) return;
            if (State != ViewBatchState.Absent || !PhotonNetwork.IsMasterClient
                || !PendingViewBatches.TryGetValue(Index, out Pending)) continue;
            try
            {
                PublishViewBatch(Index, Pending);
            }
            catch (Exception Exception)
            {
                if (Run.Network.OwnsCurrentRun)
                    LazySlotGenerator.AbortLazyGeneration(
                        Index, "Lazy-generation ViewID recovery failed", Exception);
                return;
            }
        }
    }

    internal bool RecoverAuthority()
    {
        try
        {
            Run.NativeSpawns.MasterClientChanged();
            SynchronizeAmuletConsumption();
            for (var Index = 0; Index < Run.Slots.Length; Index++)
            {
                if (!PendingViewBatches.TryGetValue(Index, out var Pending)
                    || Pending.LocalRegistrationComplete) continue;
                var State = TryApplyViewBatch(Index, false);
                if (!Run.Network.OwnsCurrentRun || State == ViewBatchState.Invalid) return false;
                if (State != ViewBatchState.Absent || !PhotonNetwork.IsMasterClient) continue;
                Pending.PublicationPending = false;
                PublishViewBatch(Index, Pending);
            }
        }
        catch (Exception Exception)
        {
            if (Run.Network.OwnsCurrentRun)
                LazySlotGenerator.AbortLazyGeneration(
                    EarliestPendingIndex(Map.Index),
                    "ViewID batch authority recovery failed", Exception);
            return false;
        }

        if (RpcReplays.RecoverAuthority(out var Failure)) return true;
        var Message = Failure?.Message
            ?? "Generated RPC state could not recover after master migration";
        if (Failure is null)
            LazySlotGenerator.AbortLazyGeneration(EarliestPendingIndex(Map.Index), Message);
        else
            LazySlotGenerator.AbortLazyGeneration(
                EarliestPendingIndex(Map.Index), Message, Failure);
        return false;
    }

    internal bool Tick()
    {
        Run.NativeSpawns.AdvanceNetworkPublications();
        if (AmuletPublicationValue is not null) PublishAmuletConsumption();
        RpcReplays.Tick();
        for (var Index = 0; Index < Run.Slots.Length; Index++)
        {
            if (!PendingViewBatches.ContainsKey(Index)) continue;
            _ = TryApplyViewBatch(Index);
            if (!Run.Network.OwnsCurrentRun) return false;
        }
        return true;
    }

    internal void ResetForInitialBatch()
    {
        CancelPendingBatches();
        RpcReplays.Release();
        RetainedViewBytes.Clear(); Run.ReplayBudget.Reserve(this, 0);
        FakeItemIdHighWater = 0;
        InitialBatchReady = false;
    }

    internal void IncludeFakeItemHighWater(int NextFakeId) =>
        FakeItemIdHighWater = Math.Max(FakeItemIdHighWater, NextFakeId);

    internal void RestoreFakeItemHighWater(int Value) => FakeItemIdHighWater = Value;

    internal void CancelPendingBatches()
    {
        Exception? Failure = null;
        foreach (var Pair in PendingViewBatches)
            try
            {
                var Pending = Pair.Value;
                if (Pending.LocalRegistrationComplete) RpcReplays.CancelBatch(Pair.Key);
                else
                {
                    RpcReplays.CancelPreparedCatalog(Pair.Key);
                    GeneratedRpcDeferral.DiscardBatch(
                        Run, Pending.Batch.Roots, Pending.Batch.Views);
                }
            }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("Multiple generated RPC batches failed to cancel.", Failure, Exception);
            }
        try
        {
            ReleaseAllProvisionalViewIds();
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("Generated RPC and provisional ViewID cleanup both failed.", Failure, Exception);
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
        PendingViewBatches.Clear();
    }

    internal void ReleasePublishedViewIds()
    {
        Exception? Failure = null;
        foreach (var View in PublishedViewBatches.Keys)
        {
            if (!View || View.ViewID == 0) continue;
            try
            {
                var Id = View.ViewID;
                var Registered = PhotonNetwork.GetPhotonView(Id);
                if (Registered is null || ReferenceEquals(Registered, View)) View.ViewID = 0;
                else PhotonCloneIdentity.DetachLocalView(View);
            }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("Multiple published PhotonView releases failed.", Failure, Exception);
            }
        }
        PublishedViewBatches.Clear();
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    internal void PlayerLeft(Photon.Realtime.Player Player)
    {
        if (Run.Network.OwnsCurrentRun)
        {
            RpcReplays.PlayerLeft(Player.ActorNumber);
            Run.HookedGeneration.PlayerLeft(Player.ActorNumber);
        }
    }

    internal void RetireReplayBatch(int Index)
    {
        RpcReplays.RetireBatch(Index);
        GeneratedRpcDeferral.ReleaseReplayBatch(Index);
        List<PhotonView>? RetiredViews = null;
        foreach (var Pair in PublishedViewBatches)
        {
            if (Pair.Value != Index) continue;
            RetiredViews ??= new List<PhotonView>();
            RetiredViews.Add(Pair.Key);
        }
        if (RetiredViews is null) return;
        foreach (var View in RetiredViews) PublishedViewBatches.Remove(View);
    }

    internal void ReleaseState()
    {
        Exception? Failure = null;
        try
        {
            CancelPendingBatches();
        }
        catch (Exception Exception)
        {
            Failure = Exception;
        }
        try
        {
            RpcReplays.Release();
        }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("ViewBatch and generated RPC state cleanup both failed.", Failure, Exception);
        }
        finally
        {
            RetainedViewBytes.Clear(); Run.ReplayBudget.Reserve(this, 0);
            PublishedViewBatches.Clear();
            FakeItemRemapPending = false;
            InitialBatchReady = true;
            FakeItemIdHighWater = 0;
            AmuletPublicationValue = null;
            AmuletPublication.Reset();
            AmuletCapacitySlots = 0;
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }
    internal static void ReplayPreflightFakeItemSync()
    {
        var Data = Map.Ingress.DeferredFakeItemSync;
        if (Data is null)
        {
            Map.Ingress.ClearFakeItemSync();
            return;
        }
        var Manager = Map.Ingress.DeferredFakeItemOwner;
        var Room = Map.Ingress.DeferredFakeItemRoom;
        Map.Ingress.DeferredFakeItemOwner = null;
        Map.Ingress.DeferredFakeItemSync = null;
        Map.Ingress.DeferredFakeItemRoom = null;
        if (!Manager || !ReferenceEquals(Manager, FakeItemManager.Instance))
            throw new InvalidOperationException("Deferred native FakeItem state lost its owning manager.");
        if (!ReferenceEquals(Room, PhotonNetwork.CurrentRoom))
            throw new InvalidOperationException("Deferred native FakeItem state belongs to a different room.");
        Map.Ingress.ReplayingFakeItemSync = true;
        try
        {
            Manager.RPC_SyncFakeItems(Data);
        }
        finally
        {
            Map.Ingress.ReplayingFakeItemSync = false;
        }
    }
    internal bool ReplayDeferredFakeItemSync()
    {
        FakeItemRemapPending = false;
        var CurrentRun = Run;
        var Data = CurrentRun.Ingress.DeferredFakeItemSync;
        if (Data is null)
        {
            CurrentRun.Ingress.DeferredFakeItemOwner = null;
            CurrentRun.Ingress.DeferredFakeItemRoom = null;
            return true;
        }
        if (!ReferenceEquals(CurrentRun.Ingress.DeferredFakeItemRoom, PhotonNetwork.CurrentRoom))
        {
            ClearDeferredFakeItemSync();
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Deferred FakeItem state belongs to a different room");
            return false;
        }

        CurrentRun.Ingress.DeferredFakeItemSync = null;
        CurrentRun.Ingress.DeferredFakeItemRoom = null;
        var Manager = CurrentRun.Ingress.DeferredFakeItemOwner;
        CurrentRun.Ingress.DeferredFakeItemOwner = null;
        if (!Manager || !ReferenceEquals(Manager, FakeItemManager.Instance))
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Deferred FakeItem state could not be applied");
            return false;
        }
        CurrentRun.Ingress.ReplayingFakeItemSync = true;
        try
        {
            Manager.RPC_SyncFakeItems(Data);
            return true;
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(Map.Index, "Deferred FakeItem state could not be applied", Exception);
            return false;
        }
        finally
        {
            CurrentRun.Ingress.ReplayingFakeItemSync = false;
        }
    }

    [HarmonyPatch(typeof(FakeItemManager), nameof(FakeItemManager.RPC_SyncFakeItems))]
    static class FakeItemSyncPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FakeItemManager __instance, byte[] data)
        {
            var CurrentRun = Map.CurrentRun;
            if (CurrentRun is null)
            {
                if (Map.Ingress.ReplayingFakeItemSync || !GameHandler.IsOnIsland
                    || MapHandler.ExistsAndInitialized) return true;
                if (data is null) return true;
                Map.Ingress.DeferredFakeItemOwner = __instance;
                Map.Ingress.DeferredFakeItemSync = (byte[])data.Clone();
                Map.Ingress.DeferredFakeItemRoom = PhotonNetwork.CurrentRoom;
                return false;
            }
            if (CurrentRun.Releasing || CurrentRun.Released
                || !ReferenceEquals(__instance, FakeItemManager.Instance)) return true;
            var ViewBatches = CurrentRun.Network.ViewBatches;
            if (CurrentRun.Ingress.ReplayingFakeItemSync || !GameHandler.IsOnIsland
                || MapHandler.ExistsAndInitialized && !ViewBatches.FakeItemRemapPending
                    && !CurrentRun.HookedGeneration.FakeRegistryUnstable
                    && (!CurrentRun.HasPublishedLayout || ViewBatches.InitialBatchReady))
                return true;
            if (data is null)
            {
                LazySlotGenerator.AbortLazyGeneration(Map.Index, "PEAK sent malformed FakeItem state");
                return false;
            }
            CurrentRun.Ingress.DeferredFakeItemOwner = __instance;
            CurrentRun.Ingress.DeferredFakeItemSync = (byte[])data.Clone();
            CurrentRun.Ingress.DeferredFakeItemRoom = PhotonNetwork.CurrentRoom;
            return false;
        }
    }

    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FixedUpdate))]
    static class ArrowShooterFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(ArrowShooter __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(SyncedAnimation), nameof(SyncedAnimation.Update))]
    static class SyncedAnimationUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(SyncedAnimation __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(Looker), nameof(Looker.Update))]
    static class LookerUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Looker __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.Update))]
    static class WindChillZoneUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(WindChillZone __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.FixedUpdate))]
    static class WindChillZoneFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(WindChillZone __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(LavaRising), nameof(LavaRising.Update))]
    static class LavaRisingUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(LavaRising __instance) =>
            GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(EruptionSpawner), nameof(EruptionSpawner.Update))]
    static class EruptionSpawnerUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(EruptionSpawner __instance) =>
            GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(TornadoSpawner), nameof(TornadoSpawner.Update))]
    static class TornadoSpawnerUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(TornadoSpawner __instance) =>
            NativeSpawnAdmission.Ready(__instance) && GeneratedBatchOriginReady(__instance.view);
    }

    [HarmonyPatch(typeof(Antlion), nameof(Antlion.Update))]
    static class AntlionUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Antlion __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(Condor), nameof(Condor.FixedUpdate))]
    static class CondorFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Condor __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(BreakableBridge), nameof(BreakableBridge.Update))]
    static class BreakableBridgeUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(BreakableBridge __instance) =>
            GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(PeakSequence), nameof(PeakSequence.Update))]
    static class PeakSequenceUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(PeakSequence __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.Update))]
    static class ShittyPitonUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(ShittyPiton __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(Dynamite), nameof(Dynamite.Update))]
    static class DynamiteUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Dynamite __instance) =>
            GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(global::Campfire), nameof(global::Campfire.Update))]
    static class CampfireUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::Campfire __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(Peak.ForceHeroTextTrigger), nameof(Peak.ForceHeroTextTrigger.Update))]
    static class ForceHeroTextTriggerUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ForceHeroTextTrigger __instance) =>
            GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(TriggerEvent), nameof(TriggerEvent.OnTriggerEnter))]
    static class TriggerEventEnterPatch
    {
        [HarmonyPrefix]
        static bool Prefix(TriggerEvent __instance) =>
            GeneratedBatchOriginReady(__instance.view, Simulating: true);
    }

    [HarmonyPatch(typeof(SlipperyJellyfish), nameof(SlipperyJellyfish.OnTriggerEnter))]
    static class SlipperyJellyfishEnterPatch
    {
        [HarmonyPrefix]
        static bool Prefix(SlipperyJellyfish __instance) =>
            GeneratedBatchOriginReady(__instance.relay.view, Simulating: true);
    }

    [HarmonyPatch(typeof(Breakable), nameof(Breakable.OnCollisionEnter))]
    static class BreakableCollisionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Breakable __instance) =>
            GeneratedBatchOriginReady(__instance.item.view, Simulating: true);
    }

    [HarmonyPatch(typeof(Item), nameof(Item.OnPlayerEnteredRoom))]
    static class ItemPlayerEnteredPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Item __instance) =>
            GeneratedBatchOriginReady(__instance.view);
    }

    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.OnPlayerEnteredRoom))]
    static class ShittyPitonPlayerEnteredPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ShittyPiton __instance) =>
            GeneratedBatchOriginReady(__instance.view);
    }
}

[HarmonyPatch]
internal static class RetiredViewIdAllocationPatch
{
    static readonly MethodInfo Allocator;
    static readonly FieldInfo PhotonViews;
    static readonly MethodInfo ContainsView;
    static readonly MethodInfo IsUnavailable;

    static RetiredViewIdAllocationPatch()
    {
        Allocator = AccessTools.DeclaredMethod(
            typeof(PhotonNetwork), nameof(PhotonNetwork.AllocateViewID), new[] { typeof(int) })
            ?? throw new MissingMethodException(typeof(PhotonNetwork).FullName, "AllocateViewID(Int32)");
        PhotonViews = AccessTools.DeclaredField(typeof(PhotonNetwork), "photonViewList")
            ?? throw new MissingFieldException(typeof(PhotonNetwork).FullName, "photonViewList");
        if (!Allocator.IsPublic || !Allocator.IsStatic || Allocator.ReturnType != typeof(int)
            || !PhotonViews.IsStatic || PhotonViews.FieldType != typeof(NonAllocDictionary<int, PhotonView>))
            throw new InvalidOperationException("Photon ViewID allocator binding changed.");
        ContainsView = AccessTools.DeclaredMethod(
            PhotonViews.FieldType, nameof(NonAllocDictionary<int, PhotonView>.ContainsKey), new[] { typeof(int) })
            ?? throw new MissingMethodException(PhotonViews.FieldType.FullName, "ContainsKey(Int32)");
        IsUnavailable = AccessTools.DeclaredMethod(
            typeof(RetiredViewIdAllocationPatch), nameof(ViewIdUnavailable),
            new[] { PhotonViews.FieldType, typeof(int) })
            ?? throw new MissingMethodException(typeof(RetiredViewIdAllocationPatch).FullName, nameof(ViewIdUnavailable));
    }

    static MethodBase TargetMethod() => Allocator;

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
    {
        var Codes = new List<CodeInstruction>(Instructions);
        var Replaced = 0;
        for (var i = 2; i < Codes.Count; i++)
        {
            if (!Codes[i - 2].LoadsField(PhotonViews) || !Codes[i].Calls(ContainsView)) continue;
            Codes[i].opcode = OpCodes.Call;
            Codes[i].operand = IsUnavailable;
            Replaced++;
        }
        if (Replaced != 2)
            throw new InvalidOperationException(
                $"Photon ViewID allocator contained {Replaced} supported availability branches instead of 2.");
        return Codes;
    }

    static bool ViewIdUnavailable(NonAllocDictionary<int, PhotonView> Views, int Id) =>
        Views.ContainsKey(Id) || Map.CurrentRun?.Retirement.RetiredViewIds.Contains(Id) == true;
}
