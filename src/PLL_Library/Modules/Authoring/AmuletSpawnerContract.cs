using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class AmuletSpawnerContract
{
    readonly ComponentBinding<PllAmuletSpawnerAuthoring> Component;
    readonly ComponentBinding<Peak.PropSpawner_AmuletStatues> Generator;
    readonly ComponentBinding<Biome> BiomeMarker;
    readonly PllZoneVolume Volume;
    readonly PllAmuletOpportunityCategory AuthoredCategory;
    readonly PllAmuletPresentation Presentation;
    readonly TransformBinding? Anchor;
    readonly Material[] TerrainMaterials;

    AmuletSpawnerContract(Transform Root, PllAmuletSpawnerAuthoring Source,
        PllBiomeBehavior Behavior, Material[] TerrainMaterials)
    {
        Component = ComponentBinding<PllAmuletSpawnerAuthoring>.Create(Root, Source);
        var Steps = Source.GetComponentsInChildren<LevelGenStep>(true);
        if (Steps.Length != 1 || Steps[0] is not Peak.PropSpawner_AmuletStatues Spawner
            || Spawner.GetType() != typeof(Peak.PropSpawner_AmuletStatues)
            || Source.GetComponentsInChildren<Biome>(true) is not { Length: 1 } Markers)
            throw new ArgumentException("An amulet source requires its compiled native generator and biome marker.", nameof(Source));
        Generator = ComponentBinding<Peak.PropSpawner_AmuletStatues>.Create(Root, Spawner);
        BiomeMarker = ComponentBinding<Biome>.Create(Root, Markers[0]);
        Volume = Source.Volume;
        AuthoredCategory = Source.Category;
        Presentation = Source.Presentation;
        Category = PllAmuletSpawnerAuthoring.ResolveCategory(Source.Category, Behavior);
        this.TerrainMaterials = TerrainMaterials;
        Key = DeterministicObjectIdentity.RelativeComponentStructuralPath(Source, Root);
        if (Presentation is PllCustomAmuletPedestal Custom)
        {
            Custom.RequireValid();
            Anchor = TransformBinding.Create(Custom.ModelPrefab.transform, Custom.AmuletAnchor);
        }
        else if (Presentation is not PllNativeAmuletPedestal)
            throw new ArgumentException("The amulet presentation is unsupported.", nameof(Source));
    }

    internal int Category { get; }
    internal string Key { get; }
    internal PllAmuletSpawnerAuthoring Source => Component.Source;
    internal PllAmuletPresentation Pedestal => Presentation;

    internal static AmuletSpawnerContract[] CreateAll(PllBiomeDefinition Definition,
        Transform Root, AuthoringCandidateGraph Graph, out int OpportunityCoverage)
    {
        var Floor = Definition.Anchors.FloorRoot.transform;
        var Sources = Root.GetComponentsInChildren<PllAmuletSpawnerAuthoring>(true);
        foreach (var Candidate in Graph.Ordered)
            if (Candidate.GetComponentInChildren<PllAmuletSpawnerAuthoring>(true))
                throw new ArgumentException("An amulet source cannot be a generated candidate.", nameof(Definition));
        if (Sources.Length == 0)
        {
            OpportunityCoverage = 0;
            return Array.Empty<AmuletSpawnerContract>();
        }
        var Materials = CaptureTerrainMaterials(Floor, Graph);
        if (Materials.Length == 0)
            throw new ArgumentException("An amulet source requires material-bearing physical terrain.", nameof(Definition));
        var Contracts = new AmuletSpawnerContract[Sources.Length];
        for (var Index = 0; Index < Sources.Length; Index++)
        {
            var Source = Sources[Index];
            if (!Source.enabled || Source.transform == Floor
                || !TransformOwnership.Contains(Floor, Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(Floor, Source.transform))
                throw new ArgumentException("An amulet source must be enabled beneath FloorRoot and outside generated storage.", nameof(Definition));
            ZoneVolumeContract.Validate(Source.Volume, Definition.BiomeId, Source.name, true);
            Contracts[Index] = new AmuletSpawnerContract(Root, Source, Definition.Behavior, Materials);
        }
        Array.Sort(Contracts, (Left, Right) => StringComparer.Ordinal.Compare(Left.Key, Right.Key));
        var Potential = PllAmuletSpawnerAuthoring.CapturePotential(Floor, Definition.VariationDefinitions,
            Contracts, static Source => (Source.Source.transform, Source.Category));
        if (Potential.Maximum > 1)
            throw new ArgumentException("An occurrence can contain only one selected amulet source.", nameof(Definition));
        OpportunityCoverage = Potential.Coverage;
        return Contracts;
    }

    internal ResolvedAmuletSpawner Resolve(Transform Root, BiomeId Id) => new(
        this, Component.Resolve(Root, Id, "amulet source"),
        Generator.Resolve(Root, Id, "amulet generator"),
        BiomeMarker.Resolve(Root, Id, "amulet biome marker"));

    internal void Validate(Transform Root, BiomeId Id)
    {
        Component.Validate(Root, Id, "amulet source");
        Generator.Validate(Root, Id, "amulet generator");
        BiomeMarker.Validate(Root, Id, "amulet biome marker");
        var Current = Source.Volume;
        if (Source.Category != AuthoredCategory || Current.Shape != Volume.Shape || Current.Center != Volume.Center
            || Current.Size != Volume.Size || Current.Radius != Volume.Radius
            || !ReferenceEquals(Source.Presentation, Presentation))
            throw new InvalidOperationException($"Biome '{Id}' changed its sealed amulet source.");
        if (Presentation is PllCustomAmuletPedestal Custom)
        {
            Custom.RequireValid();
            Anchor!.Validate(Custom.ModelPrefab.transform, Id.Value, "amulet anchor");
        }
    }

    internal void Configure(ResolvedAmuletSpawner Resolved, MapHandler Handler,
        GameObject[] Candidates, Material? NativeSentinel)
    {
        var Spawner = Resolved.Generator;
        var Root = Resolved.Authoring.transform;
        var X = Volume.Shape == PllZoneShape.Box ? Volume.Size.x : Volume.Radius * 2f;
        var Y = Volume.Shape == PllZoneShape.Box ? Volume.Size.y : Volume.Radius * 2f;
        var Z = Volume.Shape == PllZoneShape.Box ? Volume.Size.z : Volume.Radius * 2f;
        Spawner.area = new Vector2(Root.TransformVector(Vector3.right * X).magnitude,
            Root.TransformVector(Vector3.forward * Z).magnitude);
        Spawner.rayLength = Root.TransformVector(Vector3.up * Y).magnitude;
        Spawner.statueIndex = Category;
        Spawner.props = Candidates;
        foreach (var Constraint in Spawner.constraints)
            if (Constraint is PSC_RequiredMaterial Required) Required.RequiredMaterial = TerrainMaterials;
        Spawner.ReplaceMaterial = Presentation is PllNativeAmuletPedestal
            || Presentation is PllCustomAmuletPedestal { NativeSurfaceMatchMaterial: not null };
        Spawner.replaceThis = Presentation is PllCustomAmuletPedestal Custom
            ? Custom.NativeSurfaceMatchMaterial : NativeSentinel;
        Resolved.Biome.mapHandler = Handler;
    }

    internal Transform ResolveAnchor(GameObject Model) => Anchor!.Resolve(Model.transform);

    internal void WriteManifest(BinaryWriter Writer)
    {
        Component.WriteManifest(Writer); Generator.WriteManifest(Writer); BiomeMarker.WriteManifest(Writer);
        Writer.Write(Category); ZoneVolumeContract.WriteManifest(Writer, Volume);
        Writer.Write(Presentation is PllCustomAmuletPedestal);
        if (Presentation is not PllCustomAmuletPedestal Custom) return;
        Anchor!.WriteManifest(Writer);
        Writer.Write(Custom.NativeSurfaceMatchMaterial is not null);
    }

    static Material[] CaptureTerrainMaterials(Transform Floor, AuthoringCandidateGraph Graph)
    {
        var Materials = new List<Material>();
        var SeenMaterials = new HashSet<Material>();
        var Pending = new Queue<GameObject>();
        var Seen = new HashSet<GameObject>();
        Pending.Enqueue(Floor.gameObject); Seen.Add(Floor.gameObject);
        while (Pending.Count != 0)
        {
            var Root = Pending.Dequeue();
            foreach (var Collider in Root.GetComponentsInChildren<Collider>(true))
            {
                if (!Collider.enabled || Collider.isTrigger || Collider.attachedRigidbody
                    || Collider.GetComponentInParent<FakeItem>(true)
                    || Collider.GetComponentInParent<global::Luggage>(true)
                    || Collider.GetComponentInParent<Peak.ScoutStatue>(true)
                    || Collider.GetComponentInParent<PllAmuletSpawnerAuthoring>(true)) continue;
                foreach (var Renderer in Collider.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var Available = true;
                    for (var Node = Renderer.transform; Node != Collider.transform; Node = Node.parent)
                        if (!Node.gameObject.activeSelf && !Node.GetComponent<VariantObject>()) Available = false;
                    if (!Available) continue;
                    foreach (var Material in Renderer.sharedMaterials)
                        if (Material && SeenMaterials.Add(Material)) Materials.Add(Material);
                }
            }
            foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
            {
                if (Spawner.GetComponentInParent<PllAmuletSpawnerAuthoring>(true)
                    || NativePrefabContracts.EffectiveProps(Spawner) is not { } Candidates) continue;
                foreach (var Candidate in Candidates)
                    if (Graph.Contains(Candidate) && Seen.Add(Candidate)) Pending.Enqueue(Candidate);
            }
        }
        return Materials.ToArray();
    }

}

internal sealed class AmuletSpawnerPlan
{
    readonly AmuletSpawnerContract[] Contracts;
    NativeAmuletSourceContract? Provider;

    AmuletSpawnerPlan(AmuletSpawnerContract[] Contracts, int Coverage)
    {
        this.Contracts = Contracts;
        OpportunityCoverage = Coverage;
        var Bindings = new string[Contracts.Length];
        for (var Index = 0; Index < Bindings.Length; Index++) Bindings[Index] = "authored:" + Contracts[Index].Key;
        SourceBindings = Array.AsReadOnly(Bindings);
    }

    internal int OpportunityCoverage { get; }
    internal IReadOnlyList<string> SourceBindings { get; }

    internal static AmuletSpawnerPlan? Create(PllBiomeDefinition Definition,
        Transform Root, AuthoringCandidateGraph Graph)
    {
        var Contracts = AmuletSpawnerContract.CreateAll(Definition, Root, Graph, out var Coverage);
        return Contracts.Length == 0 ? null : new AmuletSpawnerPlan(Contracts, Coverage);
    }

    internal void BindProvider(NativeAmuletSourceContract Provider) => this.Provider = Provider;
    internal void ClearExternal() => Provider = null;

    internal void Configure(Transform Root, BiomeId Id, MapRun Run, MapSlot Owner)
    {
        var Recipe = Run.AmuletRecipe(Provider
            ?? throw new InvalidOperationException("The native amulet provider was not sealed before placement."));
        for (var Index = 0; Index < Contracts.Length; Index++)
        {
            var Resolved = Contracts[Index].Resolve(Root, Id);
            Recipe.Configure(Resolved, Run.Handler!);
            Run.Quicksave.EnrollAmuletSource(Owner, Resolved.Generator, SourceBindings[Index]);
        }
    }

    internal void Validate(Transform Root, BiomeId Id)
    {
        foreach (var Contract in Contracts) Contract.Validate(Root, Id);
    }

    internal void WriteManifest(BinaryWriter Writer, bool External)
    {
        Writer.Write("PLL/AmuletSpawner/v1"); Writer.Write(Contracts.Length);
        Writer.Write(OpportunityCoverage);
        foreach (var Contract in Contracts) Contract.WriteManifest(Writer);
        if (External) (Provider
            ?? throw new InvalidOperationException("The native amulet provider has no live compatibility seal."))
            .WriteFactoryFingerprint(Writer);
    }
}

internal readonly struct ResolvedAmuletSpawner
{
    internal ResolvedAmuletSpawner(AmuletSpawnerContract Contract,
        PllAmuletSpawnerAuthoring Authoring, Peak.PropSpawner_AmuletStatues Generator, Biome Biome)
    {
        this.Contract = Contract; this.Authoring = Authoring;
        this.Generator = Generator; this.Biome = Biome;
    }

    internal AmuletSpawnerContract Contract { get; }
    internal PllAmuletSpawnerAuthoring Authoring { get; }
    internal Peak.PropSpawner_AmuletStatues Generator { get; }
    internal Biome Biome { get; }
}
