using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class AuthoringBindingOwner
{
    readonly ReadOnlyMemory<byte> CandidateFingerprint;

    AuthoringBindingOwner(GameObject sourceRoot, bool generated, int candidateIndex, ReadOnlyMemory<byte> CandidateFingerprint)
    {
        SourceRoot = sourceRoot;
        Generated = generated;
        CandidateIndex = candidateIndex;
        this.CandidateFingerprint = CandidateFingerprint;
    }

    internal GameObject SourceRoot { get; }
    internal Transform BindingRoot => SourceRoot.transform;
    internal bool Generated { get; }
    internal int CandidateIndex { get; }

    internal static List<(AuthoringBindingOwner Owner, string Key, T Value)> GraphComponents<T>(
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph,
        string Name) where T : Component
    {
        var Result = new List<(AuthoringBindingOwner, string, T)>();
        Add(Template.gameObject);
        foreach (var Candidate in Graph.Ordered) Add(Candidate);
        Result.Sort((Left, Right) =>
        {
            var Owner = Left.Item1.CandidateIndex.CompareTo(Right.Item1.CandidateIndex);
            return Owner != 0 ? Owner : StringComparer.Ordinal.Compare(Left.Item2, Right.Item2);
        });
        return Result;

        void Add(GameObject GraphRoot)
        {
            foreach (var Value in GraphRoot.GetComponentsInChildren<T>(true))
            {
                if (!Value) throw new ArgumentException($"Biome '{Id}' contains a destroyed {Name}.", nameof(Template));
                var Owner = Create(Template, Graph, Value.transform, Id, Name);
                if (!ReferenceEquals(Owner.SourceRoot, GraphRoot)) continue;
                Result.Add((Owner, DeterministicObjectIdentity.RelativeComponentPath(Value, Owner.BindingRoot, 0), Value));
            }
        }
    }

    internal static AuthoringBindingOwner Create(
        Transform template,
        AuthoringCandidateGraph graph,
        Transform value,
        BiomeId id,
        string name)
    {
        if (!value) throw new ArgumentException($"Biome '{id}' contains a destroyed {name}.", nameof(value));
        if (TransformOwnership.Contains(template, value))
            return new AuthoringBindingOwner(template.gameObject, false, -1, Array.Empty<byte>());

        GameObject? owner = null;
        var ownerIndex = -1;
        for (var i = 0; i < graph.Ordered.Count; i++)
        {
            var candidate = graph.Ordered[i];
            if (!candidate || !TransformOwnership.Contains(candidate.transform, value)) continue;
            if (owner)
                throw new ArgumentException($"Biome '{id}' {name} is ambiguously owned by candidate prefabs.", nameof(value));
            owner = candidate;
            ownerIndex = i;
        }
        if (!owner)
            throw new ArgumentException($"Biome '{id}' {name} is outside its root and reachable candidate graph.", nameof(value));
        return new AuthoringBindingOwner(owner, true, ownerIndex, graph.CaptureFingerprint(ownerIndex));
    }

    internal void Validate(
        Transform template,
        AuthoringCandidateGraph graph,
        Transform value,
        BiomeId id,
        string name)
    {
        if (!SourceRoot || !value)
            throw new InvalidOperationException($"Biome '{id}' destroyed its {name} owner before sealing.");
        if (!Generated)
        {
            if (SourceRoot.transform != template || !TransformOwnership.Contains(template, value))
                throw new InvalidOperationException($"Biome '{id}' changed its {name} root ownership before sealing.");
            return;
        }
        if (CandidateIndex < 0 || CandidateIndex >= graph.Ordered.Count
            || !ReferenceEquals(graph.Ordered[CandidateIndex], SourceRoot)
            || !TransformOwnership.Contains(SourceRoot.transform, value))
            throw new InvalidOperationException($"Biome '{id}' changed its {name} candidate ownership before sealing.");
        if (!graph.CaptureFingerprint(CandidateIndex).Span.SequenceEqual(CandidateFingerprint.Span))
            throw new InvalidOperationException($"Biome '{id}' changed its {name} candidate before sealing.");
    }

    internal bool TryGetAdmittedGeneratedRoot(Transform Value, out Transform Root)
    {
        Root = null!;
        if (!Generated || !Value) return false;
        return Map.CurrentRun?.HookedGeneration is { } Hooked
            && Hooked.TryGetGeneratedRoot(Value, SourceRoot, out Root)
            || GeneratedPropOutputCapture.TryGetNativeGeneratedRoot(Value, SourceRoot, out Root);
    }

    internal bool TryGetGeneratedRoot(Transform value, out Transform root)
    {
        root = null!;
        if (!Generated || !value) return false;
        if (Map.CurrentRun?.HookedGeneration is { } Hooked
            && Hooked.TryGetGeneratedRoot(value, SourceRoot, out root)) return true;
        for (var current = value; current && current.parent; current = current.parent)
        {
            if (!current.parent.GetComponent<PropSpawner>()) continue;
            if (!SameRuntimeName(current.name, SourceRoot.name)) return false;
            root = current;
            return true;
        }
        return false;
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        writer.Write(Generated);
        writer.Write(CandidateIndex);
        if (!Generated) return;
        writer.Write(SourceRoot.name ?? string.Empty);
        writer.Write(CandidateFingerprint.Length);
        writer.Write(CandidateFingerprint.Span);
    }

    static bool SameRuntimeName(string runtime, string source) =>
        string.Equals(runtime, source, StringComparison.Ordinal)
        || string.Equals(runtime, source + "(Clone)", StringComparison.Ordinal);
}

internal sealed class ComponentBinding<T> where T : Component
{
    readonly TransformBinding transform;
    readonly int ordinal;
    readonly int count;

    ComponentBinding(T source, TransformBinding transform, int ordinal, int count)
    {
        Source = source;
        this.transform = transform;
        this.ordinal = ordinal;
        this.count = count;
    }

    internal T Source { get; }

    internal static ComponentBinding<T> Create(Transform template, T source)
    {
        if (!source) throw new ArgumentException("An authoring component was destroyed.", nameof(source));
        var values = source.GetComponents<T>();
        var ordinal = Array.IndexOf(values, source);
        if (ordinal < 0) throw new ArgumentException("The authoring component is not attached to its GameObject.", nameof(source));
        return new ComponentBinding<T>(source, TransformBinding.Create(template, source.transform), ordinal, values.Length);
    }

    internal bool TryMatchGenerated(
        AuthoringBindingOwner Owner,
        T Source,
        BiomeId Id,
        string Name,
        out Transform Root)
    {
        Root = null!;
        if (!Source || !Owner.TryGetGeneratedRoot(Source.transform, out Root)) return false;
        try
        {
            if (ReferenceEquals(Source, Resolve(Root, Id, Name))) return true;
        }
        catch (InvalidOperationException) { }
        Root = null!;
        return false;
    }

    internal T Resolve(Transform root, BiomeId id, string name)
    {
        var owner = transform.ResolveValidated(root, id.Value, name);
        var values = owner.GetComponents<T>();
        if (values.Length != count || ordinal >= values.Length || !values[ordinal])
            throw new InvalidOperationException($"Biome '{id}' runtime changed its {name} component binding.");
        return values[ordinal];
    }

    internal void Validate(Transform root, BiomeId id, string name)
    {
        if (!Source) throw new InvalidOperationException($"Biome '{id}' destroyed its {name} component.");
        transform.Validate(root, id.Value, name);
        var values = Source.GetComponents<T>();
        if (values.Length != count || ordinal >= values.Length || !ReferenceEquals(values[ordinal], Source))
            throw new InvalidOperationException($"Biome '{id}' changed its {name} component binding before sealing.");
    }

    internal void WriteManifest(BinaryWriter writer)
    {
        transform.WriteManifest(writer);
        writer.Write(ordinal); writer.Write(count);
    }
}

internal sealed class ObjectBinding
{
    readonly TransformBinding transform;

    ObjectBinding(GameObject source, TransformBinding transform)
    {
        Source = source;
        this.transform = transform;
    }

    internal GameObject Source { get; }
    internal static ObjectBinding Create(Transform template, GameObject source) => new(
        source ? source : throw new ArgumentException("An authoring root was destroyed.", nameof(source)),
        TransformBinding.Create(template, source.transform));
    internal GameObject Resolve(Transform root, BiomeId id, string name) =>
        transform.ResolveValidated(root, id.Value, name).gameObject;
    internal void Validate(Transform root, BiomeId id, string name)
    {
        if (!Source) throw new InvalidOperationException($"Biome '{id}' destroyed its {name}.");
        transform.Validate(root, id.Value, name);
    }
    internal void WriteManifest(BinaryWriter writer) => transform.WriteManifest(writer);
}
