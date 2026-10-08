using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class PropZoneContract
{
    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PllPropZoneAuthoring> Component;
    readonly ObjectBinding Root;
    readonly ComponentBinding<PropSpawner>[] Spawners;
    readonly PllPropZoneConstraint[] Constraints;
    readonly int[] ConstraintIndices;
    readonly PllZoneVolume Volume;
    readonly bool Inverted;

    PropZoneContract(
        AuthoringBindingOwner Owner,
        ComponentBinding<PllPropZoneAuthoring> Component,
        ObjectBinding Root,
        ComponentBinding<PropSpawner>[] Spawners,
        PllPropZoneConstraint[] Constraints,
        int[] ConstraintIndices,
        PllZoneVolume Volume,
        bool Inverted)
    {
        this.Owner = Owner; this.Component = Component; this.Root = Root; this.Spawners = Spawners;
        this.Constraints = Constraints; this.ConstraintIndices = ConstraintIndices;
        this.Volume = Volume; this.Inverted = Inverted;
    }

    internal bool Generated => Owner.Generated;

    internal static PropZoneContract[] CreateAll(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph,
        out HashSet<PropSpawner> Claimed)
    {
        var Entries = AuthoringBindingOwner.GraphComponents<PllPropZoneAuthoring>(
            Template, Id, Graph, "prop-zone authoring");
        var Contracts = new PropZoneContract[Entries.Count];
        Claimed = new HashSet<PropSpawner>();
        for (var Index = 0; Index < Contracts.Length; Index++)
        {
            var Entry = Entries[Index];
            var Source = Entry.Value;
            if (Source.GetComponents<PllPropZoneAuthoring>().Length != 1 || !Source.Root || Source.Spawners.Count == 0)
                throw Invalid(Id, Source, true, "an invalid root or empty spawner set");
            var Spawners = new ComponentBinding<PropSpawner>[Source.Spawners.Count];
            var Constraints = new PllPropZoneConstraint[Spawners.Length];
            var ConstraintIndices = new int[Spawners.Length];
            for (var SpawnerIndex = 0; SpawnerIndex < Spawners.Length; SpawnerIndex++)
            {
                var Spawner = Source.Spawners[SpawnerIndex];
                if (!Spawner || !Claimed.Add(Spawner)
                    || !TransformOwnership.Contains(Entry.Owner.BindingRoot, Spawner.transform))
                    throw Invalid(Id, Source, true, "a missing, foreign, or multiply-owned spawner");
                var MatchCount = 0;
                PllPropZoneConstraint? MatchedConstraint = null;
                var Values = Spawner.constraints;
                if (Values is not null)
                    for (var ConstraintIndex = 0; ConstraintIndex < Values.Count; ConstraintIndex++)
                        if (Values[ConstraintIndex] is PllPropZoneConstraint Candidate
                            && Matches(Candidate, Source.transform, Source.Volume, Source.Inverted))
                        {
                            MatchCount++;
                            MatchedConstraint = Candidate;
                            ConstraintIndices[SpawnerIndex] = ConstraintIndex;
                        }
                if (MatchCount != 1 || MatchedConstraint is null)
                    throw Invalid(Id, Source, true, "an invalid translated constraint");
                Spawners[SpawnerIndex] = ComponentBinding<PropSpawner>.Create(Entry.Owner.BindingRoot, Spawner);
                Constraints[SpawnerIndex] = MatchedConstraint;
            }
            var Contract = new PropZoneContract(
                Entry.Owner,
                ComponentBinding<PllPropZoneAuthoring>.Create(Entry.Owner.BindingRoot, Source),
                ObjectBinding.Create(Entry.Owner.BindingRoot, Source.Root),
                Spawners, Constraints, ConstraintIndices, Source.Volume, Source.Inverted);
            Contract.ValidateResolved(
                Entry.Owner.BindingRoot, Source, Source.Root, false, Floor, Campfire, Id, true);
            Contracts[Index] = Contract;
        }
        return Contracts;
    }

    internal void Validate(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Component.Source.transform, Id, "prop-zone authoring");
        Component.Validate(Owner.BindingRoot, Id, "prop-zone authoring");
        Root.Validate(Owner.BindingRoot, Id, "prop-zone root");
        foreach (var Spawner in Spawners) Spawner.Validate(Owner.BindingRoot, Id, "prop-zone Spawner");
        ValidateResolved(Owner.BindingRoot, Component.Source, Root.Source, false, Floor, Campfire, Id, false);
    }

    internal void ValidateStableRuntime(Transform RuntimeRoot, BiomeId Id)
    {
        var Source = Component.Resolve(RuntimeRoot, Id, "prop-zone authoring");
        var ResolvedRoot = Root.Resolve(RuntimeRoot, Id, "prop-zone root");
        ValidateResolved(RuntimeRoot, Source, ResolvedRoot, true, null, null, Id, false);
    }

    internal bool TryValidateGenerated(PllPropZoneAuthoring Source, BiomeId Id)
    {
        if (!Component.TryMatchGenerated(
                Owner, Source, Id, "generated prop-zone authoring", out var GeneratedRoot)) return false;
        var ResolvedRoot = Root.Resolve(GeneratedRoot, Id, "prop-zone root");
        ValidateResolved(GeneratedRoot, Source, ResolvedRoot, true, null, null, Id, false);
        return true;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); Component.WriteManifest(Writer); Root.WriteManifest(Writer);
        ZoneVolumeContract.WriteManifest(Writer, Volume); Writer.Write(Inverted);
        Writer.Write(Spawners.Length);
        for (var Index = 0; Index < Spawners.Length; Index++)
        {
            Spawners[Index].WriteManifest(Writer); Writer.Write(ConstraintIndices[Index]);
        }
    }

    void ValidateResolved(
        Transform BindingRoot,
        PllPropZoneAuthoring Source,
        GameObject ResolvedRoot,
        bool ResolveBindings,
        Transform? Floor,
        Transform? Campfire,
        BiomeId Id,
        bool Argument)
    {
        ZoneVolumeContract.Validate(Source.Volume, Id, $"prop zone '{Source.name}'", Argument);
        Transform? FeatureRoot = null;
        if (Floor is not null && Campfire is not null)
        {
            FeatureRoot = Owner.Generated ? Owner.BindingRoot
                : TransformOwnership.Contains(Floor, Source.transform) ? Floor
                : TransformOwnership.Contains(Campfire, Source.transform) ? Campfire : null;
            if (!FeatureRoot)
                throw Invalid(Id, Source, Argument, "an invalid feature owner");
        }
        if (!Source.enabled || Source.Root != Source.gameObject || Source.Root != ResolvedRoot
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Source.transform)
            || !Source.Volume.Equals(Volume) || Source.Inverted != Inverted
            || Source.Spawners.Count != Spawners.Length)
            throw Invalid(Id, Source, Argument, "an invalid root, volume, or spawner set");
        for (var Index = 0; Index < Spawners.Length; Index++)
        {
            var Spawner = ResolveBindings
                ? Spawners[Index].Resolve(BindingRoot, Id, "prop-zone Spawner")
                : Spawners[Index].Source;
            var Values = Spawner.constraints;
            if (Source.Spawners[Index] != Spawner
                || FeatureRoot is not null && !TransformOwnership.Contains(FeatureRoot, Spawner.transform)
                || Values is null || ConstraintIndices[Index] >= Values.Count
                || Values[ConstraintIndices[Index]] is not PllPropZoneConstraint Constraint
                || !ResolveBindings && !ReferenceEquals(Constraint, Constraints[Index])
                || Constraint.mute || !Matches(Constraint, Source.transform, Volume, Inverted))
                throw Invalid(Id, Source, Argument, "an invalid translated constraint");
        }
    }

    static bool Matches(
        PllPropZoneConstraint Constraint,
        Transform Root,
        PllZoneVolume Volume,
        bool Inverted) =>
        Constraint.Root == Root && Constraint.Volume.Equals(Volume) && Constraint.Inverted == Inverted;


    static Exception Invalid(BiomeId Id, PllPropZoneAuthoring Source, bool Argument, string Reason) =>
        Argument
            ? new ArgumentException($"Biome '{Id}' prop zone '{Source.name}' has {Reason}.", "Template")
            : new InvalidOperationException($"Biome '{Id}' prop zone '{Source.name}' has {Reason}.");
}
