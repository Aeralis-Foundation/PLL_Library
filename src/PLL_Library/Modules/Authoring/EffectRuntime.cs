using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class EffectState
{
    readonly RuntimeBiomeAuthoring Biome;
    readonly GameObject? PresentationRoot;
    readonly GameObject? NativeRoot;
    readonly bool PresentationActive;
    readonly IReadOnlyList<ParticleSystem> TopParticles;
    readonly IReadOnlyList<ParticleSystem> AllParticles;
    readonly ParticlePlayback[] ParticlePlaybackStates;
    readonly ParticleSystem.MinMaxGradient[] ParticleColors;
    readonly Renderer? ColorRenderer;
    readonly MaterialPropertyBlock? OriginalColorBlock;
    readonly MaterialPropertyBlock? AppliedColorBlock;
    readonly Color OriginalMaterialColor;
    readonly Color TargetMaterialColor;
    readonly int ColorProperty;
    readonly int ColorMaterialSlot;
    readonly float[] EmissionTime;
    readonly float[] EmissionDistance;
    readonly Light[] Lights;
    readonly float[] LightIntensities;
    readonly float CoverDelaySeconds;
    PllPresentationCoverage Transition;
    AudioSource? Ambience;
    bool AppearanceApplied;
    float Deadline;

    internal EffectState(RuntimeBiomeAuthoring Biome, ResolvedEffect Resolved)
    {
        this.Biome = Biome;
        Authoring = Resolved.Authoring;
        var Clone = Resolved.NativeParticles?.Instantiate(Resolved.Root.transform);
        NativeRoot = Clone?.Root;
        try
        {
            PresentationRoot = NativeRoot ? NativeRoot : Resolved.PresentationRoot;
            PresentationActive = PresentationRoot && PresentationRoot.activeSelf;
            TopParticles = Clone?.Graph.RootParticles ?? Resolved.TopParticles;
            AllParticles = Clone?.Graph.AllParticles ?? Resolved.AllParticles;
            CoverDelaySeconds = Authoring.Transition.CoverDelaySeconds;
            EmissionTime = CoverDelaySeconds > 0f ? new float[AllParticles.Count] : Array.Empty<float>();
            EmissionDistance = CoverDelaySeconds > 0f ? new float[AllParticles.Count] : Array.Empty<float>();
            Lights = CoverDelaySeconds > 0f && PresentationRoot
                ? PresentationRoot.GetComponentsInChildren<Light>(true) : Array.Empty<Light>();
            LightIntensities = new float[Lights.Length];
            for (var i = 0; i < Lights.Length; i++) LightIntensities[i] = Lights[i].intensity;
            ParticlePlaybackStates = new ParticlePlayback[AllParticles.Count];
            ParticleColors = Authoring.ParticleColorMode == PllEffectParticleColorMode.Override
                ? new ParticleSystem.MinMaxGradient[AllParticles.Count]
                : Array.Empty<ParticleSystem.MinMaxGradient>();
            ColorRenderer = Authoring.MaterialColorChannel == PllEffectMaterialColorChannel.None
                ? null : Clone?.Renderer ?? Resolved.ColorRenderer;
            if (ColorRenderer)
            {
                ColorMaterialSlot = Authoring.ColorMaterialSlot;
                TargetMaterialColor = Authoring.MaterialColor;
                ColorProperty = Shader.PropertyToID(PllEffectAppearanceValueContract.MaterialProperty(
                    Authoring.MaterialColorChannel));
                OriginalColorBlock = new MaterialPropertyBlock();
                ColorRenderer.GetPropertyBlock(OriginalColorBlock, ColorMaterialSlot);
                AppliedColorBlock = new MaterialPropertyBlock();
                if (OriginalColorBlock.isEmpty) ColorRenderer.GetPropertyBlock(AppliedColorBlock);
                else ColorRenderer.GetPropertyBlock(AppliedColorBlock, ColorMaterialSlot);
                OriginalMaterialColor = AppliedColorBlock.HasColor(ColorProperty)
                    ? AppliedColorBlock.GetColor(ColorProperty)
                    : ColorRenderer.sharedMaterials[ColorMaterialSlot].GetColor(ColorProperty);
            }
            for (var i = 0; i < AllParticles.Count; i++)
            {
                if (EmissionTime.Length != 0)
                {
                    EmissionTime[i] = AllParticles[i].emission.rateOverTimeMultiplier;
                    EmissionDistance[i] = AllParticles[i].emission.rateOverDistanceMultiplier;
                }
                if (ParticleColors.Length != 0) ParticleColors[i] = AllParticles[i].main.startColor;
                ParticlePlaybackStates[i] = AllParticles[i].isPaused
                    ? ParticlePlayback.Paused
                    : AllParticles[i].isEmitting
                        ? ParticlePlayback.Emitting
                        : AllParticles[i].isPlaying ? ParticlePlayback.Playing : ParticlePlayback.Stopped;
            }
            foreach (var Particle in AllParticles) Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (PresentationRoot) PresentationRoot.SetActive(false);
        }
        catch
        {
            if (NativeRoot) UnityEngine.Object.Destroy(NativeRoot);
            throw;
        }
    }

    internal PllEffectAuthoring Authoring { get; }
    internal bool IsPlaying { get; private set; }
    internal float Coverage => Transition.Coverage;

    internal void Play(float Now, double PresentationClock)
    {
        if (IsPlaying) return;
        Transition.SetTarget(true, PresentationClock, CoverDelaySeconds);
        var Starting = Transition.Coverage == 0f;
        try
        {
            AppearanceApplied = ParticleColors.Length != 0 || ColorRenderer;
            for (var i = 0; i < ParticleColors.Length; i++)
            {
                var Main = AllParticles[i].main;
                Main.startColor = Authoring.ParticleColor;
            }
            if (PresentationRoot) PresentationRoot.SetActive(true);
            ApplyCoverage();
            foreach (var Particle in TopParticles)
            {
                if (Starting) Particle.Clear(false);
                Particle.Play(false);
            }
            if (Authoring.Ambience)
            {
                if (Ambience is null)
                {
                    Ambience = Authoring.gameObject.AddComponent<AudioSource>();
                    Ambience.playOnAwake = false;
                    Ambience.loop = true;
                    Ambience.spatialBlend = 0f;
                    Ambience.clip = Authoring.Ambience;
                }
                Ambience.volume = Transition.Coverage;
                if (!Ambience.isPlaying) Ambience.Play();
            }
            var Sound = Authoring.Sound ? Authoring.Sound : Biome.NextEffectSound();
            if (Sound) AuthoringPresentationRuntime.PlayOneShot(Sound);
            Deadline = Authoring.Duration > 0f ? Now + Authoring.Duration : float.PositiveInfinity;
            IsPlaying = true;
        }
        catch (Exception ActivationFailure)
        {
            try { Stop(); }
            catch (Exception RollbackFailure)
            {
                throw new AggregateException(
                    "Effect activation and rollback both failed.", ActivationFailure, RollbackFailure);
            }
            throw;
        }
    }

    internal bool Update(float Now, double PresentationClock)
    {
        var Expired = IsPlaying && Now >= Deadline;
        if (Expired) Deactivate(PresentationClock);
        else if (Transition.Tick(PresentationClock)) ApplyCoverage();
        return Expired;
    }

    internal void Deactivate(double PresentationClock)
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        Deadline = 0f;
        Transition.SetTarget(false, PresentationClock, CoverDelaySeconds);
        ApplyCoverage();
    }

    void ApplyCoverage()
    {
        if (!Transition.Target && !Transition.Active)
        {
            Stop();
            return;
        }
        for (var i = 0; i < EmissionTime.Length; i++)
        {
            var Emission = AllParticles[i].emission;
            Emission.rateOverTimeMultiplier = EmissionTime[i] * Transition.Coverage;
            Emission.rateOverDistanceMultiplier = EmissionDistance[i] * Transition.Coverage;
        }
        for (var i = 0; i < Lights.Length; i++) Lights[i].intensity = LightIntensities[i] * Transition.Coverage;
        if (ColorRenderer)
        {
            AppliedColorBlock!.SetColor(ColorProperty, Color.LerpUnclamped(
                OriginalMaterialColor, TargetMaterialColor, Transition.Coverage));
            ColorRenderer.SetPropertyBlock(AppliedColorBlock, ColorMaterialSlot);
        }
        if (Ambience) Ambience.volume = Transition.Coverage;
    }

    internal void Stop()
    {
        IsPlaying = false;
        Transition.Complete(false);
        Deadline = 0f;
        List<Exception>? Failures = null;

        if (Ambience) CleanupOperations.Capture(ref Failures, () => Ambience.Stop());
        CleanupOperations.Capture(ref Failures, StopParticles);
        if (AppearanceApplied) CleanupOperations.Capture(ref Failures, RestoreAppearance);
        if (PresentationRoot) CleanupOperations.Capture(ref Failures, () => PresentationRoot.SetActive(false));
        for (var i = 0; i < EmissionTime.Length; i++)
        {
            var Particle = AllParticles[i];
            var TimeRate = EmissionTime[i];
            var DistanceRate = EmissionDistance[i];
            if (Particle) CleanupOperations.Capture(ref Failures, () =>
            {
                var Emission = Particle.emission;
                Emission.rateOverTimeMultiplier = TimeRate;
                Emission.rateOverDistanceMultiplier = DistanceRate;
            });
        }
        for (var i = 0; i < Lights.Length; i++)
        {
            var Light = Lights[i];
            var Intensity = LightIntensities[i];
            if (Light) CleanupOperations.Capture(ref Failures, () => Light.intensity = Intensity);
        }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Effect cleanup failed.", Failures);
    }

    void RestoreAppearance()
    {
        List<Exception>? Failures = null;
        for (var i = 0; i < ParticleColors.Length; i++)
        {
            var Particle = AllParticles[i];
            var Original = ParticleColors[i];
            if (Particle) CleanupOperations.Capture(ref Failures, () =>
            {
                var Main = Particle.main;
                Main.startColor = Original;
            });
        }
        if (ColorRenderer) CleanupOperations.Capture(ref Failures,
            () => ColorRenderer.SetPropertyBlock(OriginalColorBlock, ColorMaterialSlot));
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Effect appearance restoration failed.", Failures);
        AppearanceApplied = false;
    }

    internal void StopParticles()
    {
        List<Exception>? Failures = null;
        for (var i = AllParticles.Count - 1; i >= 0; i--)
        {
            var Particle = AllParticles[i];
            if (!Particle) continue;
            CleanupOperations.Capture(ref Failures,
                () => Particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear));
        }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Effect-particle cleanup failed.", Failures);
    }

    internal void Restore()
    {
        List<Exception>? Failures = null;

        CleanupOperations.Capture(ref Failures, Stop);
        if (!NativeRoot)
        {
            if (PresentationRoot) CleanupOperations.Capture(ref Failures, () => PresentationRoot.SetActive(true));
            for (var i = 0; i < AllParticles.Count; i++)
            {
                var Particle = AllParticles[i];
                if (!Particle) continue;
                var Playback = ParticlePlaybackStates[i];
                CleanupOperations.Capture(ref Failures, () => RestoreParticle(Particle, Playback));
            }
            if (PresentationRoot) CleanupOperations.Capture(ref Failures, () => PresentationRoot.SetActive(PresentationActive));
        }
        else
        {
            CleanupOperations.Capture(ref Failures, () => NativeRoot.transform.SetParent(null, false));
            CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.Destroy(NativeRoot));
        }
        var OwnedAmbience = Ambience;
        Ambience = null;
        if (OwnedAmbience) CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.Destroy(OwnedAmbience));
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Effect restoration failed.", Failures);

        static void RestoreParticle(ParticleSystem Particle, ParticlePlayback Playback)
        {
            switch (Playback)
            {
                case ParticlePlayback.Emitting:
                    Particle.Play(false);
                    break;
                case ParticlePlayback.Playing:
                    Particle.Play(false);
                    Particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    break;
                case ParticlePlayback.Paused:
                    Particle.Play(false);
                    Particle.Pause(false);
                    break;
            }
        }
    }

    enum ParticlePlayback : byte
    {
        Stopped,
        Playing,
        Emitting,
        Paused
    }
}

internal sealed class EffectZoneState
{
    readonly EffectState[] Effects;
    readonly float CoverDelaySeconds;
    PllPresentationCoverage EnvironmentTransition;
    internal bool Active { get; private set; }

    internal EffectZoneState(
        ResolvedEffectZone Resolved, EffectState[] AllEffects,
        int OccupancyKey)
    {
        Root = Resolved.Root;
        Volume = Resolved.Volume;
        this.OccupancyKey = OccupancyKey;
        EnvironmentalFogless = Resolved.EnvironmentFog
            == PllEffectZoneEnvironmentFog.EnvironmentalFogless;
        CoverDelaySeconds = Resolved.CoverDelaySeconds;
        Effects = new EffectState[Resolved.EffectIndices.Length];
        for (var i = 0; i < Effects.Length; i++) Effects[i] = AllEffects[Resolved.EffectIndices[i]];
    }

    internal GameObject Root { get; }
    internal PllZoneVolume Volume { get; }
    internal int OccupancyKey { get; }
    internal bool HasEffects => Effects.Length != 0;
    internal bool EnvironmentalFogless { get; }
    internal float EnvironmentalFogCoverage => EnvironmentalFogless
        ? EnvironmentTransition.Coverage : 0f;

    internal void TickEnvironment(double PresentationClock) =>
        EnvironmentTransition.Tick(PresentationClock);

    internal void StopEnvironment() => EnvironmentTransition.Complete(false);

    internal bool SetActive(bool Value, float Now, double PresentationClock)
    {
        if (Active == Value) return false;
        if (Value)
        {
            try
            {
                for (var i = 0; i < Effects.Length; i++) Effects[i].Play(Now, PresentationClock);
                Active = true;
                if (EnvironmentalFogless)
                    EnvironmentTransition.SetTarget(true, PresentationClock, CoverDelaySeconds);
            }
            catch (Exception ActivationFailure)
            {
                try { StopEffects(); }
                catch (Exception CleanupFailure)
                {
                    throw new AggregateException(
                        "Effect-zone activation and rollback both failed.", ActivationFailure, CleanupFailure);
                }
                throw;
            }
            return true;
        }
        foreach (var Effect in Effects) Effect.Deactivate(PresentationClock);
        Active = false;
        if (EnvironmentalFogless)
            EnvironmentTransition.SetTarget(false, PresentationClock, CoverDelaySeconds);
        return true;
    }

    void StopEffects()
    {
        List<Exception>? Failures = null;
        for (var i = Effects.Length - 1; i >= 0; i--)
            CleanupOperations.Capture(ref Failures, Effects[i].Stop);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is { Count: > 1 }) throw new AggregateException("Effect-zone cleanup failed.", Failures);
    }
}
