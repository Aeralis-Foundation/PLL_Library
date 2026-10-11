using UnityEngine;

namespace PLL.Library;

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllClimbingFixtureShape
{
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllRestPointAuthoring Source)
    {
        if (!Source || !Source.enabled)
            return "Rest Point needs an enabled source and a stable local ID.";
        if (Source.Profile is not (PllRestPointProfile.Permanent or PllRestPointProfile.NativeFragileMountedTool)
            || Source.MountedModel is not (PllMountedToolModel.Custom or PllMountedToolModel.Native))
            return "Rest Point has an unsupported mounted-tool profile.";
        if (Source.Placement is not (PllRestPointPlacement.FixedPose or PllRestPointPlacement.GeneratedTerrain)
            || !PllAuthoringValueContract.CanEncode(Source.InsertionOffset)
            || Source.Placement == PllRestPointPlacement.GeneratedTerrain && Source.Profile != PllRestPointProfile.NativeFragileMountedTool)
            return "Terrain insertion needs a fragile mounted tool and a finite insertion offset.";
        if (Source.Profile == PllRestPointProfile.NativeFragileMountedTool)
        {
            if (!Hook(Source.HangHook) || !Hook(Source.WarningHook) || !Hook(Source.BreakHook))
                return "Mounted tool observations need valid hook names.";
            if (!Presentation(Source, Source.CrackPresentation, Source.CrackRoot)
                || !Presentation(Source, Source.BreakPresentation, Source.BreakVfxRoot))
                return "Mounted tool custom effects must be separate inactive children of the hold anchor.";
        }
        return Failure(Source.transform, Source.Id, Source.ModelRoot, Source.HoldAnchor,
            Source.InteractionCenter, Source.InteractionSize,
            Source.Profile == PllRestPointProfile.NativeFragileMountedTool && Source.MountedModel == PllMountedToolModel.Native);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(Transform Owner, string Id, GameObject Model, Transform HoldAnchor,
        Vector3 InteractionCenter, Vector3 InteractionSize) =>
        Failure(Owner, Id, Model, HoldAnchor, InteractionCenter, InteractionSize, false);

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(Transform Owner, string Id, GameObject Model, Transform HoldAnchor,
        Vector3 InteractionCenter, Vector3 InteractionSize, bool NativeModel)
    {
        if (!Owner || !PllAuthoringValueContract.IsLocalId(Id))
            return "Rest Point needs a stable local ID.";
        if (!PllAuthoringValueContract.CanEncode(InteractionCenter)
            || !PllAuthoringValueContract.CanEncode(InteractionSize)
            || InteractionSize.x <= 0f || InteractionSize.y <= 0f || InteractionSize.z <= 0f)
            return "Rest Point interaction bounds must be finite and positive.";
        if (!(NativeModel ? Model && Model != Owner.gameObject && TransformOwnership.Contains(Owner, Model.transform)
                && TransformOwnership.DiscoverableWhenActivated(Owner, Model.transform) : OwnedPresentation(Owner, Model))
            || !HoldAnchor || HoldAnchor == Owner
            || !TransformOwnership.Contains(Owner, HoldAnchor)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, HoldAnchor))
            return "Rest Point needs an active model and an owned hold anchor.";
        return null;
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllHangingRopeAuthoring Source)
    {
        if (!Source || !Source.enabled)
            return "Hanging Rope needs an enabled source and a stable local ID.";
        if (Source.AnchorBehavior is not (PllRopeAnchorBehavior.Permanent or PllRopeAnchorBehavior.NativeBreakableAnchor)
            || Source.RopeAppearance is not (PllRopeAppearance.Native or PllRopeAppearance.CompatibleSkin or PllRopeAppearance.AutomaticSkin))
            return "Hanging Rope has an unsupported anchor or appearance profile.";
        if (Source.AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor
            && (!PllAuthoringValueContract.CanEncode(Source.BreakTimeMinimum) || Source.BreakTimeMinimum < 0f
                || !PllAuthoringValueContract.CanEncode(Source.BreakTimeMaximum) || Source.BreakTimeMaximum < Source.BreakTimeMinimum
                || !PllAuthoringValueContract.CanEncode(Source.BreakAnimationSeconds) || Source.BreakAnimationSeconds < 0f
                || !PllAuthoringValueContract.CanEncode(Source.DropSegments) || Source.DropSegments < 0f
                || Source.Segments + Source.DropSegments + 1f > PllHangingRopeAuthoring.MaximumSegments))
            return "Breakable Rope timing must be nonnegative and its drop must fit the native segment limit.";
        if (!Hook(Source.WarningHook) || !Hook(Source.BreakHook))
            return "Rope observations need valid hook names.";
        var RopeMesh = Source.RopeAppearance == PllRopeAppearance.AutomaticSkin ? Source.RopeRestMesh : Source.RopeMesh;
        if (Source.RopeAppearance != PllRopeAppearance.Native
            && (!RopeMesh || !RopeMesh.isReadable || RopeMesh.subMeshCount != 1
                || !Source.RopeMaterial || !Source.RopeGhostMaterial
                || !PllAuthoringValueContract.CanEncode(Source.RopeRestStart)
                || !PllAuthoringValueContract.CanEncode(Source.RopeRestEnd)
                || (Source.RopeRestEnd - Source.RopeRestStart).sqrMagnitude == 0f))
            return "Custom Rope needs a readable single-slot mesh, rope and ghost materials, and distinct finite rest endpoints.";
        return Failure(Source.transform, Source.Id, Source.ModelRoot, Source.GhostRoot,
            Source.AnchorPoint, Source.Segments, Source.SpoolOutSeconds);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(Transform Owner, string Id, GameObject Model, GameObject Ghost,
        Transform AnchorPoint, float Segments, float SpoolOutSeconds)
    {
        if (!Owner || !PllAuthoringValueContract.IsLocalId(Id)) return "Hanging Rope needs a stable local ID.";
        if (!PllAuthoringValueContract.CanEncode(Segments)
            || Segments < 1f || Segments > PllHangingRopeAuthoring.MaximumSegments)
            return "Hanging Rope segment count exceeds the supported native range.";
        if (!PllAuthoringValueContract.CanEncode(SpoolOutSeconds) || SpoolOutSeconds < 0f)
            return "Hanging Rope spool time must be finite and nonnegative.";
        if (!OwnedPresentation(Owner, Model) || !Ghost || Ghost == Model
            || Ghost == Owner.gameObject || Ghost.activeSelf
            || !TransformOwnership.Contains(Owner, Ghost.transform)
            || TransformOwnership.Overlaps(Model.transform, Ghost.transform)
            || !AnchorPoint || AnchorPoint == Owner || !TransformOwnership.Contains(Owner, AnchorPoint)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, AnchorPoint)
            || TransformOwnership.Contains(Ghost.transform, AnchorPoint))
            return "Hanging Rope needs an active model, separate inactive ghost and owned anchor point.";
        return null;
    }

    static bool OwnedPresentation(Transform Owner, GameObject Model)
    {
        if (!Model || Model == Owner.gameObject || !TransformOwnership.Contains(Owner, Model.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Model.transform)) return false;
        foreach (var Renderer in Model.GetComponentsInChildren<Renderer>(true))
        {
            if (!Renderer.enabled || !TransformOwnership.DiscoverableWhenActivated(Model.transform, Renderer.transform)) continue;
            if (Renderer is MeshRenderer && Renderer.TryGetComponent<MeshFilter>(out var Filter) && Filter.sharedMesh
                || Renderer is SkinnedMeshRenderer Skin && Skin.sharedMesh) return true;
        }
        return false;
    }

    static bool Hook(string Value) => Value is not null && (Value.Length == 0 || PllAuthoringValueContract.IsHook(Value));

    static bool Presentation(PllRestPointAuthoring Source, PllFixturePresentation Profile, GameObject Root) => Profile switch
    {
        PllFixturePresentation.None or PllFixturePresentation.Native => !PllAuthoringValueContract.DestroyedReference(Root),
        PllFixturePresentation.Custom => Root && Source.HoldAnchor && Source.ModelRoot
            && !Root.activeSelf && TransformOwnership.Contains(Source.HoldAnchor, Root.transform)
            && !TransformOwnership.Overlaps(Source.ModelRoot.transform, Root.transform),
        _ => false
    };
}
