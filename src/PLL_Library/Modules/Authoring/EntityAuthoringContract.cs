using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class EntityAuthoringContract
{
    readonly ComponentBinding<PllEntityAuthoring> Component;
    readonly TransformBinding Model;
    readonly string Id;
    readonly bool InitiallyEnabled;
    readonly NativeEntityAppearance? Appearance;
    readonly Vector3 NestOffset;
    readonly byte Kind;
    readonly string PluginGuid = string.Empty;
    readonly string FactoryId = string.Empty;
    readonly string Revision = string.Empty;
    readonly byte[] SourceFingerprint;
    GameObject? NativePrefab;
    NativeBeetleSource? BeetleProvider;
    NativeEntityAppearanceBinding? BoundAppearance;
    PluginEntityPrototype? PluginPrototype;

    EntityAuthoringContract(AuthoringBindingOwner Owner, PllEntityAuthoring Source, int FixedPlacementOrdinal)
    {
        this.Owner = Owner; this.FixedPlacementOrdinal = FixedPlacementOrdinal;
        Component = ComponentBinding<PllEntityAuthoring>.Create(Owner.BindingRoot, Source);
        Model = TransformBinding.Create(Owner.BindingRoot, Source.ModelRoot.transform);
        Id = Source.Id; InitiallyEnabled = Source.InitiallyEnabled;
        switch (Source.Behavior)
        {
            case PllBeetleBehavior Beetle: Kind = 0; Appearance = NativeEntityAppearance.Create(Beetle.Appearance); break;
            case PllMushroomZombieBehavior: Kind = 1; break;
            case PllCondorBehavior Condor:
                Kind = 2; Appearance = NativeEntityAppearance.Create(Condor.Appearance); NestOffset = Condor.NestOffset; break;
            case PllPluginEntityBehavior Plugin:
                Kind = 3; PluginGuid = Plugin.PluginGuid; FactoryId = Plugin.FactoryId; Revision = Plugin.Revision; break;
            default: throw new InvalidOperationException("The validated Entity behavior is unsupported.");
        }
        SourceFingerprint = DeterministicObjectIdentity.HashObjectHierarchy(Source.transform, "PLL/EntityDefinition/v1");
    }

    internal AuthoringBindingOwner Owner { get; }
    internal PllEntityAuthoring Source => Component.Source;
    internal ComponentBinding<PllEntityAuthoring> SourceBinding => Component;
    internal bool Generated => Owner.Generated;
    internal int FixedPlacementOrdinal { get; }
    internal bool RequiresBeetles => Kind == 0;
    internal bool RequiresZombies => Kind == 1;
    internal bool RequiresCondors => Kind == 2;

    internal EntityZoneBinding CreateZoneBinding(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
        int Count, float Chance, float Slope, float Spacing)
    {
        if (Count == 0 || Chance == 0f) return EntityZoneBinding.Empty;
        BeetleProvider?.AdmitInitialCount(Count); PluginPrototype?.AdmitCount(Count);
        return Kind switch
        {
            0 => EntityZoneBinding.Beetles(Zone, Output, Volume, NativePrefab, BoundAppearance, Count, Chance, Slope, Spacing),
            1 => EntityZoneBinding.Zombies(Zone, Output, Volume, NativePrefab, Count, Chance, Slope, Spacing, false),
            2 => EntityZoneBinding.Condor(Output, Volume, NativePrefab, BoundAppearance, Chance, Volume.Center + NestOffset),
            3 => new PluginEntityPopulation(Zone, Output, Volume, PluginPrototype!, Count, Chance, Slope, Spacing),
            _ => throw new InvalidOperationException("The sealed Entity behavior is unsupported.")
        };
    }

    internal static EntityAuthoringContract[] CreateAll(Transform Template, Transform Floor, Transform Campfire,
        BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        var Entries = AuthoringBindingOwner.GraphComponents<PllEntityAuthoring>(Template, Biome, Graph, "Entity");
        var Result = new EntityAuthoringContract[Entries.Count];
        var FixedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var Index = 0; Index < Result.Length; Index++)
        {
            var Entry = Entries[Index]; var Source = Entry.Value;
            if (PllEntityValueContract.Failure(Source) is { } Failure)
                throw new ArgumentException($"{Failure} Biome '{Biome}', Entity '{Source.Id}'.", nameof(Template));
            if (TransformOwnership.HasPropSpawnerInPath(Entry.Owner.BindingRoot, Source.transform)
                || !Entry.Owner.Generated && TransformOwnership.Contains(Floor, Source.transform)
                    == TransformOwnership.Contains(Campfire, Source.transform))
                throw new ArgumentException($"Biome '{Biome}' Entity '{Source.Id}' must belong to exactly one physical root.", nameof(Template));
            for (var Parent = Source.transform.parent; Parent && Parent != Entry.Owner.BindingRoot; Parent = Parent.parent)
                if (Parent.GetComponent<PllEntityAuthoring>() || Parent.GetComponent<PllHazardAuthoring>()
                    || Parent.GetComponent<PLLFinalGeneration>())
                    throw new ArgumentException($"Biome '{Biome}' Entity '{Source.Id}' has an unsupported nested or Final owner.", nameof(Template));
            var Ordinal = 0;
            if (!Entry.Owner.Generated)
            {
                FixedCounts.TryGetValue(Source.Id, out Ordinal); FixedCounts[Source.Id] = Ordinal + 1;
            }
            Result[Index] = new(Entry.Owner, Source, Ordinal);
        }
        return Result;
    }

    internal void ResolveExternal(NativeBeetleSource? Beetles, NativeZombieSource? Zombies, NativeCondorSource? Condors)
    {
        switch (Kind)
        {
            case 0:
                NativePrefab = (Beetles ?? throw new InvalidOperationException("Entity requires PEAK's native Beetle provider.")).Prefab;
                BeetleProvider = Beetles;
                Beetles.AdmitInitialCount(1); BoundAppearance = Appearance?.Bind(Beetles.SkinDescriptor); break;
            case 1:
                NativePrefab = (Zombies ?? throw new InvalidOperationException("Entity requires PEAK's native zombie provider.")).Prefab; break;
            case 2:
                NativePrefab = (Condors ?? throw new InvalidOperationException("Entity requires PEAK's native Condor provider.")).Prefab;
                BoundAppearance = Appearance?.Bind(Condors.SkinDescriptor); break;
            case 3:
                PluginPrototype = PluginEntityPrototype.Create(
                    PluginEntityFactoryRegistry.Require(PluginGuid, FactoryId, Revision), Source.ModelRoot, 1); break;
            default: throw new InvalidOperationException("The sealed Entity behavior is unsupported.");
        }
    }

    internal EntityBinding BindFixed(Transform Root, BiomeId Biome, FeatureOccurrence Occurrence, int Ordinal) =>
        Bind(Component.Resolve(Root, Biome, "Entity"), Occurrence, Ordinal);

    internal PllEntityAuthoring ResolveFixedSource(Transform Root, BiomeId Biome) =>
        Component.Resolve(Root, Biome, "Entity");

    internal bool TryBindGenerated(PllEntityAuthoring Source, BiomeId Biome,
        FeatureOccurrence Occurrence, int Ordinal, out EntityBinding Binding)
    {
        Binding = null!;
        if (!Source || !Owner.TryGetAdmittedGeneratedRoot(Source.transform, out var Root)
            || !ReferenceEquals(Component.Resolve(Root, Biome, "Entity"), Source)) return false;
        Binding = Bind(Source, Occurrence, Ordinal); return true;
    }

    EntityBinding Bind(PllEntityAuthoring Source, FeatureOccurrence Occurrence, int Ordinal)
    {
        ValidateMetadata(Source);
        if (Source.Binding is not null) throw new InvalidOperationException("Entity instance already has a creation owner.");
        return Source.Binding = Kind == 3
            ? PluginPrototype!.Bind(Source, Occurrence, Ordinal)
            : EntityZoneBinding.BindAuthored(Source, Occurrence, NativePrefab
                ?? throw new InvalidOperationException("Entity has no prepared native graph."), BoundAppearance, NestOffset);
    }

    internal void Validate(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Source.transform, Biome, "Entity");
        Component.Validate(Owner.BindingRoot, Biome, "Entity"); Model.Validate(Owner.BindingRoot, Biome.Value, "Entity model");
        ValidateMetadata(Source);
        if (!SourceFingerprint.AsSpan().SequenceEqual(
            DeterministicObjectIdentity.HashObjectHierarchy(Source.transform, "PLL/EntityDefinition/v1")))
            throw new InvalidOperationException($"Biome '{Biome}' changed Entity '{Id}' before sealing.");
    }

    void ValidateMetadata(PllEntityAuthoring Source)
    {
        var Matches = Kind switch
        {
            0 => Source.Behavior is PllBeetleBehavior Beetle
                && (Appearance is null ? Beetle.Appearance is null : Appearance.Matches(Beetle.Appearance)),
            1 => Source.Behavior is PllMushroomZombieBehavior,
            2 => Source.Behavior is PllCondorBehavior Condor && Condor.NestOffset.Equals(NestOffset)
                && (Appearance is null ? Condor.Appearance is null : Appearance.Matches(Condor.Appearance)),
            3 => Source.Behavior is PllPluginEntityBehavior Plugin && Plugin.PluginGuid == PluginGuid
                && Plugin.FactoryId == FactoryId && Plugin.Revision == Revision,
            _ => throw new InvalidOperationException("The sealed Entity behavior is unsupported.")
        };
        if (!Source.enabled || Source.Id != Id || Source.InitiallyEnabled != InitiallyEnabled || !Matches)
            throw new InvalidOperationException($"Entity '{Id}' changed its sealed definition.");
    }

    internal void WriteManifest(BinaryWriter Writer, bool External)
    {
        Owner.WriteManifest(Writer); Component.WriteManifest(Writer); Model.WriteManifest(Writer);
        Writer.Write(Id); Writer.Write(InitiallyEnabled); Writer.Write(Kind);
        if (!Generated) Writer.Write(FixedPlacementOrdinal);
        Writer.Write(SourceFingerprint.Length); Writer.Write(SourceFingerprint);
        Writer.Write(Appearance is not null); Appearance?.WriteManifest(Writer);
        if (Kind == 2) ManifestEncoding.WriteVector(Writer, NestOffset);
        if (Kind == 3)
        {
            Writer.Write(PluginGuid); Writer.Write(FactoryId); Writer.Write(Revision);
            if (External) (PluginPrototype ?? throw new InvalidOperationException("Entity plugin prototype is not prepared.")).WriteManifest(Writer);
        }
        if (External && Appearance is not null) BoundAppearance!.WriteManifest(Writer);
    }

    internal void ClearExternal()
    {
        NativePrefab = null; BeetleProvider = null; BoundAppearance = null;
        var Owned = PluginPrototype; PluginPrototype = null; Owned?.Dispose();
    }
}

internal abstract class EntityBinding
{
    readonly MapRun Run;
    protected bool Enabled;
    protected bool Retired;

    protected EntityBinding(PllEntityAuthoring Source, FeatureOccurrence Occurrence)
    {
        this.Source = Source; this.Occurrence = Occurrence;
        Enabled = Source.InitiallyEnabled;
        Run = Occurrence.Run;
    }

    internal PllEntityAuthoring Source { get; }
    internal FeatureOccurrence Occurrence { get; }
    internal bool IsReady => !Retired && Enabled && Source && Source.enabled && Source.gameObject.activeInHierarchy
        && ReferenceEquals(Map.CurrentRun, Run) && Run.Active && Run.IsOccurrenceReady(Occurrence);
    internal abstract void Publish();
    internal abstract void SetEnabled(bool Value);
    internal abstract void Suspend();
    internal abstract void Resume();
    internal abstract void Retire();
}
