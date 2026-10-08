using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class OccurrenceHeightmapMeshes
{
    sealed class GenerationLease : IDisposable
    {
        readonly NativeFeatureGeneration.FeatureRoots Roots;
        readonly GenerationLease? Previous;
        bool Disposed;

        internal GenerationLease(
            NativeFeatureGeneration.FeatureRoots Roots, GenerationLease? Previous)
        {
            this.Roots = Roots;
            this.Previous = Previous;
        }

        internal Transform? Owner(Transform Value) =>
            Roots.DeepestRoot(Value, out _) ?? Previous?.Owner(Value);

        public void Dispose()
        {
            if (Disposed) return;
            if (!ReferenceEquals(ActiveGeneration, this))
                throw new InvalidOperationException("Heightmap mesh generation leases were disposed out of order.");
            ActiveGeneration = Previous;
            Disposed = true;
        }
    }

    readonly struct OwnedMesh
    {
        internal OwnedMesh(
            MapRun Run, int SceneHandle, Transform Root, HeightmapMesh Owner, Mesh Mesh)
        {
            this.Run = Run;
            this.SceneHandle = SceneHandle;
            this.Root = Root;
            this.Owner = Owner;
            this.Mesh = Mesh;
        }

        internal MapRun Run { get; }
        internal int SceneHandle { get; }
        internal Transform Root { get; }
        internal HeightmapMesh Owner { get; }
        internal Mesh Mesh { get; }
    }

    static readonly List<OwnedMesh> Owned = new();
    static GenerationLease? ActiveGeneration;

    internal static IDisposable EnterGeneration(IReadOnlyList<Transform> Roots)
    {
        var Lease = new GenerationLease(
            new NativeFeatureGeneration.FeatureRoots(Roots), ActiveGeneration);
        ActiveGeneration = Lease;
        return Lease;
    }

    internal static void ReleaseNow(Transform Root) => Release(Root, true);
    internal static void ReleaseDeferred(Transform Root) => Release(Root, false);

    internal static void ReleaseRun(MapRun Run)
    {
        if (Run is null) throw new ArgumentNullException(nameof(Run));
        Release(Entry => ReferenceEquals(Entry.Run, Run), true);
    }

    internal static void ReleaseScene(int SceneHandle) =>
        Release(Entry => Entry.SceneHandle == SceneHandle, true);

    static void Release(Transform Root, bool Immediate)
    {
        if (ReferenceEquals(Root, null)) return;
        var RootAlive = (bool)Root;
        Release(Entry => ReferenceEquals(Entry.Root, Root)
            || RootAlive && Entry.Root && Entry.Root.IsChildOf(Root), Immediate);
    }

    static void Release(Predicate<OwnedMesh> Matches, bool Immediate)
    {
        var Failures = new List<Exception>();
        for (var i = Owned.Count - 1; i >= 0; i--)
        {
            if (!Matches(Owned[i])) continue;
            try { Release(i, Immediate); }
            catch (Exception Failure) { Failures.Add(Failure); }
        }
        if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
        if (Failures.Count != 0)
            throw new AggregateException("Occurrence heightmap mesh release failed.", Failures);
    }

    static void Release(int Index, bool Immediate)
    {
        var Entry = Owned[Index];
        var Owner = Entry.Owner;
        var Mesh = Entry.Mesh;
        if (Owner)
        {
            foreach (var Filter in Owner.GetComponents<MeshFilter>())
                if (Filter && ReferenceEquals(Filter.sharedMesh, Mesh)) Filter.sharedMesh = null;
            foreach (var Collider in Owner.GetComponents<MeshCollider>())
                if (Collider && ReferenceEquals(Collider.sharedMesh, Mesh)) Collider.sharedMesh = null;
            if (ReferenceEquals(Owner._mesh, Mesh)) Owner._mesh = null!;
        }
        if (Mesh)
        {
            if (Immediate) UnityEngine.Object.DestroyImmediate(Mesh);
            else UnityEngine.Object.Destroy(Mesh);
        }
        Owned.RemoveAt(Index);
    }

    static void Capture(HeightmapMesh Owner, Mesh? Previous)
    {
        if (!Owner || Previous) return;
        var Mesh = Owner._mesh;
        if (Mesh is null || !Mesh) return;
        var Root = ActiveGeneration?.Owner(Owner.transform) ?? NativeFeatureGeneration.ActiveFeatureRoot(Owner.transform);
        var Run = Map.CurrentRun;
        if (!Root || Run is null) return;
        foreach (var Entry in Owned)
            if (ReferenceEquals(Entry.Mesh, Mesh)) return;
        Owned.Add(new OwnedMesh(Run, Run.SceneHandle, Root, Owner, Mesh));
    }

    [HarmonyPatch(typeof(HeightmapMesh), nameof(HeightmapMesh.Generate))]
    static class HeightmapMeshGenerationPatch
    {
        [HarmonyPrefix]
        static void Prefix(HeightmapMesh __instance, out Mesh? __state) => __state = __instance._mesh;

        [HarmonyFinalizer]
        static Exception? Finalizer(HeightmapMesh __instance, Mesh? __state, Exception? __exception)
        {
            Capture(__instance, __state);
            return __exception;
        }
    }
}