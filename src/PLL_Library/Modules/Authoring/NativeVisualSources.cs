using System;
using System.Collections.Generic;
using System.IO;

namespace PLL.Library;

internal sealed class NativeVisualSeal
{
    readonly NativeVisualCatalog.Inventory Inventory;
    readonly Dictionary<PllNativeVisualPreset, NativeVisualSource> Committed = new();
    readonly Dictionary<PllNativeEffectParticles, NativeEffectParticleSource> CommittedEffectParticles = new();
    NativeFogSource? CommittedFog;
    int RequiredMask;
    int RequiredParticleMask;
    int RequiredEffectParticleMask;
    bool Completed;

    internal NativeVisualSeal(
        MapHandler Handler,
        IReadOnlyDictionary<string, LayoutSegmentSource> Sources)
    {
        Inventory = new NativeVisualCatalog.Inventory(Handler, Sources);
    }

    internal NativeVisualSources Prepare(
        int Required,
        int RequiredParticles,
        bool RequireFog,
        int RequiredEffects = 0)
    {
        if (Completed) throw new InvalidOperationException("The native visual source seal is complete.");
        return Inventory.Prepare(Required, RequiredParticles, RequireFog, RequiredEffects);
    }

    internal void Commit(NativeVisualSources Batch)
    {
        if (Batch is null) throw new ArgumentNullException(nameof(Batch));
        if (Completed) throw new InvalidOperationException("The native visual source seal is complete.");
        Batch.ValidateCompatible(Committed, RequiredParticleMask, CommittedFog, CommittedEffectParticles);
        Batch.MergeInto(Committed, RequiredParticleMask, CommittedEffectParticles);
        CommittedFog ??= Batch.Fog;
        RequiredMask |= Batch.RequiredMask;
        RequiredParticleMask |= Batch.RequiredParticleMask;
        RequiredEffectParticleMask |= Batch.RequiredEffectParticleMask;
    }

    internal NativeVisualSources Complete(
        int ExpectedMask,
        int ExpectedParticleMask,
        int ExpectedEffectParticleMask = 0)
    {
        if (Completed) throw new InvalidOperationException("The native visual source seal is already complete.");
        if (RequiredMask != ExpectedMask || RequiredParticleMask != ExpectedParticleMask
            || RequiredEffectParticleMask != ExpectedEffectParticleMask)
            throw new InvalidOperationException(
                $"The retained native visual source masks are {RequiredMask}/{RequiredParticleMask}/{RequiredEffectParticleMask}, "
                + $"expected {ExpectedMask}/{ExpectedParticleMask}/{ExpectedEffectParticleMask}.");
        Completed = true;
        return new NativeVisualSources(
            new Dictionary<PllNativeVisualPreset, NativeVisualSource>(Committed),
            RequiredMask, RequiredParticleMask, CommittedFog,
            new Dictionary<PllNativeEffectParticles, NativeEffectParticleSource>(CommittedEffectParticles), RequiredEffectParticleMask);
    }

    internal void Release()
    {
        Completed = true;
        try { NativeFogRenderRuntime.Release(); }
        finally { Inventory.Release(); }
    }

    internal void PrepareFogRendering(IEnumerable<PllFogShaderAdapter> ContentShaders) =>
        NativeFogRenderRuntime.Prepare(Inventory.Scene, ContentShaders);
}

internal sealed class NativeVisualSources
{
    readonly Dictionary<PllNativeVisualPreset, NativeVisualSource> Sources;
    readonly Dictionary<PllNativeEffectParticles, NativeEffectParticleSource> EffectParticles;

    internal NativeVisualSources(
        Dictionary<PllNativeVisualPreset, NativeVisualSource> Sources,
        int RequiredMask,
        int RequiredParticleMask,
        NativeFogSource? Fog,
        Dictionary<PllNativeEffectParticles, NativeEffectParticleSource> EffectParticles,
        int RequiredEffectParticleMask)
    {
        this.Sources = Sources;
        this.RequiredMask = RequiredMask;
        this.RequiredParticleMask = RequiredParticleMask;
        this.Fog = Fog;
        this.EffectParticles = EffectParticles;
        this.RequiredEffectParticleMask = RequiredEffectParticleMask;
    }

    internal int RequiredMask { get; }
    internal int RequiredParticleMask { get; }
    internal NativeFogSource? Fog { get; }
    internal int RequiredEffectParticleMask { get; }

    internal NativeVisualSource Require(PllNativeVisualPreset Preset) => Sources.TryGetValue(Preset, out var Source)
        ? Source
        : throw new InvalidOperationException($"PEAK's native visual preset '{Preset}' was not sealed.");

    internal NativeEffectParticleSource RequireEffectParticles(PllNativeEffectParticles Choice) =>
        EffectParticles.TryGetValue(Choice, out var Source)
            ? Source
            : throw new InvalidOperationException($"PEAK's native effect particles '{Choice}' were not sealed.");

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(RequiredMask);
        Writer.Write(RequiredParticleMask);
        Writer.Write(Sources.Count);
        for (var Value = (int)PllNativeVisualPreset.Beach; Value <= (int)PllNativeVisualPreset.Nadir; Value++)
            if (Sources.TryGetValue((PllNativeVisualPreset)Value, out var Source))
                Source.WriteManifest(Writer, (RequiredParticleMask & (1 << Value)) != 0);
        Writer.Write(RequiredEffectParticleMask);
        Writer.Write(EffectParticles.Count);
        for (var Value = (int)PllNativeEffectParticles.Fire; Value <= (int)PllNativeEffectParticles.SmokeLit; Value++)
            if (EffectParticles.TryGetValue((PllNativeEffectParticles)Value, out var Source)) Source.WriteManifest(Writer);
    }

    internal void ValidateCompatible(
        IReadOnlyDictionary<PllNativeVisualPreset, NativeVisualSource> Committed,
        int CommittedParticleMask,
        NativeFogSource? CommittedFog,
        IReadOnlyDictionary<PllNativeEffectParticles, NativeEffectParticleSource> CommittedEffectParticles)
    {
        foreach (var Pair in Sources)
        {
            var Mask = 1 << (int)Pair.Key;
            var CompareParticles = (RequiredParticleMask & Mask) != 0
                && (CommittedParticleMask & Mask) != 0;
            if (Committed.TryGetValue(Pair.Key, out var Current)
                && !Current.CompatibleWith(Pair.Value, CompareParticles))
                throw new InvalidOperationException(
                    $"PEAK's live native visual preset '{Pair.Key}' changed during compatibility sealing.");
        }
        if (CommittedFog is not null && Fog is not null && !CommittedFog.Matches(Fog))
            throw new InvalidOperationException(
                "PEAK's live native Caldera fog source changed during compatibility sealing.");
        foreach (var Pair in EffectParticles)
            if (CommittedEffectParticles.TryGetValue(Pair.Key, out var Current) && !ReferenceEquals(Current, Pair.Value))
                throw new InvalidOperationException("PEAK's native effect-particle source changed during compatibility sealing.");
    }

    internal void MergeInto(
        IDictionary<PllNativeVisualPreset, NativeVisualSource> Committed,
        int CommittedParticleMask,
        IDictionary<PllNativeEffectParticles, NativeEffectParticleSource> CommittedEffectParticles)
    {
        foreach (var Pair in Sources)
        {
            if (!Committed.TryGetValue(Pair.Key, out _))
            {
                Committed.Add(Pair.Key, Pair.Value);
                continue;
            }
            var Mask = 1 << (int)Pair.Key;
            if ((RequiredParticleMask & Mask) != 0 && (CommittedParticleMask & Mask) == 0)
                Committed[Pair.Key] = Pair.Value;
        }
        foreach (var Pair in EffectParticles) CommittedEffectParticles[Pair.Key] = Pair.Value;
    }
}
