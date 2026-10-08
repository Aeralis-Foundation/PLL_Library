using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class ChildPropAuthoringContract
{
    readonly ChildPropContract[] ChildProps;

    ChildPropAuthoringContract(ChildPropContract[] ChildProps) => this.ChildProps = ChildProps;

    internal static ChildPropAuthoringContract Create(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph) =>
        new(ChildPropContract.CreateAll(Template, Floor, Campfire, Id, Graph));

    internal void Validate(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        foreach (var ChildProp in ChildProps) ChildProp.Validate(Template, Floor, Campfire, Id, Graph);
    }

    internal void ValidateStableRuntime(Transform Root, BiomeId Id)
    {
        foreach (var ChildProp in ChildProps) if (!ChildProp.Generated) ChildProp.ValidateStableRuntime(Root, Id);
    }

    internal void ValidateGeneratedRuntime(Transform FeatureRoot, BiomeId Id)
    {
        foreach (var Source in FeatureRoot.GetComponentsInChildren<PllChildPropAuthoring>(true))
        {
            if (!Source || !NativePrefabContracts.HasPropSpawnerAncestor(FeatureRoot, Source.transform)) continue;
            var Matched = false;
            foreach (var Contract in ChildProps)
            {
                if (!Contract.TryValidateGenerated(Source, Id)) continue;
                if (Matched)
                    throw new InvalidOperationException(
                        $"Biome '{Id}' generated child-prop authoring '{Source.name}' matches more than one sealed source contract.");
                Matched = true;
            }
            if (!Matched)
                throw new InvalidOperationException($"Biome '{Id}' generated an unsealed child-prop authoring '{Source.name}'.");
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(ChildProps.Length);
        foreach (var ChildProp in ChildProps) ChildProp.WriteManifest(Writer);
    }

}

internal sealed class ChildPropContract
{
    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PllChildPropAuthoring> Component;
    readonly ObjectBinding Root;
    readonly TransformBinding[] Points;
    readonly ComponentBinding<PropSpawner>[] Spawners;
    readonly ComponentBinding<PropGrouper>?[] Groupers;
    readonly GameObject[] Props;
    readonly int[] PropCandidateIndices;
    readonly float Chance;
    readonly PllChildPropTiming Timing;

    ChildPropContract(
        AuthoringBindingOwner Owner,
        ComponentBinding<PllChildPropAuthoring> Component,
        ObjectBinding Root,
        TransformBinding[] Points,
        ComponentBinding<PropSpawner>[] Spawners,
        ComponentBinding<PropGrouper>?[] Groupers,
        GameObject[] Props,
        int[] PropCandidateIndices,
        float Chance,
        PllChildPropTiming Timing)
    {
        this.Owner = Owner; this.Component = Component; this.Root = Root; this.Points = Points;
        this.Spawners = Spawners; this.Groupers = Groupers; this.Props = Props;
        this.PropCandidateIndices = PropCandidateIndices; this.Chance = Chance; this.Timing = Timing;
    }

    internal bool Generated => Owner.Generated;

    internal static ChildPropContract[] CreateAll(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        var Values = AuthoringBindingOwner.GraphComponents<PllChildPropAuthoring>(Template, Id, Graph, "child-prop authoring");
        var Result = new ChildPropContract[Values.Count];
        for (var i = 0; i < Result.Length; i++) Result[i] = Create(Template, Floor, Campfire, Id, Graph, Values[i]);
        return Result;
    }

    internal void Validate(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Component.Source.transform, Id, "child-prop authoring");
        Component.Validate(Owner.BindingRoot, Id, "child-prop authoring");
        Root.Validate(Owner.BindingRoot, Id, "child-prop root");
        var Source = Component.Source;
        if (!ValidRoot(Source, Owner, Floor, Campfire) || Source.Root != Root.Source
            || !ValidChance(Source.ChancePerPoint) || !Source.ChancePerPoint.Equals(Chance)
            || Source.Timing != Timing || Source.Points.Count != Points.Length || Source.Props.Count != Props.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed child-prop authoring before sealing.");
        for (var i = 0; i < Props.Length; i++)
            if (!Source.Props[i] || !Source.Props[i].activeSelf || Source.Props[i] != Props[i] || PropCandidateIndices[i] < 0
                || PropCandidateIndices[i] >= Graph.Ordered.Count || Graph.Ordered[PropCandidateIndices[i]] != Props[i])
                throw new InvalidOperationException($"Biome '{Id}' changed child-prop candidates before sealing.");
        var Seen = new HashSet<Transform>();
        for (var i = 0; i < Points.Length; i++)
        {
            Points[i].Validate(Owner.BindingRoot, Id.Value, "child-prop point");
            Spawners[i].Validate(Owner.BindingRoot, Id, "translated child PropSpawner");
            Groupers[i]?.Validate(Owner.BindingRoot, Id, "translated child PropGrouper");
            if (!Seen.Add(Points[i].Source) || Source.Points[i] != Points[i].Source
                || !ValidTranslation(Source, Owner.Generated, Points[i].Source, Spawners[i].Source, Groupers[i]?.Source))
                throw new InvalidOperationException($"Biome '{Id}' changed a translated child-prop spawner before sealing.");
        }
    }

    internal void ValidateStableRuntime(Transform RuntimeRoot, BiomeId Id)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{Id}' cannot resolve generated child-prop authoring as stable.");
        ValidateRuntime(RuntimeRoot, null, Id);
    }

    internal bool TryValidateGenerated(PllChildPropAuthoring Source, BiomeId Id)
    {
        if (!Component.TryMatchGenerated(
                Owner, Source, Id, "generated child-prop authoring", out var GeneratedRoot)) return false;
        ValidateRuntime(GeneratedRoot, Source, Id);
        return true;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); Component.WriteManifest(Writer); Root.WriteManifest(Writer);
        ManifestEncoding.WriteScalar(Writer, Chance); Writer.Write((byte)Timing);
        Writer.Write(Props.Length);
        for (var i = 0; i < Props.Length; i++) { Writer.Write(PropCandidateIndices[i]); Writer.Write(Props[i].name ?? string.Empty); }
        Writer.Write(Points.Length);
        for (var i = 0; i < Points.Length; i++)
        {
            Points[i].WriteManifest(Writer); Spawners[i].WriteManifest(Writer);
            Writer.Write(Groupers[i] is not null); Groupers[i]?.WriteManifest(Writer);
        }
    }

    void ValidateRuntime(Transform BindingRoot, PllChildPropAuthoring? Expected, BiomeId Id)
    {
        var Source = Component.Resolve(BindingRoot, Id, "child-prop authoring");
        if (Expected is not null && !ReferenceEquals(Source, Expected))
            throw new InvalidOperationException($"Biome '{Id}' generated child-prop binding changed.");
        var RuntimeRoot = Root.Resolve(BindingRoot, Id, "child-prop root");
        if (!Source.enabled || Source.GetComponents<PllChildPropAuthoring>().Length != 1
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Source.transform)
            || Generated && Source.gameObject != BindingRoot.gameObject
            || Source.Root != RuntimeRoot || !ValidChance(Source.ChancePerPoint) || !Source.ChancePerPoint.Equals(Chance)
            || Source.Timing != Timing || Source.Points.Count != Points.Length || Source.Props.Count != Props.Length)
            throw new InvalidOperationException($"Biome '{Id}' runtime changed sealed child-prop authoring.");
        for (var i = 0; i < Props.Length; i++)
            if (!Source.Props[i] || !Source.Props[i].activeSelf || Source.Props[i] != Props[i])
                throw new InvalidOperationException($"Biome '{Id}' runtime changed sealed child-prop candidates.");
        var Seen = new HashSet<Transform>();
        for (var i = 0; i < Points.Length; i++)
        {
            var Point = Points[i].ResolveValidated(BindingRoot, Id.Value, "child-prop point");
            var RuntimeSpawner = Spawners[i].Resolve(BindingRoot, Id, "translated child PropSpawner");
            var RuntimeGrouper = Groupers[i]?.Resolve(BindingRoot, Id, "translated child PropGrouper");
            if (!Seen.Add(Point) || Source.Points[i] != Point
                || !ValidTranslation(Source, Generated, Point, RuntimeSpawner, RuntimeGrouper))
                throw new InvalidOperationException($"Biome '{Id}' runtime changed a translated child-prop spawner.");
        }
    }

    static ChildPropContract Create(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph,
        (AuthoringBindingOwner Owner, string Key, PllChildPropAuthoring Value) Entry)
    {
        var Source = Entry.Value;
        if (!ValidRoot(Source, Entry.Owner, Floor, Campfire) || Source.Points.Count == 0 || Source.Props.Count == 0
            || !ValidChance(Source.ChancePerPoint) || !Enum.IsDefined(typeof(PllChildPropTiming), Source.Timing))
            throw new ArgumentException($"Biome '{Id}' child-prop authoring '{Source.name}' is incomplete.", nameof(Template));
        var Points = new TransformBinding[Source.Points.Count];
        var Spawners = new ComponentBinding<PropSpawner>[Points.Length];
        var Groupers = new ComponentBinding<PropGrouper>?[Points.Length];
        var SeenPoints = new HashSet<Transform>();
        for (var i = 0; i < Points.Length; i++)
        {
            var Point = Source.Points[i];
            var Child = Point && Point.childCount == 1 ? Point.GetChild(0) : null;
            var Native = Child ? Child.GetComponents<PropSpawner>() : Array.Empty<PropSpawner>();
            var PointGroupers = Point ? Point.GetComponents<PropGrouper>() : Array.Empty<PropGrouper>();
            PropGrouper? Grouper = PointGroupers.Length == 1 ? PointGroupers[0] : null;
            if (!Point || !SeenPoints.Add(Point) || Child is null || Child.name != "__PLL_ChildSpawner" || Native.Length != 1
                || !ValidTranslation(Source, Entry.Owner.Generated, Point, Native[0], Grouper))
                throw new ArgumentException($"Biome '{Id}' child-prop point has an invalid staged translation.", nameof(Template));
            Points[i] = TransformBinding.Create(Entry.Owner.BindingRoot, Point);
            Spawners[i] = ComponentBinding<PropSpawner>.Create(Entry.Owner.BindingRoot, Native[0]);
            if (Grouper) Groupers[i] = ComponentBinding<PropGrouper>.Create(Entry.Owner.BindingRoot, Grouper);
        }
        var Props = new GameObject[Source.Props.Count];
        var CandidateIndices = new int[Props.Length];
        for (var i = 0; i < Props.Length; i++)
        {
            Props[i] = Source.Props[i];
            CandidateIndices[i] = IndexOf(Graph.Ordered, Props[i]);
            if (!Props[i] || !Props[i].activeSelf || CandidateIndices[i] < 0)
                throw new ArgumentException($"Biome '{Id}' child-prop authoring references an unsealed candidate.", nameof(Template));
        }
        return new ChildPropContract(
            Entry.Owner,
            ComponentBinding<PllChildPropAuthoring>.Create(Entry.Owner.BindingRoot, Source),
            ObjectBinding.Create(Entry.Owner.BindingRoot, Source.Root),
            Points, Spawners, Groupers, Props, CandidateIndices, Source.ChancePerPoint, Source.Timing);
    }

    static bool ValidTranslation(
        PllChildPropAuthoring Source,
        bool Generated,
        Transform Point,
        PropSpawner Spawner,
        PropGrouper? Grouper)
    {
        var ExpectedGrouper = Source.Timing == PllChildPropTiming.Late && !Generated;
        if (!NestedTiming(Source.Props, out var ChildTiming)) return false;
        var ImmediateChildren = ChildTiming == PllChildPropTiming.Immediate;
        var LateChildren = ChildTiming == PllChildPropTiming.Late;
        var PointGroupers = Point.GetComponents<PropGrouper>();
        var Child = Point.childCount == 1 ? Point.GetChild(0) : null;
        if (Point == Source.transform || !Point.IsChildOf(Source.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Source.transform, Point)
            || ExpectedGrouper != (bool)Grouper || PointGroupers.Length != (ExpectedGrouper ? 1 : 0)
            || Grouper && (PointGroupers[0] != Grouper || Grouper.timing != PropGrouper.PropGrouperTiming.Late)
            || Point.GetComponents<Component>().Length != (ExpectedGrouper ? 2 : 1)
            || !Child || !Child.gameObject.activeSelf || Child.name != "__PLL_ChildSpawner"
            || Spawner.transform != Child || Child.GetComponents<Component>().Length != 2
            || Child.GetComponents<PropSpawner>().Length != 1)
            return false;
        if (!Spawner.enabled || Spawner.transform.parent != Point || Spawner.transform.localPosition != Vector3.zero
            || Spawner.transform.localRotation != Quaternion.identity || Spawner.transform.localScale != Vector3.one
            || Spawner.area != Vector2.zero || Spawner.raycastPosition || Spawner.nrOfSpawns != 1
            || Spawner.randomSpawns || Spawner.minSpawnCount != 0 || !Spawner.chanceToUseSpawner.Equals(Source.ChancePerPoint)
            || Spawner.props is null || Spawner.props.Length != Source.Props.Count || Spawner.overrideProps
            || Spawner.overridePropsList is not null || Spawner.modifiers is null
            || Spawner.modifiers.Count != (ImmediateChildren ? 2 : 1)
            || Spawner.modifiers[0] is not PllSocketTransformModifier Socket || Socket.mute
            || ImmediateChildren && (Spawner.modifiers[1] is not PSM_ChildSpawners Immediate || Immediate.mute)
            || Spawner.postConstraints is not { Count: 0 } || Spawner.postSpawnBehaviors is null
            || Spawner.postSpawnBehaviors.Count != (LateChildren ? 1 : 0)
            || LateChildren && (Spawner.postSpawnBehaviors[0] is not PSB_ChildSpawners Late || Late.mute)
            || Spawner.constraints is null) return false;
        foreach (var Constraint in Spawner.constraints)
            if (Constraint is not PllNoPropSpawnConstraint || Constraint.mute) return false;
        for (var i = 0; i < Source.Props.Count; i++)
            if (Spawner.props[i] != Source.Props[i]) return false;
        return true;
    }

    static bool NestedTiming(IReadOnlyList<GameObject> Candidates, out PllChildPropTiming? Timing)
    {
        Timing = null;
        foreach (var Candidate in Candidates)
        {
            if (!Candidate) return false;
            var Child = Candidate.GetComponent<PllChildPropAuthoring>();
            if (!Child) continue;
            if (Child.Timing is not (PllChildPropTiming.Immediate or PllChildPropTiming.Late)
                || Timing.HasValue && Timing.Value != Child.Timing) return false;
            Timing = Child.Timing;
        }
        return true;
    }

    static bool ValidRoot(
        PllChildPropAuthoring Source,
        AuthoringBindingOwner Owner,
        Transform Floor,
        Transform Campfire) => Source.enabled
        && Source.GetComponents<PllChildPropAuthoring>().Length == 1
        && Source.Root == Source.gameObject
        && !TransformOwnership.HasPropSpawnerInPath(Owner.BindingRoot, Source.transform)
        && (Owner.Generated ? Source.gameObject == Owner.SourceRoot
            : TransformOwnership.Contains(Floor, Source.transform)
                || TransformOwnership.Contains(Campfire, Source.transform));

    static bool ValidChance(float Value) => Value >= 0f && Value <= 1f && ManifestEncoding.CanEncode(Value);

    static int IndexOf(IReadOnlyList<GameObject> Values, GameObject Target)
    {
        for (var i = 0; i < Values.Count; i++) if (Values[i] == Target) return i;
        return -1;
    }
}
