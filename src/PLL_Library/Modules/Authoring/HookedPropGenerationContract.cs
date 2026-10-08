using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal sealed class HookedPropGenerationContract
{
    abstract class ExecutionSource
    {
        internal abstract byte Kind { get; }
        internal abstract Component Target { get; }
        internal abstract (PropGrouper Grouper, Transform Root) Resolve(Transform Root, BiomeId Id);
        internal abstract void Validate(Transform Root, BiomeId Id);
        internal abstract void WriteManifest(BinaryWriter Writer);
    }

    sealed class SpawnerSource : ExecutionSource
    {
        readonly ComponentBinding<PropSpawner> Spawner;
        readonly ComponentBinding<PropGrouper> Grouper;
        readonly byte RunnerKind;

        internal SpawnerSource(Transform Root, PropSpawner Spawner, PropGrouper Grouper, byte Kind)
        {
            this.Spawner = ComponentBinding<PropSpawner>.Create(Root, Spawner);
            this.Grouper = ComponentBinding<PropGrouper>.Create(Root, Grouper);
            RunnerKind = Kind;
        }

        internal override byte Kind => RunnerKind;
        internal override Component Target => Spawner.Source;
        internal override (PropGrouper Grouper, Transform Root) Resolve(Transform Root, BiomeId Id) =>
            (Grouper.Resolve(Root, Id, "Hooked generation grouper"),
                Spawner.Resolve(Root, Id, "Hooked generation spawner").transform);
        internal override void Validate(Transform Root, BiomeId Id)
        {
            Spawner.Validate(Root, Id, "Hooked generation spawner");
            Grouper.Validate(Root, Id, "Hooked generation grouper");
        }
        internal override void WriteManifest(BinaryWriter Writer)
        {
            Spawner.WriteManifest(Writer); Grouper.WriteManifest(Writer);
        }
    }

    sealed class GrouperSource : ExecutionSource
    {
        readonly ComponentBinding<PropGrouper> Grouper;

        internal GrouperSource(Transform Root, PropGrouper Grouper) =>
            this.Grouper = ComponentBinding<PropGrouper>.Create(Root, Grouper);
        internal override byte Kind => 3;
        internal override Component Target => Grouper.Source;
        internal override (PropGrouper Grouper, Transform Root) Resolve(Transform Root, BiomeId Id)
        {
            var Value = Grouper.Resolve(Root, Id, "Hooked generation grouper");
            return (Value, Value.transform);
        }
        internal override void Validate(Transform Root, BiomeId Id) =>
            Grouper.Validate(Root, Id, "Hooked generation grouper");
        internal override void WriteManifest(BinaryWriter Writer) => Grouper.WriteManifest(Writer);
    }

    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PllHookedPropGenerationAuthoring> Authoring;
    readonly ExecutionSource Runner;
    readonly int[] PrefabOrdinals;
    readonly byte[] TargetHash;

    HookedPropGenerationContract(
        AuthoringBindingOwner Owner, PllHookedPropGenerationAuthoring Authoring,
        ExecutionSource Runner, int[] PrefabOrdinals,
        BiomeId Id)
    {
        this.Owner = Owner;
        this.Authoring = ComponentBinding<PllHookedPropGenerationAuthoring>.Create(Owner.BindingRoot, Authoring);
        this.Runner = Runner;
        this.PrefabOrdinals = PrefabOrdinals;
        GenerateOnLoad = Authoring.GenerateOnLoad;
        EmitHook = Authoring.EmitHook;
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true), true))
        {
            Writer.Write(1); Writer.Write("PLL/HookedPropGenerationTarget/v1");
            Writer.Write(Id.Value); Writer.Write(Owner.Generated); Writer.Write(Owner.CandidateIndex);
            Writer.Write(DeterministicObjectIdentity.RelativeComponentPath(Authoring, Owner.BindingRoot, 0));
            Writer.Write(Runner.Kind);
            Writer.Write(DeterministicObjectIdentity.CanonicalRelativeComponentPath(0,
                DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Runner.Target, Owner.BindingRoot)));
        }
        using var Sha = SHA256.Create();
        TargetHash = Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
        StableTargetKey = BitConverter.ToString(TargetHash).Replace("-", "");
    }

    internal string StableTargetKey { get; }
    internal string EmitHook { get; }
    internal bool GenerateOnLoad { get; }
    internal ReadOnlySpan<byte> TargetKeyHash => TargetHash;
    internal bool AllowsPrefab(int Ordinal) => Array.BinarySearch(PrefabOrdinals, Ordinal) >= 0;
    internal Transform SourceRoot => Authoring.Source.transform;
    internal PllHookedPropGenerationAuthoring Source => Authoring.Source;

    internal static HookedPropGenerationContract[] CreateAll(
        PllBiomeDefinition Definition, AuthoringCandidateGraph Graph,
        out GeneratedPropAssetCatalog? Catalog)
    {
        var Id = Definition.BiomeId;
        var Template = Definition.RootPrefab.transform;
        var Values = AuthoringBindingOwner.GraphComponents<PllHookedPropGenerationAuthoring>(
            Template, Id, Graph, "Hooked prop generation source");
        if (Values.Count > 256)
            throw new ArgumentException($"Biome '{Id}' has more than 256 Hooked generation targets.");
        var Roots = new Transform[Values.Count];
        var Runners = new ExecutionSource[Values.Count];
        var UnstableRoots = Values.Count == 0 ? Array.Empty<Transform>()
            : PllHookedPropGenerationSourceContract.UnstableRoots(Definition);
        for (var i = 0; i < Values.Count; i++)
        {
            var Value = Values[i];
            if (Value.Owner.Generated)
                throw new ArgumentException($"Biome '{Id}' Hooked generation target must be a stable source.");
            Runners[i] = ValidateSource(Definition, Value.Value, UnstableRoots);
            Roots[i] = Value.Value.transform;
            for (var j = 0; j < i; j++)
                if (TransformOwnership.Overlaps(Roots[j], Roots[i]))
                    throw new ArgumentException($"Biome '{Id}' Hooked generation sources overlap.");
        }
        Catalog = Definition.PrepareGeneratedPropAssetCatalog(Roots, Graph, out var AllowedPrefabOrdinals);
        var Contracts = new HookedPropGenerationContract[Values.Count];
        var Keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Contracts.Length; i++)
        {
            var Value = Values[i];
            var Contract = new HookedPropGenerationContract(Value.Owner, Value.Value,
                Runners[i], AllowedPrefabOrdinals[i], Id);
            if (!Keys.Add(Contract.StableTargetKey))
                throw new ArgumentException($"Biome '{Id}' has duplicate Hooked generation target identities.");
            Contracts[i] = Contract;
        }
        return Contracts;
    }

    internal HookedPropTarget Resolve(Transform RuntimeRoot, BiomeId Id, FeatureOccurrence Occurrence, int TargetIndex)
    {
        var Marker = Authoring.Resolve(RuntimeRoot, Id, "Hooked generation source");
        var Execution = Runner.Resolve(RuntimeRoot, Id);
        return new HookedPropTarget(this, Marker, Execution.Grouper, Execution.Root, Occurrence, TargetIndex);
    }

    internal void Validate(Transform Template, BiomeId Id, AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Authoring.Source.transform, Id, "Hooked generation source");
        Authoring.Validate(Template, Id, "Hooked generation source");
        Runner.Validate(Template, Id);
        if (Authoring.Source.EmitHook != EmitHook || Authoring.Source.GenerateOnLoad != GenerateOnLoad)
            throw new InvalidOperationException($"Biome '{Id}' changed its Hooked generation source before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); Authoring.WriteManifest(Writer);
        Writer.Write(Runner.Kind); Runner.WriteManifest(Writer);
        Writer.Write(EmitHook); Writer.Write(GenerateOnLoad); Writer.Write(TargetHash);
        Writer.Write(PrefabOrdinals.Length);
        foreach (var Ordinal in PrefabOrdinals) Writer.Write(Ordinal);
    }

    static ExecutionSource ValidateSource(
        PllBiomeDefinition Definition, PllHookedPropGenerationAuthoring Marker, IReadOnlyList<Transform> UnstableRoots)
    {
        HookedPropRuntime.ValidateHook(Marker.EmitHook, nameof(PllHookedPropGenerationAuthoring.EmitHook));
        var Source = PllHookedPropGenerationSourceContract.Validate(Definition, Marker, true, UnstableRoots,
            static Component => Component is IGenConfigStep or Peak.Island.GloomVoiceConfuser or FakeItem or ISpawner
                or IInteractible or IInteractibleConstant or TornadoSpawner or NetworkedObjectSpawner
                or MushroomZombieSpawner or Condor or SpiderManager);
        var Template = Definition.RootPrefab.transform;
        return Source.Spawner is null ? new GrouperSource(Template, Source.Grouper!)
            : new SpawnerSource(Template, Source.Spawner, Source.Grouper!,
                Source.Spawner.GetType() == typeof(PropSpawner) ? (byte)0
                    : Source.Spawner.GetType() == typeof(PropSpawner_Line) ? (byte)1 : (byte)2);
    }
}

internal sealed class HookedPropTarget
{
    internal HookedPropTarget(HookedPropGenerationContract Contract,
        PllHookedPropGenerationAuthoring Authoring, PropGrouper Grouper, Transform Root,
        FeatureOccurrence Occurrence, int TargetIndex)
    {
        this.Contract = Contract;
        this.Authoring = Authoring;
        this.Grouper = Grouper;
        this.Root = Root;
        this.Occurrence = Occurrence; this.TargetIndex = TargetIndex;
        Roots = new NativeFeatureGeneration.FeatureRoots(new[] { Root });
    }

    internal HookedPropGenerationContract Contract { get; }
    internal PllHookedPropGenerationAuthoring Authoring { get; }
    internal PropGrouper Grouper { get; }
    internal Transform Root { get; }
    internal NativeFeatureGeneration.FeatureRoots Roots { get; }
    internal FeatureOccurrence Occurrence { get; }
    internal int TargetIndex { get; }
    internal GeneratedBatchKey BatchKey => GeneratedBatchKey.Hooked(Occurrence.RequestIndex, TargetIndex);
}
