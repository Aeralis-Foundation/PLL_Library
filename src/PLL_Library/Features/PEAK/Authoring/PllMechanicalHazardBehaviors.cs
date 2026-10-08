using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public sealed class PllArrowShooterBehavior : PllHazardBehavior
{
    public Transform Muzzle = null!;
    [Min(0f)] public float Range = 80f;
    [Min(0f)] public float ReloadSeconds = 8f;
    [Min(0f)] public float Knockback = 30f;
    [Min(0)] public int InitialAmmo = 3;
    public GameObject? ArrowPrefab;
    public ParticleSystem? TrailParticles;
    public ParticleSystem? FiredParticles;
    public ParticleSystem? EmptyParticles;
    public Material? WarningLineMaterial;
    public Material? ColorblindLineMaterial;
    public AudioClip[] ShotClips = Array.Empty<AudioClip>();
}

[Serializable]
public sealed class PllMovingSawBehavior : PllHazardBehavior
{
    public Transform Rail = null!;
    public Transform RailStart = null!;
    public Transform RailEnd = null!;
    public Transform Blade = null!;
    public Transform BladeVisual = null!;
    public Collider Hitbox = null!;
    [Min(0f)] public float TimeoutSeconds = 0.25f;
    public float OscillateSpeed = 99f;
    [Min(0f)] public float TraceSpeed = 10f;
    [Min(0f)] public float ConstantInjury = 0.025f;
    [Min(0f)] public float InitialInjury = 0.15f;
    [Min(0f)] public float InitialInjuryCooldownSeconds = 2f;
    [Range(0f, 1f)] public float Buffer = 0.3f;
    public AudioClip? HitClip;
}

[Serializable]
public sealed class PllSpikeTrapBehavior : PllHazardBehavior
{
    public Transform Spikes = null!;
    public Transform ActivePose = null!;
    public Transform ReadyPose = null!;
    public Transform RestPose = null!;
    public Collider SupportCollider = null!;
    public Collider DetectionTrigger = null!;
    [Min(0f)] public float SpikeWaitSeconds = 1f;
    [Min(0f)] public float AttackWaitSeconds = 1f;
    [Min(0f)] public float ReadyTweenSeconds = 0.3f;
    [Min(0f)] public float AttackTweenSeconds = 0.2f;
    [Min(0f)] public float ReturnTweenSeconds = 1f;
    [Min(0f)] public float HitboxActiveSeconds = 0.1f;
    public AudioClip[] AttackClips = Array.Empty<AudioClip>();
    public AudioClip[] RestClips = Array.Empty<AudioClip>();
}

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllMechanicalHazardValueContract
{
    public static string? Failure(PllHazardAuthoring Source) =>
        Failure(Source.transform, Source.ModelRoot, Source.Behavior);

    public static string? Failure(Transform Owner, GameObject Model, PllHazardBehavior Behavior)
    {
        if (!Model || !TransformOwnership.Contains(Owner, Model.transform))
            return "Mechanical hazard model must belong to its definition.";
        return Behavior switch
        {
            PllArrowShooterBehavior Arrow => ArrowFailure(Owner, Arrow),
            PllMovingSawBehavior Saw => SawFailure(Owner, Saw),
            PllSpikeTrapBehavior Spike => SpikeFailure(Owner, Spike),
            _ => "Mechanical hazard behavior is unsupported."
        };
    }

    static string? ArrowFailure(Transform Owner, PllArrowShooterBehavior Arrow)
    {
        if (!Owned(Owner, Arrow.Muzzle)) return "Arrow muzzle must belong to its definition.";
        foreach (var Body in Owner.GetComponentsInChildren<Rigidbody>(true))
            if (!Body.isKinematic) return "Arrow Shooter cannot contain a dynamic rigidbody.";
        if (!Nonnegative(Arrow.Range) || Arrow.Range == 0f || !Nonnegative(Arrow.ReloadSeconds)
            || !Nonnegative(Arrow.Knockback) || Arrow.InitialAmmo < 0)
            return "Arrow range must be positive; reload, knockback and initial ammo must be nonnegative.";
        if (PllAuthoringValueContract.DestroyedReference(Arrow.ArrowPrefab)
            || PllAuthoringValueContract.DestroyedReference(Arrow.TrailParticles)
            || PllAuthoringValueContract.DestroyedReference(Arrow.FiredParticles)
            || PllAuthoringValueContract.DestroyedReference(Arrow.EmptyParticles)
            || PllAuthoringValueContract.DestroyedReference(Arrow.WarningLineMaterial)
            || PllAuthoringValueContract.DestroyedReference(Arrow.ColorblindLineMaterial))
            return "Arrow presentation contains a destroyed asset.";
        if (ClipsFailure(Arrow.ShotClips) is { } Failure) return Failure;
        if (Arrow.ArrowPrefab && PresentationFailure(Arrow.ArrowPrefab, false) is { } ArrowFailure)
            return ArrowFailure;
        if (ParticleFailure(Arrow.TrailParticles) is { } TrailFailure) return TrailFailure;
        if (ParticleFailure(Arrow.FiredParticles) is { } FiredFailure) return FiredFailure;
        if (ParticleFailure(Arrow.EmptyParticles) is { } EmptyFailure) return EmptyFailure;
        if (Arrow.TrailParticles && (Arrow.TrailParticles.main.loop || !Arrow.TrailParticles.main.playOnAwake))
            return "Arrow trail particles must be nonlooping and play on awake.";
        return null;
    }

    static string? SawFailure(Transform Owner, PllMovingSawBehavior Saw)
    {
        if (!Owned(Owner, Saw.Rail) || !Owned(Saw.Rail, Saw.RailStart)
            || !Owned(Saw.Rail, Saw.RailEnd) || !Owned(Saw.Rail, Saw.Blade)
            || !Owned(Saw.Blade, Saw.BladeVisual) || !Saw.Hitbox
            || Saw.Hitbox.transform != Saw.Blade || !Saw.Hitbox.isTrigger)
            return "Saw rail must own its endpoints, blade, visual and trigger hitbox.";
        if (Saw.Blade.parent != Saw.Rail || Saw.RailStart.parent != Saw.Rail
            || Saw.RailEnd.parent != Saw.Rail || Saw.RailStart == Saw.RailEnd
            || Saw.Blade == Saw.BladeVisual || Owned(Saw.Blade, Saw.RailStart)
            || Owned(Saw.Blade, Saw.RailEnd))
            return "Saw blade must be a direct rail child, separate from both endpoints and its visual.";
        if (!Saw.Hitbox.enabled || !TransformOwnership.DiscoverableWhenActivated(Owner, Saw.Blade)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Saw.BladeVisual))
            return "Saw blade, visual and enabled hitbox must be active when the definition activates.";
        var Start = Saw.RailStart.localPosition; var End = Saw.RailEnd.localPosition;
        if (!Start.x.Equals(End.x) || !Start.z.Equals(End.z) || Start.y.Equals(End.y)
            || !Start.x.Equals(Saw.Blade.localPosition.x) || !Start.z.Equals(Saw.Blade.localPosition.z)
            || !Finite(Start) || !Finite(End))
            return "Saw endpoints must define a finite nonzero rail-local Y path.";
        if (!Nonnegative(Saw.TimeoutSeconds) || !PllAuthoringValueContract.CanEncode(Saw.OscillateSpeed)
            || !Nonnegative(Saw.TraceSpeed) || Saw.TraceSpeed == 0f
            || !Nonnegative(Saw.ConstantInjury) || !Nonnegative(Saw.InitialInjury)
            || !Nonnegative(Saw.InitialInjuryCooldownSeconds) || !Nonnegative(Saw.Buffer) || Saw.Buffer > 1f)
            return "Saw settings must be finite, with positive trace speed and buffer between zero and one.";
        if (PllAuthoringValueContract.DestroyedReference(Saw.HitClip))
            return "Saw hit sound contains a destroyed clip.";
        return MovingBodyFailure(Saw.Blade);
    }

    static string? SpikeFailure(Transform Owner, PllSpikeTrapBehavior Spike)
    {
        if (!Owned(Owner, Spike.Spikes) || !Owned(Owner, Spike.ActivePose)
            || !Owned(Owner, Spike.ReadyPose) || !Owned(Owner, Spike.RestPose)
            || !Spike.SupportCollider || !Owned(Owner, Spike.SupportCollider.transform)
            || Spike.SupportCollider.isTrigger || !Spike.DetectionTrigger
            || !Owned(Owner, Spike.DetectionTrigger.transform) || !Spike.DetectionTrigger.isTrigger)
            return "Spike trap must own its spikes, phase poses, solid support and detection trigger.";
        if (Spike.DetectionTrigger.transform != Owner)
            return "Spike detection trigger must be attached to its definition root.";
        if (Spike.Spikes == Owner || Spike.ActivePose == Spike.ReadyPose
            || Spike.ActivePose == Spike.RestPose || Spike.ReadyPose == Spike.RestPose
            || Owned(Spike.Spikes, Spike.ActivePose) || Owned(Spike.Spikes, Spike.ReadyPose)
            || Owned(Spike.Spikes, Spike.RestPose) || Owned(Spike.Spikes, Spike.SupportCollider.transform)
            || Owned(Spike.Spikes, Spike.DetectionTrigger.transform))
            return "Spike phase poses and support must remain outside the moving spikes.";
        if (!Spike.SupportCollider.enabled || !Spike.DetectionTrigger.enabled
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Spike.SupportCollider.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Spike.DetectionTrigger.transform))
            return "Spike support and enabled detection trigger must be active when the definition activates.";
        if (Vector3.Dot(Owner.up, Vector3.up) < 0.9999f)
            return "Native spike traps require an upright source.";
        if (!Nonnegative(Spike.SpikeWaitSeconds) || !Nonnegative(Spike.AttackWaitSeconds)
            || !Nonnegative(Spike.ReadyTweenSeconds) || !Nonnegative(Spike.AttackTweenSeconds)
            || !Nonnegative(Spike.ReturnTweenSeconds) || !Nonnegative(Spike.HitboxActiveSeconds)
            || Spike.HitboxActiveSeconds > Spike.AttackWaitSeconds)
            return "Spike timings must be finite and nonnegative; the hitbox window cannot exceed attack wait.";
        return MovingBodyFailure(Spike.Spikes)
            ?? ClipsFailure(Spike.AttackClips) ?? ClipsFailure(Spike.RestClips);
    }

    static string? MovingBodyFailure(Transform Body)
    {
        foreach (var Rigidbody in Body.GetComponentsInChildren<Rigidbody>(true))
            if (!Rigidbody.isKinematic) return "Mechanical moving bodies cannot contain dynamic rigidbodies.";
        foreach (var Collider in Body.GetComponentsInChildren<MeshCollider>(true))
            if (!Collider.convex) return "Mechanical moving bodies cannot contain concave mesh colliders.";
        return null;
    }

    public static string? PresentationFailure(GameObject Prefab, bool Particles)
    {
        foreach (var Component in Prefab.GetComponentsInChildren<Component>(true))
            if (Component is not Transform and not MeshFilter and not MeshRenderer
                && !(Particles && Component is ParticleSystem or ParticleSystemRenderer))
                return "Arrow presentation accepts only meshes, renderers and particle systems.";
        return null;
    }

    static string? ParticleFailure(ParticleSystem? Value) => Value ? PresentationFailure(Value.gameObject, true) : null;

    static string? ClipsFailure(AudioClip[] Clips)
    {
        if (Clips is null) return "Mechanical sound clips must be an array.";
        foreach (var Clip in Clips) if (!Clip) return "Mechanical sound clips cannot contain missing assets.";
        return null;
    }

    static bool Owned(Transform Owner, Transform Value) => Value && TransformOwnership.Contains(Owner, Value);
    static bool Nonnegative(float Value) => Value >= 0f && PllAuthoringValueContract.CanEncode(Value);
    static bool Finite(Vector3 Value) => PllAuthoringValueContract.CanEncode(Value.x)
        && PllAuthoringValueContract.CanEncode(Value.y) && PllAuthoringValueContract.CanEncode(Value.z);
}
