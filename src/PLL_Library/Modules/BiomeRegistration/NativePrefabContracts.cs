using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static partial class NativePrefabContracts
{
    internal static ref List<IValidationConstraint> ValidationConstraints(PropSpawner Spawner) =>
        ref Spawner.validationConstraints;

    internal static void ValidateCampfireRoot(
        Transform segmentRoot,
        Transform campfireRoot,
        string name,
        bool requireUnassignedView = true)
    {
        if (!segmentRoot || !campfireRoot)
            throw new ArgumentException($"The {name} must have valid feature roots.", "definition");

        var campfires = campfireRoot.GetComponentsInChildren<global::Campfire>(true);
        if (campfires.Length > 1)
            throw new ArgumentException($"The {name} contains more than one authored Campfire.", "definition");

        var factory = FindCampfireFactory(segmentRoot, campfireRoot, null, name);
        if (segmentRoot != campfireRoot)
            factory = FindCampfireFactory(campfireRoot, campfireRoot, factory, name);

        if (campfires.Length == 0)
        {
            if (!factory)
                throw new ArgumentException($"The {name} must contain one authored Campfire or one generated Campfire factory.", "definition");
            ValidateCampfireFactory(factory, campfireRoot, name, requireUnassignedView);
            return;
        }

        if (factory)
            throw new ArgumentException($"The {name} cannot contain both an authored Campfire and a Campfire factory.", "definition");

        var authoring = campfireRoot.GetComponentsInChildren<PllCampfireAuthoring>(true);
        if (authoring.Length != 1 || !authoring[0] || authoring[0].gameObject != campfires[0].gameObject
            || authoring[0].Root != campfires[0].gameObject)
            throw new ArgumentException($"The {name} authored Campfire must be the staged model-first campfire root.", "definition");
        ValidateCampfire(campfires[0], campfireRoot, name, requireUnassignedView);
    }

    internal static void ValidateCampfire(
        global::Campfire campfire,
        Transform owner,
        string name,
        bool requireUnassignedView)
    {
        if (!campfire || !campfire.enabled || !TransformOwnership.Contains(owner, campfire.transform)
            || !TransformOwnership.DiscoverableWhenActivated(owner, campfire.transform)
            || TransformOwnership.HasPropSpawnerInPath(owner, campfire.transform))
            throw new ArgumentException($"The {name} Campfire must be enabled, active when its root is activated, and outside every PropSpawner hierarchy.", "definition");
        var views = campfire.GetComponents<PhotonView>();
        if (views.Length != 1 || !views[0] || !views[0].enabled || requireUnassignedView && views[0].ViewID != 0)
            throw new ArgumentException($"The {name} Campfire must own one enabled PhotonView on the same GameObject, unassigned in source assets.", "definition");
        if (campfire.moraleBoostRadius <= 0f || !float.IsFinite(campfire.moraleBoostRadius)
            || !float.IsFinite(campfire.moraleBoostBaseline) || !float.IsFinite(campfire.moraleBoostPerAdditionalScout))
            throw new ArgumentException($"The {name} Campfire must use a positive finite morale radius and finite morale values.", "definition");
        var rendererAvailable = false;
        foreach (var renderer in campfire.GetComponentsInChildren<Renderer>(true))
            if (renderer && renderer.enabled && TransformOwnership.DiscoverableWhenActivated(owner, renderer.transform)
                && !TransformOwnership.HasPropSpawnerInPath(owner, renderer.transform))
            {
                rendererAvailable = true;
                break;
            }
        if (!campfire.fireParticles || !campfire.smokeParticlesOff || !campfire.smokeParticlesLit || !campfire.logRoot
            || !TransformOwnership.Contains(owner, campfire.fireParticles.transform) || !TransformOwnership.Contains(owner, campfire.smokeParticlesOff.transform)
            || !TransformOwnership.Contains(owner, campfire.smokeParticlesLit.transform) || !TransformOwnership.Contains(owner, campfire.logRoot)
            || TransformOwnership.HasPropSpawnerInPath(owner, campfire.fireParticles.transform)
            || TransformOwnership.HasPropSpawnerInPath(owner, campfire.smokeParticlesOff.transform)
            || TransformOwnership.HasPropSpawnerInPath(owner, campfire.smokeParticlesLit.transform)
            || TransformOwnership.HasPropSpawnerInPath(owner, campfire.logRoot)
            || !rendererAvailable)
            throw new ArgumentException($"The {name} Campfire is missing a stable renderer, particle systems, or log root outside every PropSpawner hierarchy.", "definition");
    }

    static PropSpawner? FindCampfireFactory(
        Transform featureRoot,
        Transform campfireRoot,
        PropSpawner? factory,
        string name)
    {
        foreach (var spawner in featureRoot.GetComponentsInChildren<PropSpawner>(true))
        {
            if (!spawner || !spawner.enabled || !TransformOwnership.DiscoverableWhenActivated(featureRoot, spawner.transform)
                || !CanCreateCampfire(spawner)) continue;
            if (!TransformOwnership.Contains(campfireRoot, spawner.transform))
                throw new ArgumentException($"The {name} Campfire factory must be under the Campfire root.", "definition");
            if (factory)
                throw new ArgumentException($"The {name} contains more than one active Campfire factory.", "definition");
            factory = spawner;
        }
        return factory;
    }

    static bool CanCreateCampfire(PropSpawner spawner) =>
        ContainsCampfire(EffectiveProps(spawner), new HashSet<GameObject>());

    static bool ContainsCampfire(GameObject[]? candidates, HashSet<GameObject> visited)
    {
        if (candidates is null) return false;
        foreach (var candidate in candidates)
            if (ContainsCampfire(candidate, visited)) return true;
        return false;
    }

    static bool ContainsCampfire(GameObject candidate, HashSet<GameObject> visited)
    {
        if (!candidate || !candidate.activeSelf || !visited.Add(candidate)) return false;
        foreach (var campfire in candidate.GetComponentsInChildren<global::Campfire>(true))
            if (campfire && campfire.enabled && TransformOwnership.DiscoverableWhenActivated(candidate.transform, campfire.transform)) return true;
        foreach (var spawner in candidate.GetComponentsInChildren<PropSpawner>(true))
            if (spawner && spawner.enabled && TransformOwnership.DiscoverableWhenActivated(candidate.transform, spawner.transform)
                && ContainsCampfire(EffectiveProps(spawner), visited)) return true;
        return false;
    }

    static void ValidateCampfireFactory(
        PropSpawner factory,
        Transform campfireRoot,
        string name,
        bool requireUnassignedView)
    {
        if (factory.GetType() != typeof(PropSpawner) || !factory.enabled || !TransformOwnership.Contains(campfireRoot, factory.transform)
            || !TransformOwnership.DiscoverableWhenActivated(campfireRoot, factory.transform))
            throw new ArgumentException($"The {name} Campfire factory must be an enabled discoverable native PropSpawner.", "definition");
        if (HasPropSpawnerAncestor(campfireRoot, factory.transform))
            throw new ArgumentException($"The {name} Campfire factory cannot belong to another PropSpawner hierarchy.", "definition");
        if (TransformOwnership.HasConfigurationBranch(campfireRoot, factory.transform))
            throw new ArgumentException($"The {name} Campfire factory cannot belong to a mutually exclusive configuration branch.", "definition");
        if (factory.area != Vector2.zero || factory.raycastPosition || factory.nrOfSpawns != 1
            || factory.randomSpawns || factory.minSpawnCount != 0 || !factory.chanceToUseSpawner.Equals(1f)
            || factory.currentSpawns != 0 || !factory.syncTransforms || factory.overrideProps || factory.overridePropsList
            || !ValidCampfireModifiers(factory.modifiers) || !ValidCampfireConstraints(factory.constraints)
            || factory.postConstraints is not { Count: 0 }
            || factory.postSpawnBehaviors is not { Count: 0 })
            throw new ArgumentException($"The {name} Campfire factory does not match the deterministic native fixture.", "definition");
        if (factory.props is not { Length: > 0 })
            throw new ArgumentException($"The {name} Campfire factory has no prefab candidates.", "definition");
        foreach (var candidate in factory.props)
            if (candidate) ValidateCampfireCandidate(candidate, name, requireUnassignedView);
    }

    static bool ValidCampfireModifiers(IReadOnlyList<PropSpawnerMod>? modifiers)
    {
        if (modifiers is null) return false;
        var segment = 0;
        foreach (var modifier in modifiers)
        {
            if (modifier is null || modifier.mute) return false;
            var type = modifier.GetType();
            if (type == typeof(Campfire_Set_Segment))
            {
                segment++;
                continue;
            }
            if (type != typeof(PSM_SetForwardRotationToNormal)
                && type != typeof(PSM_SetUpRotationToNormal)
                && type != typeof(PSM_SnapPosition)
                && type != typeof(PSM_SpecificRotation)) return false;
        }
        return segment == 1;
    }

    static bool ValidCampfireConstraints(IReadOnlyList<PropSpawnerConstraint>? constraints) =>
        constraints is { Count: 0 }
        || constraints is { Count: 1 } && constraints[0] is PllNoPropSpawnConstraint { mute: false };

    static void ValidateCampfireCandidate(GameObject candidate, string name, bool requireUnassignedView)
    {
        if (!candidate.activeSelf)
            throw new ArgumentException($"The {name} Campfire factory has an inactive prefab candidate.", "definition");
        if (candidate.GetComponentsInChildren<RespawnChest>(true).Length != 0
            || candidate.GetComponentsInChildren<PllLuggageAuthoring>(true).Length != 0)
            throw new ArgumentException($"The {name} Campfire factory candidate cannot contain RespawnChest or PllLuggageAuthoring.", "definition");
        var campfires = candidate.GetComponentsInChildren<global::Campfire>(true);
        if (campfires.Length != 1)
            throw new ArgumentException($"The {name} Campfire factory candidate must contain exactly one Campfire.", "definition");
        ValidateCampfire(campfires[0], candidate.transform, $"{name} generated candidate", requireUnassignedView);
    }


    internal static RespawnChest? ValidateOptionalRespawn(
        Transform featureRoot,
        string name,
        bool requireUnassignedView = true)
    {
        foreach (var spawner in featureRoot.GetComponentsInChildren<PropSpawner>(true))
        {
            if (!spawner || !TransformOwnership.DiscoverableWhenActivated(featureRoot, spawner.transform) || spawner.props is not { } props) continue;
            foreach (var prop in props)
                if (prop && prop.GetComponentInChildren<RespawnChest>(true))
                    throw new ArgumentException($"The {name} cannot generate RespawnChest from a PropSpawner; its respawn bindings must exist before feature generation.", "definition");
        }

        var chests = featureRoot.GetComponentsInChildren<RespawnChest>(true);
        if (chests.Length == 0) return null;
        if (chests.Length != 1)
            throw new ArgumentException($"The {name} contains more than one RespawnChest.", "definition");
        ValidateRespawn(chests[0], featureRoot, name, requireUnassignedView);
        return chests[0];
    }

    internal static void ValidateRespawn(
        RespawnChest chest,
        Transform owner,
        string name,
        bool requireUnassignedView)
    {
        if (!chest || !chest.enabled || !TransformOwnership.Contains(owner, chest.transform)
            || !TransformOwnership.DiscoverableWhenActivated(owner, chest.transform) || !chest.skeleton
            || !TransformOwnership.Contains(owner, chest.skeleton.transform)
            || TransformOwnership.HasPropSpawnerInPath(owner, chest.transform)
            || TransformOwnership.HasPropSpawnerInPath(owner, chest.skeleton.transform))
            throw new ArgumentException($"The {name} RespawnChest must be enabled, discoverable, and reference an owned skeleton outside every PropSpawner hierarchy.", "definition");
        var views = chest.GetComponents<PhotonView>();
        var animators = chest.GetComponents<Animator>();
        if (views.Length != 1 || !views[0] || !views[0].enabled || requireUnassignedView && views[0].ViewID != 0
            || animators.Length != 1 || !animators[0] || !animators[0].enabled)
            throw new ArgumentException($"The {name} RespawnChest must own one enabled PhotonView and Animator on the same GameObject, with its source PhotonView unassigned.", "definition");
    }

    internal static bool HasPresentation(GameObject ModelRoot)
    {
        var RendererFound = false;
        foreach (var Value in ModelRoot.GetComponentsInChildren<MeshRenderer>(true))
            if (Value && Value.enabled && TransformOwnership.DiscoverableWhenActivated(ModelRoot.transform, Value.transform))
            {
                RendererFound = true;
                break;
            }
        if (!RendererFound) return false;
        foreach (var Value in ModelRoot.GetComponentsInChildren<Collider>(true))
            if (Value && Value.enabled && TransformOwnership.DiscoverableWhenActivated(ModelRoot.transform, Value.transform)) return true;
        return false;
    }

    internal static bool HasPropSpawnerAncestor(Transform Root, Transform Value)
    {
        if (Value == Root || !TransformOwnership.Contains(Root, Value)) return false;
        for (var Current = Value.parent; Current; Current = Current.parent)
        {
            if (Current.GetComponent<PropSpawner>()) return true;
            if (Current == Root) return false;
        }
        return false;
    }

}
