using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllPropInteractionValueContract
{
    public static string? Failure(Transform Owner, PllBreakablePropAuthoring? Break,
        PllSwingingPropAuthoring? Motion, PllVineAuthoring? Vine)
    {
        if (!Owner || !Break && !Motion && !Vine) return "Prop interaction definition is missing.";
        if (Break && (Break.transform != Owner || !Break.enabled || !PllAuthoringValueContract.IsLocalId(Break.Id))
            || Motion && (Motion.transform != Owner || !Motion.enabled || !PllAuthoringValueContract.IsLocalId(Motion.Id))
            || Vine && (Vine.transform != Owner || !Vine.enabled || !PllAuthoringValueContract.IsLocalId(Vine.Id)))
            return "Prop interaction components need enabled co-located definitions with stable IDs.";
        if (Owner.GetComponentInChildren<Animator>(true) || Owner.GetComponentInChildren<Animation>(true)
            || Owner.GetComponentInChildren<SkinnedMeshRenderer>(true))
            return "Prop interactions support rigid geometry without another animation owner.";
        if (Break && Break.EmitHook.Length != 0 && !PllAuthoringValueContract.IsHook(Break.EmitHook)
            || Motion && (Motion.StartedHook.Length != 0 && !PllAuthoringValueContract.IsHook(Motion.StartedHook)
                || Motion.PausedHook.Length != 0 && !PllAuthoringValueContract.IsHook(Motion.PausedHook)))
            return "Prop observations must use valid scoped hook names.";
        if ((Vine || Motion && Motion.Profile == PllPropMotion.PhysicalPivot) && Owner.lossyScale != Vector3.one)
            return "Native spans and Physical Pivot require a unit-scaled source frame.";
        if ((Vine || Motion && Motion.Profile == PllPropMotion.PhysicalPivot) && Owner.parent
            && (Owner.parent.GetComponentInParent<Rigidbody>() || Owner.parent.GetComponentInParent<Animator>()
                || Owner.parent.GetComponentInParent<Animation>()))
            return "Native spans and Physical Pivot cannot inherit a moving physics or animation parent.";
        if (Vine)
        {
            if (Vine.Profile is not PllVineProfile.Vine and not PllVineProfile.HangingBridge
                || Vine.Appearance is not PllVineAppearance.Native and not PllVineAppearance.CustomSpan)
                return "Vine profile or appearance is unsupported.";
            if (Vine.Endpoints is not PllVineEndpoints.AuthoredAnchors and not PllVineEndpoints.NativePlatformConnection)
                return "Vine endpoint policy is unsupported.";
            if (Vine.Endpoints == PllVineEndpoints.AuthoredAnchors
                && (!Owned(Owner, Vine.StartAnchor) || !Owned(Owner, Vine.EndAnchor)
                    || Vine.StartAnchor == Vine.EndAnchor || Vine.StartAnchor == Owner || Vine.EndAnchor == Owner))
                return "Vine endpoints must be distinct owned child anchors.";
            if (Vine.Endpoints == PllVineEndpoints.AuthoredAnchors
                && (!Finite(Vine.StartAnchor.localPosition) || !Finite(Vine.EndAnchor.localPosition)
                    || Vine.StartAnchor.position == Vine.EndAnchor.position)
                || !Positive(Vine.MinimumDistance) || !Positive(Vine.MaximumDistance)
                || Vine.MinimumDistance > Vine.MaximumDistance || !Nonnegative(Vine.MaximumHeightDifference)
                || !Nonnegative(Vine.HangAmount) || !Finite(Vine.ColliderOffset)
                || !Positive(Vine.BoxShape.x) || !Positive(Vine.BoxShape.y))
                return "Vine endpoints and span settings must be finite, with valid positive distance and box dimensions.";
            if (Owner.GetComponentInChildren<Collider>(true) || Owner.GetComponentInChildren<Rigidbody>(true))
                return "Vine definitions cannot contain authored collision or rigidbodies.";
            if (Vine.Appearance == PllVineAppearance.CustomSpan
                && (!Vine.SpanMesh || !Vine.SpanMesh.isReadable || !Finite(Vine.ModelStart)
                    || !Finite(Vine.ModelEnd) || !Finite(Vine.ModelUp) || Vine.ModelStart == Vine.ModelEnd
                    || Vector3.Cross(Vine.ModelEnd - Vine.ModelStart, Vine.ModelUp).sqrMagnitude < 0.000001f))
                return "Custom vine needs a readable mesh and a nonzero rest span with an independent up direction.";
#if !PLL_CREATOR_SDK
            if (Vine.Appearance == PllVineAppearance.CustomSpan && Vine.BundleSpanVersion != PllVineAuthoring.SpanNormalizationVersion)
                return "Custom vine span is not compiled for this recipe. Rebuild the bundle with Creator.";
#endif
            if (Vine.Profile == PllVineProfile.Vine && (Break || Motion))
                return "Breakable and native response profiles require a Hanging Bridge, not a climbing Vine.";
            if (Motion && Motion.Profile != PllPropMotion.NativeBridgeResponse)
                return "Native spans cannot use pivot motion.";
            if (Motion && (Motion.StartedHook.Length != 0 || Motion.PausedHook.Length != 0))
                return "Native Bridge Response does not expose pivot start or pause hooks.";
            if (Motion && Motion.BridgeResponse is not PllBridgeResponseProfile.Native and not PllBridgeResponseProfile.Custom)
                return "Native Bridge Response settings profile is unsupported.";
            if (Motion && Motion.BridgeResponse == PllBridgeResponseProfile.Custom
                && (!Positive(Motion.WarningSeconds) || !Nonnegative(Motion.StartShakeDistance)
                    || !Nonnegative(Motion.StartShakeAmount) || !Nonnegative(Motion.ClimbingShakeAmount)
                    || !Positive(Motion.ClimbingShakeInterval)
                    || PllAuthoringValueContract.DestroyedReference(Motion.WarningClip)))
                return "Custom bridge response needs finite warning, distance and feedback settings.";
        }
        if (Motion && !Vine)
        {
            if (Motion.Profile is not PllPropMotion.VisualPivot and not PllPropMotion.PhysicalPivot
                || !Owned(Owner, Motion.MotionRoot) || Motion.MotionRoot == Owner)
                return "Pivot motion needs an owned moving child and a supported pivot profile.";
            if (!Finite(Motion.Axis) || Motion.Axis.sqrMagnitude < 0.000001f
                || !Finite(Motion.CenterDegrees) || !Nonnegative(Motion.AmplitudeDegrees)
                || !Positive(Motion.PeriodSeconds) || !Finite(Motion.InitialPhase)
                || Motion.InitialPhase < 0f || Motion.InitialPhase > 1f)
                return "Pivot settings need finite angles, a nonzero axis, a positive period and phase between zero and one.";
            foreach (var Body in Owner.GetComponentsInChildren<Rigidbody>(true))
                if (Body.transform != Motion.MotionRoot || !Body.isKinematic || Body.useGravity)
                    return "Pivot motion admits one nongravitating kinematic body on the motion root.";
            var Colliders = Owner.GetComponentsInChildren<Collider>(true);
            if (Motion.Profile == PllPropMotion.PhysicalPivot && Colliders.Length == 0)
                return "Physical Pivot needs matching moving support collision.";
            foreach (var Collider in Colliders)
            {
                if (Motion.Profile == PllPropMotion.VisualPivot && Owned(Motion.MotionRoot, Collider.transform))
                    return "Visual Pivot cannot move gameplay collision.";
                if (Motion.Profile == PllPropMotion.PhysicalPivot
                    && (!Owned(Motion.MotionRoot, Collider.transform) || Collider.isTrigger
                        || !Collider.enabled || !TransformOwnership.DiscoverableWhenActivated(Owner, Collider.transform)
                        || Collider is not BoxCollider and not SphereCollider and not CapsuleCollider and not MeshCollider
                        || Collider is MeshCollider Mesh && (!Mesh.convex || !Mesh.sharedMesh)))
                    return "Physical Pivot needs owned solid primitive or convex compound collision.";
            }
        }
        if (!Break) return null;
        if (Break.Capacity < 1
            || Break.BridgeCapacity is not PllBridgeCapacity.NativeRandom and not PllBridgeCapacity.Fixed
            || Vine && (Break.NativeMaximumPeople < 2 || Break.Capacity >= Break.NativeMaximumPeople))
            return "Prop capacity must be positive and below the native exclusive capacity limit.";
        if (Vine) return OptionalFailure(Owner, Break);
        if (!Break.IntactModel || Break.IntactModel.transform == Owner
            || !Owned(Owner, Break.IntactModel.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Break.IntactModel.transform)
            || Break.SupportColliders is null
            || Break.SupportColliders.Length == 0)
            return "Breakable Prop needs an owned intact model and explicit support colliders.";
        if (Break.Trigger is not PllPropBreakTrigger.Impact and not PllPropBreakTrigger.SupportLoad and not PllPropBreakTrigger.Manual
            || Break.Trigger == PllPropBreakTrigger.Impact && !Positive(Break.ImpactSpeed)
            || Break.Trigger == PllPropBreakTrigger.SupportLoad && !Nonnegative(Break.OverloadSeconds))
            return "Break trigger needs a supported profile and valid speed or overload duration.";
        var Seen = new HashSet<Collider>();
        foreach (var Collider in Break.SupportColliders)
            if (!Collider || !Collider.enabled || Collider.isTrigger || !Owned(Owner, Collider.transform)
                || !Seen.Add(Collider) || !TransformOwnership.DiscoverableWhenActivated(Owner, Collider.transform)
                || Collider is not BoxCollider and not SphereCollider and not CapsuleCollider and not MeshCollider
                || Collider is MeshCollider Mesh && (!Mesh.sharedMesh
                    || Motion && Motion.Profile == PllPropMotion.PhysicalPivot && !Mesh.convex))
                return "Breakable support must contain unique enabled owned solid colliders, convex when moving.";
        foreach (var Collider in Owner.GetComponentsInChildren<Collider>(true))
            if (!Seen.Contains(Collider)) return "Breakable Prop cannot retain undeclared intact collision.";
        foreach (var Body in Owner.GetComponentsInChildren<Rigidbody>(true))
            if (Body.transform != (Motion && Motion.Profile == PllPropMotion.PhysicalPivot ? Motion.MotionRoot : Owner)
                || !Body.isKinematic || Body.useGravity)
                return "Breakable Prop admits one nongravitating kinematic collision body.";
        return OptionalFailure(Owner, Break);
    }

    static string? OptionalFailure(Transform Owner, PllBreakablePropAuthoring Break)
    {
        if (PllAuthoringValueContract.DestroyedReference(Break.BrokenModel)
            || PllAuthoringValueContract.DestroyedReference(Break.BreakParticles)
            || PllAuthoringValueContract.DestroyedReference(Break.BreakClip))
            return "Break presentation contains a destroyed asset.";
        if (Break.BrokenModel && (!Owned(Owner, Break.BrokenModel.transform)
            || Break.BrokenModel == Break.IntactModel || Break.BrokenModel.activeSelf
            || Break.IntactModel && TransformOwnership.Overlaps(Break.IntactModel.transform, Break.BrokenModel.transform)
            || Break.BrokenModel.GetComponentInChildren<Collider>(true)
            || Break.BrokenModel.GetComponentInChildren<Rigidbody>(true)))
            return "Broken Model must be a separate inactive owned collision-free presentation.";
        if (Break.BreakParticles && (!Owned(Owner, Break.BreakParticles.transform)
            || Break.IntactModel && TransformOwnership.Contains(Break.IntactModel.transform, Break.BreakParticles.transform)
            || Break.BreakParticles.main.loop || Break.BreakParticles.main.playOnAwake
            || Break.BreakParticles.GetComponentInChildren<Collider>(true)))
            return "Break particles must be owned, collision-free, nonlooping and stopped at creation.";
        return null;
    }

    static bool Owned(Transform Owner, Transform Value) => Value && TransformOwnership.Contains(Owner, Value);
    static bool Finite(float Value) => PllAuthoringValueContract.CanEncode(Value);
    static bool Finite(Vector3 Value) => Finite(Value.x) && Finite(Value.y) && Finite(Value.z);
    static bool Positive(float Value) => Finite(Value) && Value > 0f;
    static bool Nonnegative(float Value) => Finite(Value) && Value >= 0f;
}
