using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class EntityZoneContract
{
    readonly ComponentBinding<PllEntityZoneAuthoring> Component;
    readonly TransformBinding Output;
    readonly PllZoneVolume Volume;
    readonly Recipe RecipeValue;

    EntityZoneContract(Transform Template, PllEntityZoneAuthoring Source)
    {
        Component = ComponentBinding<PllEntityZoneAuthoring>.Create(Template, Source);
        Output = TransformBinding.Create(Template, Source.OutputRoot);
        Id = Source.Id; Volume = Source.Volume;
        RecipeValue = Source.Source switch
        {
            PllBeetleZoneSource Beetles => new BeetleRecipe(Beetles),
            PllMushroomZombieZoneSource Zombies => new ZombieRecipe(Zombies),
            PllCondorZoneSource Condor => new CondorRecipe(Condor),
            PllPluginEntityZoneSource Plugin => new PluginEntityRecipe(Plugin),
            PllAuthoredEntityZoneSource Authored => new AuthoredRecipe(Authored),
            _ => throw new InvalidOperationException("The validated Entity Zone source is unsupported.")
        };
    }

    internal string Id { get; }
    internal bool RequiresBeetles => RecipeValue is BeetleRecipe Recipe && (Recipe.Enabled || Recipe.HasAppearance)
        || RecipeValue is AuthoredRecipe Authored && Authored.Source.Behavior is PllBeetleBehavior;
    internal bool RequiresZombies => RecipeValue is ZombieRecipe { Enabled: true }
        || RecipeValue is AuthoredRecipe Authored && Authored.Source.Behavior is PllMushroomZombieBehavior;
    internal bool RequiresCondors => RecipeValue is CondorRecipe Recipe && (Recipe.Enabled || Recipe.HasAppearance)
        || RecipeValue is AuthoredRecipe Authored && Authored.Source.Behavior is PllCondorBehavior;
    internal bool HasAppearance => RecipeValue.HasAppearance;
    internal bool RequiresPluginEntities => RecipeValue is PluginEntityRecipe
        || RecipeValue is AuthoredRecipe Authored && Authored.Source.Behavior is PllPluginEntityBehavior;

    internal void ResolveDefinitionSources(IReadOnlyList<EntityAuthoringContract> Definitions)
    {
        if (RecipeValue is AuthoredRecipe Authored) Authored.Resolve(Definitions);
    }

    internal static EntityZoneContract[] CreateAll(
        Transform Template, Transform Floor, Transform Campfire,
        BiomeId Id, AuthoringCandidateGraph Graph)
    {
        foreach (var Candidate in Graph.Ordered)
            if (Candidate.GetComponentInChildren<PllEntityZoneAuthoring>(true))
                throw new ArgumentException($"Biome '{Id}' places an Entity Zone inside a generated candidate.", nameof(Template));
        var Sources = Template.GetComponentsInChildren<PllEntityZoneAuthoring>(true);
        if (Sources.Length == 0) return Array.Empty<EntityZoneContract>();
        var Entries = new List<(string Key, PllEntityZoneAuthoring Source)>(Sources.Length);
        var Ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var Source in Sources)
        {
            ValidateIngress(Source, Template, Floor, Campfire, Id);
            if (PllActorZoneSourceContract.RegisterId(Ids, Source.Id) is { } Failure)
                throw new ArgumentException($"{Failure} Biome '{Id}'.", nameof(Template));
            Entries.Add((DeterministicObjectIdentity.RelativeComponentPath(Source, Template, 0), Source));
        }
        Entries.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Key, Right.Key));
        var Result = new EntityZoneContract[Entries.Count];
        for (var Index = 0; Index < Result.Length; Index++)
            Result[Index] = new EntityZoneContract(Template, Entries[Index].Source);
        return Result;
    }

    internal void ResolveExternal(NativeBeetleSource? Beetles, NativeZombieSource? Zombies, NativeCondorSource? Condors)
    {
        switch (RecipeValue)
        {
            case BeetleRecipe Recipe: Recipe.Resolve(Beetles); break;
            case ZombieRecipe Recipe: Recipe.Resolve(Zombies); break;
            case CondorRecipe Recipe: Recipe.Resolve(Condors); break;
            case PluginEntityRecipe Recipe: Recipe.Resolve(); break;
            case AuthoredRecipe: break;
            default: throw new InvalidOperationException("The sealed Entity Zone recipe is unsupported.");
        }
    }

    internal void ClearExternal() => RecipeValue.ClearExternal();

    internal EntityZoneBinding Bind(Transform RuntimeRoot, BiomeId Biome)
    {
        var Source = Component.Resolve(RuntimeRoot, Biome, "Entity Zone");
        var Root = Output.ResolveValidated(RuntimeRoot, Biome.Value, "Entity Zone output");
        ValidateMetadata(Source, Biome);
        return Source.Binding = RecipeValue.Bind(Source, Root, Volume);
    }

    internal void Validate(Transform Template, BiomeId Biome)
    {
        Component.Validate(Template, Biome, "Entity Zone");
        Output.Validate(Template, Biome.Value, "Entity Zone output");
        ValidateMetadata(Component.Source, Biome);
        if (Component.Source.OutputRoot != Output.Source || Output.Source.childCount != 0)
            throw new InvalidOperationException($"Biome '{Biome}' Entity Zone '{Id}' changed its compiled output branch.");
    }

    internal void WriteManifest(BinaryWriter Writer, bool External)
    {
        Component.WriteManifest(Writer); Output.WriteManifest(Writer); Writer.Write(Id);
        ZoneVolumeContract.WriteManifest(Writer, Volume);
        RecipeValue.WriteManifest(Writer, External);
    }

    void ValidateMetadata(PllEntityZoneAuthoring Source, BiomeId Biome)
    {
        if (!Source.enabled || Source.Id != Id || !Source.Volume.Equals(Volume)
            || !RecipeValue.Matches(Source.Source))
            throw new InvalidOperationException($"Biome '{Biome}' Entity Zone '{Id}' changed its sealed recipe.");
    }

    internal abstract class Recipe
    {
        internal abstract bool Enabled { get; }
        internal virtual bool HasAppearance => false;
        internal abstract bool Matches(PllEntityZoneSource Source);
        internal abstract EntityZoneBinding Bind(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume);
        internal abstract void WriteManifest(BinaryWriter Writer, bool External);
        internal abstract void ClearExternal();
    }

    internal abstract class GroundRecipe : Recipe
    {
        protected readonly int Count;
        protected readonly float Chance;
        protected readonly float Slope;
        protected readonly float Spacing;

        protected GroundRecipe(int Count, float Chance, float Slope, float Spacing)
        {
            this.Count = Count; this.Chance = Chance; this.Slope = Slope; this.Spacing = Spacing;
        }

        internal override bool Enabled => Count != 0 && Chance > 0f;
        protected bool Matches(int Count, float Chance, float Slope, float Spacing) =>
            this.Count == Count && this.Chance.Equals(Chance) && this.Slope.Equals(Slope) && this.Spacing.Equals(Spacing);
        protected void WriteGround(BinaryWriter Writer)
        {
            Writer.Write(Count); ManifestEncoding.WriteScalar(Writer, Chance);
            ManifestEncoding.WriteScalar(Writer, Slope); ManifestEncoding.WriteScalar(Writer, Spacing);
        }
    }

    sealed class BeetleRecipe : GroundRecipe
    {
        readonly NativeEntityAppearance? Appearance;
        NativeEntityAppearanceBinding? BoundAppearance;
        NativeBeetleSource? Provider;

        internal BeetleRecipe(PllBeetleZoneSource Source)
            : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
            => Appearance = NativeEntityAppearance.Create(Source.Appearance);

        internal override bool HasAppearance => Appearance is not null;

        internal void Resolve(NativeBeetleSource? Source)
        {
            if (Enabled && Source is null)
                throw new InvalidOperationException("The Entity Zone requires PEAK's native Beetle provider.");
            if (Enabled) Source!.AdmitInitialCount(Count);
            if (Appearance is not null)
                BoundAppearance = Appearance.Bind(Source?.SkinDescriptor
                    ?? throw new InvalidOperationException("The Beetle appearance requires PEAK's native skin."));
            Provider = Source;
        }

        internal override bool Matches(PllEntityZoneSource Source) => Source is PllBeetleZoneSource Beetles
            && Matches(Beetles.MaximumInitialCount, Beetles.SpawnChance, Beetles.MaximumSlope, Beetles.MinimumSpacing)
            && (Appearance is null ? Beetles.Appearance is null : Appearance.Matches(Beetles.Appearance));
        internal override EntityZoneBinding Bind(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            EntityZoneBinding.Beetles(Zone, Output, Volume, Provider?.Prefab, BoundAppearance, Count, Chance, Slope, Spacing);
        internal override void WriteManifest(BinaryWriter Writer, bool External)
        {
            Writer.Write((byte)0); WriteGround(Writer);
            if (Appearance is not null)
            {
                Writer.Write("PLL/BeetleAppearance/v1"); Appearance.WriteManifest(Writer);
                if (External)
                    (BoundAppearance ?? throw new InvalidOperationException("The Beetle appearance has no native skin binding.")).WriteManifest(Writer);
            }
        }
        internal override void ClearExternal() { Provider = null; BoundAppearance = null; }
    }

    sealed class ZombieRecipe : GroundRecipe
    {
        readonly bool CullRandomly;
        NativeZombieSource? Provider;

        internal ZombieRecipe(PllMushroomZombieZoneSource Source)
            : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
            => CullRandomly = Source.CullRandomly;

        internal void Resolve(NativeZombieSource? Source)
        {
            if (Enabled && Source is null)
                throw new InvalidOperationException("The Entity Zone requires PEAK's native NPC zombie provider.");
            Provider = Source;
        }

        internal override bool Matches(PllEntityZoneSource Source) => Source is PllMushroomZombieZoneSource Zombies
            && Matches(Zombies.MaximumInitialCount, Zombies.SpawnChance, Zombies.MaximumSlope, Zombies.MinimumSpacing)
            && Zombies.CullRandomly == CullRandomly;
        internal override EntityZoneBinding Bind(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            EntityZoneBinding.Zombies(Zone, Output, Volume, Provider?.Prefab, Count, Chance, Slope, Spacing, CullRandomly);
        internal override void WriteManifest(BinaryWriter Writer, bool External)
        {
            Writer.Write((byte)1); WriteGround(Writer); Writer.Write(CullRandomly);
        }
        internal override void ClearExternal() => Provider = null;
    }

    sealed class CondorRecipe : Recipe
    {
        readonly NativeEntityAppearance? Appearance;
        NativeEntityAppearanceBinding? BoundAppearance;
        readonly float Chance;
        readonly Vector3 Nest;
        NativeCondorSource? Provider;

        internal CondorRecipe(PllCondorZoneSource Source)
        {
            Chance = Source.SpawnChance; Nest = Source.NestPosition;
            Appearance = NativeEntityAppearance.Create(Source.Appearance);
        }
        internal override bool Enabled => Chance > 0f;
        internal override bool HasAppearance => Appearance is not null;
        internal void Resolve(NativeCondorSource? Source)
        {
            if (Enabled && Source is null)
                throw new InvalidOperationException("The Entity Zone requires PEAK's complete native Condor provider.");
            if (Appearance is not null)
                BoundAppearance = Appearance.Bind(Source?.SkinDescriptor
                    ?? throw new InvalidOperationException("The Condor appearance requires PEAK's native skin."));
            Provider = Source;
        }
        internal override bool Matches(PllEntityZoneSource Source) => Source is PllCondorZoneSource Condor
            && Condor.SpawnChance.Equals(Chance) && Condor.NestPosition.Equals(Nest)
            && (Appearance is null ? Condor.Appearance is null : Appearance.Matches(Condor.Appearance));
        internal override EntityZoneBinding Bind(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            EntityZoneBinding.Condor(Output, Volume, Provider?.Prefab, BoundAppearance, Chance, Nest);
        internal override void WriteManifest(BinaryWriter Writer, bool External)
        {
            Writer.Write((byte)2); ManifestEncoding.WriteScalar(Writer, Chance); ManifestEncoding.WriteVector(Writer, Nest);
            if (Appearance is not null)
            {
                Writer.Write("PLL/CondorAppearance/v1"); Appearance.WriteManifest(Writer);
                if (External)
                    (BoundAppearance ?? throw new InvalidOperationException("The Condor appearance has no native skin binding.")).WriteManifest(Writer);
            }
        }
        internal override void ClearExternal() { Provider = null; BoundAppearance = null; }
    }

    sealed class AuthoredRecipe : GroundRecipe
    {
        readonly GameObject Prefab;
        readonly string DefinitionId;
        EntityAuthoringContract Definition = null!;

        internal AuthoredRecipe(PllAuthoredEntityZoneSource Source)
            : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
        {
            Prefab = Source.Prefab; this.Source = Prefab.GetComponent<PllEntityAuthoring>(); DefinitionId = this.Source.Id;
        }
        internal PllEntityAuthoring Source { get; }
        internal void Resolve(IReadOnlyList<EntityAuthoringContract> Definitions)
        {
            foreach (var Candidate in Definitions)
                if (Candidate.Source == Source) { Definition = Candidate; return; }
            throw new InvalidOperationException("Authored Entity Zone has no canonical Entity definition.");
        }
        internal override bool Matches(PllEntityZoneSource Source) => Source is PllAuthoredEntityZoneSource Authored
            && Authored.Prefab == Prefab && this.Source.Id == DefinitionId
            && Matches(Authored.MaximumInitialCount, Authored.SpawnChance, Authored.MaximumSlope, Authored.MinimumSpacing);
        internal override EntityZoneBinding Bind(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume) =>
            Definition.CreateZoneBinding(Zone, Output, Volume, Count, Chance, Slope, Spacing);
        internal override void WriteManifest(BinaryWriter Writer, bool External)
        {
            Writer.Write((byte)4); WriteGround(Writer); Writer.Write(DefinitionId);
        }
        internal override void ClearExternal() { }
    }

    static void ValidateIngress(
        PllEntityZoneAuthoring Source, Transform Template,
        Transform Floor, Transform Campfire, BiomeId Biome)
    {
        if (PllEntityZoneValueContract.MetadataFailure(Source) is { } Failure)
            throw new ArgumentException($"{Failure} Biome '{Biome}', Entity Zone '{Source.Id}'.", nameof(Source));
        if (PllActorZoneSourceContract.CompiledFailure(Template, Floor, Campfire, Source, Source.OutputRoot) is { } ShapeFailure)
            throw new ArgumentException($"{ShapeFailure} Biome '{Biome}', Entity Zone '{Source.Id}'.", nameof(Source));
    }
}
