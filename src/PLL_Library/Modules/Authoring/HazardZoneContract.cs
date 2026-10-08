using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class HazardZoneContract
{
    readonly ComponentBinding<PllHazardZoneAuthoring> Component;
    readonly TransformBinding Output;
    readonly PllZoneVolume Volume;
    readonly Recipe RecipeValue;

    HazardZoneContract(Transform Template, PllHazardZoneAuthoring Source)
    {
        Component = ComponentBinding<PllHazardZoneAuthoring>.Create(Template, Source);
        Output = TransformBinding.Create(Template, Source.OutputRoot); Id = Source.Id; Volume = Source.Volume;
        RecipeValue = Source.Source switch
        {
            PllTornadoZoneSource Tornado => new TornadoRecipe(Tornado),
            PllTumbleweedZoneSource Tumbleweed => new TumbleweedRecipe(Tumbleweed),
            PllAuthoredHazardZoneSource Authored => new AuthoredRecipe(Authored),
            _ => throw new InvalidOperationException("The validated Hazard Zone source is unsupported.")
        };
    }

    internal string Id { get; }
    internal bool RequiresTornados => RecipeValue is TornadoRecipe { Enabled: true };
    internal bool RequiresTumbleweeds => RecipeValue is TumbleweedRecipe { Enabled: true };

    internal static HazardZoneContract[] CreateAll(Transform Template, Transform Floor, Transform Campfire,
        BiomeId Biome, AuthoringCandidateGraph Graph, IReadOnlyList<EntityZoneContract> EntityZones)
    {
        foreach (var Candidate in Graph.Ordered)
            if (Candidate.GetComponentInChildren<PllHazardZoneAuthoring>(true))
                throw new ArgumentException($"Biome '{Biome}' places a Hazard Zone inside a generated candidate.", nameof(Template));
        var Sources = Template.GetComponentsInChildren<PllHazardZoneAuthoring>(true);
        if (Sources.Length == 0) return Array.Empty<HazardZoneContract>();
        var Entries = new List<(string Key, PllHazardZoneAuthoring Source)>(Sources.Length);
        var Ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var Entity in EntityZones) Ids.Add(Entity.Id);
        foreach (var Source in Sources)
        {
            if (PllHazardZoneValueContract.Failure(Source) is { } Failure)
                throw new ArgumentException($"{Failure} Biome '{Biome}', Hazard Zone '{Source.Id}'.", nameof(Source));
            if (PllActorZoneSourceContract.RegisterId(Ids, Source.Id) is { } IdFailure)
                throw new ArgumentException($"{IdFailure} Biome '{Biome}'.", nameof(Source));
            if (PllActorZoneSourceContract.CompiledFailure(Template, Floor, Campfire, Source, Source.OutputRoot) is { } ShapeFailure)
                throw new ArgumentException($"{ShapeFailure} Biome '{Biome}', Hazard Zone '{Source.Id}'.", nameof(Source));
            Entries.Add((DeterministicObjectIdentity.RelativeComponentPath(Source, Template, 0), Source));
        }
        Entries.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Key, Right.Key));
        var Result = new HazardZoneContract[Entries.Count];
        for (var Index = 0; Index < Result.Length; Index++) Result[Index] = new(Template, Entries[Index].Source);
        return Result;
    }

    internal void ResolveExternal(NativeTornadoSource? Tornados, NativeTumbleweedSource? Tumbleweeds)
    {
        switch (RecipeValue)
        {
            case TornadoRecipe Recipe: Recipe.Resolve(Tornados); break;
            case TumbleweedRecipe Recipe: Recipe.Resolve(Tumbleweeds); break;
            case AuthoredRecipe: break;
            default: throw new InvalidOperationException("The sealed Hazard Zone recipe is unsupported.");
        }
    }
    internal void ClearExternal() => RecipeValue.ClearExternal();
    internal HazardZoneBinding Bind(Transform RuntimeRoot, BiomeId Biome)
    {
        var Source = Component.Resolve(RuntimeRoot, Biome, "Hazard Zone"); ValidateMetadata(Source, Biome);
        return Source.Binding = RecipeValue.Bind(Source,
            Output.ResolveValidated(RuntimeRoot, Biome.Value, "Hazard Zone output"), Volume);
    }
    internal void Validate(Transform Template, BiomeId Biome)
    {
        Component.Validate(Template, Biome, "Hazard Zone"); Output.Validate(Template, Biome.Value, "Hazard Zone output");
        ValidateMetadata(Component.Source, Biome);
        if (Component.Source.OutputRoot != Output.Source || Output.Source.childCount != 0)
            throw new InvalidOperationException($"Biome '{Biome}' Hazard Zone '{Id}' changed its compiled output branch.");
    }
    void ValidateMetadata(PllHazardZoneAuthoring Source, BiomeId Biome)
    {
        if (!Source.enabled || Source.Id != Id || !Source.Volume.Equals(Volume) || !RecipeValue.Matches(Source.Source))
            throw new InvalidOperationException($"Biome '{Biome}' Hazard Zone '{Id}' changed its sealed recipe.");
    }
    internal void WriteManifest(BinaryWriter Writer)
    {
        Component.WriteManifest(Writer); Output.WriteManifest(Writer); Writer.Write(Id);
        ZoneVolumeContract.WriteManifest(Writer, Volume); RecipeValue.WriteManifest(Writer);
    }

    abstract class Recipe
    {
        internal abstract HazardZoneBinding Bind(PllHazardZoneAuthoring Zone, Transform Output, PllZoneVolume Volume);
        internal abstract bool Matches(PllHazardZoneSource Source);
        internal abstract void WriteManifest(BinaryWriter Writer);
        internal abstract void ClearExternal();
    }

    abstract class PointRecipe : Recipe
    {
        protected PointRecipe(int MaximumAlive, Vector3[] Points)
        {
            this.MaximumAlive = MaximumAlive; this.Points = (Vector3[])Points.Clone();
        }
        protected int MaximumAlive { get; }
        protected Vector3[] Points { get; }
        internal bool Enabled => MaximumAlive > 0;
        protected bool Matches(int MaximumAlive, Vector3[] Points)
        {
            if (this.MaximumAlive != MaximumAlive || Points is null || this.Points.Length != Points.Length) return false;
            for (var Index = 0; Index < Points.Length; Index++) if (!this.Points[Index].Equals(Points[Index])) return false;
            return true;
        }
        protected void WritePoints(BinaryWriter Writer)
        {
            Writer.Write(MaximumAlive); Writer.Write(Points.Length);
            foreach (var Point in Points) ManifestEncoding.WriteVector(Writer, Point);
        }
    }
    sealed class TornadoRecipe : PointRecipe
    {
        NativeTornadoSource? Provider;
        internal TornadoRecipe(PllTornadoZoneSource Source) : base(Source.MaximumAlive, Source.Points) { }
        internal void Resolve(NativeTornadoSource? Source)
        {
            if (Enabled && Source is null) throw new InvalidOperationException("The Hazard Zone requires PEAK's native Tornado provider.");
            NativeNetworkPrefabGraph.AdmitAliveCount(MaximumAlive);
            Provider = Source;
        }
        internal override bool Matches(PllHazardZoneSource Source) => Source is PllTornadoZoneSource Tornado
            && Matches(Tornado.MaximumAlive, Tornado.Points);
        internal override HazardZoneBinding Bind(PllHazardZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            new HazardZoneBinding.Tornado(Output, Provider, MaximumAlive, Points);
        internal override void WriteManifest(BinaryWriter Writer) { Writer.Write((byte)0); WritePoints(Writer); }
        internal override void ClearExternal() => Provider = null;
    }
    sealed class TumbleweedRecipe : PointRecipe
    {
        readonly Vector3 Offset;
        NativeTumbleweedSource? Provider;
        internal TumbleweedRecipe(PllTumbleweedZoneSource Source) : base(Source.MaximumAlive, Source.Positions) => Offset = Source.SpawnOffset;
        internal void Resolve(NativeTumbleweedSource? Source)
        {
            if (Enabled && Source is null) throw new InvalidOperationException("The Hazard Zone requires PEAK's native Tumbleweed provider.");
            NativeNetworkPrefabGraph.AdmitAliveCount(MaximumAlive);
            Provider = Source;
        }
        internal override bool Matches(PllHazardZoneSource Source) => Source is PllTumbleweedZoneSource Tumbleweed
            && Matches(Tumbleweed.MaximumAlive, Tumbleweed.Positions) && Tumbleweed.SpawnOffset.Equals(Offset);
        internal override HazardZoneBinding Bind(PllHazardZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            new HazardZoneBinding.Tumbleweed(Output, Provider, MaximumAlive, Points, Offset);
        internal override void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write((byte)1); WritePoints(Writer); ManifestEncoding.WriteVector(Writer, Offset);
        }
        internal override void ClearExternal() => Provider = null;
    }

    sealed class AuthoredRecipe : Recipe
    {
        readonly GameObject Prefab;
        readonly string DefinitionId;
        readonly int Count;
        readonly float Chance;
        readonly float Slope;
        readonly float Spacing;
        internal AuthoredRecipe(PllAuthoredHazardZoneSource Source)
        {
            Prefab = Source.Prefab; DefinitionId = Prefab.GetComponent<PllHazardAuthoring>().Id;
            Count = Source.MaximumInitialCount; Chance = Source.SpawnChance;
            Slope = Source.MaximumSlope; Spacing = Source.MinimumSpacing;
        }
        internal override bool Matches(PllHazardZoneSource Source) => Source is PllAuthoredHazardZoneSource Authored
            && Authored.Prefab == Prefab && Prefab.GetComponent<PllHazardAuthoring>().Id == DefinitionId
            && Authored.MaximumInitialCount == Count && Authored.SpawnChance.Equals(Chance)
            && Authored.MaximumSlope.Equals(Slope) && Authored.MinimumSpacing.Equals(Spacing);
        internal override HazardZoneBinding Bind(PllHazardZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            new HazardZoneBinding.Authored(Zone, Output, Volume, Prefab, Count, Chance, Slope, Spacing);
        internal override void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write((byte)2); Writer.Write(DefinitionId); Writer.Write(Count);
            ManifestEncoding.WriteScalar(Writer, Chance); ManifestEncoding.WriteScalar(Writer, Slope);
            ManifestEncoding.WriteScalar(Writer, Spacing);
        }
        internal override void ClearExternal() { }
    }
}
