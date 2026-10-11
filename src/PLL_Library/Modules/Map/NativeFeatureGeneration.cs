using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Splines.ExtrusionShapes;

namespace PLL.Library;

internal static class NativeFeatureGeneration
{
    internal sealed class FeatureRoots : IReadOnlyList<Transform>
    {
        readonly Transform[] Values;

        internal static FeatureRoots Empty { get; } = new(Array.Empty<Transform>());

        internal FeatureRoots(IReadOnlyList<Transform> Roots)
        {
            if (Roots is null) throw new ArgumentNullException(nameof(Roots));
            Values = new Transform[Roots.Count];
            var RootIds = new HashSet<int>();
            for (var i = 0; i < Roots.Count; i++)
            {
                var Root = Roots[i];
                if (!Root)
                    throw new InvalidOperationException("A native feature root is missing.");
                if (!RootIds.Add(Root.GetInstanceID()))
                    throw new InvalidOperationException("A native feature root was declared more than once.");
                Values[i] = Root;
            }
        }

        public int Count => Values.Length;
        public Transform this[int Index] => Values[Index];

        internal Transform? DeepestRoot(Transform Candidate, out int Rank) =>
            NativeFeatureGeneration.DeepestRoot(Values, Candidate, out Rank);

        public IEnumerator<Transform> GetEnumerator() =>
            ((IEnumerable<Transform>)Values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
    }

    internal static Transform? DeepestRoot(
        IReadOnlyList<Transform> Roots, Transform Candidate, out int Rank)
    {
        Rank = -1;
        if (!Candidate) return null;
        Transform? Owner = null;
        for (var i = 0; i < Roots.Count; i++)
        {
            var Root = Roots[i];
            if (!Root || Candidate.GetInstanceID() != Root.GetInstanceID() && !Candidate.IsChildOf(Root)
                || Owner && !Root.IsChildOf(Owner)) continue;
            Rank = i;
            Owner = Root;
        }
        return Owner;
    }

    internal sealed class GenerationOperation
    {
        internal sealed class VariationResolution
        {
            readonly Func<Transform, VariationOccurrence> PrepareCapability;
            internal VariationResolution(Func<Transform, VariationOccurrence> Prepare) => PrepareCapability = Prepare;
            internal VariationOccurrence Prepare(Transform Root) => PrepareCapability(Root);
        }

        internal sealed class Configuration
        {
            readonly Action<VariationSelection[]> RecordVariationsCapability;

            internal Configuration(
                bool RequiresNativeVariantSelection, int ConfigurationSeed,
                Action<VariationSelection[]> RecordVariations, VariationResolution? VariationResolution,
                Func<(bool SelectorRequired, VariationSwapper[] ForcedSwappers)>? ApplyNativeVariations = null)
            {
                RecordVariationsCapability = RecordVariations
                    ?? throw new ArgumentNullException(nameof(RecordVariations));
                this.RequiresNativeVariantSelection = RequiresNativeVariantSelection;
                Seed = ConfigurationSeed;
                Resolution = VariationResolution;
                this.ApplyNativeVariations = ApplyNativeVariations;
            }

            internal bool RequiresNativeVariantSelection { get; }
            internal int Seed { get; }
            internal VariationResolution? Resolution { get; }
            internal Func<(bool SelectorRequired, VariationSwapper[] ForcedSwappers)>? ApplyNativeVariations { get; }

            internal void RecordVariations(VariationSelection[] Variations) =>
                RecordVariationsCapability(Variations);
        }

        readonly FeatureRoots OwnedFeatureRoots;
        readonly FeatureRoots OwnedCollisionRoots;
        readonly Func<IDisposable?>? OpenExternalCollisionScopeCapability;

        internal GenerationOperation(
            PropGrouper GrouperValue, FeatureRoots OwnedRootsValue,
            IReadOnlyList<Transform> CollisionFeatureRootsValue,
            PlacementFrame PlacementFrameValue, int GenerationSeedValue,
            bool NoSkyPropsValue, bool RunSynchronouslyValue,
            MapRun.FeatureWriter WriterValue, Configuration? PendingConfigurationValue,
            Func<IDisposable?>? OpenExternalCollisionScope, FinalForceOccurrence? FinalForceValue,
            IReadOnlyDictionary<LevelGenStep, int>? CriticalRanks = null,
            IReadOnlyList<Transform>? ExcludedRoots = null, bool ReplaceExisting = false,
            Action? AfterClear = null,
            Func<GameObject, GameObject, NativeFlowRuntime[]>? BindGeneratedFlows = null,
            Action<NativeFlowSurfaceQuery>? InitializeFixedFlows = null,
            Action? PrepareFixedFlows = null, GeneratedGeometryBudget? GeometryBudget = null,
            FeatureOwnership? PreparedOwnership = null, string? ActorGenerationOwner = null,
            Action? CompleteAuthoring = null)
        {
            if (!GrouperValue) throw new ArgumentNullException(nameof(GrouperValue));
            if (!GrouperValue.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Native feature generation requires an active execution grouper.");
            if (OwnedRootsValue is null) throw new ArgumentNullException(nameof(OwnedRootsValue));
            if (OwnedRootsValue.Count == 0)
                throw new InvalidOperationException("Native feature generation requires at least one declared root.");
            foreach (var Root in OwnedRootsValue)
                if (Root != GrouperValue.transform && !Root.IsChildOf(GrouperValue.transform))
                    throw new InvalidOperationException(
                        "A declared native feature root left its execution grouper.");
            var CollisionRoots = new FeatureRoots(CollisionFeatureRootsValue);
            Grouper = GrouperValue;
            OwnedFeatureRoots = OwnedRootsValue;
            OwnedCollisionRoots = CollisionRoots;
            PlacementFrame = PlacementFrameValue;
            GenerationSeed = GenerationSeedValue;
            NoSkyProps = NoSkyPropsValue;
            RunSynchronously = RunSynchronouslyValue;
            Writer = WriterValue ?? throw new ArgumentNullException(nameof(WriterValue));
            PendingConfiguration = PendingConfigurationValue;
            OpenExternalCollisionScopeCapability = OpenExternalCollisionScope;
            FinalForce = FinalForceValue;
            this.CriticalRanks = CriticalRanks;
            this.ExcludedRoots = ExcludedRoots ?? Array.Empty<Transform>();
            this.ReplaceExisting = ReplaceExisting; this.AfterClear = AfterClear;
            this.BindGeneratedFlows = BindGeneratedFlows;
            this.InitializeFixedFlows = InitializeFixedFlows;
            this.PrepareFixedFlows = PrepareFixedFlows;
            this.GeometryBudget = GeometryBudget;
            this.PreparedOwnership = PreparedOwnership;
            this.ActorGenerationOwner = ActorGenerationOwner;
            this.CompleteAuthoring = CompleteAuthoring;
        }

        internal PropGrouper Grouper { get; }
        internal FeatureRoots FeatureRootSet => OwnedFeatureRoots;
        internal FeatureRoots CollisionRoots => OwnedCollisionRoots;
        internal PlacementFrame PlacementFrame { get; }
        internal int GenerationSeed { get; }
        internal string? ActorGenerationOwner { get; }
        internal bool NoSkyProps { get; }
        internal bool RunSynchronously { get; }
        internal MapRun.FeatureWriter Writer { get; }
        internal Configuration? PendingConfiguration { get; }
        internal FinalForceOccurrence? FinalForce { get; }
        internal IReadOnlyDictionary<LevelGenStep, int>? CriticalRanks { get; }
        internal IReadOnlyList<Transform> ExcludedRoots { get; }
        internal bool ReplaceExisting { get; }
        internal Action? AfterClear { get; }
        internal Func<GameObject, GameObject, NativeFlowRuntime[]>? BindGeneratedFlows { get; }
        internal Action<NativeFlowSurfaceQuery>? InitializeFixedFlows { get; }
        internal Action? PrepareFixedFlows { get; }
        internal GeneratedGeometryBudget? GeometryBudget { get; }
        internal FeatureOwnership? PreparedOwnership { get; }
        internal Action? CompleteAuthoring { get; }

        internal IDisposable? OpenExternalCollisionScope() =>
            OpenExternalCollisionScopeCapability?.Invoke();
    }

    internal sealed class NativeSpawnReservation : IDisposable
    {
        readonly NativeOutputInventory Owner;
        internal readonly GameObject Prefab;
        readonly NativeSpawnReservation? PreviousClone;
        readonly List<RawSplineMesh> SplineMeshes = new();
        Exception? PreparationFailure;
        bool Pending = true;
        internal Transform? Root { get; private set; }
        internal int Nodes { get; set; }
        internal bool TrackFootprint { get; }

        internal NativeSpawnReservation(NativeOutputInventory Owner, GameObject Prefab, int Nodes,
            bool TrackFootprint = true)
        {
            this.Owner = Owner; this.Prefab = Prefab; this.Nodes = Nodes;
            this.TrackFootprint = TrackFootprint;
            PreviousClone = ActiveClone;
        }

        internal RawSplineMesh Capture(SplineExtrude Extrude)
        {
            var Resource = Owner.CaptureSpline(Extrude, this);
            SplineMeshes.Add(Resource); return Resource;
        }

        internal void RecordFailure(Exception Failure) => PreparationFailure ??= Failure;

        internal void Complete(GameObject Output)
        {
            if (!Pending || !Output)
                throw new InvalidOperationException("A native clone reservation cannot accept this output.");
            foreach (var Extrude in Output.GetComponentsInChildren<SplineExtrude>(true))
                if (!RawSplines.ContainsKey(Extrude) && !NativeFlowRuntime.TryGetOwner(Extrude.transform, out _))
                    Capture(Extrude);
            if (PreparationFailure is not null) ExceptionPropagation.Throw(PreparationFailure);
            var Identity = Output.AddComponent<PllGeneratedPrefabIdentity>();
            Owner.Transfer(this, Output.transform);
            Root = Output.transform; Pending = false;
            CloseCloneScope();
            Identity.Bind(Prefab, this);
        }

        public void Dispose()
        {
            if (!Pending) return;
            Pending = false; CloseCloneScope();
            ReleaseSplines(); Owner.ReleasePending(this);
        }

        internal void DetachPending()
        {
            if (!Pending) return;
            Pending = false;
            while (ActiveClone is { Pending: false } Closed) ActiveClone = Closed.PreviousClone;
        }
        internal void ReleaseOutput() => Owner.ReleaseOutput(this);

        void CloseCloneScope()
        {
            if (!ReferenceEquals(ActiveClone, this))
                throw new InvalidOperationException("Native clone resource scopes completed out of order.");
            ActiveClone = PreviousClone;
        }

        internal void ReleaseSplines()
        {
            List<Exception>? Failures = null;
            foreach (var Resource in SplineMeshes) CleanupOperations.Capture(ref Failures, Resource.Dispose);
            GeneratedGeometryBudget.ThrowFailures(Failures);
            SplineMeshes.Clear();
        }
    }

    internal sealed class NativeOutputInventory : IDisposable
    {
        readonly FeatureOwnership Ownership;
        readonly GeneratedGeometryBudget? Budget;
        readonly HashSet<Transform> Baseline = new();
        readonly HashSet<Mesh> AssetMeshes = new();
        readonly Dictionary<GameObject, int> PrefabNodes = new();
        readonly Dictionary<Transform, NativeSpawnReservation> Outputs = new();
        readonly HashSet<NativeSpawnReservation> Pending = new();
        readonly HashSet<RawSplineMesh> SplineMeshes = new();
        Dictionary<Mesh, (long Bytes, HashSet<NativeSpawnReservation?> Outputs)> ProceduralMeshes = new();
        readonly HashSet<Transform> RetainedRoots = new();
        int Nodes;
        int Instances;
        long Bytes;
        bool Released;

        internal NativeOutputInventory(FeatureOwnership Ownership, GeneratedGeometryBudget? Budget)
        {
            this.Ownership = Ownership; this.Budget = Budget;
            if (Budget is null) return;
            foreach (var Root in Ownership.Roots)
                foreach (var Stacker in Root.GetComponentsInChildren<Peak.TowerStacker>(true))
                {
                    if (!Ownership.Contains(Stacker.transform)) continue;
                    foreach (var Part in Stacker.parts) CapturePrefab(Part);
                    foreach (var Output in Stacker.stack) RetainedRoots.Add(Output.transform);
                }
            foreach (var Root in Ownership.Roots)
                foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                {
                    if (!Component || !Ownership.Contains(Component.transform)) continue;
                    var Node = Component.transform;
                    var Retained = false;
                    for (var Current = Node; Current; Current = Current.parent)
                        if (RetainedRoots.Contains(Current)) { Retained = true; break; }
                    if (Component is Transform && !Retained) Baseline.Add(Node);
                    if (!Retained && MeshOf(Component) is { } Mesh) AssetMeshes.Add(Mesh);
                }
        }

        internal NativeSpawnReservation Begin(GameObject Prefab, bool TrackFootprint = true)
        {
            var Count = TrackFootprint && Budget is not null ? CapturePrefab(Prefab) : 0;
            Budget?.ReserveNativeOutput(this, Instances + RetainedRoots.Count + (TrackFootprint ? 1 : 0),
                checked(Nodes + Count), ProceduralMeshes.Count, Bytes);
            var Reservation = new NativeSpawnReservation(this, Prefab, Count, TrackFootprint);
            if (TrackFootprint) Instances++;
            Pending.Add(Reservation); Nodes += Count;
            ActiveClone = Reservation;
            return Reservation;
        }

        internal RawSplineMesh CaptureSpline(SplineExtrude Extrude, NativeSpawnReservation? Clone = null)
        {
            var Resource = new RawSplineMesh(this, Extrude, Budget, Clone);
            SplineMeshes.Add(Resource); RawSplines.Add(Extrude, Resource);
            Resource.BindRiver();
            return Resource;
        }

        internal void PrepareSplines()
        {
            foreach (var Root in Ownership.Roots)
                foreach (var Extrude in Root.GetComponentsInChildren<SplineExtrude>(true))
                    if (Ownership.Contains(Extrude.transform) && !RawSplines.ContainsKey(Extrude)
                        && !NativeFlowRuntime.TryGetOwner(Extrude.transform, out _))
                        CaptureSpline(Extrude);
        }

        internal void RequirePrepared()
        {
            foreach (var Resource in SplineMeshes) Resource.RequirePrepared();
        }

        internal void ReleaseSplines()
        {
            var Resources = new RawSplineMesh[SplineMeshes.Count]; SplineMeshes.CopyTo(Resources);
            List<Exception>? Failures = null;
            foreach (var Resource in Resources) CleanupOperations.Capture(ref Failures, Resource.Dispose);
            GeneratedGeometryBudget.ThrowFailures(Failures);
        }

        internal void ForgetSpline(RawSplineMesh Resource) => SplineMeshes.Remove(Resource);

        internal void ClearSplineMeshes()
        {
            List<Exception>? Failures = null;
            foreach (var Resource in SplineMeshes)
                if (Resource.IsFixed) CleanupOperations.Capture(ref Failures, Resource.ReleaseMesh);
            GeneratedGeometryBudget.ThrowFailures(Failures);
        }

        internal void Transfer(NativeSpawnReservation Reservation, Transform Root)
        {
            if (!Pending.Contains(Reservation) || !Ownership.Contains(Root)
                || !Outputs.TryAdd(Root, Reservation))
                throw new InvalidOperationException("A native clone left its reserved output owner.");
            Pending.Remove(Reservation);
        }

        internal bool TryGetTrackedPrefab(Transform Root, out GameObject Prefab)
        {
            if (Outputs.TryGetValue(Root, out var Reservation) && Reservation.TrackFootprint)
            {
                Prefab = Reservation.Prefab; return true;
            }
            Prefab = null!; return false;
        }

        internal void ReleasePending(NativeSpawnReservation Reservation)
        {
            if (!Pending.Remove(Reservation)) return;
            if (Reservation.TrackFootprint) Instances--;
            Nodes -= Reservation.Nodes; Reserve();
        }

        internal void ReleaseOutput(NativeSpawnReservation Reservation)
        {
            if (Reservation.Root is not { } Root || !Outputs.ContainsKey(Root)) return;
            Reservation.ReleaseSplines();
            Outputs.Remove(Root);
            if (Reservation.TrackFootprint) Instances--;
            Nodes -= Reservation.Nodes;
            List<Mesh>? Released = null;
            foreach (var Entry in ProceduralMeshes)
                if (Entry.Value.Outputs.Remove(Reservation) && Entry.Value.Outputs.Count == 0)
                {
                    Bytes -= Entry.Value.Bytes; (Released ??= new List<Mesh>()).Add(Entry.Key);
                }
            if (Released is not null)
                foreach (var Mesh in Released) ProceduralMeshes.Remove(Mesh);
            Reserve();
        }

        internal void Complete()
        {
            RequirePrepared();
            if (Pending.Count != 0)
                throw new InvalidOperationException("Native output publication has pending clones.");
            if (Budget is null)
            {
                var Renderers = new HashSet<Renderer>();
                foreach (var Root in Ownership.Roots)
                    foreach (var Renderer in Root.GetComponentsInChildren<Renderer>(true))
                        if (Ownership.Contains(Renderer.transform) && Renderers.Add(Renderer))
                            NativeFogRenderRuntime.BindRenderer(Renderer);
                return;
            }
            var CompletedNodes = 0;
            long CompletedBytes = 0;
            var OutputNodes = new Dictionary<NativeSpawnReservation, int>();
            var CompletedMeshes = new Dictionary<Mesh, (long Bytes, HashSet<NativeSpawnReservation?> Outputs)>();
            var Seen = new HashSet<Transform>();
            foreach (var Root in Ownership.Roots)
            {
                if (!Root) continue;
                foreach (var Node in Root.GetComponentsInChildren<Transform>(true))
                {
                    if (!Seen.Add(Node) || !Ownership.Contains(Node) || NativeFlowRuntime.TryGetOwner(Node, out _)) continue;
                    if (PropInteractionRuntime.OwnsNativeGeometry(Node))
                    {
                        foreach (var SpanRenderer in Node.GetComponents<Renderer>()) NativeFogRenderRuntime.BindRenderer(SpanRenderer);
                        continue;
                    }
                    NativeSpawnReservation? MeshOwner = null;
                    for (var Current = Node; Current; Current = Current.parent)
                        if (Outputs.TryGetValue(Current, out MeshOwner)) break;
                    if (MeshOwner is { TrackFootprint: false }) continue;
                    if (!Baseline.Contains(Node))
                    {
                        CompletedNodes++;
                        if (MeshOwner is not null)
                        {
                            OutputNodes.TryGetValue(MeshOwner, out var Count);
                            OutputNodes[MeshOwner] = Count + 1;
                        }
                    }
                    var SurfaceMesh = Node.TryGetComponent<MeshFilter>(out var Filter)
                        && Node.TryGetComponent<Renderer>(out var Renderer) && NativeFlowRuntime.OwnsRenderer(Renderer)
                        ? Filter.sharedMesh : null;
                    foreach (var Component in Node.GetComponents<Component>())
                    {
                        if (Component is Renderer OutputRenderer)
                            NativeFogRenderRuntime.BindRenderer(OutputRenderer);
                        if (MeshOf(Component) is not { } Mesh || AssetMeshes.Contains(Mesh) || RawMeshes.Contains(Mesh)
                            || Mesh == SurfaceMesh) continue;
                        if (CompletedMeshes.TryGetValue(Mesh, out var MeshEntry))
                        {
                            MeshEntry.Outputs.Add(MeshOwner); continue;
                        }
                        long MeshBytes;
                        if (Mesh.isReadable)
                        {
                            using var Data = Mesh.AcquireReadOnlyMeshData(Mesh);
                            var Snapshot = Data[0];
                            MeshBytes = Snapshot.GetIndexData<byte>().Length;
                            for (var Stream = 0; Stream < Snapshot.vertexBufferCount; Stream++)
                                MeshBytes = checked(MeshBytes + Snapshot.GetVertexData<byte>(Stream).Length);
                        }
                        else
                        {
                            using var IndexBuffer = NativeMeshAssets.OpenIndexBuffer(Mesh);
                            MeshBytes = IndexBuffer is null ? 0 : checked((long)IndexBuffer.count * IndexBuffer.stride);
                            for (var Stream = 0; Stream < Mesh.vertexBufferCount; Stream++)
                                MeshBytes = checked(MeshBytes + (long)Mesh.vertexCount * Mesh.GetVertexBufferStride(Stream));
                        }
                        CompletedBytes = checked(CompletedBytes + MeshBytes);
                        var MeshOutputs = new HashSet<NativeSpawnReservation?> { MeshOwner };
                        CompletedMeshes.Add(Mesh, (MeshBytes, MeshOutputs));
                    }
                }
            }
            Budget.ReserveNativeOutput(this, Instances + RetainedRoots.Count,
                CompletedNodes, CompletedMeshes.Count, CompletedBytes);
            Nodes = CompletedNodes; Bytes = CompletedBytes; ProceduralMeshes = CompletedMeshes;
            foreach (var Output in Outputs.Values)
            {
                OutputNodes.TryGetValue(Output, out var Count); Output.Nodes = Count;
            }
        }

        internal void AfterClear()
        {
            if (Released) return;
            RetainedRoots.RemoveWhere(Root => !Root);
            Complete();
            if (Outputs.Count == 0 && RetainedRoots.Count == 0 && Nodes == 0
                && ProceduralMeshes.Count == 0 && SplineMeshes.Count == 0)
                Budget?.ReleaseNativeOutput(this);
        }

        internal void PublishActorIdentities(string GenerationOwner, int GenerationSeed)
        {
            var Ordered = new List<(string Key, Transform Root)>(Outputs.Count);
            foreach (var Root in Outputs.Keys)
            {
                if (!Root) continue;
                var Owner = Ownership.Roots.DeepestRoot(Root, out var Rank)
                    ?? throw new InvalidOperationException("A native actor output left its generation owner.");
                Ordered.Add((DeterministicObjectIdentity.RelativeComponentPath(Root, Owner, Rank), Root));
            }
            Ordered.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Key, Right.Key));
            for (var Ordinal = 0; Ordinal < Ordered.Count; Ordinal++)
                Ordered[Ordinal].Root.GetComponent<PllGeneratedPrefabIdentity>().PublishActorIdentity(
                    new ActorPlacementIdentity(GenerationOwner, 0, GenerationSeed, Ordinal));
        }

        public void Dispose()
        {
            ReleaseSplines();
            Released = true;
            foreach (var Reservation in Pending) Reservation.DetachPending();
            Outputs.Clear(); Pending.Clear(); RetainedRoots.Clear();
            ProceduralMeshes.Clear(); PrefabNodes.Clear();
            Baseline.Clear(); AssetMeshes.Clear(); Instances = Nodes = 0; Bytes = 0;
            Budget?.ReleaseNativeOutput(this);
        }

        void Reserve() => Budget?.ReserveNativeOutput(this,
            Instances + RetainedRoots.Count, Nodes, ProceduralMeshes.Count, Bytes);

        int CapturePrefab(GameObject Prefab)
        {
            if (PrefabNodes.TryGetValue(Prefab, out var Count)) return Count;
            foreach (var Component in Prefab.GetComponentsInChildren<Component>(true))
            {
                if (Component is Transform) Count++;
                if (MeshOf(Component) is { } Mesh) AssetMeshes.Add(Mesh);
            }
            PrefabNodes.Add(Prefab, Count); return Count;
        }

        static Mesh? MeshOf(Component Component) => Component switch
        {
            MeshFilter Filter => Filter.sharedMesh ? Filter.sharedMesh : null,
            SkinnedMeshRenderer Renderer => Renderer.sharedMesh ? Renderer.sharedMesh : null,
            MeshCollider Collider => Collider.sharedMesh ? Collider.sharedMesh : null,
            _ => null
        };
    }

    internal sealed class FeatureOwnership
    {
        internal FeatureOwnership(FeatureRoots RootsValue, IReadOnlyList<Transform> ExcludedRoots,
            GeneratedGeometryBudget? GeometryBudget)
        {
            Roots = RootsValue;
            Towers = new NativeTowerStacker.Generation(RootsValue);
            this.ExcludedRoots = ExcludedRoots;
            Outputs = new NativeOutputInventory(this, GeometryBudget);
        }

        internal FeatureRoots Roots { get; }
        internal NativeTowerStacker.Generation Towers { get; }
        internal NativeOutputInventory Outputs { get; }
        readonly IReadOnlyList<Transform> ExcludedRoots;
        internal bool Contains(Transform Value)
        {
            if (Roots.DeepestRoot(Value, out _) is null) return false;
            foreach (var Root in ExcludedRoots) if (TransformOwnership.Contains(Root, Value)) return false;
            return true;
        }
    }

    sealed class GenerationLease
    {
        readonly MapRun.FeatureWriter Writer;
        OperationCanceledException? WriterCancellation;

        internal GenerationLease(
            long EpochValue, PropGrouper GrouperValue,
            FeatureOwnership OwnershipValue, bool RunSynchronouslyValue,
            MapRun.FeatureWriter WriterValue,
            IReadOnlyDictionary<LevelGenStep, int>? CriticalRanks)
        {
            Epoch = EpochValue;
            Grouper = GrouperValue;
            Ownership = OwnershipValue;
            RunSynchronously = RunSynchronouslyValue;
            Writer = WriterValue;
            this.CriticalRanks = CriticalRanks;
            LastTransportPumpTimestamp = Stopwatch.GetTimestamp();
        }

        internal long Epoch { get; }
        internal PropGrouper Grouper { get; }
        internal FeatureOwnership Ownership { get; }
        internal FeatureRoots Roots => Ownership.Roots;
        internal bool RunSynchronously { get; }
        internal IReadOnlyDictionary<LevelGenStep, int>? CriticalRanks { get; }
        internal bool CancellationRequested { get; private set; }
        internal bool Cancelled => CancellationRequested || Writer.CancellationRequested;
        internal UnityEngine.Random.State GenerationRandomState { get; set; }
        internal int RandomDepth { get; set; }
        internal bool RandomHandoffReady { get; set; }
        internal long LastTransportPumpTimestamp { get; set; }
        internal Action? AfterClear { get; set; }
        internal Func<GameObject, GameObject, NativeFlowRuntime[]>? BindGeneratedFlows { get; set; }

        internal void Cancel() => CancellationRequested = true;

        internal OperationCanceledException OwnedCancellation() =>
            WriterCancellation ??= MapRun.FeatureWriter.Cancelled();

        internal bool OwnsCancellation(Exception Failure) =>
            ReferenceEquals(WriterCancellation, Failure);

        internal void RequireCurrent()
        {
            try { Writer.RequireCurrent(); }
            catch (OperationCanceledException) when (Writer.CancellationRequested)
            {
                throw OwnedCancellation();
            }
            if (CancellationRequested) throw OwnedCancellation();
        }
    }

    delegate void NativeRandomScopeAction(ref NativeRandomScope Scope);

    internal sealed class RawSplineMesh : IDisposable
    {
        readonly NativeOutputInventory Owner;
        readonly SplineExtrude Extrude;
        readonly GeneratedGeometryBudget? Budget;
        readonly NativeSpawnReservation? Clone;
        readonly LavaRiver? River;
        readonly Transform? Lights, Details;
        Mesh? OwnedMesh, BorrowedMesh, BorrowedTarget, BorrowedFilter, BorrowedCollider;
        MeshFilter? Filter;
        MeshCollider? Collider;
        Exception? PreparationFailure;
        bool Released;

        internal RawSplineMesh(NativeOutputInventory Owner, SplineExtrude Extrude, GeneratedGeometryBudget? Budget,
            NativeSpawnReservation? Clone)
        {
            this.Owner = Owner; this.Extrude = Extrude; this.Budget = Budget; this.Clone = Clone;
            River = Extrude.GetComponentInParent<LavaRiver>(true);
            if (River)
            {
                Lights = River.transform.Find("BakedLights"); Details = River.transform.Find("DetailObjects");
            }
        }

        internal void BindRiver()
        {
            if (River) RawRivers.TryAdd(River, this);
        }

        internal bool IsFixed => Clone is null;

        internal void RecordFailure(Exception Failure)
        {
            PreparationFailure ??= Failure; Clone?.RecordFailure(Failure);
        }

        internal void RequirePrepared()
        {
            if (PreparationFailure is not null) ExceptionPropagation.Throw(PreparationFailure);
        }

        internal bool PrepareRebuild()
        {
            RequirePrepared();
            var Empty = !Extrude.Container || Extrude.Splines.Count == 0 || Extrude.Range.x == Extrude.Range.y;
            long Bytes = 0;
            if (!Empty)
            {
                Extrude.Shape ??= new Circle { SideCount = Extrude.Sides };
                var Settings = new ExtrudeSettings<IExtrudeShape>(Extrude.Shape)
                {
                    Radius = Extrude.Radius, CapEnds = Extrude.Capped,
                    Range = Extrude.Range, FlipNormals = Extrude.FlipNormals
                };
                var Vertices = 0; var Indices = 0;
                foreach (var Spline in Extrude.Splines)
                {
                    if (Spline.Count < 2) continue;
                    var Segments = Mathf.Ceil(Spline.GetLength()
                        * Mathf.Abs(Settings.Range.y - Settings.Range.x) * Extrude.SegmentsPerUnit);
                    if (!float.IsFinite(Segments) || Segments < 0 || Segments >= int.MaxValue)
                        throw new InvalidOperationException("Native spline segments exceed the supported count.");
                    Settings.SegmentCount = Mathf.Max((int)Segments, 1);
                    if (Extrude.Shape.SideCount < 0
                        || 6L * (Extrude.Shape.SideCount + 1L) * (Settings.SegmentCount + 2L) > int.MaxValue)
                        throw new InvalidOperationException("Native spline geometry exceeds the supported count.");
                    SplineMesh.GetVertexAndIndexCount<Spline, IExtrudeShape>(
                        Spline, Settings, out var SplineVertices, out var SplineIndices);
                    Vertices = checked(Vertices + SplineVertices); Indices = checked(Indices + SplineIndices);
                }
                Bytes = checked(32L * Vertices + (Vertices < ushort.MaxValue ? 2L : 4L) * Indices);
            }
            Budget?.ReserveNativeOutput(this, 0, 0, 1, Bytes);
            if (!OwnedMesh)
            {
                BorrowedMesh = Extrude.m_Mesh; BorrowedTarget = Extrude.m_TargetMesh;
                Extrude.TryGetComponent(out Filter); Extrude.TryGetComponent(out Collider);
                BorrowedFilter = Filter ? Filter.sharedMesh : null;
                BorrowedCollider = Collider ? Collider.sharedMesh : null;
                OwnedMesh = new Mesh(); RawMeshes.Add(OwnedMesh);
                OwnedMesh.name = "<Spline Extruded Mesh>"; OwnedMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            Extrude.m_Mesh = OwnedMesh; Extrude.m_TargetMesh = OwnedMesh;
            if (Filter) Filter.sharedMesh = OwnedMesh;
            if (Empty)
            {
                OwnedMesh!.Clear();
                if (Collider && Collider.sharedMesh == OwnedMesh) Collider.sharedMesh = null;
            }
            return !Empty;
        }

        internal void ReleaseMesh()
        {
            if (OwnedMesh is not null)
            {
                if (Extrude)
                {
                    if (Extrude.m_Mesh == OwnedMesh) Extrude.m_Mesh = BorrowedMesh;
                    if (Extrude.m_TargetMesh == OwnedMesh) Extrude.m_TargetMesh = BorrowedTarget;
                }
                if (Filter && Filter.sharedMesh == OwnedMesh) Filter.sharedMesh = BorrowedFilter;
                if (Collider && Collider.sharedMesh == OwnedMesh) Collider.sharedMesh = BorrowedCollider;
                UnityEngine.Object.DestroyImmediate(OwnedMesh);
                RawMeshes.Remove(OwnedMesh); OwnedMesh = null;
            }
            Budget?.ReleaseNativeOutput(this);
        }

        public void Dispose()
        {
            if (Released) return;
            ReleaseMesh(); RawSplines.Remove(Extrude);
            if (River is not null && RawRivers.TryGetValue(River, out var Owner)
                && ReferenceEquals(Owner, this)) RawRivers.Remove(River);
            this.Owner.ForgetSpline(this);
            Released = true;
        }

        internal void ClearChildren()
        {
            Clear(Lights); Clear(Details); Clear(River ? River.splineObjectParent : null);

            static void Clear(Transform? Parent)
            {
                if (!Parent) return;
                for (var i = Parent.childCount - 1; i >= 0; i--)
                {
                    var Child = Parent.GetChild(i);
                    if (Child.GetComponent<PllGeneratedPrefabIdentity>()) DestroyNativeOutput(Child.gameObject);
                }
            }
        }
    }

    static readonly Dictionary<SplineExtrude, RawSplineMesh> RawSplines = new();
    static readonly Dictionary<LavaRiver, RawSplineMesh> RawRivers = new();
    static readonly HashSet<Mesh> RawMeshes = new();
    static NativeSpawnReservation? ActiveClone;
    static RawSplineCall? ActiveSplineCall;

    sealed class RawSplineCall : IDisposable
    {
        readonly RawSplineCall? Previous;
        internal RawSplineMesh Owner { get; }
        internal bool? PreparedRebuild { get; set; }

        internal RawSplineCall(RawSplineMesh Owner)
        {
            this.Owner = Owner; Previous = ActiveSplineCall; ActiveSplineCall = this;
        }

        public void Dispose() => ActiveSplineCall = Previous;
    }

    internal static bool OwnsRawSplineMesh(Mesh Mesh) => RawMeshes.Contains(Mesh);

    static RawSplineMesh? RawSplineOwner(SplineExtrude Extrude)
    {
        if (NativeFlowRuntime.TryGetOwner(Extrude.transform, out _)) return null;
        if (RawSplines.TryGetValue(Extrude, out var Resource)) return Resource;
        return ActiveClone?.Capture(Extrude);
    }

    [HarmonyPatch(typeof(SplineExtrude), nameof(SplineExtrude.EnsureMeshExists))]
    static class RawSplineEnsurePatch
    {
        [HarmonyPrefix]
        static bool Prefix(SplineExtrude __instance)
        {
            if (RawSplineOwner(__instance) is not { } Resource) return true;
            try
            {
                var Rebuild = Resource.PrepareRebuild();
                if (ActiveSplineCall is { } Call && ReferenceEquals(Call.Owner, Resource))
                    Call.PreparedRebuild = Rebuild;
            }
            catch (Exception Failure) { Resource.RecordFailure(Failure); throw; }
            return false;
        }
    }

    [HarmonyPatch(typeof(SplineExtrude), nameof(SplineExtrude.Rebuild))]
    static class RawSplineRebuildPatch
    {
        [HarmonyPrefix]
        static bool Prefix(SplineExtrude __instance, out RawSplineMesh? __state)
        {
            __state = RawSplineOwner(__instance);
            if (__state is not { } Resource) return true;
            if (ActiveSplineCall is { PreparedRebuild: { } Rebuild } Call && ReferenceEquals(Call.Owner, Resource))
            {
                Call.PreparedRebuild = null; return Rebuild;
            }
            return Resource.PrepareRebuild();
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(RawSplineMesh? __state, Exception? __exception)
        {
            if (__exception is not null) __state?.RecordFailure(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SplineExtrude), nameof(SplineExtrude.CleanupMesh))]
    static class RawSplineCleanupPatch
    {
        [HarmonyPrefix]
        static bool Prefix(SplineExtrude __instance)
        {
            if (!RawSplines.TryGetValue(__instance, out var Resource)) return true;
            Resource.ReleaseMesh(); return false;
        }
    }

    [HarmonyPatch(typeof(SplineExtrude), nameof(SplineExtrude.OnEnable))]
    static class RawSplineEnablePatch
    {
        [HarmonyPrefix]
        static void Prefix(SplineExtrude __instance, out RawSplineCall? __state) =>
            __state = RawSplineOwner(__instance) is { } Owner ? new RawSplineCall(Owner) : null;

        [HarmonyFinalizer]
        static void Finalizer(RawSplineCall? __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(SplineExtrude), nameof(SplineExtrude.targetMesh), MethodType.Setter)]
    static class RawSplineTargetPatch
    {
        [HarmonyPrefix]
        static void Prefix(SplineExtrude __instance, out RawSplineCall? __state) =>
            __state = RawSplineOwner(__instance) is { } Owner ? new RawSplineCall(Owner) : null;

        [HarmonyFinalizer]
        static void Finalizer(RawSplineCall? __state) => __state?.Dispose();
    }

    static void AssignNativeLavaMesh(SplineExtrude Extrude)
    {
        if (RawSplineOwner(Extrude) is null) Extrude.GetComponent<MeshFilter>().mesh = new Mesh();
    }

    [HarmonyPatch(typeof(LavaRiver), nameof(LavaRiver.Apply))]
    static class RawLavaApplyPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var GetFilter = SymbolExtensions.GetMethodInfo(() => default(SplineExtrude)!.GetComponent<MeshFilter>());
            var Replacement = ((Action<SplineExtrude>)AssignNativeLavaMesh).Method;
            var Replaced = false;
            for (var i = 0; i + 3 < Codes.Count; i++)
            {
                if (Codes[i].opcode != OpCodes.Dup || !Codes[i + 1].Calls(GetFilter)
                    || Codes[i + 2].opcode != OpCodes.Newobj || Codes[i + 2].operand is not ConstructorInfo Constructor
                    || Constructor.DeclaringType != typeof(Mesh)
                    || Codes[i + 3].operand is not MethodInfo SetMesh || SetMesh.DeclaringType != typeof(MeshFilter)
                    || SetMesh.Name != "set_mesh") continue;
                if (Replaced) throw new InvalidOperationException("Native lava mesh allocation is ambiguous.");
                Codes[i + 1].opcode = OpCodes.Call; Codes[i + 1].operand = Replacement;
                Codes[i + 2].opcode = Codes[i + 3].opcode = OpCodes.Nop;
                Codes[i + 2].operand = Codes[i + 3].operand = null;
                Replaced = true;
            }
            if (!Replaced) throw new InvalidOperationException("Native lava mesh allocation was not found.");
            return Codes;
        }
    }

    static void DestroyCreatedLavaOutput(UnityEngine.Object Output)
    {
        if (Output is GameObject Root && Root
            && Root.GetComponentInParent<LavaRiver>(true) is { } River && RawRivers.ContainsKey(River)
            && !Root.GetComponent<PllGeneratedPrefabIdentity>()) return;
        DestroyNativeOutput(Output);
    }

    [HarmonyPatch(typeof(LavaRiver), nameof(LavaRiver.Clear))]
    static class RawLavaClearPatch
    {
        [HarmonyPrefix]
        static void Prefix(LavaRiver __instance)
        {
            if (RawRivers.TryGetValue(__instance, out var Resource)) Resource.ClearChildren();
        }

        [HarmonyTranspiler, HarmonyPriority(Priority.Last)]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Original = ((Action<UnityEngine.Object>)UnityEngine.Object.DestroyImmediate).Method;
            var Retire = ((Action<UnityEngine.Object>)DestroyNativeOutput).Method;
            var Replacement = ((Action<UnityEngine.Object>)DestroyCreatedLavaOutput).Method;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(Original) || Instruction.Calls(Retire)) Instruction.operand = Replacement;
                yield return Instruction;
            }
        }
    }

    struct NativeRandomScope
    {
        internal NativeRandomScope(GenerationLease Lease, UnityEngine.Random.State Ambient)
        {
            this.Lease = Lease;
            this.Ambient = Ambient;
        }

        internal GenerationLease? Lease;
        internal UnityEngine.Random.State Ambient;
    }

    static readonly Dictionary<PropGrouper, FeatureOwnership> OwnedFeatures = new();
    static readonly long TransportPumpIntervalTicks = Stopwatch.Frequency / 30;
    static GenerationLease? activeLease;
    static long nextEpoch;

    internal static IEnumerator Run(GenerationOperation Operation)
    {
        var Writer = Operation.Writer;
        Writer.RequireCurrent();
        while (activeLease is not null)
        {
            yield return null;
            Writer.RequireCurrent();
        }
        if (nextEpoch == long.MaxValue)
            throw new InvalidOperationException("Native feature generation exhausted its monotonic epoch.");

        var FeatureRootSet = Operation.FeatureRootSet;
        var CollisionRoots = Operation.CollisionRoots;
        var RandomState = UnityEngine.Random.state;
        var Grouper = Operation.Grouper;
        var Ownership = Operation.PreparedOwnership;
        if (Ownership is null)
        {
            if (Operation.ReplaceExisting && OwnedFeatures.TryGetValue(Grouper, out var Existing)) Ownership = Existing;
            else
            {
                Ownership = new FeatureOwnership(FeatureRootSet, Operation.ExcludedRoots, Operation.GeometryBudget);
                Ownership.Outputs.PrepareSplines();
            }
        }
        var Lease = new GenerationLease(
            ++nextEpoch, Grouper, Ownership, Operation.RunSynchronously, Writer, Operation.CriticalRanks);
        Lease.AfterClear = Operation.AfterClear;
        Lease.BindGeneratedFlows = Operation.BindGeneratedFlows;
        var WriterLease = Writer.RetainOperation();
        var Configuration = Operation.PendingConfiguration;
        var FinalForce = Operation.FinalForce;
        IDisposable? ExternalCollisionScope = null;
        IDisposable? PropSpawnerGenerationScope = null;
        IDisposable? RpcRootReservation = null;
        IDisposable? TowerGenerationScope = null;
        var AwaitPending = false;
        var Disposed = false;
        var Cleaned = false;

        void ReleaseExternalCollisionScope()
        {
            var Scope = ExternalCollisionScope;
            ExternalCollisionScope = null;
            Scope?.Dispose();
        }

        void Cleanup()
        {
            if (Cleaned || AwaitPending) return;
            Cleaned = true;
            List<Exception>? Failures = null;
            CleanupOperations.Capture(ref Failures, ReleaseExternalCollisionScope);
            CleanupOperations.Capture(ref Failures, () => TowerGenerationScope?.Dispose());
            CleanupOperations.Capture(ref Failures, () =>
            {
                if (Lease.RandomDepth != 0)
                    throw new InvalidOperationException("Native feature random scope remained active during cleanup.");
                if (!Lease.RandomHandoffReady) UnityEngine.Random.state = RandomState;
            });
            CleanupOperations.Capture(ref Failures, () => RpcRootReservation?.Dispose());
            CleanupOperations.Capture(ref Failures, () => PropSpawnerGenerationScope?.Dispose());
            if (ReferenceEquals(activeLease, Lease)) activeLease = null;
            else if (activeLease is not null)
                (Failures ??= new()).Add(new InvalidOperationException(
                    "Native feature generation lost its active lease during cleanup."));
            CleanupOperations.Capture(ref Failures, WriterLease.Dispose);

            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is not null)
                throw new AggregateException("Native feature generation cleanup failed.", Failures);
        }
        activeLease = Lease;
        try
        {
            PropSpawnerGenerationScope = NativePropSpawnerGeneration.EnterGeneration(
                FeatureRootSet, CollisionRoots, Operation.PlacementFrame,
                Operation.NoSkyProps,
                () => Lease.Cancelled, () => RequireCurrentLease(Lease));
            Lease.RequireCurrent();
            RpcRootReservation = GeneratedRpcDeferral.ReserveRoots(
                FeatureRootSet, () => Lease.Cancelled);
            TowerGenerationScope = NativeTowerStacker.EnterGeneration(
                Ownership.Towers, FeatureRootSet,
                () => Lease.Cancelled, () => RequireCurrentLease(Lease));
            if (Operation.ReplaceExisting) OwnedFeatures[Grouper] = Ownership;
            else OwnedFeatures.Add(Grouper, Ownership);
            Lease.RequireCurrent();
            FinalForce?.Stage(Lease.Epoch);
            if (Configuration is not null)
            {
                Lease.RequireCurrent();
                UnityEngine.Random.InitState(Configuration.Seed);
                Configuration.RecordVariations(Configure(Configuration, Lease));
            }
            Lease.RequireCurrent();
            FinalForce?.Prepare(Lease.Epoch);
            Lease.RequireCurrent();
            Operation.PrepareFixedFlows?.Invoke();
            Lease.RequireCurrent();
            ExternalCollisionScope = Operation.OpenExternalCollisionScope();
            Lease.RequireCurrent();
            UnityEngine.Random.InitState(Operation.GenerationSeed);
            Lease.RequireCurrent();
            var GenerationTask = Grouper.RunAll(
                updateLightmap: false, runSynchronously: Operation.RunSynchronously);
            Lease.GenerationRandomState = UnityEngine.Random.state;
            UnityEngine.Random.state = RandomState;
            Lease.RandomHandoffReady = true;
            var Awaiter = GenerationTask.GetAwaiter();
            Exception? AwaitFailure = null;
            AwaitPending = !Awaiter.IsCompleted;

            void CompleteAwait()
            {
                try
                {
                    Awaiter.GetResult();
                }
                catch (Exception ExceptionValue)
                {
                    AwaitFailure = ExceptionValue;
                }
                AwaitPending = false;
                if (!Disposed) return;

                Exception? CleanupFailure = null;
                try
                {
                    Cleanup();
                }
                catch (Exception ExceptionValue)
                {
                    CleanupFailure = ExceptionValue;
                }
                var DetachedFailure = AwaitFailure is not null && Lease.OwnsCancellation(AwaitFailure)
                    ? CleanupFailure
                    : AwaitFailure is not null && CleanupFailure is not null
                        ? new AggregateException(
                            "Native feature generation and its deferred cleanup both failed.",
                            AwaitFailure, CleanupFailure)
                        : AwaitFailure ?? CleanupFailure;
                if (DetachedFailure is not null)
                    Plugin.PLLLog.LogError(
                        $"Native feature generation failed after its map iterator stopped. {DetachedFailure}");
            }

            if (AwaitPending)
            {
                try
                {
                    Awaiter.OnCompleted(CompleteAwait);
                }
                catch
                {
                    AwaitPending = false;
                    throw;
                }
            }
            else CompleteAwait();

            var CancellationObserved = false;
            while (AwaitPending)
            {
                yield return null;
                CancellationObserved |= Lease.Cancelled;
            }
            if (AwaitFailure is not null) ExceptionPropagation.Throw(AwaitFailure);
            if (CancellationObserved) throw Lease.OwnedCancellation();
            Lease.RequireCurrent();
            Ownership.Towers.ValidateCompleted();
            Lease.RequireCurrent();
            var RandomScope = EnterNativeRandom(Grouper);
            try
            {
                FinalForce?.Complete(Lease.Epoch);
                if (Operation.InitializeFixedFlows is { } InitializeFlows)
                {
                    Physics.SyncTransforms();
                    InitializeFlows(NativePropSpawnerGeneration.TraceFlowSurface);
                }
            }
            finally
            {
                ExitNativeRandom(ref RandomScope);
            }
            Lease.RequireCurrent();
            ReapplyRockMaterials(Grouper);
            Lease.RequireCurrent();
            if (Operation.CompleteAuthoring is { } CompleteAuthoring)
            {
                var Scope = EnterNativeRandom(Grouper);
                try { CompleteAuthoring(); }
                finally { ExitNativeRandom(ref Scope); }
                Lease.RequireCurrent();
            }
            Ownership.Outputs.Complete();
            if (Operation.ActorGenerationOwner is { } ActorOwner)
                Ownership.Outputs.PublishActorIdentities(ActorOwner, Operation.GenerationSeed);
            try
            {
                ReleaseExternalCollisionScope();
                Lease.RequireCurrent();
                PlacementExecution.RebaseTrapWarningLines(FeatureRootSet);
            }
            catch (Exception Failure)
            {
                Exception? CleanupFailure = null;
                try { Cleanup(); }
                catch (Exception ExceptionValue) { CleanupFailure = ExceptionValue; }
                if (CleanupFailure is not null)
                    throw new AggregateException(
                        "Native trap-indicator refresh and generation cleanup both failed.",
                        Failure, CleanupFailure);
                throw;
            }
            Lease.RequireCurrent();
        }
        finally
        {
            Disposed = true;
            Cleanup();
        }
    }

    static void ReapplyRockMaterials(PropGrouper grouper)
    {
        foreach (var swapper in grouper.GetComponentsInChildren<RockMaterialSwapper>(true))
        {
            if (!swapper.isActiveAndEnabled || !Map.CurrentRun!.HookedGeneration.OwnedByOuter(grouper.transform, swapper.transform)) continue;
            foreach (var parent in swapper.parents)
            {
                if (!parent) continue;
                foreach (var renderer in parent.GetComponentsInChildren<MeshRenderer>(true))
                    if (!NativeFlowRuntime.OwnsRenderer(renderer) && !Map.CurrentRun!.SurfaceTextures.OwnsRenderer(renderer)
                        && !Map.CurrentRun.NativeMaterials.OwnsSlot(renderer, 0)
                        && Map.CurrentRun!.HookedGeneration.OwnedByOuter(grouper.transform, renderer.transform))
                        renderer.sharedMaterial = swapper.mat;
            }
        }
    }

    [HarmonyPatch(typeof(RockMaterialSwapper), "Start")]
    static class FlowMaterialOwnershipPatch
    {
        [HarmonyPrefix]
        static bool Prefix(RockMaterialSwapper __instance)
        {
            foreach (var Parent in __instance.parents)
            {
                if (!Parent) continue;
                foreach (var Renderer in Parent.GetComponentsInChildren<MeshRenderer>(true))
                    if (!NativeFlowRuntime.OwnsRenderer(Renderer) && Map.CurrentRun?.SurfaceTextures.OwnsRenderer(Renderer) != true
                        && Map.CurrentRun?.NativeMaterials.OwnsSlot(Renderer, 0) != true)
                        Renderer.sharedMaterial = __instance.mat;
            }
            return false;
        }
    }

    internal static bool IsCurrent(Transform root, long epoch) =>
        activeLease is { Cancelled: false } lease && lease.Epoch == epoch && OwnsRoot(lease, root);

    internal static bool HasLiveOperation => activeLease is not null;
    internal static TreePlatform[] CaptureOwnedTreePlatforms()
    {
        var Lease = activeLease ?? throw new InvalidOperationException("Platform capture requires the current generation lease.");
        Lease.RequireCurrent();
        var Platforms = new List<TreePlatform>();
        var Seen = new HashSet<TreePlatform>();
        foreach (var Root in Lease.Ownership.Roots)
            foreach (var Platform in Root.GetComponentsInChildren<TreePlatform>(true))
                if (Platform.gameObject.activeInHierarchy && Lease.Ownership.Contains(Platform.transform) && Seen.Add(Platform))
                    Platforms.Add(Platform);
        return Platforms.ToArray();
    }
    internal static bool OwnsGenerationOutput(Transform Value) =>
        activeLease is { Cancelled: false } Lease && Lease.Ownership.Contains(Value);

    internal static NativeOutputInventory OutputInventory(PropGrouper Grouper) => OwnedFeatures[Grouper].Outputs;

    internal static NativeSpawnReservation? BeginNativeSpawn(GameObject Prefab, bool TrackFootprint = true)
    {
        var Lease = activeLease;
        if (Lease is null) return null;
        RequireCurrentLease(Lease);
        return Lease.Ownership.Outputs.Begin(Prefab, TrackFootprint);
    }

    [HarmonyPatch(typeof(HelperFunctions), nameof(HelperFunctions.InstantiatePrefab),
        new[] { typeof(GameObject), typeof(Transform) })]
    static class InstantiateCapturePatch
    {
        [HarmonyPrefix]
        static void Prefix(GameObject __0, Transform __1, out NativeSpawnReservation? __state) =>
            __state = __1 && OwnsGenerationOutput(__1) && !NativeFlowRuntime.TryGetOwner(__1, out _)
                ? BeginNativeSpawn(__0) : null;

        [HarmonyFinalizer]
        static Exception? Finalizer(Transform __1, GameObject __result,
            NativeSpawnReservation? __state, Exception? __exception)
        {
            if (__state is null) return __exception;
            try
            {
                if (__exception is not null) return __exception;
                if (!OwnsGenerationOutput(__1)) return MapRun.FeatureWriter.Cancelled();
                __state.Complete(__result);
                return null;
            }
            finally { __state.Dispose(); }
        }
    }

    static GameObject InstantiateNativeOutput(
        GameObject Prefab, Vector3 Position, Quaternion Rotation, Transform Parent)
    {
        var Lease = activeLease;
        if (Lease is null || !Lease.Ownership.Contains(Parent) || NativeFlowRuntime.TryGetOwner(Parent, out _))
            return UnityEngine.Object.Instantiate(Prefab, Position, Rotation, Parent);
        RequireCurrentLease(Lease);
        using var Reservation = Lease.Ownership.Outputs.Begin(Prefab, !PropInteractionRuntime.OwnsNativeGeometry(Parent));
        var Output = UnityEngine.Object.Instantiate(Prefab, Position, Rotation, Parent);
        var Transferred = false;
        try
        {
            RequireCurrentLease(Lease);
            Reservation.Complete(Output);
            Transferred = true;
            return Output;
        }
        finally
        {
            if (!Transferred && Output) DestroyNativeOutput(Output);
        }
    }

    internal static void DestroyNativeOutput(UnityEngine.Object Output)
    {
        if (Output is not GameObject Root || !Root)
        {
            UnityEngine.Object.DestroyImmediate(Output);
            return;
        }
        List<Exception>? Failures = null;
        var Components = Array.Empty<Component>();
        CleanupOperations.Capture(ref Failures, () =>
        {
            Components = Root.GetComponentsInChildren<Component>(true);
            foreach (var Component in Components)
                if (Component is Renderer Renderer)
                    CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireRenderer(Renderer));
                else if (Component is ShaderEffect Effect)
                    CleanupOperations.Capture(ref Failures, () => NativeFogRenderRuntime.RetireEffect(Effect));
            foreach (var Component in Components)
                if (Component is PllNativeFlowCondition Bridge)
                    CleanupOperations.Capture(ref Failures, Bridge.Release);
                else if (Component is SplineExtrude Extrude && RawSplines.TryGetValue(Extrude, out var Resource))
                    CleanupOperations.Capture(ref Failures, Resource.Dispose);
        });
        CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.DestroyImmediate(Output));
        foreach (var Component in Components)
            if (Component is PllGeneratedPrefabIdentity Identity)
                CleanupOperations.Capture(ref Failures, Identity.Release);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null) throw new AggregateException("Native output cleanup failed.", Failures);
    }

    [HarmonyPatch]
    static class NativeOutputCapturePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(LavaRiver), "AddLights");
            yield return AccessTools.DeclaredMethod(typeof(LavaRiver), "addAlongSpline");
            yield return AccessTools.DeclaredMethod(typeof(LavaRiver), "SpawnItemsAlongSpline");
            yield return AccessTools.DeclaredMethod(typeof(LavaRiver), nameof(LavaRiver.Clear));
            yield return AccessTools.DeclaredMethod(typeof(PropSpawner), "Spawn", new[] { typeof(PropSpawner.SpawnData) });
            yield return AccessTools.DeclaredMethod(typeof(PropSpawner), nameof(PropSpawner.Clear));
            yield return AccessTools.DeclaredMethod(typeof(DecorSpawner), nameof(DecorSpawner.Clear));
            yield return AccessTools.DeclaredMethod(typeof(DesertRockSpawner), nameof(DesertRockSpawner.Clear));
            yield return AccessTools.DeclaredMethod(typeof(Peak.TowerStacker), "delete");
            yield return AccessTools.DeclaredMethod(typeof(JungleVine), "PickTreePlatforms");
            yield return AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(
                typeof(Peak.LevelGeneration.PhysicsSimStep), nameof(Peak.LevelGeneration.PhysicsSimStep.Execute)));
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Original = ((Func<GameObject, Vector3, Quaternion, Transform, GameObject>)UnityEngine.Object.Instantiate).Method;
            var Replacement = ((Func<GameObject, Vector3, Quaternion, Transform, GameObject>)InstantiateNativeOutput).Method;
            var Destroy = ((Action<UnityEngine.Object>)UnityEngine.Object.DestroyImmediate).Method;
            var Retire = ((Action<UnityEngine.Object>)DestroyNativeOutput).Method;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.opcode == OpCodes.Call && Instruction.operand is MethodInfo Method)
                {
                    if (Method == Original) Instruction.operand = Replacement;
                    else if (Method == Destroy) Instruction.operand = Retire;
                }
                yield return Instruction;
            }
        }
    }

    internal static NativeFlowRuntime[] BindGeneratedFlows(GameObject Prefab, GameObject Instance)
    {
        var Lease = activeLease
            ?? throw new InvalidOperationException("Generated flow binding has no native generation lease.");
        RequireCurrentLease(Lease);
        return Lease.BindGeneratedFlows?.Invoke(Prefab, Instance) ?? Array.Empty<NativeFlowRuntime>();
    }

    static VariationSelection[] Configure(
        GenerationOperation.Configuration Configuration, GenerationLease Lease)
    {
        var Root = Lease.Grouper.transform;
        var NativeVariations = Configuration.ApplyNativeVariations?.Invoke()
            ?? (SelectorRequired: false, ForcedSwappers: Array.Empty<VariationSwapper>());
        if (Configuration.RequiresNativeVariantSelection || NativeVariations.SelectorRequired)
        {
            var Selector = Root.gameObject.AddComponent<VariantObjectSelector>();
            try { Selector.RunStep(); }
            finally { UnityEngine.Object.DestroyImmediate(Selector); }
            Lease.RequireCurrent();
        }
        var Variations = Configuration.Resolution?.Prepare(Root);
        var InactiveVariationSelectors = Variations?.Selectors ?? Array.Empty<VariantObjectSelector>();
        foreach (var Step in Root.GetComponentsInChildren<IGenConfigStep>(true))
        {
            Lease.RequireCurrent();
            var Component = (Component)Step;
            var FeatureRoot = Lease.Roots.DeepestRoot(Component.transform, out _);
            if (!FeatureRoot || !Lease.Ownership.Contains(Component.transform)) continue;
            if (NativeCollision.IsGeneratedOutput(Root, Component.transform)) continue;
            if (!Component.gameObject.activeInHierarchy
                && (Step is not VariantObjectSelector Selector
                    || Array.IndexOf(InactiveVariationSelectors, Selector) < 0)) continue;
            if (Step is VariationSwapper Swapper
                && (Array.IndexOf(NativeVariations.ForcedSwappers, Swapper) >= 0 || !CanConfigure(FeatureRoot, Swapper))) continue;
            Step.RunStep();
            if (Step is ChildMaterialSwapper Material
                && !Material.forceDisable && Material.chosenTheme != null)
            {
                Lease.RequireCurrent();
                Material.ApplyRandomTheme();
            }
        }
        Lease.RequireCurrent();
        return Variations is null
            ? CaptureNativeSelections(Root)
            : Variations.CaptureSelections();
    }

    static VariationSelection[] CaptureNativeSelections(Transform Root)
    {
        var Result = new List<VariationSelection>();
        foreach (var Swapper in Root.GetComponentsInChildren<VariationSwapper>(true))
        {
            if (!Swapper.gameObject.activeInHierarchy || !CanConfigure(Root, Swapper)) continue;
            foreach (var Variation in Swapper.Variations)
                if (Variation.parent.activeSelf)
                    Result.Add(new VariationSelection(
                        DeterministicObjectIdentity.RelativeComponentPath(Swapper, Root, 0),
                        DeterministicObjectIdentity.RelativeComponentPath(Variation.parent.transform, Root, 0)));
        }
        foreach (var Variant in Root.GetComponentsInChildren<VariantObject>(true))
        {
            if (!Variant || !Variant.gameObject.activeSelf) continue;
            Result.Add(new VariationSelection(
                ((int)Variant.group).ToString(CultureInfo.InvariantCulture),
                DeterministicObjectIdentity.RelativeComponentPath(Variant, Root, 0)));
        }
        Result.Sort((Left, Right) =>
        {
            var Order = string.CompareOrdinal(Left.RootPath, Right.RootPath);
            return Order != 0 ? Order : string.CompareOrdinal(Left.Id, Right.Id);
        });
        return Result.ToArray();
    }
    static bool CanConfigure(Transform root, VariationSwapper swapper)
    {
        if (swapper.Variations is not { Length: > 1 } variations) return false;
        foreach (var variation in variations)
        {
            if (variation is null || !variation.parent) return false;
            var parent = variation.parent.transform;
            if (parent != root && !parent.IsChildOf(root)) return false;
        }
        return true;
    }


    internal static void Reset()
    {
        activeLease?.Cancel();
        GeneratedRpcDeferral.Reset();
    }

    internal static void ReleaseOwnership()
    {
        if (activeLease is not null)
            throw new InvalidOperationException("Native feature ownership cannot be released while generation is active.");
        List<Exception>? Failures = null;
        foreach (var Ownership in OwnedFeatures.Values)
            CleanupOperations.Capture(ref Failures, Ownership.Outputs.Dispose);
        OwnedFeatures.Clear(); GeneratedGeometryBudget.ThrowFailures(Failures);
    }

    internal static void Clear(PropGrouper grouper)
    {
        Map.CurrentRun?.PropInteractions.Retire(grouper.gameObject);
        Map.CurrentRun?.SurfaceTextures.Retire(grouper.gameObject);
        Map.CurrentRun?.NativeMaterials.Retire(grouper.gameObject);
        Map.CurrentRun?.NativeSpawners.Retire(grouper.gameObject, GeneratedOnly: true);
        Map.CurrentRun?.ClimbingFixtures.Retire(grouper.gameObject);
        Map.CurrentRun?.NativeSpawns.RetireOutputs(grouper.gameObject);
        if (!OwnedFeatures.TryGetValue(grouper, out var Ownership))
        {
            grouper.ClearAll();
            return;
        }
        Clear(Ownership);
        Ownership.Outputs.Dispose();
        OwnedFeatures.Remove(grouper);
    }

    internal static void ClearDeferredOutput(Transform Root)
    {
        foreach (var Step in Root.GetComponentsInChildren<LevelGenStep>(true))
            if (Step && Step is not Peak.TowerStacker
                && Map.CurrentRun!.HookedGeneration.OwnedByOuter(Root, Step.transform)) Step.Clear();
    }

    internal static Transform? ActiveFeatureRoot(Transform Value) =>
        activeLease is { } Lease ? Lease.Roots.DeepestRoot(Value, out _) : null;

    internal static bool IsActiveDescendant(Transform Value) =>
        ActiveFeatureRoot(Value) is { } Root && Value.GetInstanceID() != Root.GetInstanceID();

    static bool OwnsRoot(GenerationLease Lease, Transform Value) =>
        Lease.Roots.DeepestRoot(Value, out _) is { } Owner
        && Owner.GetInstanceID() == Value.GetInstanceID();

    static LevelGenStep[] OwnedSteps(PropGrouper Grouper)
    {
        var Lease = activeLease;
        if (Lease is null || !ReferenceEquals(Lease.Grouper, Grouper))
            return Grouper.GetComponentsInChildren<LevelGenStep>();
        var Ordinary = new List<LevelGenStep>();
        var Critical = Lease.CriticalRanks is null
            ? null : new List<(int Rank, int Index, LevelGenStep Step)>();
        var Seen = new HashSet<int>();
        foreach (var Root in Lease.Roots)
        {
            if (!Root.gameObject.activeInHierarchy) continue;
            foreach (var Step in Root.GetComponentsInChildren<LevelGenStep>())
            {
                if (!Step || !Lease.Ownership.Contains(Step.transform) || !Seen.Add(Step.GetInstanceID())) continue;
                if (Step is Peak.TowerStacker Stacker
                    && !Lease.Ownership.Towers.RequiresGeneration(Stacker)) continue;
                if (Critical is not null && Lease.CriticalRanks!.TryGetValue(Step, out var Rank))
                    Critical.Add((Rank, Critical.Count, Step));
                else Ordinary.Add(Step);
            }
        }
        if (Critical is not { Count: > 0 }) return Ordinary.ToArray();
        Critical.Sort((Left, Right) =>
        {
            var Rank = Left.Rank.CompareTo(Right.Rank);
            return Rank != 0 ? Rank : Left.Index.CompareTo(Right.Index);
        });
        var Result = new LevelGenStep[Critical.Count + Ordinary.Count];
        for (var Index = 0; Index < Critical.Count; Index++) Result[Index] = Critical[Index].Step;
        Ordinary.CopyTo(Result, Critical.Count);
        return Result;
    }

    static ILevelGenFinalizer[] OwnedFinalizers(PropGrouper grouper)
    {
        var lease = activeLease;
        if (lease is null || !ReferenceEquals(lease.Grouper, grouper))
            return grouper.GetComponentsInChildren<ILevelGenFinalizer>();
        var result = new List<ILevelGenFinalizer>();
        var seen = new HashSet<int>();
        foreach (var root in lease.Roots)
        {
            if (!root.gameObject.activeInHierarchy) continue;
            foreach (var finalizer in root.GetComponentsInChildren<ILevelGenFinalizer>())
            {
                var component = (Component)finalizer;
                if (component && lease.Ownership.Contains(component.transform)
                    && seen.Add(component.GetInstanceID())) result.Add(finalizer);
            }
        }
        return result.ToArray();
    }

    static void Clear(FeatureOwnership Ownership)
    {
        var Steps = new List<LevelGenStep>();
        var Seen = new HashSet<int>();
        foreach (var Root in Ownership.Roots)
        {
            if (!Root) continue;
            foreach (var Step in Root.GetComponentsInChildren<LevelGenStep>(true))
            {
                if (!Step || !Ownership.Contains(Step.transform) || !Seen.Add(Step.GetInstanceID())
                    || Step is Peak.TowerStacker) continue;
                Steps.Add(Step);
            }
        }

        var Failures = new List<Exception>();
        foreach (var Step in Steps)
            try { if (Step) Step.Clear(); }
            catch (Exception Failure) { Failures.Add(Failure); }
        try { Ownership.Towers.ClearGenerated(); }
        catch (Exception Failure) { Failures.Add(Failure); }
        try { Ownership.Outputs.ClearSplineMeshes(); }
        catch (Exception Failure) { Failures.Add(Failure); }
        if (Failures.Count == 0) Ownership.Outputs.AfterClear();
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0)
            throw new AggregateException("Native feature output cleanup failed.", Failures);
    }

    static NativeRandomScope EnterNativeRandom(PropGrouper grouper)
    {
        var lease = activeLease;
        if (lease is null || !ReferenceEquals(lease.Grouper, grouper)) return default;
        lease.RequireCurrent();
        if (!lease.RandomHandoffReady) return default;
        if (lease.RandomDepth > 0)
        {
            lease.RandomDepth++;
            return new NativeRandomScope(lease, default);
        }
        var ambient = UnityEngine.Random.state;
        UnityEngine.Random.state = lease.GenerationRandomState;
        lease.RandomDepth = 1;
        return new NativeRandomScope(lease, ambient);
    }

    static void ExitNativeRandom(ref NativeRandomScope scope)
    {
        if (scope.Lease is not { } lease) return;
        scope.Lease = null;
        if (lease.RandomDepth <= 0)
            throw new InvalidOperationException("Native feature random-scope depth is invalid.");
        if (--lease.RandomDepth != 0) return;
        lease.GenerationRandomState = UnityEngine.Random.state;
        UnityEngine.Random.state = scope.Ambient;
    }

    static void RequireCurrentLease(GenerationLease Lease)
    {
        if (!ReferenceEquals(activeLease, Lease))
            throw Lease.OwnedCancellation();
        Lease.RequireCurrent();
    }

    static void PumpOutgoingDuringSynchronousGeneration(PropGrouper Grouper)
    {
        var Lease = activeLease;
        if (Lease is null || Lease.Cancelled || !Lease.RunSynchronously
            || !ReferenceEquals(Lease.Grouper, Grouper)
            || !PhotonNetwork.IsConnected || !PhotonNetwork.IsMessageQueueRunning
            || PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom is null) return;
        var Timestamp = Stopwatch.GetTimestamp();
        if (Timestamp - Lease.LastTransportPumpTimestamp < TransportPumpIntervalTicks) return;
        Lease.LastTransportPumpTimestamp = Timestamp;
        PhotonNetwork.NetworkingClient.LoadBalancingPeer.SendOutgoingCommands();
    }

    [HarmonyPatch(typeof(PropGrouper), nameof(PropGrouper.ShouldWaitForFrame), MethodType.Getter)]
    static class PropGrouperTransportPatch
    {
        [HarmonyPostfix]
        static void Postfix(PropGrouper __instance) =>
            PumpOutgoingDuringSynchronousGeneration(__instance);
    }

    [HarmonyPatch]
    static class PropGrouperRandomPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var RunAll = AccessTools.DeclaredMethod(
                typeof(PropGrouper), nameof(PropGrouper.RunAll), new[] { typeof(bool), typeof(bool) })
                ?? throw new MissingMethodException(typeof(PropGrouper).FullName, nameof(PropGrouper.RunAll));
            var RunAllMoveNext = BindAsyncMoveNext(RunAll);
            MethodInfo? Done = null;
            foreach (var Instruction in PatchProcessor.GetOriginalInstructions(RunAllMoveNext))
            {
                if (Instruction.operand is not MethodInfo Candidate
                    || Instruction.opcode != OpCodes.Call && Instruction.opcode != OpCodes.Callvirt
                    || Candidate.DeclaringType?.DeclaringType != typeof(PropGrouper)
                    || Candidate.ReturnType != RunAll.ReturnType) continue;
                if (Done is not null)
                    throw new AmbiguousMatchException("PropGrouper.RunAll calls multiple nested async factories.");
                Done = Candidate;
            }
            return new[]
            {
                RunAllMoveNext,
                BindAsyncMoveNext(Done ?? throw new MissingMethodException(
                    "PropGrouper.RunAll has no nested async completion factory."))
            };
        }

        static MethodBase BindAsyncMoveNext(MethodInfo Factory)
        {
            var StateMachine = Factory.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                ?? throw new MissingMethodException(
                    $"{Factory.DeclaringType?.FullName}.{Factory.Name} has no async state machine.");
            var MoveNext = AccessTools.DeclaredMethod(
                    StateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes)
                ?? throw new MissingMethodException(StateMachine.FullName, nameof(IAsyncStateMachine.MoveNext));
            if (MoveNext.IsStatic || MoveNext.ReturnType != typeof(void))
                throw new MissingMethodException($"{StateMachine.FullName}.MoveNext has an incompatible signature.");
            return MoveNext;
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase __originalMethod)
        {
            var codes = new List<CodeInstruction>(instructions);
            var stateMachine = __originalMethod.DeclaringType
                ?? throw new MissingMethodException("PropGrouper.RunAll async state-machine type was not found.");
            var state = AccessTools.DeclaredField(stateMachine, "<>1__state")
                ?? throw new MissingFieldException(stateMachine.FullName, "<>1__state");
            var owner = AccessTools.DeclaredField(stateMachine, "<>4__this")
                ?? throw new MissingFieldException(stateMachine.FullName, "<>4__this");
            var builder = AccessTools.DeclaredField(stateMachine, "<>t__builder")
                ?? throw new MissingFieldException(stateMachine.FullName, "<>t__builder");
            FieldInfo? grouper = null;
            var expectedStates = 4;
            var expectedAwaits = 4;
            if (owner.FieldType != typeof(PropGrouper))
            {
                grouper = AccessTools.DeclaredField(owner.FieldType, "<>4__this");
                if (grouper?.FieldType != typeof(PropGrouper))
                    throw new MissingFieldException("PropGrouper.RunAll async owner chain was not found.");
                expectedStates = 5;
                expectedAwaits = 5;
            }
            var Enter = ((Func<PropGrouper, NativeRandomScope>)EnterNativeRandom).Method;
            var Exit = ((NativeRandomScopeAction)ExitNativeRandom).Method;
            if (codes.Count < 8
                || codes[0].opcode != OpCodes.Ldarg_0
                || codes[1].opcode != OpCodes.Ldfld || !Equals(codes[1].operand, state)
                || codes[2].opcode != OpCodes.Stloc_0
                || codes[3].opcode != OpCodes.Ldarg_0
                || codes[4].opcode != OpCodes.Ldfld || !Equals(codes[4].operand, owner)
                || codes[5].opcode != OpCodes.Stloc_1
                || codes[6].opcode != OpCodes.Ldloc_0
                || codes[6].blocks.Count != 1
                || codes[6].blocks[0].blockType != ExceptionBlockType.BeginExceptionBlock
                || codes[7].opcode != OpCodes.Switch
                || codes[7].operand is not Label[] states || states.Length != expectedStates)
                throw new MissingMethodException("PropGrouper.RunAll async entry shape was not found.");

            var setException = -1;
            var setResult = -1;
            var awaits = new List<int>();
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].operand is not MethodInfo method
                    || method.DeclaringType != builder.FieldType) continue;
                switch (method.Name)
                {
                    case "AwaitOnCompleted":
                        awaits.Add(i);
                        break;
                    case "SetException":
                        if (setException >= 0)
                            throw new AmbiguousMatchException("PropGrouper.RunAll SetException call is ambiguous.");
                        setException = i;
                        break;
                    case "SetResult":
                        if (setResult >= 0)
                            throw new AmbiguousMatchException("PropGrouper.RunAll SetResult call is ambiguous.");
                        setResult = i;
                        break;
                }
            }
            if (setException < 3 || setResult < 2 || setResult + 1 != codes.Count - 1
                || codes[setException - 3].opcode != OpCodes.Ldarg_0
                || codes[setException - 2].opcode != OpCodes.Ldflda
                || !Equals(codes[setException - 2].operand, builder)
                || codes[setException - 1].opcode != OpCodes.Ldloc_S
                || codes[setException + 1].opcode != OpCodes.Leave
                || codes[setException + 1].operand is not Label finished
                || codes[setResult - 2].opcode != OpCodes.Ldarg_0
                || codes[setResult - 1].opcode != OpCodes.Ldflda
                || !Equals(codes[setResult - 1].operand, builder)
                || codes[setResult + 1].opcode != OpCodes.Ret
                || !codes[setResult + 1].labels.Contains(finished)
                || awaits.Count != expectedAwaits)
                throw new MissingMethodException("PropGrouper.RunAll async completion shape was not found.");
            foreach (var awaitCall in awaits)
                if (awaitCall + 1 >= codes.Count
                    || codes[awaitCall + 1].opcode != OpCodes.Leave
                    || codes[awaitCall + 1].operand is not Label suspended
                    || !codes[setResult + 1].labels.Contains(suspended))
                    throw new MissingMethodException("PropGrouper.RunAll async suspension shape was not found.");

            var scope = generator.DeclareLocal(typeof(NativeRandomScope));

            void InsertExit(int index, bool moveLabels)
            {
                var load = new CodeInstruction(OpCodes.Ldloca, scope);
                if (moveLabels)
                {
                    load.labels.AddRange(codes[index].labels);
                    codes[index].labels.Clear();
                }
                codes.InsertRange(index, new CodeInstruction[]
                {
                    load,
                    new(OpCodes.Call, Exit)
                });
            }

            InsertExit(setResult + 1, true);
            InsertExit(setResult - 2, false);
            InsertExit(setException - 3, false);

            var enterStart = new CodeInstruction(OpCodes.Ldloc_1);
            enterStart.blocks.AddRange(codes[6].blocks);
            codes[6].blocks.Clear();
            var entry = new List<CodeInstruction>
            {
                enterStart
            };
            if (grouper is not null) entry.Add(new CodeInstruction(OpCodes.Ldfld, grouper));
            entry.Add(new CodeInstruction(OpCodes.Call, Enter));
            entry.Add(new CodeInstruction(OpCodes.Stloc, scope));
            codes.InsertRange(6, entry);
            if (grouper is null)
            {
                var Replacement = ((Func<PropGrouper, LevelGenStep[]>)OwnedSteps).Method;
                var replaced = false;
                foreach (var code in codes)
                {
                    if (code.operand is not MethodInfo method || !method.IsGenericMethod
                        || method.Name != nameof(Component.GetComponentsInChildren)
                        || method.GetParameters().Length != 0
                        || method.GetGenericArguments() is not { Length: 1 } arguments
                        || arguments[0] != typeof(LevelGenStep)) continue;
                    if (replaced)
                        throw new AmbiguousMatchException("PropGrouper.RunAll LevelGenStep discovery is ambiguous.");
                    code.opcode = OpCodes.Call;
                    code.operand = Replacement;
                    replaced = true;
                }
                if (!replaced)
                    throw new MissingMethodException("PropGrouper.RunAll LevelGenStep discovery was not found.");
            }
            else
            {
                var Replacement = ((Func<PropGrouper, ILevelGenFinalizer[]>)OwnedFinalizers).Method;
                var replaced = false;
                foreach (var code in codes)
                {
                    if (code.operand is not MethodInfo method || !method.IsGenericMethod
                        || method.Name != nameof(Component.GetComponentsInChildren)
                        || method.GetParameters().Length != 0
                        || method.GetGenericArguments() is not { Length: 1 } arguments
                        || arguments[0] != typeof(ILevelGenFinalizer)) continue;
                    if (replaced)
                        throw new AmbiguousMatchException("PropGrouper.RunAll finalizer discovery is ambiguous.");
                    code.opcode = OpCodes.Call;
                    code.operand = Replacement;
                    replaced = true;
                }
                if (!replaced)
                    throw new MissingMethodException("PropGrouper.RunAll finalizer discovery was not found.");
            }
            for (var i = codes.Count - 1; i >= 0; i--)
            {
                var Code = codes[i];
                if (Code.opcode != OpCodes.Leave && Code.opcode != OpCodes.Leave_S) continue;
                if (Code.blocks.Count != 0)
                {
                    var Boundary = new CodeInstruction(OpCodes.Nop);
                    Boundary.blocks.AddRange(Code.blocks);
                    Code.blocks.Clear();
                    codes.InsertRange(i + 1, new[] { new CodeInstruction(OpCodes.Nop), Boundary });
                }
                else if (i + 1 < codes.Count && codes[i + 1].blocks.Count != 0)
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Nop));
            }
            return codes;
        }
    }

    [HarmonyPatch(typeof(PropGrouper), nameof(PropGrouper.ClearAll))]
    static class ClearPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PropGrouper __instance)
        {
            var lease = activeLease;
            if (lease is null || !ReferenceEquals(lease.Grouper, __instance)) return true;
            Clear(lease.Ownership);
            lease.AfterClear?.Invoke();
            return false;
        }
    }

    [HarmonyPatch(typeof(PropGrouper), nameof(PropGrouper.RunAll))]
    static class GenerationPatch
    {
        [HarmonyPrefix]
        static void Prefix() => MapPublication.SuppressOriginals();
    }

}
