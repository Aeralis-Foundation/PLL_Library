using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal sealed class GeneratedPropOutputCapture
{
    readonly HookedPropTarget Target;
    readonly GeneratedPropAssetCatalog Catalog;
    readonly GeneratedGeometryBudget Budget;
    readonly Dictionary<int, int> PrefabNodes = new();
    readonly HashSet<Transform> Baseline = new();
    readonly Dictionary<Transform, int> Instances = new();
    readonly Dictionary<Mesh, byte[]> MeshHashes = new();
    long RawMeshBytes;
    int Renderers;
    int Colliders;
    int PendingInstances;
    int RawNodes, PendingNodes, ProceduralMeshes;
    static CandidateAttempt? ActiveAttempt;

    sealed class CandidateAttempt
    {
        internal CandidateAttempt(Transform Spawner)
        {
            this.Spawner = Spawner; Previous = ActiveAttempt;
            ActiveAttempt = this;
        }
        internal Transform Spawner { get; }
        internal CandidateAttempt? Previous { get; }
        internal GameObject? Output { get; set; }
    }

    internal GeneratedPropOutputCapture(HookedPropTarget Target, GeneratedPropAssetCatalog Catalog, int Revision,
        GeneratedGeometryBudget Budget, int GenerationSeed)
    {
        this.Target = Target; this.Catalog = Catalog;
        this.Revision = Revision;
        this.Budget = Budget;
        this.GenerationSeed = GenerationSeed;
    }
    internal HookedPropTarget Owner => Target;
    internal int Revision { get; }
    readonly int GenerationSeed;

    internal void AfterClear()
    {
        Budget.ReleaseNativeOutput(Target);
        foreach (var Node in Target.Root.GetComponentsInChildren<Transform>(true)) Baseline.Add(Node);
    }

    int BeginSpawn(GameObject Prefab)
    {
        var Ordinal = Catalog.PrefabOrdinal(Prefab);
        if (!Target.Contract.AllowsPrefab(Ordinal))
            throw new InvalidOperationException("Hooked generation selected a foreign prefab.");
        if (!PrefabNodes.TryGetValue(Ordinal, out var Nodes))
        {
            Nodes = Prefab.GetComponentsInChildren<Transform>(true).Length;
            PrefabNodes.Add(Ordinal, Nodes);
        }
        PruneDestroyed();
        Budget.ReserveNativeOutput(Target, Instances.Count + PendingInstances + 1,
            checked(RawNodes + PendingNodes + Nodes), ProceduralMeshes, RawMeshBytes);
        PendingInstances++; PendingNodes += Nodes; return Ordinal;
    }

    void PruneDestroyed()
    {
        List<Transform>? Destroyed = null;
        foreach (var Instance in Instances.Keys)
            if (!Instance) (Destroyed ??= new List<Transform>()).Add(Instance!);
        if (Destroyed is null) return;
        foreach (var Instance in Destroyed)
        {
            RawNodes -= PrefabNodes[Instances[Instance]]; Instances.Remove(Instance);
        }
    }

    void EndSpawn(int Ordinal)
    {
        PendingInstances--; PendingNodes -= PrefabNodes[Ordinal];
        Budget.ReserveNativeOutput(Target, Instances.Count + PendingInstances, RawNodes + PendingNodes, ProceduralMeshes, RawMeshBytes);
    }

    void Record(int Ordinal, GameObject Output)
    {
        if (Baseline.Contains(Output.transform)
            || !TransformOwnership.Contains(Target.Root, Output.transform)
            || !Instances.TryAdd(Output.transform, Ordinal))
            throw new InvalidOperationException("Hooked generation produced foreign, duplicate, or excessive output.");
        RawNodes += PrefabNodes[Ordinal];
        Budget.ReserveNativeOutput(Target, Instances.Count + PendingInstances, RawNodes + PendingNodes, ProceduralMeshes, RawMeshBytes);
    }

    internal bool TryGetGeneratedRoot(Transform Value, GameObject Source, out Transform Root)
    {
        for (var Node = Value; Node && Node != Target.Root; Node = Node.parent)
            if (Instances.TryGetValue(Node, out var Ordinal) && ReferenceEquals(Catalog.Prefab(Ordinal), Source))
            {
                Root = Node; return true;
            }
        Root = null!; return false;
    }

    internal static bool TryGetNativeGeneratedRoot(Transform Value, GameObject Source, out Transform Root)
    {
        for (var Node = Value; Node; Node = Node.parent)
            if (Node.GetComponent<PllGeneratedPrefabIdentity>() is { } Identity
                && ReferenceEquals(Identity.Prefab, Source))
            {
                Root = Node; return true;
            }
        Root = null!; return false;
    }

    internal byte[] Freeze()
    {
        var NativeOutputs = NativeFeatureGeneration.OutputInventory(Target.Grouper);
        var Paths = new Dictionary<Transform, int[]> { [Target.Root] = Array.Empty<int>() };
        var Live = new List<Transform>(Instances.Count);
        PruneDestroyed();
        foreach (var Instance in Instances.Keys)
            if (Instance)
            {
                Live.Add(Instance); Path(Target.Root, Instance, Paths);
            }
        Live.Sort((Left, Right) => Compare(Paths[Left], Paths[Right]));
        for (var Ordinal = 0; Ordinal < Live.Count; Ordinal++)
            Live[Ordinal].GetComponent<PllGeneratedPrefabIdentity>().PublishActorIdentity(
                new ActorPlacementIdentity(Target.Contract.StableTargetKey, Revision, GenerationSeed, Ordinal));
        var Nodes = new List<Transform>();
        var ActualRawNodes = 0;
        foreach (var Node in Target.Root.GetComponentsInChildren<Transform>(true))
        {
            if (Baseline.Contains(Node)) continue;
            var Owner = InstanceOwner(Node, NativeOutputs, out var NativeFootprint);
            if (!Owner) throw new InvalidOperationException("Hooked generation produced unrecorded output.");
            Nodes.Add(Node); Path(Target.Root, Node, Paths);
            if (!NativeFootprint && !NativeFlowRuntime.TryGetOwner(Node, out _) && !PropInteractionRuntime.OwnsNativeGeometry(Node)) ActualRawNodes++;
        }
        RawNodes = ActualRawNodes;
        Budget.ReserveNativeOutput(Target, Live.Count, ActualRawNodes, ProceduralMeshes, RawMeshBytes);
        Nodes.Sort((Left, Right) => Compare(Paths[Left], Paths[Right]));
        foreach (var Instance in Live) ValidateInstance(Instance, Catalog.Prefab(Instances[Instance]).transform, NativeOutputs);
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true), true))
        {
            Writer.Write("PLL/HookedPropGeneratedOutput/v2"); Writer.Write(Live.Count);
            foreach (var Instance in Live)
            {
                WritePath(Writer, Paths[Instance]); Writer.Write(Instances[Instance]);
                var Flow = Instance.GetComponent<PllNativeFlowCondition>();
                Writer.Write((bool)Flow);
                if (Flow) Flow.WriteRealizedOutput(Writer);
            }
            Writer.Write(Nodes.Count);
            foreach (var Node in Nodes)
            {
                WritePath(Writer, Paths[Node]);
                var Matrix = Target.Root.worldToLocalMatrix * Node.localToWorldMatrix;
                for (var i = 0; i < 16; i++) WriteFloat(Writer, Matrix[i / 4, i % 4]);
                Writer.Write(Node.gameObject.activeSelf); Writer.Write(Node.gameObject.layer);
                var Tag = Node.gameObject.tag;
                if (Tag == "EditorOnly") throw new InvalidOperationException("Hooked output uses an EditorOnly tag.");
                Writer.Write(Tag);
                var FlowOutput = NativeFlowRuntime.TryGetOwner(Node, out _);
                Writer.Write(FlowOutput);
                if (FlowOutput) continue;
                var Components = Node.GetComponents<Component>();
                if (Node.GetComponents<MeshFilter>().Length != Node.GetComponents<MeshRenderer>().Length)
                    throw new InvalidOperationException("Hooked output has an unpaired MeshFilter.");
                var NodeRenderers = Node.GetComponents<Renderer>(); Writer.Write(NodeRenderers.Length);
                foreach (var Renderer in NodeRenderers)
                {
                    WriteRenderer(Writer, Renderer, Components, Paths);
                    NativeFogRenderRuntime.BindRenderer(Renderer);
                }
                var NodeColliders = Node.GetComponents<Collider>(); Writer.Write(NodeColliders.Length);
                foreach (var Collider in NodeColliders) WriteCollider(Writer, Collider, Components);
            }
        }
        using var Sha = SHA256.Create();
        var Hash = Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
        MeshHashes.Clear(); Baseline.Clear(); return Hash;
    }

    Transform? InstanceOwner(Transform Node, NativeFeatureGeneration.NativeOutputInventory? NativeOutputs,
        out bool NativeFootprint)
    {
        NativeFootprint = false;
        for (var Current = Node; Current && Current != Target.Root; Current = Current.parent)
        {
            if (!NativeFootprint && NativeOutputs is not null && NativeOutputs.TryGetTrackedPrefab(Current, out _))
                NativeFootprint = true;
            if (Instances.ContainsKey(Current)) return Current;
        }
        return null;
    }

    void ValidateInstance(Transform Instance, Transform Source, NativeFeatureGeneration.NativeOutputInventory NativeOutputs)
    {
        var Pending = new Stack<(Transform Instance, Transform Source)>();
        Pending.Push((Instance, Source));
        while (Pending.Count != 0)
        {
            var Pair = Pending.Pop();
            var Actual = Pair.Instance.GetComponents<Component>();
            var Expected = Pair.Source.GetComponents<Component>();
            var Index = 0;
            foreach (var Component in Actual)
            {
                if (!Component) throw new InvalidOperationException("Hooked output has a missing script.");
                if (Component is Peak.SpawnedItemTracker && Index == Expected.Length
                    && Pair.Instance.GetComponent<ISpawner>() is not null) continue;
                if (NativeSpawnAdmission.IsOwnedSourceLifetime(Component)) continue;
                if (Component is PllNativeFlowCondition or PllGeneratedPrefabIdentity) continue;
                if (NativeFlowRuntime.IsOwnedComponent(Component)) continue;
                if (Component.GetComponentInParent<PllPropInteractionDriver>(true)?.Owner is { } Interaction
                    && Interaction.OwnsRuntimeComponent(Component)) continue;
                if (Index == Expected.Length || !Expected[Index] || Component.GetType() != Expected[Index++].GetType())
                    throw new InvalidOperationException("Hooked output changed its sealed component graph.");
            }
            if (Index != Expected.Length) throw new InvalidOperationException("Hooked output removed a sealed component.");
            var SourceIndex = 0;
            for (var i = 0; i < Pair.Instance.childCount; i++)
            {
                var Child = Pair.Instance.GetChild(i);
                if (Instances.ContainsKey(Child)) continue;
                if (NativeFlowRuntime.TryGetOwner(Child, out _)) continue;
                if (PropInteractionRuntime.OwnsNativeGeometry(Child)) continue;
                if (NativeOutputs.TryGetTrackedPrefab(Child, out var NativePrefab))
                {
                    Pending.Push((Child, NativePrefab.transform)); continue;
                }
                if (SourceIndex == Pair.Source.childCount)
                    throw new InvalidOperationException("Hooked output changed its sealed hierarchy.");
                Pending.Push((Child, Pair.Source.GetChild(SourceIndex++)));
            }
            if (SourceIndex != Pair.Source.childCount)
                throw new InvalidOperationException("Hooked output removed a sealed node.");
        }
    }

    static int[] Path(Transform Root, Transform Node, Dictionary<Transform, int[]> Paths)
    {
        if (Paths.TryGetValue(Node, out var Values)) return Values;
        var Length = 0;
        for (var Current = Node; Current != Root; Current = Current.parent)
        {
            if (!Current) throw new InvalidOperationException("Hooked output escaped its target root.");
            Length++;
        }
        Values = new int[Length];
        for (var Current = Node; Current != Root; Current = Current.parent)
            Values[--Length] = Current.GetSiblingIndex();
        Paths.Add(Node, Values); return Values;
    }

    static int Compare(int[] Left, int[] Right)
    {
        for (var i = 0; i < Math.Min(Left.Length, Right.Length); i++)
            if (Left[i] != Right[i]) return Left[i].CompareTo(Right[i]);
        return Left.Length.CompareTo(Right.Length);
    }

    static void WritePath(BinaryWriter Writer, int[] Values)
    {
        Writer.Write(0); Writer.Write(Values.Length);
        foreach (var Value in Values) Writer.Write(Value);
    }

    static void WriteFloat(BinaryWriter Writer, float Value)
    {
        if (!float.IsFinite(Value)) throw new InvalidOperationException("Hooked output contains nonfinite geometry.");
        Writer.Write(Value == 0f ? 0 : BitConverter.SingleToInt32Bits(Value));
    }

    static void WriteVector(BinaryWriter Writer, Vector3 Value)
    {
        WriteFloat(Writer, Value.x); WriteFloat(Writer, Value.y); WriteFloat(Writer, Value.z);
    }

    void WriteMaterials(BinaryWriter Writer, Renderer Renderer)
    {
        if (Renderer.HasPropertyBlock()) throw new InvalidOperationException("Hooked raw output has a material property block.");
        var Materials = Renderer.sharedMaterials; Writer.Write(Materials.Length);
        foreach (var Material in Materials)
        {
            Writer.Write((bool)Material);
            if (Material) Writer.Write(Catalog.MaterialOrdinal(Map.CurrentRun!.NativeMaterials.CanonicalMaterial(
                Map.CurrentRun.SurfaceTextures.CanonicalMaterial(NativeFogRenderRuntime.CanonicalMaterial(Material)))));
        }
    }

    void WriteRenderer(BinaryWriter Writer, Renderer Renderer, Component[] Components,
        Dictionary<Transform, int[]> Paths)
    {
        if (++Renderers > 65536) throw new InvalidOperationException("Hooked output exceeds its renderer budget.");
        Writer.Write(Array.IndexOf(Components, Renderer));
        var FlowSurface = NativeFlowRuntime.OwnsRenderer(Renderer);
        Writer.Write(FlowSurface);
        if (FlowSurface) return;
        Writer.Write(Renderer.enabled); Writer.Write(Renderer.forceRenderingOff);
        if (Renderer.GetComponentInParent<PllPropInteractionDriver>(true)?.Owner is { } Interaction
            && Interaction.TryNativeRenderer(Renderer, out var NativeMesh, out var NativeMaterial))
        {
            Writer.Write((byte)3); WriteMesh(Writer, NativeMesh, true, true);
            Writer.Write(VisualFingerprintWriter.HashSerializedObject(NativeMaterial, "PLL/PropNativeMaterial/v1"));
            return;
        }
        if (Renderer is MeshRenderer)
        {
            Writer.Write((byte)1);
            var Filters = Renderer.GetComponents<MeshFilter>();
            if (Filters.Length != 1 || Renderer.GetComponents<MeshRenderer>().Length != 1)
                throw new InvalidOperationException("Hooked output requires one MeshFilter and MeshRenderer pair.");
            Writer.Write(Array.IndexOf(Components, Filters[0])); WriteMesh(Writer, Filters[0].sharedMesh, false);
            WriteMaterials(Writer, Renderer);
            return;
        }
        if (Renderer is not SkinnedMeshRenderer Skinned || !Skinned.sharedMesh
            || !Catalog.TryMeshOrdinal(Skinned.sharedMesh, out var Ordinal))
            throw new InvalidOperationException("Hooked output contains an unsupported renderer or skinned mesh.");
        Writer.Write((byte)2); Writer.Write(Ordinal); WriteMaterials(Writer, Renderer);
        var Instance = InstanceOwner(Renderer.transform, null, out _)!;
        Writer.Write((bool)Skinned.rootBone);
        if (Skinned.rootBone) WriteBone(Skinned.rootBone);
        var Bones = Skinned.bones; Writer.Write(Bones.Length);
        foreach (var Bone in Bones) WriteBone(Bone);
        WriteVector(Writer, Skinned.localBounds.center); WriteVector(Writer, Skinned.localBounds.extents);
        Writer.Write(Skinned.updateWhenOffscreen); Writer.Write(Skinned.skinnedMotionVectors); Writer.Write((int)Skinned.quality);
        Writer.Write(Skinned.sharedMesh.blendShapeCount);
        for (var i = 0; i < Skinned.sharedMesh.blendShapeCount; i++) WriteFloat(Writer, Skinned.GetBlendShapeWeight(i));

        void WriteBone(Transform Bone)
        {
            if (!Bone || !TransformOwnership.Contains(Instance, Bone))
                throw new InvalidOperationException("Hooked skinned output has a foreign bone.");
            WritePath(Writer, Path(Target.Root, Bone, Paths));
        }
    }

    void WriteMesh(BinaryWriter Writer, Mesh Mesh, bool Required, bool InteractionOwned = false)
    {
        if (!Mesh)
        {
            if (Required) throw new InvalidOperationException("Hooked mesh collider has no mesh.");
            Writer.Write((byte)0); return;
        }
        var Sealed = Catalog.TryMeshOrdinal(Mesh, out var Ordinal);
        if (Sealed && (!Mesh.isReadable || Mesh.blendShapeCount != 0))
        {
            Writer.Write((byte)1); Writer.Write(Ordinal); return;
        }
        if (!Mesh.isReadable || Mesh.blendShapeCount != 0)
            throw new InvalidOperationException("Hooked output has an unreadable or unsupported procedural mesh.");
        if (!MeshHashes.TryGetValue(Mesh, out var Hash))
        {
            for (var i = 0; i < Mesh.subMeshCount; i++)
                if (Mesh.GetTopology(i) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Hooked output mesh has nontriangle topology.");
            if (!Nonnegative(Mesh.bounds.extents)) throw new InvalidOperationException("Hooked output mesh has negative bounds.");
            var Admitted = Sealed || InteractionOwned || NativeFeatureGeneration.OwnsRawSplineMesh(Mesh);
            Hash = VisualFingerprintWriter.HashGeneratedPropMesh(Mesh,
                Admitted ? long.MaxValue : Budget.RemainingMeshBytes, out var ByteCount);
            if (!Admitted)
            {
                RawMeshBytes += ByteCount; ProceduralMeshes++;
                Budget.ReserveNativeOutput(Target, Instances.Count, RawNodes, ProceduralMeshes, RawMeshBytes);
            }
            MeshHashes.Add(Mesh, Hash);
        }
        Writer.Write((byte)2); Writer.Write(Hash);
    }

    void WriteCollider(BinaryWriter Writer, Collider Collider, Component[] Components)
    {
        if (++Colliders > 65536) throw new InvalidOperationException("Hooked output exceeds its collider budget.");
        Writer.Write(Array.IndexOf(Components, Collider)); Writer.Write(Collider.enabled); Writer.Write(Collider.isTrigger);
        WriteFloat(Writer, Collider.contactOffset);
        var Material = Collider.sharedMaterial; Writer.Write((bool)Material);
        if (Material)
        {
            if (Catalog.TryPhysicsMaterialOrdinal(Material, out var Ordinal)) { Writer.Write((byte)1); Writer.Write(Ordinal); }
            else Writer.Write((byte)2);
            Writer.Write(Material.name);
            WriteFloat(Writer, Material.dynamicFriction); WriteFloat(Writer, Material.staticFriction); WriteFloat(Writer, Material.bounciness);
            Writer.Write((int)Material.frictionCombine); Writer.Write((int)Material.bounceCombine);
        }
        switch (Collider)
        {
            case BoxCollider Box:
                if (!Nonnegative(Box.size)) throw new InvalidOperationException("Hooked box collider has a negative size.");
                Writer.Write((byte)1); WriteVector(Writer, Box.center); WriteVector(Writer, Box.size); break;
            case SphereCollider Sphere:
                if (Sphere.radius < 0f) throw new InvalidOperationException("Hooked sphere collider has a negative radius.");
                Writer.Write((byte)2); WriteVector(Writer, Sphere.center); WriteFloat(Writer, Sphere.radius); break;
            case CapsuleCollider Capsule:
                if (Capsule.radius < 0f || Capsule.height < 0f || Capsule.direction is < 0 or > 2)
                    throw new InvalidOperationException("Hooked capsule collider has invalid dimensions.");
                Writer.Write((byte)3); WriteVector(Writer, Capsule.center); WriteFloat(Writer, Capsule.radius);
                WriteFloat(Writer, Capsule.height); Writer.Write(Capsule.direction); break;
            case MeshCollider Mesh:
                Writer.Write((byte)4); WriteMesh(Writer, Mesh.sharedMesh, true); Writer.Write(Mesh.convex); Writer.Write((int)Mesh.cookingOptions); break;
            default: throw new InvalidOperationException("Hooked output contains an unsupported collider.");
        }
    }

    static bool Nonnegative(Vector3 Value) => Value.x >= 0f && Value.y >= 0f && Value.z >= 0f;

    [HarmonyPatch(typeof(HelperFunctions), nameof(HelperFunctions.SpawnPrefab),
        new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform) })]
    static class SpawnCapturePatch
    {
        [HarmonyPrefix]
        static void Prefix(GameObject __0, Transform __3,
            out (GeneratedPropOutputCapture? Capture, int Ordinal,
                NativeFeatureGeneration.NativeSpawnReservation? Native)? __state)
        {
            __state = null;
            var Capture = Map.CurrentRun?.HookedGeneration.ActiveCapture;
            if (!__3 || !NativeFeatureGeneration.OwnsGenerationOutput(__3)
                || NativeFlowRuntime.TryGetOwner(__3, out _)) return;
            __state = Capture is not null && TransformOwnership.Contains(Capture.Target.Root, __3)
                ? (Capture, Capture.BeginSpawn(__0), NativeFeatureGeneration.BeginNativeSpawn(__0, false))
                : (null, -1, NativeFeatureGeneration.BeginNativeSpawn(__0));
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(GameObject __0, Transform __3, GameObject __result,
            (GeneratedPropOutputCapture? Capture, int Ordinal,
                NativeFeatureGeneration.NativeSpawnReservation? Native)? __state, Exception? __exception)
        {
            if (__state is not { } Spawn) return __exception;
            try
            {
                if (Spawn.Capture is { } Capture) Capture.EndSpawn(Spawn.Ordinal);
                if (__exception is not null) return __exception;
                if (ActiveAttempt is { Output: null } Attempt && Attempt.Spawner == __3)
                    Attempt.Output = __result;
                if (!NativeFeatureGeneration.OwnsGenerationOutput(__3)
                    || Spawn.Capture is not null
                        && !ReferenceEquals(Map.CurrentRun?.HookedGeneration.ActiveCapture, Spawn.Capture))
                    return MapRun.FeatureWriter.Cancelled();
                if (Spawn.Capture is { } Owner) Owner.Record(Spawn.Ordinal, __result);
                if (Spawn.Native is { } Native) Native.Complete(__result);
                else __result.AddComponent<PllGeneratedPrefabIdentity>().Bind(__0);
                var Flows = NativeFeatureGeneration.BindGeneratedFlows(__0, __result);
                if (Flows.Length != 0) __result.AddComponent<PllNativeFlowCondition>().Bind(__0, Flows);
                Map.CurrentRun!.NativeSpawners.EnrollGenerated(__0, __result);
                return null;
            }
            finally { Spawn.Native?.Dispose(); }
        }
    }

    [HarmonyPatch(typeof(PropSpawner), "Spawn", typeof(PropSpawner.SpawnData))]
    static class CandidateFailurePatch
    {
        [HarmonyPrefix]
        static void Prefix(PropSpawner __instance, out CandidateAttempt? __state) =>
            __state = NativeFeatureGeneration.OwnsGenerationOutput(__instance.transform)
                ? new CandidateAttempt(__instance.transform) : null;

        [HarmonyPostfix]
        static void Postfix(PropSpawner.SpawnData __0, ref GameObject __result, CandidateAttempt? __state)
        {
            if (__state is null || !__result || !__result.TryGetComponent<PllNativeFlowCondition>(out var Flows)) return;
            if (Flows.Initialize(__0)) return;
            NativeFeatureGeneration.DestroyNativeOutput(__result);
            __result = null!;
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(CandidateAttempt? __state, Exception? __exception)
        {
            if (__state is not { } Attempt) return __exception;
            ActiveAttempt = Attempt.Previous;
            if (__exception is null || Attempt.Output is not { } Output || !Output) return __exception;
            try { NativeFeatureGeneration.DestroyNativeOutput(Output); }
            catch (Exception Failure)
            {
                return new AggregateException(
                    "Native candidate generation and cleanup both failed.", __exception, Failure);
            }
            return __exception;
        }
    }
}
