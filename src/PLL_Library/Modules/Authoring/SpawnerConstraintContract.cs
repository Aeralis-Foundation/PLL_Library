using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class SpawnerConstraintContract
{
    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PropSpawner> Spawner;
    readonly string[] Types;
    readonly bool[] Muted;
    readonly bool ZoneBound;

    SpawnerConstraintContract(
        AuthoringBindingOwner Owner,
        ComponentBinding<PropSpawner> Spawner,
        string[] Types,
        bool[] Muted,
        bool ZoneBound)
    {
        this.Owner = Owner; this.Spawner = Spawner; this.Types = Types; this.Muted = Muted;
        this.ZoneBound = ZoneBound;
    }

    internal bool Generated => Owner.Generated;

    internal static SpawnerConstraintContract[] CreateAll(
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph,
        bool RequireNoSpawn,
        ISet<PropSpawner> ZonedSpawners)
    {
        var Entries = AuthoringBindingOwner.GraphComponents<PropSpawner>(Template, Id, Graph, "PropSpawner");
        var Contracts = new SpawnerConstraintContract[Entries.Count];
        for (var Index = 0; Index < Contracts.Length; Index++)
        {
            var Entry = Entries[Index];
            var Constraints = Entry.Value.constraints ?? throw new ArgumentException(
                $"Biome '{Id}' PropSpawner '{Entry.Value.name}' has a null staged constraint list.", nameof(Template));
            var Types = new string[Constraints.Count];
            var Muted = new bool[Constraints.Count];
            for (var ConstraintIndex = 0; ConstraintIndex < Constraints.Count; ConstraintIndex++)
            {
                var Constraint = Constraints[ConstraintIndex] ?? throw new ArgumentException(
                    $"Biome '{Id}' PropSpawner '{Entry.Value.name}' has a null constraint.", nameof(Template));
                Types[ConstraintIndex] = ConstraintType(Constraint);
                Muted[ConstraintIndex] = Constraint.mute;
            }
            var Contract = new SpawnerConstraintContract(
                Entry.Owner,
                ComponentBinding<PropSpawner>.Create(Entry.Owner.BindingRoot, Entry.Value),
                Types, Muted, ZonedSpawners.Contains(Entry.Value));
            Contract.ValidateResolved(Entry.Value, RequireNoSpawn, Id, true);
            Contracts[Index] = Contract;
        }
        return Contracts;
    }

    internal void Validate(Transform Template, BiomeId Id, AuthoringCandidateGraph Graph, bool RequireNoSpawn)
    {
        Owner.Validate(Template, Graph, Spawner.Source.transform, Id, "PropSpawner");
        Spawner.Validate(Owner.BindingRoot, Id, "PropSpawner");
        ValidateResolved(Spawner.Source, RequireNoSpawn, Id, false);
    }

    internal void ValidateStableRuntime(Transform RuntimeRoot, BiomeId Id, bool RequireNoSpawn)
    {
        ValidateResolved(Spawner.Resolve(RuntimeRoot, Id, "PropSpawner"), RequireNoSpawn, Id, false);
    }

    internal bool TryValidateGenerated(PropSpawner Source, BiomeId Id, bool RequireNoSpawn)
    {
        if (!Spawner.TryMatchGenerated(
                Owner, Source, Id, "generated PropSpawner", out _)) return false;
        ValidateResolved(Source, RequireNoSpawn, Id, false);
        return true;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); Spawner.WriteManifest(Writer); Writer.Write(Types.Length);
        for (var Index = 0; Index < Types.Length; Index++)
        {
            Writer.Write(Types[Index]); Writer.Write(Muted[Index]);
        }
    }

    void ValidateResolved(PropSpawner Source, bool RequireNoSpawn, BiomeId Id, bool Argument)
    {
        var Values = Source.constraints;
        if (Values is null || Values.Count != Types.Length) throw Invalid(Id, Source, Argument);
        var NoSpawn = 0;
        var Zones = 0;
        var SeenZone = false;
        for (var Index = 0; Index < Values.Count; Index++)
        {
            var Value = Values[Index];
            if (Value is null || ConstraintType(Value) != Types[Index] || Value.mute != Muted[Index])
                throw Invalid(Id, Source, Argument);
            if (Value is PllNoPropSpawnConstraint)
            {
                if (SeenZone || Value.mute) throw Invalid(Id, Source, Argument);
                NoSpawn++;
                continue;
            }
            if (Value is PllPropZoneConstraint)
            {
                if (Value.mute || ++Zones > 1) throw Invalid(Id, Source, Argument);
                SeenZone = true;
                continue;
            }
            if (NoSpawn != 0 || SeenZone) throw Invalid(Id, Source, Argument);
        }
        if (NoSpawn != (RequireNoSpawn ? 1 : 0) || Zones != (ZoneBound ? 1 : 0))
            throw Invalid(Id, Source, Argument);
    }

    static string ConstraintType(PropSpawnerConstraint Constraint) =>
        Constraint.GetType().FullName ?? Constraint.GetType().Name;

    static Exception Invalid(BiomeId Id, PropSpawner Spawner, bool Argument) =>
        Argument
            ? new ArgumentException(
                $"Biome '{Id}' PropSpawner '{Spawner.name}' has an invalid generated constraint order or coverage.", "source")
            : new InvalidOperationException(
                $"Biome '{Id}' PropSpawner '{Spawner.name}' changed its sealed constraint order or coverage.");
}
