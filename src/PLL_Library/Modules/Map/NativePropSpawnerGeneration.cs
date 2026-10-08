using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class NativePropSpawnerGeneration
{
    const int InitialNativeLineCheckHits = 64;
    const int MaximumNativeLineCheckHits = 1 << 16;

    sealed class ColliderReferenceComparer : IEqualityComparer<Collider>
    {
        internal static ColliderReferenceComparer Instance { get; } = new();

        public bool Equals(Collider? Left, Collider? Right) => ReferenceEquals(Left, Right);
        public int GetHashCode(Collider Value) => RuntimeHelpers.GetHashCode(Value);
    }

    enum ColliderScope : byte
    {
        Rejected,
        CollisionRoot,
        CollisionFixture,
        OccurrenceSupport,
        SpawnerSupport
    }

    readonly struct ColliderClassification
    {
        internal ColliderClassification(
            Transform? Owner, int Rank, ColliderScope Scope)
        {
            this.Owner = Owner;
            this.Rank = Rank;
            this.Scope = Scope;
        }

        internal Transform? Owner { get; }
        internal int Rank { get; }
        internal ColliderScope Scope { get; }
    }

    sealed class Generation : IDisposable
    {
        readonly NativeFeatureGeneration.FeatureRoots Roots;
        readonly NativeFeatureGeneration.FeatureRoots CollisionRoots;
        readonly Func<bool> CancellationRequestedCapability;
        readonly Action RequireCurrentGeneration;
        Transform? SpawnerSupportRoot;
        Transform? ProxyRoot;
        int? OccurrenceSupportId;
        int[]? SpawnerSupportIds;
        RaycastHit[]? LineCheckBuffer;
        List<Collider>? ComponentColliders;
        readonly Dictionary<Collider, ColliderClassification> ColliderClassifications = new(ColliderReferenceComparer.Instance);
        float GenerationSliceStarted;

        internal Generation(
            NativeFeatureGeneration.FeatureRoots FeatureRoots,
            NativeFeatureGeneration.FeatureRoots CollisionRoots,
            PlacementFrame PlacementFrame, bool NoSkyProps,
            Func<bool> IsCancellationRequested, Action RequireCurrent)
        {
            Roots = FeatureRoots ?? throw new ArgumentNullException(nameof(FeatureRoots));
            this.CollisionRoots = CollisionRoots ?? throw new ArgumentNullException(nameof(CollisionRoots));
            CancellationRequestedCapability = IsCancellationRequested
                ?? throw new ArgumentNullException(nameof(IsCancellationRequested));
            RequireCurrentGeneration = RequireCurrent
                ?? throw new ArgumentNullException(nameof(RequireCurrent));
            Placement = PlacementFrame;
            this.NoSkyProps = NoSkyProps;
            GenerationSliceStarted = Time.realtimeSinceStartup;
        }

        internal PlacementFrame Placement { get; }
        internal bool NoSkyProps { get; }
        internal bool Cancelled => CancellationRequestedCapability();

        internal bool Owns(Transform Candidate) =>
            Roots.DeepestRoot(Candidate, out _) is not null;

        internal RaycastHit[] LineCheckHits =>
            LineCheckBuffer ??= new RaycastHit[InitialNativeLineCheckHits];

        internal bool GrowLineCheckHits()
        {
            var Hits = LineCheckHits;
            if (Hits.Length >= MaximumNativeLineCheckHits) return false;
            LineCheckBuffer = new RaycastHit[Math.Min(Hits.Length * 2, MaximumNativeLineCheckHits)];
            return true;
        }

        internal void RegisterExternalSupports(
            Transform SpawnerRoot, Transform ProxyRoot, Collider OccurrenceSupport,
            IReadOnlyList<Collider> SpawnerSupports)
        {
            if (SpawnerSupportRoot || this.ProxyRoot || OccurrenceSupportId.HasValue
                || SpawnerSupportIds is not null)
                throw new InvalidOperationException("Native external collision was registered more than once.");
            if (CollisionRoots.DeepestRoot(SpawnerRoot, out _) is null)
                throw new InvalidOperationException(
                    "Native external collision belongs to a foreign collision root.");
            if (!ProxyRoot || !OccurrenceSupport)
                throw new InvalidOperationException("Native external collision registration is invalid.");
            var SharedId = OccurrenceSupport.GetInstanceID();
            var SupportIds = new int[SpawnerSupports.Count];
            var SupportCount = 0;
            foreach (var Support in SpawnerSupports)
            {
                if (!Support)
                    throw new InvalidOperationException("Native external collision registration is invalid.");
                var SupportId = Support.GetInstanceID();
                if (SupportId == SharedId)
                    throw new InvalidOperationException("Native external collision registration is invalid.");
                for (var i = 0; i < SupportCount; i++)
                    if (SupportIds[i] == SupportId)
                        throw new InvalidOperationException("Native external collision registration is invalid.");
                SupportIds[SupportCount++] = SupportId;
            }
            ColliderClassifications.Clear();
            SpawnerSupportRoot = SpawnerRoot;
            this.ProxyRoot = ProxyRoot;
            OccurrenceSupportId = SharedId;
            SpawnerSupportIds = SupportIds;
        }

        internal Transform ResolveCollisionRoot(
            Transform Spawner, out bool UsesExternalSupport)
        {
            var Root = CollisionRoots.DeepestRoot(Spawner, out _)
                ?? throw new InvalidOperationException(
                    "A native PropSpawner has no declared collision root.");
            var SupportRoot = SpawnerSupportRoot;
            UsesExternalSupport = SupportRoot
                && TransformOwnership.Contains(SupportRoot, Spawner);
            return Root;
        }

        internal bool TryOwn(
            Transform SpawnerCollisionRoot, bool SpawnerUsesExternalSupport,
            Collider Collider, out int Rank, out Transform Owner, bool AllowFixtures = false)
        {
            if (!Collider)
            {
                Rank = -1;
                Owner = null!;
                return false;
            }
            if (!ColliderClassifications.TryGetValue(Collider, out var Classification))
            {
                var Value = Collider.transform;
                if (Collider.attachedRigidbody
                    || Value.GetComponentInParent<PllBreakablePropAuthoring>(true)
                    || Value.GetComponentInParent<PllSwingingPropAuthoring>(true)
                    || Value.GetComponentInParent<PllVineAuthoring>(true)
                    || Value.GetComponentInParent<PllRestPointAuthoring>(true)
                    || Value.GetComponentInParent<PllHangingRopeAuthoring>(true))
                    Classification = new ColliderClassification(
                        null, -1, ColliderScope.Rejected);
                else
                {
                    var ColliderId = Collider.GetInstanceID();
                    if (OccurrenceSupportId == ColliderId)
                        Classification = new ColliderClassification(
                            Value, CollisionRoots.Count,
                            ColliderScope.OccurrenceSupport);
                    else if (TrySpawnerSupport(ColliderId, out var SupportRank))
                        Classification = new ColliderClassification(
                            Value, CollisionRoots.Count + 1 + SupportRank,
                            ColliderScope.SpawnerSupport);
                    else if (ProxyRoot
                        && TransformOwnership.Contains(ProxyRoot, Value))
                        Classification = new ColliderClassification(
                            null, -1, ColliderScope.Rejected);
                    else
                    {
                        var Root = CollisionRoots.DeepestRoot(Value, out var RootRank);
                        if (Root && IsFixture(Value, Root))
                            Classification = new ColliderClassification(
                                Root, RootRank, ColliderScope.CollisionFixture);
                        else if (Root)
                            Classification = new ColliderClassification(
                                Root, RootRank, ColliderScope.CollisionRoot);
                        else
                            Classification = new ColliderClassification(
                                null, -1, ColliderScope.Rejected);
                    }
                }
                ColliderClassifications.Add(Collider, Classification);
            }

            Rank = Classification.Rank;
            Owner = Classification.Owner!;
            if (!Owner) return false;
            return Classification.Scope switch
            {
                ColliderScope.CollisionRoot =>
                    Owner.GetInstanceID() == SpawnerCollisionRoot.GetInstanceID(),
                ColliderScope.CollisionFixture => AllowFixtures
                    && Owner.GetInstanceID() == SpawnerCollisionRoot.GetInstanceID(),
                ColliderScope.OccurrenceSupport => true,
                ColliderScope.SpawnerSupport => SpawnerUsesExternalSupport,
                _ => throw new InvalidOperationException(
                    "An accepted native collider has no authorization scope.")
            };

            bool TrySpawnerSupport(int ColliderId, out int SupportRank)
            {
                var SupportIds = SpawnerSupportIds;
                if (SupportIds is not null)
                    for (var i = 0; i < SupportIds.Length; i++)
                    {
                        if (SupportIds[i] != ColliderId) continue;
                        SupportRank = i;
                        return true;
                    }
                SupportRank = -1;
                return false;
            }
        }

        internal bool TryOwnOccurrenceSupport(
            Transform SpawnerRoot, bool External, Collider Collider, out int Rank, out Transform Owner,
            bool AllowFixtures = false)
        {
            var SupportRoot = CollisionRoots.DeepestRoot(Collider.transform, out _) ?? SpawnerRoot;
            return TryOwn(SupportRoot, External, Collider, out Rank, out Owner, AllowFixtures);
        }

        static bool IsFixture(Transform Value, Transform Root)
        {
            var RootId = Root.GetInstanceID();
            for (var Node = Value; ; Node = Node.parent)
            {
                if (Node.GetComponent<global::Luggage>()
                    || Node.GetComponent<Peak.ScoutStatue>()
                    || Node.GetComponent<Peak.PropSpawner_AmuletStatues>()) return true;
                if (Node.GetInstanceID() == RootId) return false;
            }
        }

        internal int CompareComponents(Collider Left, Collider Right)
        {
            var LeftId = Left.GetInstanceID();
            var RightId = Right.GetInstanceID();
            if (LeftId == RightId) return 0;
            var Components = ComponentColliders ??= new List<Collider>();
            Left.transform.GetComponents(Components);
            var LeftIndex = -1;
            var RightIndex = -1;
            for (var i = 0; i < Components.Count; i++)
            {
                var ComponentId = Components[i].GetInstanceID();
                if (ComponentId == LeftId) LeftIndex = i;
                if (ComponentId == RightId) RightIndex = i;
            }
            Components.Clear();
            if (LeftIndex < 0 || RightIndex < 0)
                throw new InvalidOperationException("A native line-check collider left its component owner.");
            return LeftIndex.CompareTo(RightIndex);
        }

        internal bool SliceExpired() =>
            !Application.isBatchMode
            && Time.realtimeSinceStartup - GenerationSliceStarted >= 1f / 30f;

        internal void RefreshSlice() =>
            GenerationSliceStarted = Time.realtimeSinceStartup;

        internal void RequireCurrent()
        {
            if (!ReferenceEquals(ActiveGeneration, this))
                throw MapRun.FeatureWriter.Cancelled();
            RequireCurrentGeneration();
        }

        public void Dispose()
        {
            ColliderClassifications.Clear();
            ComponentColliders?.Clear();
            if (ReferenceEquals(ActiveGeneration, this)) ActiveGeneration = null;
        }
    }

    static readonly MethodInfo TryToSpawnMethod = AccessTools.DeclaredMethod(
        typeof(PropSpawner), "TryToSpawn", new[] { typeof(int) })
        ?? throw new MissingMethodException(typeof(PropSpawner).FullName, "TryToSpawn");
    static readonly MethodInfo NativeLineCheckMethod =
        ((Func<Vector3, Vector3, HelperFunctions.LayerType, float,
            QueryTriggerInteraction, RaycastHit>)HelperFunctions.LineCheck).Method;
    static Generation? ActiveGeneration;

    internal static void RequireMountedToolHit(Transform Spawner, RaycastHit Hit)
    {
        var Generation = ActiveGeneration
            ?? throw new InvalidOperationException("Mounted tool placement requires the current generation lease.");
        Generation.RequireCurrent();
        if (!Generation.Owns(Spawner) || !Hit.collider || Hit.collider.isTrigger)
            throw new InvalidOperationException("Mounted tool placement requires an owned terrain hit.");
        var Root = Generation.ResolveCollisionRoot(Spawner, out var External);
        if (!Generation.TryOwnOccurrenceSupport(Root, External, Hit.collider, out _, out _))
            throw new InvalidOperationException("Mounted tool placement cannot attach to foreign or dynamic support.");
    }

    internal static RaycastHit TraceFlowSurface(
        Transform Source, Vector3 From, Vector3 To, HelperFunctions.LayerType LayerType,
        float Radius, QueryTriggerInteraction TriggerInteraction)
    {
        var Generation = ActiveGeneration
            ?? throw new InvalidOperationException("Native flow tracing has no generation lease.");
        Generation.RequireCurrent();
        if (!Generation.Owns(Source) || Radius != 0f)
            throw new InvalidOperationException("Native flow tracing requires an owned point-ray source.");
        var Direction = To - From;
        var Distance = Direction.magnitude;
        if (Distance <= 0f) return default;
        var SourceRoot = Generation.ResolveCollisionRoot(Source, out var External);
        int Count;
        while (true)
        {
            var Hits = Generation.LineCheckHits;
            Count = Physics.RaycastNonAlloc(new Ray(From, Direction), Hits, Distance,
                HelperFunctions.GetMask(LayerType).value, TriggerInteraction);
            if (Count < Hits.Length) break;
            if (!Generation.GrowLineCheckHits())
                throw new InvalidOperationException(
                    $"A native flow ray reached its {MaximumNativeLineCheckHits}-hit limit.");
        }
        var Buffer = Generation.LineCheckHits;
        var Result = default(RaycastHit);
        var NearestRank = -1;
        Transform? NearestOwner = null;
        for (var i = 0; i < Count; i++)
        {
            var Hit = Buffer[i]; var Collider = Hit.collider;
            if (!Collider || NativeFlowRuntime.TryGetOwner(Collider.transform, out var Flow)
                    && Flow.SourceTransform == Source
                || !Generation.TryOwnOccurrenceSupport(SourceRoot, External,
                    Collider, out var Rank, out var Owner, AllowFixtures: true)) continue;
            if (!Result.collider || CompareOwnedHits(Generation, Hit, Rank, Owner,
                Result, NearestRank, NearestOwner!) < 0)
            {
                Result = Hit; NearestRank = Rank; NearestOwner = Owner;
            }
        }
        return Result;
    }

    internal static IDisposable EnterGeneration(
        NativeFeatureGeneration.FeatureRoots FeatureRoots,
        NativeFeatureGeneration.FeatureRoots CollisionRoots,
        PlacementFrame PlacementFrame, bool NoSkyProps,
        Func<bool> IsCancellationRequested, Action RequireCurrentGeneration)
    {
        if (ActiveGeneration is not null)
            throw new InvalidOperationException("Native PropSpawner generation was entered more than once.");
        var Value = new Generation(
            FeatureRoots, CollisionRoots, PlacementFrame, NoSkyProps,
            IsCancellationRequested, RequireCurrentGeneration);
        ActiveGeneration = Value;
        return Value;
    }

    internal static void RegisterExternalSupports(
        Transform SpawnerRoot, Transform ProxyRoot, Collider OccurrenceSupport,
        IReadOnlyList<Collider> SpawnerSupports)
    {
        var Value = ActiveGeneration
            ?? throw new InvalidOperationException("Native external collision has no generation lease.");
        Value.RequireCurrent();
        Value.RegisterExternalSupports(SpawnerRoot, ProxyRoot, OccurrenceSupport, SpawnerSupports);
    }

    static RaycastHit LineCheckForNativePropSpawner(
        Vector3 From, Vector3 To, HelperFunctions.LayerType LayerType,
        float Radius, QueryTriggerInteraction TriggerInteraction, PropSpawner Spawner)
    {
        if (Radius != 0f)
            return HelperFunctions.LineCheck(
                From, To, LayerType, Radius, TriggerInteraction);
        var Generation = ActiveGeneration;
        var EntityZone = Generation is not null && Spawner.GetComponentInParent<PllEntityZoneAuthoring>();
        var OwnedOnly = Generation is { Cancelled: false }
            && (Generation.NoSkyProps || EntityZone) && Generation.Owns(Spawner.transform);
        var Excluded = NativeTerrainMapProxy.ExcludedSupport(Spawner);
        if (!OwnedOnly && !Excluded)
            return HelperFunctions.LineCheck(
                From, To, LayerType, Radius, TriggerInteraction);
        if (Generation is null)
            throw new InvalidOperationException(
                "Native line-check filtering lost its generation lease.");
        return FilterLineCheck(Generation, Spawner, OwnedOnly, From, To, LayerType, Excluded, EntityZone);
    }

    static RaycastHit FilterLineCheck(
        Generation Generation, PropSpawner Spawner, bool OwnedOnly,
        Vector3 From, Vector3 To, HelperFunctions.LayerType LayerType,
        Collider? Excluded, bool EntityZone)
    {
        var Result = default(RaycastHit);
        var Direction = To - From;
        var Distance = Direction.magnitude;
        if (Distance <= 0f) return Result;
        int Count;
        while (true)
        {
            var Hits = Generation.LineCheckHits;
            Count = Physics.RaycastNonAlloc(
                new Ray(From, Direction), Hits, Distance,
                HelperFunctions.GetMask(LayerType).value,
                EntityZone ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.UseGlobal);
            if (Count < Hits.Length) break;
            if (!Generation.GrowLineCheckHits())
                throw new InvalidOperationException(
                    $"A native prop ray reached its {MaximumNativeLineCheckHits}-hit limit.");
        }

        var Buffer = Generation.LineCheckHits;
        var SpawnerUsesExternalSupport = false;
        var SpawnerCollisionRoot = OwnedOnly
            ? Generation.ResolveCollisionRoot(
                Spawner.transform, out SpawnerUsesExternalSupport)
            : null;
        var NearestRank = -1;
        Transform? NearestOwner = null;
        var ExcludedId = Excluded ? Excluded.GetInstanceID() : (int?)null;
        for (var i = 0; i < Count; i++)
        {
            var Hit = Buffer[i];
            var Candidate = Hit.collider;
            if (!Candidate || Candidate.GetInstanceID() == ExcludedId) continue;
            if (!OwnedOnly)
            {
                if (!Result.collider || Hit.distance < Result.distance) Result = Hit;
                continue;
            }
            int Rank;
            Transform Owner;
            if (!(EntityZone
                    ? Generation.TryOwnOccurrenceSupport(SpawnerCollisionRoot!, SpawnerUsesExternalSupport,
                        Candidate, out Rank, out Owner)
                    : Generation.TryOwn(SpawnerCollisionRoot!, SpawnerUsesExternalSupport,
                        Candidate, out Rank, out Owner))) continue;
            if (!Result.collider || CompareOwnedHits(
                    Generation, Hit, Rank, Owner,
                    Result, NearestRank, NearestOwner!) < 0)
            {
                Result = Hit;
                NearestRank = Rank;
                NearestOwner = Owner;
            }
        }
        return Result;
    }

    [HarmonyPatch]
    static class PropSpawnerPointLineCheckPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(
                typeof(PropSpawner), nameof(PropSpawner.GetRandomPoint), Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    typeof(PropSpawner).FullName, nameof(PropSpawner.GetRandomPoint));
            yield return AccessTools.DeclaredMethod(
                typeof(PropSpawner_Line), nameof(PropSpawner_Line.GetRandomPoint), Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    typeof(PropSpawner_Line).FullName, nameof(PropSpawner_Line.GetRandomPoint));
            yield return AccessTools.DeclaredMethod(
                typeof(PropSpawner_Sphere), nameof(PropSpawner_Sphere.GetRandomPoint), Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    typeof(PropSpawner_Sphere).FullName, nameof(PropSpawner_Sphere.GetRandomPoint));
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var CallIndex = -1;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (!Codes[i].Calls(NativeLineCheckMethod)) continue;
                if (CallIndex >= 0)
                    throw new AmbiguousMatchException(
                        "PropSpawner.GetRandomPoint line-check call is ambiguous.");
                CallIndex = i;
            }
            if (CallIndex < 2)
                throw new MissingMethodException(
                    "PropSpawner.GetRandomPoint line-check call was not found.");
            var Call = Codes[CallIndex];
            if (Call.opcode != OpCodes.Call
                || Codes[CallIndex - 2].opcode != OpCodes.Ldc_R4
                || !Equals(Codes[CallIndex - 2].operand, 0f)
                || Codes[CallIndex - 1].opcode != OpCodes.Ldc_I4_1
                || Call.labels.Count != 0 || Call.blocks.Count != 0)
                throw new MissingMethodException(
                    "PropSpawner.GetRandomPoint line-check shape was not found.");

            Codes.Insert(CallIndex, new CodeInstruction(OpCodes.Ldarg_0));
            Call.opcode = OpCodes.Call;
            Call.operand = ((Func<Vector3, Vector3, HelperFunctions.LayerType, float,
                QueryTriggerInteraction, PropSpawner, RaycastHit>)LineCheckForNativePropSpawner).Method;
            return Codes;
        }
    }

    static int CompareOwnedHits(
        Generation Generation, RaycastHit Left, int LeftRank, Transform LeftOwner,
        RaycastHit Right, int RightRank, Transform RightOwner)
    {
        var Order = Left.distance.CompareTo(Right.distance);
        if (Order != 0) return Order;
        Order = LeftRank.CompareTo(RightRank);
        if (Order != 0) return Order;
        Order = CompareTransformPaths(
            LeftOwner, Left.collider.transform, Right.collider.transform);
        if (Order != 0) return Order;
        Order = Generation.CompareComponents(Left.collider, Right.collider);
        if (Order != 0) return Order;
        Order = Left.triangleIndex.CompareTo(Right.triangleIndex);
        if (Order != 0) return Order;
        Order = Compare(Left.point, Right.point);
        return Order != 0 ? Order : Compare(Left.normal, Right.normal);
    }

    static int CompareTransformPaths(Transform Root, Transform Left, Transform Right)
    {
        if (Left.GetInstanceID() == Right.GetInstanceID()) return 0;
        var LeftDepth = Depth(Root, Left);
        var RightDepth = Depth(Root, Right);
        var LeftNode = Left;
        var RightNode = Right;
        while (LeftDepth > RightDepth)
        {
            LeftNode = LeftNode.parent;
            LeftDepth--;
            if (LeftNode.GetInstanceID() == RightNode.GetInstanceID()) return 1;
        }
        while (RightDepth > LeftDepth)
        {
            RightNode = RightNode.parent;
            RightDepth--;
            if (LeftNode.GetInstanceID() == RightNode.GetInstanceID()) return -1;
        }
        while (LeftNode.parent.GetInstanceID() != RightNode.parent.GetInstanceID())
        {
            LeftNode = LeftNode.parent;
            RightNode = RightNode.parent;
        }
        return LeftNode.GetSiblingIndex().CompareTo(RightNode.GetSiblingIndex());
    }

    static int Depth(Transform Root, Transform Value)
    {
        var Depth = 0;
        var RootId = Root.GetInstanceID();
        while (Value.GetInstanceID() != RootId)
        {
            Value = Value.parent
                ?? throw new InvalidOperationException("A native line-check collider left its generation root.");
            Depth++;
        }
        return Depth;
    }

    static int Compare(Vector3 Left, Vector3 Right)
    {
        var Order = Left.x.CompareTo(Right.x);
        if (Order != 0) return Order;
        Order = Left.y.CompareTo(Right.y);
        return Order != 0 ? Order : Left.z.CompareTo(Right.z);
    }

    static bool NativeSpawnerSliceExpired(bool SlicesNativeSpawner) =>
        SlicesNativeSpawner && ActiveGeneration?.SliceExpired() == true;

    static bool RefreshNativeSpawnerSlice(PropSpawner Spawner)
    {
        var Value = ActiveGeneration;
        if (Value is null || !Value.Owns(Spawner.transform)) return false;
        Value.RefreshSlice();
        return true;
    }
    [HarmonyPatch]
    static class PropSpawnerSlicePatch
    {
        static MethodBase TargetMethod()
        {
            var Factory = AccessTools.DeclaredMethod(
                typeof(PropSpawner), nameof(PropSpawner.SpawnNew), new[] { typeof(bool) })
                ?? throw new MissingMethodException(
                    typeof(PropSpawner).FullName, nameof(PropSpawner.SpawnNew));
            return AccessTools.EnumeratorMoveNext(Factory)
                ?? throw new MissingMethodException("PropSpawner.SpawnNew iterator was not found.");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase __originalMethod)
        {
            var Codes = new List<CodeInstruction>(instructions);
            var Iterator = __originalMethod.DeclaringType
                ?? throw new MissingMethodException("PropSpawner.SpawnNew iterator type was not found.");
            var State = AccessTools.DeclaredField(Iterator, "<>1__state")
                ?? throw new MissingFieldException(Iterator.FullName, "<>1__state");
            var Current = AccessTools.DeclaredField(Iterator, "<>2__current")
                ?? throw new MissingFieldException(Iterator.FullName, "<>2__current");
            var Owner = AccessTools.DeclaredField(Iterator, "<>4__this")
                ?? throw new MissingFieldException(Iterator.FullName, "<>4__this");
            var SuccessfulSpawns = AccessTools.DeclaredField(Iterator, "<successfulSpawns>5__4")
                ?? throw new MissingFieldException(Iterator.FullName, "<successfulSpawns>5__4");
            var TryToSpawnCall = TryToSpawnMethod;
            var Expired = ((Func<bool, bool>)NativeSpawnerSliceExpired).Method;
            var Refresh = ((Func<PropSpawner, bool>)RefreshNativeSpawnerSlice).Method;
            var SwitchIndex = -1;
            var SpawnIndex = -1;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (Codes[i].opcode == OpCodes.Switch)
                {
                    if (SwitchIndex >= 0)
                        throw new AmbiguousMatchException(
                            "PropSpawner.SpawnNew state switch is ambiguous.");
                    SwitchIndex = i;
                }
                if (!Codes[i].Calls(TryToSpawnCall)) continue;
                if (SpawnIndex >= 0)
                    throw new AmbiguousMatchException(
                        "PropSpawner.SpawnNew attempt call is ambiguous.");
                SpawnIndex = i;
            }
            if (SwitchIndex < 7 || SpawnIndex < 0
                || Codes[SwitchIndex - 7].opcode != OpCodes.Ldarg_0
                || Codes[SwitchIndex - 6].opcode != OpCodes.Ldfld
                || !Equals(Codes[SwitchIndex - 6].operand, State)
                || Codes[SwitchIndex - 5].opcode != OpCodes.Stloc_1
                || Codes[SwitchIndex - 4].opcode != OpCodes.Ldarg_0
                || Codes[SwitchIndex - 3].opcode != OpCodes.Ldfld
                || !Equals(Codes[SwitchIndex - 3].operand, Owner)
                || Codes[SwitchIndex - 2].opcode != OpCodes.Stloc_2
                || Codes[SwitchIndex - 1].opcode != OpCodes.Ldloc_1
                || Codes[SwitchIndex].operand is not Label[] States || States.Length != 4)
                throw new MissingMethodException(
                    "PropSpawner.SpawnNew state switch shape was not found.");
            if (SpawnIndex < 3
                || Codes[SpawnIndex - 3].opcode != OpCodes.Ldloc_2
                || Codes[SpawnIndex - 2].opcode != OpCodes.Ldarg_0
                || Codes[SpawnIndex - 1].opcode != OpCodes.Ldfld
                || !Equals(Codes[SpawnIndex - 1].operand, SuccessfulSpawns)
                || Codes[SpawnIndex].opcode != OpCodes.Call
                || SpawnIndex + 13 >= Codes.Count
                || Codes[SpawnIndex + 1].opcode != OpCodes.Brfalse
                || Codes[SpawnIndex + 1].operand is not Label Failure
                || Codes[SpawnIndex + 2].opcode != OpCodes.Ldarg_0
                || Codes[SpawnIndex + 3].opcode != OpCodes.Ldnull
                || Codes[SpawnIndex + 4].opcode != OpCodes.Stfld
                || !Equals(Codes[SpawnIndex + 4].operand, Current)
                || Codes[SpawnIndex + 5].opcode != OpCodes.Ldarg_0
                || Codes[SpawnIndex + 6].opcode != OpCodes.Ldc_I4_1
                || Codes[SpawnIndex + 7].opcode != OpCodes.Stfld
                || !Equals(Codes[SpawnIndex + 7].operand, State)
                || Codes[SpawnIndex + 8].opcode != OpCodes.Ldc_I4_1
                || Codes[SpawnIndex + 9].opcode != OpCodes.Stloc_0
                || Codes[SpawnIndex + 10].opcode != OpCodes.Leave
                || Codes[SpawnIndex + 10].operand is not Label Finished)
                throw new MissingMethodException(
                    "PropSpawner.SpawnNew attempt yield shape was not found.");

            int TargetIndex(Label Label, string Site)
            {
                var Target = -1;
                for (var i = 0; i < Codes.Count; i++)
                {
                    if (!Codes[i].labels.Contains(Label)) continue;
                    if (Target >= 0)
                        throw new AmbiguousMatchException(
                            $"PropSpawner.SpawnNew {Site} target is ambiguous.");
                    Target = i;
                }
                if (Target < 0)
                    throw new MissingMethodException(
                        $"PropSpawner.SpawnNew {Site} target was not found.");
                return Target;
            }

            var StateOneIndex = TargetIndex(States[1], "state-one");
            var FailureIndex = TargetIndex(Failure, "failed-attempt");
            var FinishedIndex = TargetIndex(Finished, "return");
            if (StateOneIndex != SpawnIndex + 11
                || Codes[StateOneIndex].opcode != OpCodes.Ldarg_0
                || Codes[StateOneIndex + 1].opcode != OpCodes.Ldc_I4_M1
                || Codes[StateOneIndex + 2].opcode != OpCodes.Stfld
                || !Equals(Codes[StateOneIndex + 2].operand, State)
                || FailureIndex + 3 >= Codes.Count
                || Codes[FailureIndex].opcode != OpCodes.Ldarg_0
                || Codes[FailureIndex + 1].opcode != OpCodes.Ldfld
                || Codes[FailureIndex + 2].opcode != OpCodes.Ldc_I4
                || !Equals(Codes[FailureIndex + 2].operand, 1500)
                || Codes[FailureIndex + 3].opcode != OpCodes.Blt
                || Codes[FailureIndex + 3].operand is not Label Attempt
                || TargetIndex(Attempt, "attempt-loop") >= SpawnIndex
                || FinishedIndex + 1 >= Codes.Count
                || Codes[FinishedIndex].opcode != OpCodes.Ldloc_0
                || Codes[FinishedIndex + 1].opcode != OpCodes.Ret)
                throw new MissingMethodException(
                    "PropSpawner.SpawnNew attempt loop or return shape was not found.");

            var Check = generator.DefineLabel();
            var ResumeState = generator.DefineLabel();
            var SlicesNativeSpawner = generator.DeclareLocal(typeof(bool));
            var ExtendedStates = new Label[States.Length + 1];
            Array.Copy(States, ExtendedStates, States.Length);
            ExtendedStates[States.Length] = ResumeState;
            Codes[SwitchIndex].operand = ExtendedStates;
            Codes[SpawnIndex + 1].operand = Check;

            var CheckStart = new CodeInstruction(OpCodes.Ldloc, SlicesNativeSpawner);
            CheckStart.labels.Add(Check);
            var ResumeStart = new CodeInstruction(OpCodes.Ldarg_0);
            ResumeStart.labels.Add(ResumeState);
            Codes.InsertRange(SpawnIndex + 11, new CodeInstruction[]
            {
                CheckStart,
                new(OpCodes.Call, Expired),
                new(OpCodes.Brfalse, Failure),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldnull),
                new(OpCodes.Stfld, Current),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldc_I4_4),
                new(OpCodes.Stfld, State),
                new(OpCodes.Ldc_I4_1),
                new(OpCodes.Stloc_0),
                new(OpCodes.Leave, Finished),
                ResumeStart,
                new(OpCodes.Ldc_I4_M1),
                new(OpCodes.Stfld, State),
                new(OpCodes.Br, Failure)
            });
            Codes.InsertRange(SwitchIndex - 1, new CodeInstruction[]
            {
                new(OpCodes.Ldloc_2),
                new(OpCodes.Call, Refresh),
                new(OpCodes.Stloc, SlicesNativeSpawner)
            });
            var Fault = -1;
            for (var i = 0; i < Codes.Count; i++)
                foreach (var Block in Codes[i].blocks)
                    if (Block.blockType == ExceptionBlockType.BeginFaultBlock)
                    {
                        if (Fault >= 0)
                            throw new AmbiguousMatchException("PropSpawner.SpawnNew fault handler is ambiguous.");
                        Fault = i;
                    }
            if (Fault < 0 || Fault + 2 >= Codes.Count
                || Codes[Fault].opcode != OpCodes.Ldarg_0
                || Codes[Fault + 1].opcode != OpCodes.Call
                || Codes[Fault + 1].operand is not MethodInfo Dispose
                || Dispose.Name != "System.IDisposable.Dispose"
                || Codes[Fault + 2].opcode != OpCodes.Endfinally)
                throw new MissingMethodException("PropSpawner.SpawnNew exception cleanup was not found.");
            var Catch = new CodeInstruction(OpCodes.Pop);
            Catch.blocks.AddRange(Codes[Fault].blocks);
            Codes[Fault].blocks.Clear();
            foreach (var Block in Catch.blocks)
                if (Block.blockType == ExceptionBlockType.BeginFaultBlock)
                {
                    Block.blockType = ExceptionBlockType.BeginCatchBlock;
                    Block.catchType = typeof(Exception);
                }
            Codes[Fault + 2].opcode = OpCodes.Rethrow;
            Codes.Insert(Fault, Catch);
            return Codes;
        }
    }
    [HarmonyPatch(typeof(PSC_Height), nameof(PSC_Height.CheckConstraint),
        typeof(PropSpawner.SpawnData))]
    static class HeightConstraintPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            PSC_Height __instance, PropSpawner.SpawnData spawnData, ref bool __result)
        {
            var Value = ActiveGeneration;
            if (Value is null || !Value.Owns(spawnData.spawnerTransform)) return true;
            var Height = Value.Placement.InversePoint(spawnData.pos).y;
            __result = Height > __instance.minHeight && Height < __instance.maxHeight;
            return false;
        }
    }

    [HarmonyPatch(typeof(PSC_Perlin), nameof(PSC_Perlin.CheckConstraint),
        typeof(PropSpawner.SpawnData))]
    static class PerlinConstraintPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            PSC_Perlin __instance, PropSpawner.SpawnData spawnData, ref bool __result)
        {
            var Generation = ActiveGeneration;
            if (Generation is null || !Generation.Owns(spawnData.spawnerTransform)) return true;
            var Position = Generation.Placement.InversePoint(spawnData.pos);
            var Value = Mathf.PerlinNoise(
                (Position.x + 500f) * __instance.perlinSize * 0.1f + __instance.perlinOffset,
                (Position.z + 500f) * __instance.perlinSize * 0.1f + __instance.perlinOffset);
            __result = Value > __instance.minMax.x && Value < __instance.minMax.y;
            return false;
        }
    }

    [HarmonyPatch(typeof(PSC_VolumeLight), nameof(PSC_VolumeLight.CheckConstraint),
        typeof(PropSpawner.SpawnData))]
    static class LightConstraintPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            PSC_VolumeLight __instance, PropSpawner.SpawnData spawnData, ref bool __result)
        {
            var Value = ActiveGeneration;
            var Run = Map.CurrentRun;
            if (Value is null || Run is null || !Value.Owns(spawnData.spawnerTransform)) return true;
            var Alpha = Run.LightVolumes.SampleCanonical(Value.Placement.InversePoint(spawnData.pos)).a;
            __result = Alpha > __instance.minMax.x && Alpha < __instance.minMax.y;
            return false;
        }
    }
}
