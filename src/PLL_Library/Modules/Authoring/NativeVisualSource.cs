using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeVisualSource
{
    readonly NativeTrackedParticleSource[] ParticleSources;
    readonly byte[] ProfileFingerprint;
    readonly byte[] BaseFingerprint;
    readonly byte[] Fingerprint;

    NativeVisualSource(
        PllNativeVisualPreset Preset,
        DayNightProfile DayNightProfile,
        Vector3 IntervalEntryPosition,
        Vector3 IntervalExitPosition,
        Vector3 ProgressionAxis,
        NativeTrackedParticleSource[] Particles,
        NativeSpecialDaySnapshot? SpecialDay,
        byte[] ProfileFingerprint)
    {
        this.Preset = Preset;
        this.DayNightProfile = DayNightProfile;
        this.IntervalEntryPosition = IntervalEntryPosition;
        this.IntervalExitPosition = IntervalExitPosition;
        this.ProgressionAxis = ProgressionAxis;
        ParticleSources = Particles;
        this.Particles = Array.AsReadOnly(Particles);
        this.SpecialDay = SpecialDay;
        this.ProfileFingerprint = ProfileFingerprint;
        BaseFingerprint = CreateFingerprint(false);
        Fingerprint = Particles.Length == 0 ? BaseFingerprint : CreateFingerprint(true);
    }

    internal PllNativeVisualPreset Preset { get; }
    internal DayNightProfile DayNightProfile { get; }
    internal Vector3 IntervalEntryPosition { get; }
    internal Vector3 IntervalExitPosition { get; }
    internal Vector3 ProgressionAxis { get; }
    internal IReadOnlyList<NativeTrackedParticleSource> Particles { get; }
    internal NativeSpecialDaySnapshot? SpecialDay { get; }

    internal static NativeVisualSource Capture(
        NativeVisualInterval Interval,
        NativeTrackedParticleSource[] Particles,
        NativeSpecialDaySnapshot? SpecialDay)
    {
        var Profile = Interval.Source.DayNightProfile
            ?? throw new InvalidOperationException($"PEAK's native visual preset '{Interval.Preset}' lost its DayNightProfile.");
        return new NativeVisualSource(
            Interval.Preset, Profile, Interval.EntryPosition, Interval.ExitPosition, Interval.Axis,
            Particles, SpecialDay,
            VisualFingerprintWriter.HashSerializedObject(Profile, "PLL/NativeDayNightProfile/v1"));
    }

    internal bool CompatibleWith(
        NativeVisualSource Other,
        bool IncludeParticles) =>
        DeterministicObjectIdentity.SameHash(BaseFingerprint, Other.BaseFingerprint)
        && (!IncludeParticles
            || DeterministicObjectIdentity.SameHash(Fingerprint, Other.Fingerprint));

    internal void WriteManifest(BinaryWriter Writer, bool IncludeParticles)
    {
        Writer.Write((byte)Preset);
        ManifestEncoding.WriteVector(Writer, IntervalEntryPosition);
        ManifestEncoding.WriteVector(Writer, IntervalExitPosition);
        ManifestEncoding.WriteVector(Writer, ProgressionAxis);
        Writer.Write(ProfileFingerprint.Length);
        Writer.Write(ProfileFingerprint);
        Writer.Write(IncludeParticles ? ParticleSources.Length : 0);
        if (IncludeParticles)
            foreach (var Particle in ParticleSources) Particle.WriteManifest(Writer);
        Writer.Write(SpecialDay is not null);
        SpecialDay?.WriteManifest(Writer);
    }

    byte[] CreateFingerprint(bool IncludeParticles)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true), true))
        {
            Writer.Write("PLL/NativeVisualSource/v1");
            WriteManifest(Writer, IncludeParticles);
        }
        using var Sha = SHA256.Create();
        return Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
    }
}

internal sealed class NativeTrackedParticleSource
{
    readonly string StructuralPath;
    readonly ParticleVisualGraph Graph;

    NativeTrackedParticleSource(
        string StructuralPath,
        GameObject Fx,
        bool X,
        bool Y,
        bool Z,
        Vector3 PositionOffset,
        float RepositionDistance,
        Vector3 SourcePosition,
        Vector3 IntervalEntryPosition,
        ParticleVisualGraph Graph)
    {
        this.StructuralPath = StructuralPath;
        this.Fx = Fx;
        this.X = X;
        this.Y = Y;
        this.Z = Z;
        this.PositionOffset = PositionOffset;
        this.RepositionDistance = RepositionDistance;
        this.SourcePosition = SourcePosition;
        this.IntervalEntryPosition = IntervalEntryPosition;
        this.Graph = Graph;
    }

    internal GameObject Fx { get; }
    internal bool X { get; }
    internal bool Y { get; }
    internal bool Z { get; }
    internal Vector3 PositionOffset { get; }
    internal float RepositionDistance { get; }
    internal Vector3 SourcePosition { get; }
    internal Vector3 IntervalEntryPosition { get; }

    internal Vector3 PreparePosition(Vector3 OccurrenceEntry) =>
        SourcePosition + OccurrenceEntry - IntervalEntryPosition;

    internal ResolvedParticleGraph ResolveClone(GameObject Clone) =>
        Graph.Resolve(Clone, Clone ? Clone.transform : null!, null, false);

    internal static NativeTrackedParticleSource Capture(
        PlayerTrackParticles Source,
        NativeVisualInterval Interval,
        string StructuralPath)
    {
        if (!Source.fx) throw new InvalidOperationException("A native tracked-particle source has no fx graph.");
        if (!UnityValueContract.Finite(Source.positionOffset)
            || !float.IsFinite(Source.repositionDelta)
            || Source.repositionDelta < 0f
            || !UnityValueContract.Finite(Source.fx.transform.position))
            throw new InvalidOperationException("A native tracked-particle source has invalid tracking values.");
        return new NativeTrackedParticleSource(
            StructuralPath, Source.fx, Source.x, Source.y, Source.z, Source.positionOffset,
            Source.repositionDelta, Source.fx.transform.position, Interval.EntryPosition,
            ParticleVisualGraph.Capture(Source.fx, Source.fx.transform, null, false));
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(StructuralPath);
        Writer.Write(X); Writer.Write(Y); Writer.Write(Z);
        ManifestEncoding.WriteVector(Writer, PositionOffset);
        ManifestEncoding.WriteScalar(Writer, RepositionDistance);
        ManifestEncoding.WriteVector(Writer, SourcePosition);
        ManifestEncoding.WriteVector(Writer, IntervalEntryPosition);
        Graph.WriteManifest(Writer);
    }
}

internal sealed class NativeEffectParticleSource
{
    internal const int SupportedMask = (1 << ((int)PllNativeEffectParticles.SmokeLit + 1)) - 2;
    readonly NativeParticlePrototypePool Owner;
    readonly GameObject Prototype;
    readonly ParticleVisualGraph Graph;
    readonly PllNativeEffectParticles Choice;
    readonly string StructuralPath;

    NativeEffectParticleSource(
        NativeParticlePrototypePool Owner, GameObject Prototype,
        ParticleVisualGraph Graph, PllNativeEffectParticles Choice,
        string StructuralPath, ParticleSystemRenderer Renderer)
    {
        this.Owner = Owner; this.Prototype = Prototype; this.Graph = Graph;
        this.Choice = Choice; this.StructuralPath = StructuralPath;
        this.Renderer = Renderer;
    }

    internal ParticleSystemRenderer Renderer { get; }

    internal static NativeEffectParticleSource Capture(
        NativeParticlePrototypePool Owner, PllNativeEffectParticles Choice,
        ParticleSystem Particle, string StructuralPath)
    {
        var Source = Particle.gameObject;
        var Renderer = Source.GetComponent<ParticleSystemRenderer>();
        if (!Renderer || Source.transform.childCount != 0 || Source.GetComponents<Component>().Length != 3)
            throw new InvalidOperationException("PEAK's native campfire effect must be one pure particle-system graph.");
        ParticleVisualGraphCapture.ValidateCallbacks(Particle);
        GameObject? Prototype = null;
        try
        {
            Prototype = UnityEngine.Object.Instantiate(Source, Owner.RequireRoot().transform, false);
            Prototype.name = Source.name;
            var Graph = ParticleVisualGraph.Capture(Prototype, Prototype.transform, null, false);
            var Result = new NativeEffectParticleSource(
                Owner, Prototype, Graph, Choice, StructuralPath, Prototype.GetComponent<ParticleSystemRenderer>());
            Prototype = null;
            return Result;
        }
        finally
        {
            if (Prototype) UnityEngine.Object.Destroy(Prototype);
        }
    }

    internal NativeEffectParticleClone Instantiate(Transform Parent)
    {
        if (!Parent) throw new ArgumentNullException(nameof(Parent));
        GameObject? Root = null;
        try
        {
            Root = UnityEngine.Object.Instantiate(Prototype, Owner.StagingRoot.transform, false);
            var Resolved = Graph.Resolve(Root, Root.transform, null, false);
            foreach (var Particle in Resolved.AllParticles)
                Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Root.SetActive(false);
            Root.transform.SetParent(Parent, false);
            var Result = new NativeEffectParticleClone(Root, Resolved, Root.GetComponent<ParticleSystemRenderer>());
            Root = null;
            return Result;
        }
        finally
        {
            if (Root) UnityEngine.Object.Destroy(Root);
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write((byte)Choice); Writer.Write(StructuralPath); Graph.WriteManifest(Writer);
    }
}

internal readonly struct NativeEffectParticleClone
{
    internal NativeEffectParticleClone(GameObject Root, ResolvedParticleGraph Graph, ParticleSystemRenderer Renderer)
    {
        this.Root = Root; this.Graph = Graph; this.Renderer = Renderer;
    }

    internal GameObject Root { get; }
    internal ResolvedParticleGraph Graph { get; }
    internal ParticleSystemRenderer Renderer { get; }
}
