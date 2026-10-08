using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllActorZoneSourceContract
{
    public static string? RegisterId(HashSet<string> Ids, string Id) => Ids.Add(Id)
        ? null : $"Actor Zone ID '{Id}' is duplicated.";

    public static string? NewSourceParentFailure(
        Transform Root, Transform Floor, Transform Campfire, Transform Parent, string Id)
    {
        if (!PllAuthoringValueContract.IsLocalId(Id))
            return "Actor Zone ID must be a stable local ID without spaces or path separators.";
        return ParentFailure(Root, Floor, Campfire, Parent)
            ?? PosePathFailure(Root, Parent) ?? DuplicateIdFailure(Root, Id, null);
    }

    public static string? DuplicateIdFailure(Transform Root, string Id, Component? Except)
    {
        foreach (var Zone in Root.GetComponentsInChildren<PllEntityZoneAuthoring>(true))
            if (Zone != Except && string.Equals(Zone.Id, Id, StringComparison.Ordinal))
                return $"Actor Zone ID '{Id}' is duplicated.";
        foreach (var Zone in Root.GetComponentsInChildren<PllHazardZoneAuthoring>(true))
            if (Zone != Except && string.Equals(Zone.Id, Id, StringComparison.Ordinal))
                return $"Actor Zone ID '{Id}' is duplicated.";
        return null;
    }

    public static string? SourceFailure(
        Transform Root, Transform Floor, Transform Campfire, LevelGenStep Zone, Transform? Output)
    {
        if ((RootFailure(Root, Floor, Campfire, Zone) ?? PosePathFailure(Root, Zone.transform)) is { } Failure) return Failure;
        if (Output) return "Actor Zone contains compiled output in a source prefab. Remove its generated output branch.";
        return Zone.transform.childCount == 0 ? null : "Actor Zone must be an empty source object.";
    }

    public static string? CompiledFailure(
        Transform Root, Transform Floor, Transform Campfire, LevelGenStep Zone, Transform? Output)
    {
        if (RootFailure(Root, Floor, Campfire, Zone) is { } Failure) return Failure;
        if (!Output || Output.parent != Zone.transform || Zone.transform.childCount != 1
            || Output.childCount != 0 || !Output.gameObject.activeSelf
            || !Output.localPosition.Equals(Vector3.zero) || !Output.localRotation.Equals(Quaternion.identity)
            || !Output.localScale.Equals(Vector3.one) || Output.GetComponents<Component>().Length != 1)
            return "Actor Zone must own exactly one active empty compiled output branch with an identity local transform.";
        return null;
    }

    static string? RootFailure(Transform Root, Transform Floor, Transform Campfire, LevelGenStep Zone)
    {
        if (!Zone.enabled || !Zone.gameObject.activeSelf || Zone.GetComponents<LevelGenStep>().Length != 1)
            return "Actor Zone must be active and own its only enabled generation step.";
        if (Zone.transform == Floor || Zone.transform == Campfire)
            return "Actor Zone must be a separate stable object under FloorRoot or CampfireRoot.";
        if (Zone.GetComponent<PLLFinalGeneration>())
            return "Actor Zone cannot belong to Final Generation.";
        if (Zone.GetComponent<PllHookedPropGenerationAuthoring>())
            return "Actor Zone cannot belong to Hooked Prop Generation.";
        return ParentFailure(Root, Floor, Campfire, Zone.transform.parent);
    }

    static string? ParentFailure(Transform Root, Transform Floor, Transform Campfire, Transform Parent)
    {
        if (!Parent || !TransformOwnership.Contains(Root, Parent)
            || TransformOwnership.Contains(Floor, Parent) == TransformOwnership.Contains(Campfire, Parent))
            return "Actor Zone must belong to exactly one FloorRoot or CampfireRoot.";
        for (var Current = Parent; Current != Root; Current = Current.parent)
        {
            if (Current.GetComponent<PropSpawner>())
                return "Actor Zone cannot belong to a PropSpawner's generated-child storage.";
            if (Current.GetComponent<PLLFinalGeneration>())
                return "Actor Zone cannot belong to Final Generation.";
            if (Current.GetComponent<PllHookedPropGenerationAuthoring>())
                return "Actor Zone cannot belong to Hooked Prop Generation.";
            if (Current.GetComponent<PllEntityZoneAuthoring>() || Current.GetComponent<PllHazardZoneAuthoring>())
                return "Actor Zone cannot belong to another actor zone.";
        }
        return null;
    }

    static string? PosePathFailure(Transform Root, Transform Value)
    {
        for (var Current = Value; Current != Root; Current = Current.parent)
        {
            var Position = Current.localPosition; var Rotation = Current.localRotation; var Scale = Current.localScale;
            if (!PllAuthoringValueContract.CanEncode(Position.x) || !PllAuthoringValueContract.CanEncode(Position.y)
                || !PllAuthoringValueContract.CanEncode(Position.z) || !PllAuthoringValueContract.CanEncode(Rotation.x)
                || !PllAuthoringValueContract.CanEncode(Rotation.y) || !PllAuthoringValueContract.CanEncode(Rotation.z)
                || !PllAuthoringValueContract.CanEncode(Rotation.w) || Rotation.Equals(default(Quaternion))
                || !PllAuthoringValueContract.CanEncode(Scale.x) || !PllAuthoringValueContract.CanEncode(Scale.y)
                || !PllAuthoringValueContract.CanEncode(Scale.z))
                return "Actor Zone transform paths must have finite local poses and scales within the synchronized range.";
        }
        return null;
    }
}
