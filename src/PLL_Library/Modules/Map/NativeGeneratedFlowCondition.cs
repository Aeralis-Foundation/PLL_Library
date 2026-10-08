using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal readonly struct ActorPlacementIdentity
{
    internal ActorPlacementIdentity(string GenerationOwner, int Revision, int GenerationSeed, int Ordinal)
    {
        this.GenerationOwner = GenerationOwner; this.Revision = Revision;
        this.GenerationSeed = GenerationSeed; this.Ordinal = Ordinal;
    }
    internal string GenerationOwner { get; }
    internal int Revision { get; }
    internal int GenerationSeed { get; }
    internal int Ordinal { get; }
}

[DisallowMultipleComponent]
internal sealed class PllGeneratedPrefabIdentity : MonoBehaviour
{
    NativeFeatureGeneration.NativeSpawnReservation? Reservation;
    internal GameObject Prefab { get; private set; } = null!;
    internal ActorPlacementIdentity? ActorPlacement { get; private set; }
    internal void PublishActorIdentity(ActorPlacementIdentity Identity)
    {
        if (ActorPlacement is not null) return;
        ActorPlacement = Identity;
    }
    internal void Bind(GameObject Source, NativeFeatureGeneration.NativeSpawnReservation? Reservation = null)
    {
        Prefab = Source; this.Reservation = Reservation;
    }
    internal void Release()
    {
        var Owned = Reservation; Reservation = null;
        Owned?.ReleaseOutput();
    }
    void OnDestroy() => Release();
}

[DisallowMultipleComponent]
internal sealed class PllNativeFlowCondition : MonoBehaviour
{
    NativeFlowRuntime[] Flows = Array.Empty<NativeFlowRuntime>();
    bool Initialized;

    internal GameObject OwnerPrefab { get; private set; } = null!;
    internal void Bind(GameObject Prefab, NativeFlowRuntime[] Sources)
    {
        OwnerPrefab = Prefab; Flows = Sources;
    }
    internal IReadOnlyList<NativeFlowRuntime> Sources => Flows;

    internal bool Initialize(PropSpawner.SpawnData Data)
    {
        if (Initialized)
            throw new InvalidOperationException("A generated native flow initialized more than once.");
        var SelectedCount = 0;
        foreach (var Flow in Flows)
            if (Flow.SourceTransform.gameObject.activeInHierarchy) Flows[SelectedCount++] = Flow;
            else Flow.Dispose();
        if (SelectedCount != Flows.Length) Array.Resize(ref Flows, SelectedCount);
        if (SelectedCount != 0)
        {
            Physics.SyncTransforms();
            foreach (var Flow in Flows)
                if (!Flow.Initialize(Data, NativePropSpawnerGeneration.TraceFlowSurface))
                {
                    Release(); return false;
                }
        }
        Initialized = true;
        return true;
    }

    internal void WriteRealizedOutput(BinaryWriter Writer)
    {
        if (!Initialized)
            throw new InvalidOperationException("A generated native flow was not initialized before output publication.");
        Writer.Write(Flows.Length);
        foreach (var Flow in Flows) Flow.WriteRealizedOutput(Writer);
    }

    internal void Release()
    {
        var Owned = Flows; Flows = Array.Empty<NativeFlowRuntime>();
        List<Exception>? Failures = null;
        for (var i = Owned.Length - 1; i >= 0; i--)
            AuthoringCleanup.Capture(ref Failures, Owned[i].Dispose);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null) throw new AggregateException("Native flow cleanup failed.", Failures);
    }

    void OnDestroy() => Release();
}
