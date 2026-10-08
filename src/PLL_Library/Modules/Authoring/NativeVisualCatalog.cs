using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PLL.Library;

internal sealed class NativeVisualCatalog
{
    internal const int SupportedMask = (1 << ((int)PllNativeVisualPreset.Nadir + 1)) - 2;
    readonly Inventory Owner;
    readonly Dictionary<PllNativeVisualPreset, NativeVisualInterval> intervals;
    readonly Dictionary<Transform, NativeVisualInterval[]> groups;
    readonly Dictionary<PllNativeVisualPreset, NativeParticleCandidate[]> Particles;
    readonly Dictionary<PllNativeVisualPreset, SpecialDayZone> PrimarySpecialDays = new();
    int EvaluatedSpecialDayMask;

    NativeVisualCatalog(
        Inventory Owner,
        Dictionary<PllNativeVisualPreset, NativeVisualInterval> Intervals,
        Dictionary<Transform, NativeVisualInterval[]> Groups,
        Dictionary<PllNativeVisualPreset, NativeParticleCandidate[]> ParticleCatalog)
    {
        this.Owner = Owner;
        intervals = Intervals;
        groups = Groups;
        Particles = ParticleCatalog;
    }

    internal sealed class Inventory
    {
        readonly IReadOnlyDictionary<string, LayoutSegmentSource> Sources;
        readonly Dictionary<PllNativeVisualPreset, NativeVisualSource> CapturedSources = new();
        readonly Dictionary<PllNativeEffectParticles, NativeEffectParticleSource> CapturedEffectParticles = new();
        readonly NativeParticlePrototypePool EffectPrototypes;
        GameObject[]? Roots;
        List<PlayerTrackParticles>? ParticleControllers;
        List<SpecialDayZone>? SpecialDayZones;
        NativeFogSource? CapturedFog;
        int CapturedMask;
        int CapturedParticleMask;

        internal Inventory(
            MapHandler Handler,
            IReadOnlyDictionary<string, LayoutSegmentSource> Sources)
        {
            this.Handler = Handler ? Handler : throw new ArgumentNullException(nameof(Handler));
            this.Sources = Sources ?? throw new ArgumentNullException(nameof(Sources));
            Scene = Handler.gameObject.scene;
            EffectPrototypes = new NativeParticlePrototypePool(Handler.transform);
        }

        internal MapHandler Handler { get; }
        internal Scene Scene { get; }
        internal IReadOnlyList<PlayerTrackParticles> Particles =>
            ParticleControllers ??= CaptureComponents<PlayerTrackParticles>();
        internal IReadOnlyList<SpecialDayZone> SpecialDays =>
            SpecialDayZones ??= CaptureComponents<SpecialDayZone>();

        internal NativeVisualSources Prepare(
            int RequiredMask,
            int RequiredParticleMask,
            bool RequireFog,
            int RequiredEffectParticleMask = 0)
        {
            if ((RequiredMask & ~SupportedMask) != 0)
                throw new InvalidOperationException($"Native visual source mask '{RequiredMask}' is not supported.");
            if ((RequiredParticleMask & ~RequiredMask) != 0)
                throw new InvalidOperationException(
                    $"Native particle source mask '{RequiredParticleMask}' requires an unavailable visual source.");
            if ((RequiredEffectParticleMask & ~NativeEffectParticleSource.SupportedMask) != 0)
                throw new InvalidOperationException($"Native effect-particle source mask '{RequiredEffectParticleMask}' is not supported.");

            var NewMask = RequiredMask & ~CapturedMask;
            var NewParticleMask = RequiredParticleMask & ~CapturedParticleMask;
            var CaptureMask = NewMask | NewParticleMask;
            var CaptureFog = RequireFog && CapturedFog is null;
            if ((CaptureMask != 0 || CaptureFog || RequiredEffectParticleMask != 0)
                && (!Handler || !Scene.IsValid() || !Scene.isLoaded))
                throw new InvalidOperationException("PEAK's native visual catalog scene is not loaded.");

            var BatchEffectParticles = new Dictionary<PllNativeEffectParticles, NativeEffectParticleSource>();
            global::Campfire? Campfire = null;
            for (var Value = (int)PllNativeEffectParticles.Fire; Value <= (int)PllNativeEffectParticles.SmokeLit; Value++)
            {
                if ((RequiredEffectParticleMask & (1 << Value)) == 0) continue;
                var Choice = (PllNativeEffectParticles)Value;
                if (!CapturedEffectParticles.TryGetValue(Choice, out var Captured))
                {
                    if (!Campfire)
                    {
                        var Gloom = Require("Gloom");
                        Campfire = Gloom.SegmentCampfire
                            ? Gloom.SegmentCampfire.GetComponent<global::Campfire>()
                            : null;
                        if (!Campfire || !Campfire.enabled || Campfire.gameObject.scene != Scene)
                            throw new InvalidOperationException("PEAK's native Gloom campfire particle owner is unavailable.");
                    }
                    var Particle = Choice switch
                    {
                        PllNativeEffectParticles.Fire => Campfire.fireParticles,
                        PllNativeEffectParticles.SmokeOff => Campfire.smokeParticlesOff,
                        PllNativeEffectParticles.SmokeLit => Campfire.smokeParticlesLit,
                        _ => throw new InvalidOperationException("The native effect-particle choice is not supported.")
                    };
                    if (!Particle || !TransformOwnership.Contains(Campfire.transform, Particle.transform))
                        throw new InvalidOperationException("PEAK's native campfire particle graph left its typed owner.");
                    Captured = NativeEffectParticleSource.Capture(
                        EffectPrototypes, Choice, Particle,
                        DeterministicObjectIdentity.RelativeComponentPath(Particle, Campfire.transform, 0));
                    CapturedEffectParticles.Add(Choice, Captured);
                }
                BatchEffectParticles.Add(Choice, Captured);
            }

            if (CaptureMask != 0 || CaptureFog)
            {
                var Catalog = Create(this, CaptureMask, NewParticleMask, CaptureFog);
                var PendingSources = new Dictionary<PllNativeVisualPreset, NativeVisualSource>();
                for (var Value = (int)PllNativeVisualPreset.Beach;
                    Value <= (int)PllNativeVisualPreset.Nadir; Value++)
                {
                    var Mask = 1 << Value;
                    if ((CaptureMask & Mask) == 0) continue;
                    PendingSources.Add(
                        (PllNativeVisualPreset)Value,
                        Catalog.Capture(
                            (PllNativeVisualPreset)Value,
                            (NewParticleMask & Mask) != 0));
                }
                var PendingFog = CaptureFog ? Catalog.CaptureFog() : null;
                foreach (var Pair in PendingSources) CapturedSources[Pair.Key] = Pair.Value;
                CapturedMask |= CaptureMask;
                CapturedParticleMask |= NewParticleMask;
                if (CaptureFog)
                {
                    CapturedFog = PendingFog;
                }
            }

            var BatchSources = new Dictionary<PllNativeVisualPreset, NativeVisualSource>();
            for (var Value = (int)PllNativeVisualPreset.Beach;
                Value <= (int)PllNativeVisualPreset.Nadir; Value++)
            {
                if ((RequiredMask & (1 << Value)) == 0) continue;
                var Preset = (PllNativeVisualPreset)Value;
                if (!CapturedSources.TryGetValue(Preset, out var Source))
                    throw new InvalidOperationException(
                        $"PEAK's native visual preset '{Preset}' was not captured by its live inventory.");
                BatchSources.Add(Preset, Source);
            }
            return new NativeVisualSources(
                BatchSources, RequiredMask, RequiredParticleMask,
                RequireFog ? CapturedFog : null, BatchEffectParticles, RequiredEffectParticleMask);
        }

        internal void Release() => EffectPrototypes.Release();

        List<T> CaptureComponents<T>() where T : Component
        {
            Roots ??= Scene.GetRootGameObjects();
            var Result = new List<T>();
            foreach (var Root in Roots)
                Result.AddRange(Root.GetComponentsInChildren<T>(true));
            return Result;
        }

        internal LayoutSegmentSource Require(string Name) => Sources.TryGetValue(Name, out var Source)
            ? Source
            : throw new InvalidOperationException($"PEAK's native visual source '{Name}' is unavailable.");

        internal FogCutoutHandler RequireFogCutoutHandler()
        {
            Roots ??= Scene.GetRootGameObjects();
            FogCutoutHandler? Result = null;
            foreach (var Root in Roots)
                foreach (var Handler in Root.GetComponentsInChildren<FogCutoutHandler>(true))
                {
                    if (Result)
                        throw new InvalidOperationException("PEAK's native FogCutoutHandler ownership is ambiguous.");
                    Result = Handler;
                }
            return Result
                ? Result
                : throw new InvalidOperationException("PEAK's native FogCutoutHandler is unavailable.");
        }
    }

    static NativeVisualCatalog Create(
        Inventory Owner,
        int RequiredMask,
        int RequiredParticleMask,
        bool RequireFog)
    {
        var Intervals = new Dictionary<PllNativeVisualPreset, NativeVisualInterval>();
        AddSingle(PllNativeVisualPreset.Beach, "Beach");
        AddSingle(PllNativeVisualPreset.Tropics, "Tropics");
        AddSingle(PllNativeVisualPreset.Roots, "Roots");
        AddSingle(PllNativeVisualPreset.Alpine, "Alpine");
        AddSingle(PllNativeVisualPreset.Mesa, "Mesa");
        AddSingle(PllNativeVisualPreset.Nadir, "Nadir");
        if (RequireFog || Required(PllNativeVisualPreset.Caldera) || Required(PllNativeVisualPreset.Volcano)
            || Required(PllNativeVisualPreset.Peak))
        {
            Add(PllNativeVisualPreset.Caldera, Owner.Require("Caldera"));
            var Volcano = Owner.Require("Volcano") as NativeOrdinarySegmentSource
                ?? throw new InvalidOperationException("PEAK's native Volcano visual source is unavailable.");
            var Peak = Volcano.ContextualPeak
                ?? throw new InvalidOperationException("PEAK's Volcano-owned generic Peak prototype is unavailable.");
            var VolcanoInterval = NativeVisualInterval.Create(PllNativeVisualPreset.Volcano, Volcano);
            Intervals.Add(PllNativeVisualPreset.Volcano, VolcanoInterval);
            Intervals.Add(PllNativeVisualPreset.Peak, NativeVisualInterval.CreatePeak(Peak, VolcanoInterval));
            ValidateComposite(PllNativeVisualPreset.Caldera, PllNativeVisualPreset.Volcano);
        }
        if (Required(PllNativeVisualPreset.Gloom) || Required(PllNativeVisualPreset.Citadel))
        {
            Add(PllNativeVisualPreset.Gloom, Owner.Require("Gloom"));
            Add(PllNativeVisualPreset.Citadel, Owner.Require("Citadel"));
            ValidateComposite(PllNativeVisualPreset.Gloom, PllNativeVisualPreset.Citadel);
        }

        var PendingGroups = new Dictionary<Transform, List<NativeVisualInterval>>();
        foreach (var Interval in Intervals.Values)
        {
            if (Interval.Container.gameObject.scene != Owner.Scene)
                throw new InvalidOperationException(
                    $"PEAK's native visual preset '{Interval.Preset}' left the native scene.");
            if (!PendingGroups.TryGetValue(Interval.Container, out var Group))
            {
                Group = new List<NativeVisualInterval>();
                PendingGroups.Add(Interval.Container, Group);
            }
            Group.Add(Interval);
        }
        var Groups = new Dictionary<Transform, NativeVisualInterval[]>();
        foreach (var Pair in PendingGroups)
        {
            Pair.Value.Sort((Left, Right) => Left.Lower.CompareTo(Right.Lower));
            for (var Index = 0; Index < Pair.Value.Count; Index++)
            {
                var Interval = Pair.Value[Index];
                if (Interval.ProjectionAxis != Pair.Value[0].ProjectionAxis)
                    throw new InvalidOperationException(
                        "PEAK's native visual intervals in one common container use different progression axes.");
                if (!(Interval.Upper > Interval.Lower))
                    throw new InvalidOperationException(
                        $"PEAK's native visual preset '{Interval.Preset}' has a zero or reversed interval.");
                if (Index != 0 && Interval.Lower < Pair.Value[Index - 1].Upper)
                    throw new InvalidOperationException(
                        $"PEAK's native visual intervals '{Pair.Value[Index - 1].Preset}' and '{Interval.Preset}' overlap.");
                Interval.UpperInclusive = Index == Pair.Value.Count - 1;
            }
            Groups.Add(Pair.Key, Pair.Value.ToArray());
        }
        var ParticleCatalog = RequiredParticleMask == 0
            ? new Dictionary<PllNativeVisualPreset, NativeParticleCandidate[]>()
            : ClassifyParticles(Owner.Particles, Groups, RequiredParticleMask);
        return new NativeVisualCatalog(Owner, Intervals, Groups, ParticleCatalog);

        void Add(PllNativeVisualPreset Preset, LayoutSegmentSource Source) =>
            Intervals.Add(Preset, NativeVisualInterval.Create(Preset, Source));

        void AddSingle(PllNativeVisualPreset Preset, string Source)
        {
            if (Required(Preset)) Add(Preset, Owner.Require(Source));
        }

        bool Required(PllNativeVisualPreset Preset) => (RequiredMask & (1 << (int)Preset)) != 0;

        void ValidateComposite(PllNativeVisualPreset LowerPreset, PllNativeVisualPreset UpperPreset)
        {
            var Lower = Intervals[LowerPreset];
            var Upper = Intervals[UpperPreset];
            if (!NativeSegmentCatalog.IsCompositePair(Lower.Source, Upper.Source)
                || Lower.Container != Upper.Container || Lower.Upper != Upper.Lower)
                throw new InvalidOperationException(
                    $"PEAK's native visual composite '{LowerPreset}/{UpperPreset}' has no exact shared boundary.");
        }
    }

    static Dictionary<PllNativeVisualPreset, NativeParticleCandidate[]> ClassifyParticles(
        IReadOnlyList<PlayerTrackParticles> Controllers,
        Dictionary<Transform, NativeVisualInterval[]> Groups,
        int RequiredParticleMask)
    {
        var Pending = new Dictionary<PllNativeVisualPreset, List<NativeParticleCandidate>>();
        foreach (var Controller in Controllers)
        {
            if (!Controller || !Controller.enabled) continue;
            var Interval = ClassifyParticle(
                Groups, Controller.transform, Controller.bounds.center, RequiredParticleMask);
            if (Interval is null) continue;
            var Key = DeterministicObjectIdentity.RelativeComponentPath(
                Controller, Interval.Container, 0);
            if (!Pending.TryGetValue(Interval.Preset, out var Candidates))
            {
                Candidates = new List<NativeParticleCandidate>();
                Pending.Add(Interval.Preset, Candidates);
            }
            Candidates.Add(new NativeParticleCandidate(Key, Controller, Interval));
        }

        var Result = new Dictionary<PllNativeVisualPreset, NativeParticleCandidate[]>();
        foreach (var Pair in Pending)
        {
            Pair.Value.Sort(static (Left, Right) =>
                StringComparer.Ordinal.Compare(Left.Key, Right.Key));
            Result.Add(Pair.Key, Pair.Value.ToArray());
        }
        return Result;
    }

    internal NativeFogSource CaptureFog()
    {
        if (!intervals.TryGetValue(PllNativeVisualPreset.Caldera, out var target))
            throw new InvalidOperationException("PEAK's native Caldera fog interval is unavailable.");
        var specialDay = PrimarySpecialDay(target)
            ?? throw new InvalidOperationException("PEAK's primary Caldera SpecialDay fog source is unavailable.");
        if (target.Source is not NativeOrdinarySegmentSource { NativeRole: Segment.Caldera }
            || !target.Source.WallNext)
            throw new InvalidOperationException("PEAK's native Caldera role has no exact outgoing fog wall.");
        var Origin = target.Source.WallNext.GetComponentInParent<FogSphereOrigin>(true);
        if (!FogOriginContract.IsNativeTemplate(Origin, true))
            throw new InvalidOperationException(
                "PEAK's disabled native Caldera fog-origin contract is unavailable.");
        return NativeFogSource.Capture(
            Origin!,
            NativeFogCutoutTable.Capture(Owner.RequireFogCutoutHandler()).Select(target.Source),
            specialDay);
    }

    SpecialDayZone? PrimarySpecialDay(NativeVisualInterval Target)
    {
        var Mask = 1 << (int)Target.Preset;
        if ((EvaluatedSpecialDayMask & Mask) != 0)
            return PrimarySpecialDays.TryGetValue(Target.Preset, out var Cached) ? Cached : null;

        SpecialDayZone? Result = null;
        if (Target.Preset != PllNativeVisualPreset.Peak)
            foreach (var Zone in Owner.SpecialDays)
            {
                if (!Zone || !Zone.enabled || !Zone.gameObject.activeSelf || Zone.globalZone) continue;
                var Interval = Assign(Target, Zone.transform, Zone.bounds.center);
                if (Interval is null || Interval.Preset != Target.Preset) continue;
                if (Result)
                    throw new InvalidOperationException(
                        $"PEAK's native visual preset '{Target.Preset}' has multiple primary SpecialDay zones.");
                Result = Zone;
            }
        if (Result) PrimarySpecialDays.Add(Target.Preset, Result);
        EvaluatedSpecialDayMask |= Mask;
        return Result;
    }

    internal NativeVisualSource Capture(PllNativeVisualPreset Preset, bool IncludeParticles)
    {
        if (!intervals.TryGetValue(Preset, out var Target))
            throw new InvalidOperationException($"PEAK's native visual preset '{Preset}' is unavailable.");

        NativeTrackedParticleSource[] CapturedParticles;
        if (!IncludeParticles || !Particles.TryGetValue(Preset, out var Candidates))
            CapturedParticles = Array.Empty<NativeTrackedParticleSource>();
        else
        {
            CapturedParticles = new NativeTrackedParticleSource[Candidates.Length];
            for (var Index = 0; Index < Candidates.Length; Index++)
            {
                var Candidate = Candidates[Index];
                CapturedParticles[Index] = NativeTrackedParticleSource.Capture(
                    Candidate.Controller, Candidate.Interval, Candidate.Key);
            }
        }

        var SpecialDay = PrimarySpecialDay(Target);
        var Special = SpecialDay
            ? NativeSpecialDaySnapshot.Capture(
                SpecialDay,
                DeterministicObjectIdentity.RelativeComponentPath(
                    SpecialDay, Target.Container, 0))
            : null;
        return NativeVisualSource.Capture(Target, CapturedParticles, Special);
    }

    NativeVisualInterval? Assign(NativeVisualInterval target, Transform owner, Vector3 center)
    {
        foreach (var Candidate in intervals.Values)
            if (Candidate.Source is NativeOrdinarySegmentSource { ContextualPeak: { } Peak }
                && TransformOwnership.Contains(Peak.SegmentParent.transform, owner)) return null;

        NativeVisualInterval? Interval = null;
        foreach (var Candidate in intervals.Values)
        {
            var Campfire = Candidate.Source.SegmentCampfire;
            if (!Campfire || !TransformOwnership.Contains(Campfire.transform, owner)) continue;
            if (Interval is not null)
                throw new InvalidOperationException("A native SpecialDay zone has ambiguous campfire ownership.");
            Interval = Candidate;
        }
        Interval ??= Classify(groups, owner, center);
        return Interval is not null
            && ReferenceEquals(groups[target.Container], groups[Interval.Container])
                ? Interval
                : null;
    }

    static NativeVisualInterval? Classify(
        IReadOnlyDictionary<Transform, NativeVisualInterval[]> Groups,
        Transform Owner,
        Vector3 Center)
    {
        var OwnedGroup = FindOwnedGroup(Groups, Owner, SupportedMask);
        if (OwnedGroup is null) return null;
        return FindInterval(OwnedGroup, Center)
            ?? throw new InvalidOperationException(
                "A native visual component in a validated common container maps to no projected interval.");
    }

    static NativeVisualInterval? ClassifyParticle(
        IReadOnlyDictionary<Transform, NativeVisualInterval[]> Groups,
        Transform Owner,
        Vector3 Center,
        int RequiredParticleMask)
    {
        var OwnedGroup = FindOwnedGroup(Groups, Owner, RequiredParticleMask);
        if (OwnedGroup is null) return null;
        var Result = FindInterval(OwnedGroup, Center);
        if (Result is not null)
            return (RequiredParticleMask & (1 << (int)Result.Preset)) != 0
                ? Result
                : null;
        foreach (var Interval in OwnedGroup)
            if ((RequiredParticleMask & (1 << (int)Interval.Preset)) == 0) return null;
        throw new InvalidOperationException(
            "A demanded native particle source maps to no projected interval.");
    }

    static NativeVisualInterval[]? FindOwnedGroup(
        IReadOnlyDictionary<Transform, NativeVisualInterval[]> Groups,
        Transform Owner,
        int RelevantMask)
    {
        NativeVisualInterval[]? Result = null;
        foreach (var Pair in Groups)
        {
            var Required = false;
            foreach (var Interval in Pair.Value)
                if ((RelevantMask & (1 << (int)Interval.Preset)) != 0)
                {
                    Required = true;
                    break;
                }
            if (!Required) continue;
            if (!TransformOwnership.Contains(Pair.Key, Owner)) continue;
            if (Result is not null && !ReferenceEquals(Result, Pair.Value))
                throw new InvalidOperationException(
                    "A native visual component has ambiguous common-container ownership.");
            Result = Pair.Value;
        }
        return Result;
    }

    static NativeVisualInterval? FindInterval(
        NativeVisualInterval[] Group,
        Vector3 Center)
    {
        if (!UnityValueContract.Finite(Center))
            throw new InvalidOperationException("A native visual Bounds center is non-finite.");

        NativeVisualInterval? Result = null;
        foreach (var Interval in Group)
        {
            var Projection = Vector3.Dot(Center, Interval.ProjectionAxis);
            if (Projection < Interval.Lower || Projection > Interval.Upper
                || Projection == Interval.Upper && !Interval.UpperInclusive) continue;
            if (Result is not null)
                throw new InvalidOperationException(
                    "A native visual Bounds center maps to more than one projected interval.");
            Result = Interval;
        }
        return Result;
    }

    readonly struct NativeParticleCandidate
    {
        internal NativeParticleCandidate(
            string Key,
            PlayerTrackParticles Controller,
            NativeVisualInterval Interval)
        {
            this.Key = Key;
            this.Controller = Controller;
            this.Interval = Interval;
        }

        internal string Key { get; }
        internal PlayerTrackParticles Controller { get; }
        internal NativeVisualInterval Interval { get; }
    }
}

internal sealed class NativeVisualInterval
{
    NativeVisualInterval(
        PllNativeVisualPreset preset,
        LayoutSegmentSource Source,
        Vector3 entryPosition,
        Vector3 exitPosition,
        NativeVisualInterval? ProjectionOwner)
    {
        if (!Source.DayNightProfile)
            throw new InvalidOperationException($"PEAK's native visual preset '{preset}' has no DayNightProfile.");
        Preset = preset;
        this.Source = Source;
        Container = Source.Container;
        Axis = Source.ProgressionAxis.normalized;
        if (!UnityValueContract.Finite(Axis) || Axis.sqrMagnitude < 0.999f)
            throw new InvalidOperationException($"PEAK's native visual preset '{preset}' has an invalid progression axis.");
        EntryPosition = entryPosition;
        ExitPosition = exitPosition;
        ProjectionAxis = ProjectionOwner?.ProjectionAxis ?? Axis;
        var Entry = Vector3.Dot(entryPosition, ProjectionAxis);
        var Exit = Vector3.Dot(exitPosition, ProjectionAxis);
        Lower = ProjectionOwner is null ? Entry : Math.Min(Entry, Exit);
        Upper = ProjectionOwner is null ? Exit : Math.Max(Entry, Exit);
    }

    internal PllNativeVisualPreset Preset { get; }
    internal LayoutSegmentSource Source { get; }
    internal Transform Container { get; }
    internal Vector3 Axis { get; }
    internal Vector3 ProjectionAxis { get; }
    internal Vector3 EntryPosition { get; }
    internal Vector3 ExitPosition { get; }
    internal float Lower { get; }
    internal float Upper { get; }
    internal bool UpperInclusive { get; set; }

    internal static NativeVisualInterval Create(PllNativeVisualPreset Preset, LayoutSegmentSource Source)
    {
        if (Source.EntryPorts.Count != 1 || Source.ExitPorts.Count != 1)
            throw new InvalidOperationException($"PEAK's native visual preset '{Preset}' must expose one entry and one exit.");
        return new NativeVisualInterval(
            Preset, Source,
            Source.AuthoredPosition(Source.EntryPorts[0].Pose),
            Source.AuthoredPosition(Source.ExitPorts[0].Pose), null);
    }

    internal static NativeVisualInterval CreatePeak(
        PeakAttachmentSource Peak,
        NativeVisualInterval ProjectionOwner)
    {
        var Bridge = Peak.BridgeExit;
        if (Peak.ExitPorts.Count != 1)
            throw new InvalidOperationException("PEAK's generic Peak visual interval has no bridge or summit boundary.");
        return new NativeVisualInterval(
            PllNativeVisualPreset.Peak, Peak,
            Peak.AuthoredPosition(Bridge.Pose),
            Peak.AuthoredPosition(Peak.ExitPorts[0].Pose), ProjectionOwner);
    }
}
