using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public sealed class PllPluginEntityZoneSource : PllEntityZoneSource
{
    public string PluginGuid = string.Empty;
    public string FactoryId = string.Empty;
    public string Revision = string.Empty;
    public GameObject Prefab = null!;
    [Min(0)] public int MaximumInitialCount = 15;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    [Range(0f, 90f)] public float MaximumSlope = 45f;
    [Min(0f)] public float MinimumSpacing = 10f;
}

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllPluginEntityZoneValueContract
{
    internal static string? MetadataFailure(PllPluginEntityZoneSource Source)
    {
        if (ProviderFailure(Source.PluginGuid, Source.FactoryId, Source.Revision) is { } Failure) return Failure;
        if (!Source.Prefab || Source.Prefab.scene.IsValid())
            return "Entity prototype must be a prefab asset.";
        if (Source.Prefab.TryGetComponent<PllEntityAuthoring>(out var Entity)
            && (Entity.Behavior is not PllPluginEntityBehavior Behavior || Behavior.PluginGuid != Source.PluginGuid
                || Behavior.FactoryId != Source.FactoryId || Behavior.Revision != Source.Revision))
            return "Entity prototype definition must select the zone's installed factory.";
        if (Source.Prefab.TryGetComponent<PllHazardAuthoring>(out var Hazard)
            && (Hazard.Behavior is not PllPluginHazardBehavior HazardBehavior || HazardBehavior.PluginGuid != Source.PluginGuid
                || HazardBehavior.FactoryId != Source.FactoryId || HazardBehavior.Revision != Source.Revision))
            return "Hazard prototype definition must select the zone's installed factory.";
        return PllEntityZoneValueContract.GroundFailure(Source.MaximumInitialCount,
            Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing);
    }

    internal static string? ProviderFailure(string PluginGuid, string FactoryId, string Revision) =>
        PllAuthoringValueContract.IsLocalId(PluginGuid) && PllAuthoringValueContract.IsLocalId(FactoryId)
            && PllAuthoringValueContract.IsLocalId(Revision) ? null
            : "Entity plugin, factory and revision must be stable local IDs.";

    internal static string? DefinitionFailure(GameObject Root, GameObject Model, bool InitiallyEnabled)
    {
        if (!InitiallyEnabled) return "Installed fixed behavior must be initially enabled.";
        if (Root == Model)
            return "Installed fixed behavior needs a passive definition root and a separate owned model child.";
        foreach (var Component in Root.GetComponents<Component>())
            if (Component is not (Transform or PllEntityAuthoring or PllHazardAuthoring))
                return "Installed fixed behavior needs a passive definition root and a separate owned model child.";
        return CodefreeFailure(Model, null);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? PrefabFailure(GameObject Prefab)
    {
        if (!Prefab) return "Entity prototype is missing.";
        if (Prefab.TryGetComponent<PllEntityAuthoring>(out var Entity))
            return PllEntityValueContract.Failure(Entity);
        if (Prefab.TryGetComponent<PllHazardAuthoring>(out var Hazard))
            return PllHazardValueContract.Failure(Hazard) ?? CodefreeFailure(Prefab, Hazard);
        return CodefreeFailure(Prefab, null);
    }

    internal static string? CodefreeFailure(GameObject Prefab, Component? Definition, GameObject? Model = null)
    {
        foreach (var Component in Prefab.GetComponentsInChildren<Component>(true))
        {
            if (!Component) return "Entity prototype contains a missing component.";
            if (Component == Definition) continue;
            if (Component is not Transform && Model && !TransformOwnership.Contains(Model.transform, Component.transform))
                return "Entity model components must belong to the selected model root.";
            if (Component is Transform or MeshFilter or MeshRenderer or SkinnedMeshRenderer
                or BoxCollider or SphereCollider or CapsuleCollider or MeshCollider or Rigidbody) continue;
            return $"Entity prototype contains unsupported component '{Component.GetType().FullName}'.";
        }
        return null;
    }
}
