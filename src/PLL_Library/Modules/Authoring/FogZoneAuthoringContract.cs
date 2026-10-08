using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal readonly struct FogZoneSettings
{
    internal FogZoneSettings(PllFogZoneAuthoring Source)
    {
        Volume = Source.Volume; InitiallyEnabled = Source.InitiallyEnabled;
        VisibilityDistance = Source.VisibilityDistance; Color = Source.Color;
        BoundaryBlendDistance = Source.BoundaryBlendDistance;
        Priority = Source.Priority; Transition = Source.Transition;
    }
    internal PllZoneVolume Volume { get; }
    internal bool InitiallyEnabled { get; }
    internal float VisibilityDistance { get; }
    internal Color Color { get; }
    internal float BoundaryBlendDistance { get; }
    internal int Priority { get; }
    internal PllPresentationTransition Transition { get; }
}

internal sealed class FogZoneAuthoringContract
{
    internal readonly ComponentBinding<PllFogZoneAuthoring> SourceBinding;
    readonly string StableId;
    readonly byte[] TargetHash;

    FogZoneAuthoringContract(AuthoringBindingOwner Owner, PllFogZoneAuthoring Source, BiomeId Biome)
    {
        this.Owner = Owner;
        SourceBinding = ComponentBinding<PllFogZoneAuthoring>.Create(Owner.BindingRoot, Source);
        StableId = Source.StableId; Settings = new FogZoneSettings(Source);
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true), true))
        {
            Writer.Write("PLL/FogZoneTarget/v1"); Writer.Write(Biome.Value); Writer.Write(StableId);
        }
        using var Sha = SHA256.Create();
        TargetHash = Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
        StableTargetKey = BitConverter.ToString(TargetHash).Replace("-", "");
    }

    internal PllFogZoneAuthoring Source => SourceBinding.Source;
    internal AuthoringBindingOwner Owner { get; }
    internal bool Generated => Owner.Generated;
    internal FogZoneSettings Settings { get; }
    internal string StableTargetKey { get; }
    internal string StableSourceId => StableId;
    internal ReadOnlySpan<byte> TargetKeyHash => TargetHash;

    internal static FogZoneAuthoringContract[] CreateAll(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        var Sources = AuthoringBindingOwner.GraphComponents<PllFogZoneAuthoring>(Template, Biome, Graph, "fog-zone authoring");
        if (Sources.Count > PllBundleSerialization.MaximumHookedActions)
            throw new ArgumentException($"Biome '{Biome}' exceeds the world Fog Zone target budget.");
        var Contracts = new FogZoneAuthoringContract[Sources.Count];
        var Ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Contracts.Length; i++)
        {
            var Entry = Sources[i]; var Source = Entry.Value;
            Source.ValidateSettings();
            if (!Source.enabled || !TransformOwnership.DiscoverableWhenActivated(Entry.Owner.BindingRoot, Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(Entry.Owner.BindingRoot, Source.transform))
                throw new ArgumentException($"Biome '{Biome}' Fog Zone '{Source.name}' has an invalid source owner.");
            if (!Ids.Add(Source.StableId))
                throw new ArgumentException($"Biome '{Biome}' has duplicate Fog Zone identity '{Source.StableId}'.");
            Contracts[i] = new FogZoneAuthoringContract(Entry.Owner, Source, Biome);
        }
        return Contracts;
    }

    internal ResolvedFogZone ResolveStable(Transform RuntimeRoot, BiomeId Biome)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{Biome}' cannot resolve a generated Fog Zone as stable authoring.");
        return Resolve(SourceBinding.Resolve(RuntimeRoot, Biome, "fog-zone authoring"), Biome);
    }

    internal bool TryResolveGenerated(PllFogZoneAuthoring Source, BiomeId Biome, out ResolvedFogZone Resolved)
    {
        Resolved = default;
        if (!Source || !Owner.TryGetAdmittedGeneratedRoot(Source.transform, out var RuntimeRoot)) return false;
        if (SourceBinding.Resolve(RuntimeRoot, Biome, "generated fog-zone authoring") != Source) return false;
        Resolved = Resolve(Source, Biome);
        return true;
    }

    internal ResolvedFogZone ResolveGenerated(Transform AdmittedCandidateRoot, BiomeId Biome)
    {
        if (!Generated) throw new InvalidOperationException($"Biome '{Biome}' cannot resolve a stable Fog Zone as generated authoring.");
        return Resolve(SourceBinding.Resolve(AdmittedCandidateRoot, Biome, "generated fog-zone authoring"), Biome);
    }

    ResolvedFogZone Resolve(PllFogZoneAuthoring Source, BiomeId Biome)
    {
        if (!Same(Source)) throw new InvalidOperationException($"Biome '{Biome}' runtime Fog Zone changed its sealed contract.");
        PllFogZoneAuthoring.ValidateTransform(Source.transform, Settings.Volume.Shape);
        return new ResolvedFogZone(Source.gameObject, Settings, StableId, StableTargetKey);
    }

    internal void Validate(Transform Template, BiomeId Biome, AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Source.transform, Biome, "fog-zone authoring");
        SourceBinding.Validate(Owner.BindingRoot, Biome, "fog-zone authoring");
        if (!Source.enabled || !Same(Source))
            throw new InvalidOperationException($"Biome '{Biome}' changed Fog Zone '{Source.name}' before sealing.");
    }

    bool Same(PllFogZoneAuthoring Source) => Source.StableId == StableId
        && Source.Volume.Equals(Settings.Volume) && Source.InitiallyEnabled == Settings.InitiallyEnabled
        && Source.VisibilityDistance == Settings.VisibilityDistance && Source.Color.Equals(Settings.Color)
        && Source.BoundaryBlendDistance == Settings.BoundaryBlendDistance && Source.Priority == Settings.Priority
        && Source.Transition.CoverDelaySeconds == Settings.Transition.CoverDelaySeconds;

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer); SourceBinding.WriteManifest(Writer);
        Writer.Write(StableId); Writer.Write(TargetHash);
        ZoneVolumeContract.WriteManifest(Writer, Settings.Volume);
        Writer.Write(Settings.InitiallyEnabled);
        ManifestEncoding.WriteScalar(Writer, Settings.VisibilityDistance);
        ManifestEncoding.WriteColor(Writer, Settings.Color);
        ManifestEncoding.WriteScalar(Writer, Settings.BoundaryBlendDistance);
        Writer.Write(Settings.Priority);
        ManifestEncoding.WriteScalar(Writer, Settings.Transition.CoverDelaySeconds);
    }
}

internal readonly struct ResolvedFogZone
{
    internal ResolvedFogZone(GameObject Root, FogZoneSettings Settings, string StableSourceId, string StableTargetKey)
    {
        this.Root = Root; this.Settings = Settings;
        this.StableSourceId = StableSourceId; this.StableTargetKey = StableTargetKey;
    }
    internal GameObject Root { get; }
    internal FogZoneSettings Settings { get; }
    internal string StableSourceId { get; }
    internal string StableTargetKey { get; }
}
