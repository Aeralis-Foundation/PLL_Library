using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class VisualEnvironmentRuntime
{
    readonly ParticleRoot[] segmentRoots;
    readonly TrackedParticleRoot[] CustomTrackedRoots;
    readonly NativeParticlePrototype[] NativeParticlePrototypes;
    readonly NativeVisualContract? NativeAppearance;
    readonly Transform? NativeParentTarget;
    readonly Vector3 OccurrenceEntry;
    readonly NativeSpecialDaySnapshot? specialDay;
    readonly MapSlot Slot;
    readonly NativeFogSource? NativeFog;
    readonly bool BaseEnvironmentalFog;
    GameObject? NativeParent;
    TrackedParticleRoot[]? NativeTrackedRoots;
    int? CharacterInstanceId;
    bool current;
    bool OwnsEnvironment;
    PllPresentationCoverage NativeTransition;
    float ZoneFogCoverage;

    internal VisualEnvironmentRuntime(
        MapSlot Slot,
        BiomeAuthoringContract contract,
        ResolvedBiomeAuthoring resolved,
        NativeParticlePrototypePool PrototypePool)
    {
        this.Slot = Slot;
        NativeAppearance = contract.NativeVisualAppearance;
        var segmentCount = 0;
        var trackedCount = 0;
        foreach (var visual in resolved.CustomVisuals)
        {
            if (visual.Space == PllCustomVisualSpace.Segment) segmentCount++;
            else trackedCount++;
        }
        var native = contract.NativeVisualSource;
        IReadOnlyList<NativeTrackedParticleSource>? NativeParticles = contract.SuppressNativeParticles
            ? null
            : native?.Particles;
        segmentRoots = segmentCount == 0 ? Array.Empty<ParticleRoot>() : new ParticleRoot[segmentCount];
        CustomTrackedRoots = trackedCount == 0 ? Array.Empty<TrackedParticleRoot>() : new TrackedParticleRoot[trackedCount];
        NativeParticlePrototypes = NativeParticles is { Count: > 0 }
            ? new NativeParticlePrototype[NativeParticles.Count]
            : Array.Empty<NativeParticlePrototype>();
        NativeParentTarget = NativeParticlePrototypes.Length == 0 ? null : Slot.CustomResolved!.Root.Value;
        OccurrenceEntry = Slot.PlacedEntry;

        var createdSegments = 0;
        var createdTracked = 0;
        try
        {
            foreach (var visual in resolved.CustomVisuals)
            {
                var particles = new ParticleRoot(visual.Root, visual.RootParticles, visual.AllParticles,
                    visual.Authoring.Transition.CoverDelaySeconds);
                if (visual.Space == PllCustomVisualSpace.Segment)
                {
                    segmentRoots[createdSegments++] = particles;
                    continue;
                }
                CustomTrackedRoots[createdTracked++] = new TrackedParticleRoot(
                    particles, true, true, true,
                    visual.PositionOffset, visual.RepositionDistance);
            }

            if (NativeParticles is not null)
                for (var Index = 0; Index < NativeParticles.Count; Index++)
                    NativeParticlePrototypes[Index] = PrototypePool.Capture(NativeParticles[Index]);
            specialDay = native?.SpecialDay;
            BaseEnvironmentalFog = Slot.Source is CustomLayoutSegmentSource
                { Registration.FogMode: PllFogMode.Fogless };
            NativeFog = BaseEnvironmentalFog || contract.RequiresNativeFog
                ? NativeFogRuntime.Require()
                : null;
            if (specialDay is not null || NativeFog is not null)
                SpecialDayProjection.PrepareAdapter();
        }
        catch (Exception failure)
        {
            List<Exception>? cleanup = null;
            for (var i = createdTracked - 1; i >= 0; i--)
                AuthoringCleanup.Capture(ref cleanup, CustomTrackedRoots[i].Stop);
            for (var i = createdSegments - 1; i >= 0; i--)
                AuthoringCleanup.Capture(ref cleanup, segmentRoots[i].Stop);

            if (cleanup is not null)
            {
                cleanup.Insert(0, failure);
                throw new AggregateException("Visual-environment construction and rollback both failed.", cleanup);
            }
            throw;
        }
    }

    internal void Activate(double PresentationClock)
    {
        if (current) return;
        try
        {
            CreateNativeParticles();
            if (!OwnsEnvironment) SpecialDayProjection.Apply(Slot, specialDay, NativeFog);
            OwnsEnvironment = specialDay is not null || NativeFog is not null;
            NativeTransition.SetTarget(specialDay is not null, PresentationClock, NativeAppearance?.TransitionSeconds ?? 0f);
            for (var i = 0; i < segmentRoots.Length; i++) segmentRoots[i].Play(PresentationClock);
            current = true;
            PublishEnvironment();
        }
        catch (Exception activationFailure)
        {
            try { Release(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Visual-environment activation and rollback both failed.", activationFailure, cleanupFailure);
            }
            throw;
        }
    }

    internal void Update(Character? nextCharacter, Vector3 center, double PresentationClock)
    {
        if (!current) return;
        int? NextCharacterInstanceId = nextCharacter ? nextCharacter.GetInstanceID() : null;
        if (CharacterInstanceId != NextCharacterInstanceId)
        {
            StopTracked();
            CharacterInstanceId = NextCharacterInstanceId;
            if (CharacterInstanceId is null) return;
            try
            {
                for (var Index = 0; Index < CustomTrackedRoots.Length; Index++) CustomTrackedRoots[Index].Play(center, PresentationClock);
                if (NativeTrackedRoots is { } NativeRoots)
                    for (var Index = 0; Index < NativeRoots.Length; Index++) NativeRoots[Index].Play(center, PresentationClock);
            }
            catch (Exception activationFailure)
            {
                try { StopTracked(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Tracked-particle activation and rollback both failed.", activationFailure, cleanupFailure);
                }
                CharacterInstanceId = null;
                throw;
            }
            return;
        }
        if (CharacterInstanceId is null) return;
        for (var Index = 0; Index < CustomTrackedRoots.Length; Index++) CustomTrackedRoots[Index].Reposition(center);
        if (NativeTrackedRoots is { } CurrentNativeRoots)
            for (var Index = 0; Index < CurrentNativeRoots.Length; Index++) CurrentNativeRoots[Index].Reposition(center);
    }

    internal void Release()
    {
        List<Exception>? Failures = null;
        AuthoringCleanup.Capture(ref Failures, StopParticles);
        AuthoringCleanup.Capture(ref Failures, ClearEnvironment);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 })
            throw new AggregateException("Visual-particle and environment cleanup failed.", Failures);
    }

    internal void StopParticles()
    {
        current = false;
        CharacterInstanceId = null;
        List<Exception>? failures = null;

        CaptureTrackedStops(ref failures);
        for (var i = segmentRoots.Length - 1; i >= 0; i--)
        {
            var root = segmentRoots[i];
            AuthoringCleanup.Capture(ref failures, root.Stop);
        }
        ReleaseNativeParticles(ref failures);
        if (failures is { Count: 1 }) ExceptionPropagation.Throw(failures[0]);
        if (failures is { Count: > 1 })
            throw new AggregateException("Visual-particle cleanup failed.", failures);
    }

    internal void SetZoneEnvironmentFog(float Coverage)
    {
        if (!OwnsEnvironment || ZoneFogCoverage.Equals(Coverage)) return;
        ZoneFogCoverage = Coverage;
        PublishEnvironment();
    }

    internal void Deactivate(double PresentationClock)
    {
        current = false;
        CharacterInstanceId = null;
        foreach (var Root in segmentRoots) Root.Deactivate(PresentationClock);
        foreach (var Root in CustomTrackedRoots) Root.Deactivate(PresentationClock);
        if (NativeTrackedRoots is { } NativeRoots)
            foreach (var Root in NativeRoots) Root.Deactivate(PresentationClock);
        NativeTransition.SetTarget(false, PresentationClock, NativeAppearance?.TransitionSeconds ?? 0f);
        PublishEnvironment();
    }

    internal void TickEnvironment(double PresentationClock)
    {
        var ParticlesPresent = false;
        foreach (var Root in segmentRoots)
        {
            Root.Tick(PresentationClock);
            ParticlesPresent |= Root.Present;
        }
        foreach (var Root in CustomTrackedRoots)
        {
            Root.Tick(PresentationClock);
            ParticlesPresent |= Root.Present;
        }
        if (NativeTrackedRoots is { } NativeRoots)
            foreach (var Root in NativeRoots)
            {
                Root.Tick(PresentationClock);
                ParticlesPresent |= Root.Present;
            }
        var Changed = NativeTransition.Tick(PresentationClock);
        if (Changed) PublishEnvironment();
        if (!current && !ParticlesPresent && NativeParent)
        {
            List<Exception>? Failures = null;
            ReleaseNativeParticles(ref Failures);
            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is { Count: > 1 }) throw new AggregateException("Native visual-particle cleanup failed.", Failures);
        }
    }

    internal void ClearEnvironment()
    {
        if (!OwnsEnvironment) return;
        List<Exception>? Failures = null;
        NativeTransition.Complete(false);
        ZoneFogCoverage = 0f;
        AuthoringCleanup.Capture(ref Failures, () => SpecialDayProjection.Clear(Slot));
        AuthoringCleanup.Capture(
            ref Failures, () => FogCutoutProjection.SetEnvironmentFog(Slot, 0f));
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Visual-environment cleanup failed.", Failures);
        OwnsEnvironment = false;
    }

    void PublishEnvironment()
    {
        if (!OwnsEnvironment) return;
        var FogCoverage = current && BaseEnvironmentalFog ? 1f : ZoneFogCoverage;
        if (!current && NativeTransition.Coverage == 0f && FogCoverage == 0f)
        {
            ClearEnvironment();
            return;
        }
        SpecialDayProjection.SetCoverage(Slot, NativeTransition.Coverage, FogCoverage);
        FogCutoutProjection.SetEnvironmentFog(Slot, FogCoverage);
    }

    void CreateNativeParticles()
    {
        if (NativeParticlePrototypes.Length == 0 || NativeParent) return;
        var Parent = new GameObject("PLL_NativeVisuals");
        var Roots = new TrackedParticleRoot[NativeParticlePrototypes.Length];
        var Created = 0;
        try
        {
            Parent.SetActive(false);
            Parent.transform.SetParent(NativeParentTarget!, false);
            for (; Created < Roots.Length; Created++)
                Roots[Created] = NativeParticlePrototypes[Created].Instantiate(Parent.transform, OccurrenceEntry, NativeAppearance);
            Parent.SetActive(true);
            NativeParent = Parent;
            NativeTrackedRoots = Roots;
        }
        catch (Exception Failure)
        {
            List<Exception>? CleanupFailures = null;
            for (var Index = Created - 1; Index >= 0; Index--)
                AuthoringCleanup.Capture(ref CleanupFailures, Roots[Index].Stop);
            if (Parent)
            {
                AuthoringCleanup.Capture(ref CleanupFailures, () => Parent.SetActive(false));
                AuthoringCleanup.Capture(ref CleanupFailures, () => UnityEngine.Object.Destroy(Parent));
            }
            if (CleanupFailures is not null)
            {
                CleanupFailures.Insert(0, Failure);
                throw new AggregateException(
                    "Native visual-particle construction and rollback both failed.", CleanupFailures);
            }
            throw;
        }
    }

    void StopTracked()
    {
        List<Exception>? failures = null;
        CaptureTrackedStops(ref failures);
        if (failures is { Count: 1 }) ExceptionPropagation.Throw(failures[0]);
        if (failures is { Count: > 1 }) throw new AggregateException("Tracked-particle cleanup failed.", failures);
    }

    void CaptureTrackedStops(ref List<Exception>? Failures)
    {
        if (NativeTrackedRoots is { } NativeRoots)
            for (var Index = NativeRoots.Length - 1; Index >= 0; Index--)
                AuthoringCleanup.Capture(ref Failures, NativeRoots[Index].Stop);
        for (var Index = CustomTrackedRoots.Length - 1; Index >= 0; Index--)
            AuthoringCleanup.Capture(ref Failures, CustomTrackedRoots[Index].Stop);
    }

    void ReleaseNativeParticles(ref List<Exception>? Failures)
    {
        NativeTrackedRoots = null;
        var Parent = NativeParent;
        NativeParent = null;
        if (!Parent) return;
        AuthoringCleanup.Capture(ref Failures, () => Parent.SetActive(false));
        AuthoringCleanup.Capture(ref Failures, () => UnityEngine.Object.Destroy(Parent));
    }

}

internal sealed class NativeParticlePrototypePool
{
    readonly Dictionary<NativeTrackedParticleSource, NativeParticlePrototype> Prototypes = new();
    Transform? Parent;
    GameObject? Root;

    internal NativeParticlePrototypePool(Transform Parent) => this.Parent = Parent;

    internal GameObject StagingRoot => Parent && Root
        ? Root
        : throw new InvalidOperationException("The native particle prototype pool was released.");

    internal GameObject RequireRoot()
    {
        var Target = Parent
            ? Parent
            : throw new InvalidOperationException("The native particle prototype pool was released.");
        if (Root) return Root;
        var OwnedRoot = new GameObject("PLL_NativeVisualPrototypes");
        try
        {
            OwnedRoot.SetActive(false);
            OwnedRoot.transform.SetParent(Target, false);
            Root = OwnedRoot;
            return OwnedRoot;
        }
        catch
        {
            if (OwnedRoot) UnityEngine.Object.Destroy(OwnedRoot);
            throw;
        }
    }

    internal NativeParticlePrototype Capture(NativeTrackedParticleSource Source)
    {
        if (Prototypes.TryGetValue(Source, out var Existing)) return Existing;
        var Owner = RequireRoot();
        GameObject? Prototype = null;
        try
        {
            var SourceName = Source.Fx.name;
            Prototype = UnityEngine.Object.Instantiate(Source.Fx, Owner.transform, false);
            Source.ResolveClone(Prototype);
            Prototype.name = SourceName;
            var Captured = new NativeParticlePrototype(Source, Prototype);
            Prototypes.Add(Source, Captured);
            Prototype = null;
            return Captured;
        }
        catch
        {
            if (Prototype) UnityEngine.Object.Destroy(Prototype);
            throw;
        }
    }

    internal void Release()
    {
        Prototypes.Clear();
        Parent = null;
        var OwnedRoot = Root;
        Root = null;
        if (!OwnedRoot) return;
        OwnedRoot.SetActive(false);
        UnityEngine.Object.Destroy(OwnedRoot);
    }
}

internal sealed class NativeParticlePrototype
{
    readonly NativeTrackedParticleSource Source;
    readonly GameObject Prototype;

    internal NativeParticlePrototype(NativeTrackedParticleSource Source, GameObject Prototype)
    {
        this.Source = Source;
        this.Prototype = Prototype;
    }

    internal TrackedParticleRoot Instantiate(Transform Parent, Vector3 OccurrenceEntry, NativeVisualContract? Appearance)
    {
        GameObject? Root = null;
        try
        {
            Root = UnityEngine.Object.Instantiate(Prototype, Parent, false);
            var Graph = Source.ResolveClone(Root);
            Appearance?.ApplyAppearance(Graph);
            Root.transform.position = Source.PreparePosition(OccurrenceEntry);
            var Particles = new ParticleRoot(Root, Graph.RootParticles, Graph.AllParticles,
                Appearance?.TransitionSeconds ?? 0f);
            var Result = new TrackedParticleRoot(
                Particles, Source.X, Source.Y, Source.Z,
                Source.PositionOffset, Source.RepositionDistance);
            Root = null;
            return Result;
        }
        catch
        {
            if (Root) UnityEngine.Object.Destroy(Root);
            throw;
        }
    }
}

internal sealed class ParticleRoot
{
    readonly GameObject root;
    readonly IReadOnlyList<ParticleSystem> rootParticles;
    readonly IReadOnlyList<ParticleSystem> allParticles;
    readonly float CoverDelaySeconds;
    readonly float[] EmissionTime;
    readonly float[] EmissionDistance;
    PllPresentationCoverage Transition;

    internal ParticleRoot(
        GameObject root,
        IReadOnlyList<ParticleSystem> rootParticles,
        IReadOnlyList<ParticleSystem> allParticles,
        float CoverDelaySeconds)
    {
        this.root = root;
        this.rootParticles = rootParticles;
        this.allParticles = allParticles;
        this.CoverDelaySeconds = CoverDelaySeconds;
        EmissionTime = CoverDelaySeconds > 0f ? new float[allParticles.Count] : Array.Empty<float>();
        EmissionDistance = CoverDelaySeconds > 0f ? new float[allParticles.Count] : Array.Empty<float>();
        for (var i = 0; i < EmissionTime.Length; i++)
        {
            EmissionTime[i] = allParticles[i].emission.rateOverTimeMultiplier;
            EmissionDistance[i] = allParticles[i].emission.rateOverDistanceMultiplier;
        }
        try
        {
            foreach (var particle in allParticles)
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            root.SetActive(false);
        }
        catch (Exception failure)
        {
            try { Stop(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("Particle-root preparation and rollback both failed.", failure, cleanup);
            }
            throw;
        }
    }

    internal Vector3 Position
    {
        get => root.transform.position;
        set => root.transform.position = value;
    }

    internal bool Present => Transition.Target || Transition.Active;

    internal void Play(double PresentationClock)
    {
        var WasPresent = Present;
        if (!Transition.SetTarget(true, PresentationClock, CoverDelaySeconds)) return;
        try
        {
            root.SetActive(true);
            ApplyCoverage();
            if (WasPresent) return;
            foreach (var particle in rootParticles)
            {
                particle.Clear(true);
                particle.Play(true);
            }
        }
        catch (Exception activationFailure)
        {
            try { Stop(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Particle-root activation and rollback both failed.", activationFailure, cleanupFailure);
            }
            throw;
        }
    }

    internal void Stop()
    {
        Transition.Complete(false);
        List<Exception>? failures = null;
        for (var i = allParticles.Count - 1; i >= 0; i--)
        {
            var particle = allParticles[i];
            if (!particle) continue;
            AuthoringCleanup.Capture(ref failures,
                () => particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear));
        }
        if (root) AuthoringCleanup.Capture(ref failures, () => root.SetActive(false));
        for (var i = 0; i < EmissionTime.Length; i++)
        {
            var Particle = allParticles[i];
            var TimeRate = EmissionTime[i];
            var DistanceRate = EmissionDistance[i];
            if (Particle) AuthoringCleanup.Capture(ref failures, () =>
            {
                var Emission = Particle.emission;
                Emission.rateOverTimeMultiplier = TimeRate;
                Emission.rateOverDistanceMultiplier = DistanceRate;
            });
        }
        if (failures is { Count: 1 }) ExceptionPropagation.Throw(failures[0]);
        if (failures is { Count: > 1 }) throw new AggregateException("Particle-root cleanup failed.", failures);
    }

    internal void Tick(double PresentationClock)
    {
        if (Transition.Tick(PresentationClock)) ApplyCoverage();
    }

    internal void Deactivate(double PresentationClock)
    {
        if (Transition.SetTarget(false, PresentationClock, CoverDelaySeconds)) ApplyCoverage();
    }

    void ApplyCoverage()
    {
        for (var i = 0; i < EmissionTime.Length; i++)
        {
            var Emission = allParticles[i].emission;
            Emission.rateOverTimeMultiplier = EmissionTime[i] * Transition.Coverage;
            Emission.rateOverDistanceMultiplier = EmissionDistance[i] * Transition.Coverage;
        }
        if (!Present && root.activeSelf)
        {
            foreach (var Particle in allParticles)
                Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            root.SetActive(false);
        }
    }
}

internal sealed class TrackedParticleRoot
{
    readonly ParticleRoot particles;
    readonly bool x;
    readonly bool y;
    readonly bool z;
    readonly Vector3 offset;
    readonly float distanceSquared;
    readonly Vector3 preparedPosition;
    Vector3 lastCenter;

    internal TrackedParticleRoot(
        ParticleRoot particles,
        bool x,
        bool y,
        bool z,
        Vector3 offset,
        float repositionDistance)
    {
        this.particles = particles;
        this.x = x;
        this.y = y;
        this.z = z;
        this.offset = offset;
        distanceSquared = repositionDistance * repositionDistance;
        preparedPosition = particles.Position;
    }

    internal void Play(Vector3 center, double PresentationClock)
    {
        Position(center);
        lastCenter = center;
        particles.Play(PresentationClock);
    }

    internal void Reposition(Vector3 center)
    {
        if ((center - lastCenter).sqrMagnitude < distanceSquared) return;
        Position(center);
        lastCenter = center;
    }

    internal void Stop() => particles.Stop();
    internal void Deactivate(double PresentationClock) => particles.Deactivate(PresentationClock);
    internal bool Present => particles.Present;
    internal void Tick(double PresentationClock) => particles.Tick(PresentationClock);

    void Position(Vector3 center)
    {
        var value = preparedPosition;
        if (x) value.x = center.x + offset.x;
        if (y) value.y = center.y + offset.y;
        if (z) value.z = center.z + offset.z;
        particles.Position = value;
    }
}
