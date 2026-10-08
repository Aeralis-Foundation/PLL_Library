using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NoSpawnContract
{
    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PLLNoPropSpawn> Component;
    readonly ComponentBinding<Collider>[] Colliders;

    NoSpawnContract(
        AuthoringBindingOwner Owner,
        ComponentBinding<PLLNoPropSpawn> Component,
        ComponentBinding<Collider>[] Colliders)
    {
        this.Owner = Owner; this.Component = Component; this.Colliders = Colliders;
    }

    internal bool Generated => Owner.Generated;

    internal static NoSpawnContract[] CreateAll(Transform Template, BiomeId Id, AuthoringCandidateGraph Graph)
    {
        var Entries = AuthoringBindingOwner.GraphComponents<PLLNoPropSpawn>(
            Template, Id, Graph, "no-prop-spawn marker");
        var Contracts = new NoSpawnContract[Entries.Count];
        for (var Index = 0; Index < Contracts.Length; Index++)
        {
            var Entry = Entries[Index];
            var Source = Entry.Value;
            if (Source.GetComponents<PLLNoPropSpawn>().Length != 1)
                throw Invalid(Id, Source, true);
            var Values = Source.GetComponentsInChildren<Collider>(true);
            var ColliderCount = 0;
            foreach (var Collider in Values)
                if (Collider && Collider.enabled) ColliderCount++;
            var Colliders = new ComponentBinding<Collider>[ColliderCount];
            var ColliderIndex = 0;
            foreach (var Collider in Values)
                if (Collider && Collider.enabled)
                    Colliders[ColliderIndex++] = ComponentBinding<Collider>.Create(
                        Entry.Owner.BindingRoot, Collider);
            var Contract = new NoSpawnContract(
                Entry.Owner,
                ComponentBinding<PLLNoPropSpawn>.Create(Entry.Owner.BindingRoot, Source),
                Colliders);
            Contract.ValidateResolved(Entry.Owner.BindingRoot, Source, Values, false, Id, true);
            Contracts[Index] = Contract;
        }
        return Contracts;
    }

    internal void Validate(Transform Template, BiomeId Id, AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Component.Source.transform, Id, "no-prop-spawn marker");
        Component.Validate(Owner.BindingRoot, Id, "no-prop-spawn marker");
        foreach (var Collider in Colliders) Collider.Validate(Owner.BindingRoot, Id, "no-prop-spawn Collider");
        var Source = Component.Source;
        ValidateResolved(
            Owner.BindingRoot, Source, Source.GetComponentsInChildren<Collider>(true), false, Id, false);
    }

    internal void ValidateStableRuntime(Transform RuntimeRoot, BiomeId Id)
    {
        var Source = Component.Resolve(RuntimeRoot, Id, "no-prop-spawn marker");
        ValidateResolved(
            RuntimeRoot, Source, Source.GetComponentsInChildren<Collider>(true), true, Id, false);
    }

    internal bool TryValidateGenerated(PLLNoPropSpawn Source, BiomeId Id)
    {
        if (!Component.TryMatchGenerated(
                Owner, Source, Id, "generated no-prop-spawn marker", out var GeneratedRoot)) return false;
        ValidateResolved(
            GeneratedRoot, Source, Source.GetComponentsInChildren<Collider>(true), true, Id, false);
        return true;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); Component.WriteManifest(Writer); Writer.Write(Colliders.Length);
        foreach (var Collider in Colliders) Collider.WriteManifest(Writer);
    }

    void ValidateResolved(
        Transform BindingRoot,
        PLLNoPropSpawn Source,
        Collider[] Current,
        bool ResolveBindings,
        BiomeId Id,
        bool Argument)
    {
        if (!Source.enabled || Colliders.Length == 0
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Source.transform))
            throw Invalid(Id, Source, Argument);
        var Covered = 0;
        foreach (var Collider in Current)
        {
            if (!Collider || !Collider.enabled) continue;
            if (Covered >= Colliders.Length) throw Invalid(Id, Source, Argument);
            var Expected = ResolveBindings
                ? Colliders[Covered].Resolve(BindingRoot, Id, "no-prop-spawn Collider")
                : Colliders[Covered].Source;
            if (Collider != Expected || !Expected.enabled
                || !TransformOwnership.Contains(Source.transform, Expected.transform))
                throw Invalid(Id, Source, Argument);
            Covered++;
        }
        if (Covered != Colliders.Length) throw Invalid(Id, Source, Argument);
    }

    static Exception Invalid(BiomeId Id, PLLNoPropSpawn Source, bool Argument) =>
        Argument
            ? new ArgumentException(
                $"Biome '{Id}' no-prop-spawn marker '{Source.name}' has invalid enabled Collider coverage.", "Template")
            : new InvalidOperationException(
                $"Biome '{Id}' no-prop-spawn marker '{Source.name}' changed its enabled Collider coverage.");
}
