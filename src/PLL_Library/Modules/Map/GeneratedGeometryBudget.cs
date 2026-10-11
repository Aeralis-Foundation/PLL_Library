using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class GeneratedGeometryBudget
{
    readonly Dictionary<NativeFlowRuntime, (int Instances, int Nodes, int Meshes, long Bytes)> Reservations = new();
    readonly Dictionary<object, (int Instances, int Nodes, int Meshes, long Bytes)> NativeOutputs = new();
    int Instances, Nodes, Meshes;
    long Bytes;
    string? RealizedFingerprint;
    internal int SourceCount => Reservations.Count;
    internal long RemainingMeshBytes => 64L * 1024 * 1024 - Bytes;

    internal void Reserve(NativeFlowRuntime Owner, int Instances, int Nodes, int Meshes, long Bytes)
    {
        Reservations.TryGetValue(Owner, out var Previous);
        Replace(Previous, (Instances, Nodes, Meshes, Bytes));
        Reservations[Owner] = (Instances, Nodes, Meshes, Bytes);
        RealizedFingerprint = null;
    }

    internal void Release(NativeFlowRuntime Owner)
    {
        if (!Reservations.Remove(Owner, out var Previous)) return;
        Instances -= Previous.Instances; Nodes -= Previous.Nodes;
        Meshes -= Previous.Meshes; Bytes -= Previous.Bytes;
        RealizedFingerprint = null;
    }

    internal void ReserveNativeOutput(object Owner, int Instances, int Nodes, int Meshes, long Bytes)
    {
        NativeOutputs.TryGetValue(Owner, out var Previous);
        Replace(Previous, (Instances, Nodes, Meshes, Bytes));
        NativeOutputs[Owner] = (Instances, Nodes, Meshes, Bytes);
    }

    void Replace((int Instances, int Nodes, int Meshes, long Bytes) Previous,
        (int Instances, int Nodes, int Meshes, long Bytes) Current)
    {
        var TotalInstances = checked(Instances - Previous.Instances + Current.Instances);
        var TotalNodes = checked(Nodes - Previous.Nodes + Current.Nodes);
        var TotalMeshes = checked(Meshes - Previous.Meshes + Current.Meshes);
        var TotalBytes = checked(Bytes - Previous.Bytes + Current.Bytes);
        if (TotalInstances > 4096)
            throw new InvalidOperationException($"Generated instance count {TotalInstances} exceeds the occurrence limit of 4096.");
        if (TotalNodes > 65536)
            throw new InvalidOperationException($"Generated node count {TotalNodes} exceeds the occurrence limit of 65536.");
        if (TotalMeshes > 256)
            throw new InvalidOperationException($"Generated mesh count {TotalMeshes} exceeds the occurrence limit of 256.");
        if (TotalBytes > 64L * 1024 * 1024)
            throw new InvalidOperationException($"Generated mesh storage {TotalBytes} bytes exceeds the occurrence limit of 67108864 bytes.");
        Instances = TotalInstances; Nodes = TotalNodes;
        Meshes = TotalMeshes; Bytes = TotalBytes;
    }

    internal void ReleaseNativeOutput(object Owner)
    {
        if (!NativeOutputs.Remove(Owner, out var Previous)) return;
        Instances -= Previous.Instances; Nodes -= Previous.Nodes;
        Meshes -= Previous.Meshes; Bytes -= Previous.Bytes;
    }

    internal void SetMeshBytes(NativeFlowRuntime Owner, long Bytes)
    {
        var Current = Reservations[Owner];
        Reserve(Owner, Current.Instances, Current.Nodes, Current.Meshes, Bytes);
    }

    internal string FreezeRealizedOutput(Transform OccurrenceRoot)
    {
        if (RealizedFingerprint is not null) return RealizedFingerprint;
        var Sources = new List<(string Identity, NativeFlowRuntime Owner)>(Reservations.Count);
        foreach (var Owner in Reservations.Keys)
            Sources.Add((DeterministicObjectIdentity.RelativeComponentStructuralPath(Owner.SourceTransform, OccurrenceRoot), Owner));
        Sources.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Identity, Right.Identity));
        using var Sha = System.Security.Cryptography.SHA256.Create();
        using var Stream = new System.Security.Cryptography.CryptoStream(
            System.IO.Stream.Null, Sha, System.Security.Cryptography.CryptoStreamMode.Write);
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write("PLL/OccurrenceRealizedFlows/v1"); Writer.Write(Sources.Count);
            foreach (var Source in Sources) { Writer.Write(Source.Identity); Source.Owner.WriteRealizedOutput(Writer); }
        }
        Stream.FlushFinalBlock();
        return RealizedFingerprint = BitConverter.ToString(Sha.Hash!).Replace("-", string.Empty);
    }

    internal void ReleaseAll()
    {
        var Owners = new NativeFlowRuntime[Reservations.Count]; Reservations.Keys.CopyTo(Owners, 0);
        var OutputOwners = new object[NativeOutputs.Count]; NativeOutputs.Keys.CopyTo(OutputOwners, 0);
        List<Exception>? Failures = null;
        foreach (var Owner in Owners) CleanupOperations.Capture(ref Failures, Owner.Dispose);
        foreach (var Owner in OutputOwners)
            if (Owner is IDisposable Lifetime) CleanupOperations.Capture(ref Failures, Lifetime.Dispose);
        NativeOutputs.Clear(); Instances = Nodes = Meshes = 0; Bytes = 0;
        ThrowFailures(Failures);
    }

    internal static void ThrowFailures(List<Exception>? Failures)
    {
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Native flow cleanup failed.", Failures);
    }
}
