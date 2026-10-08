using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public abstract class PllEntityBehavior
{
    private protected PllEntityBehavior() { }
}

[Serializable]
public sealed class PllBeetleBehavior : PllEntityBehavior
{
    [SerializeReference] public PllNativeEntityAppearance? Appearance;
}

[Serializable]
public sealed class PllMushroomZombieBehavior : PllEntityBehavior { }

[Serializable]
public sealed class PllCondorBehavior : PllEntityBehavior
{
    [SerializeReference] public PllNativeEntityAppearance? Appearance;
    public Vector3 NestOffset = new(0f, 0f, 10f);
}

[Serializable]
public sealed class PllPluginEntityBehavior : PllEntityBehavior
{
    public string PluginGuid = string.Empty;
    public string FactoryId = string.Empty;
    public string Revision = string.Empty;
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Entity")]
public sealed class PllEntityAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] string IdValue = string.Empty;
    [SerializeField] GameObject ModelRootValue = null!;
    [SerializeField] bool InitiallyEnabledValue = true;
#if PLL_CREATOR_SDK
    [SerializeReference] PllEntityBehavior BehaviorValue = null!;
#endif
    [SerializeReference, HideInInspector] object BundleBehavior = null!;

    public string Id => IdValue;
    public GameObject ModelRoot => ModelRootValue;
    public bool InitiallyEnabled => InitiallyEnabledValue;
#if PLL_CREATOR_SDK
    public PllEntityBehavior Behavior => BehaviorValue;
    internal void SealBundleSerialization() => BundleBehavior = BehaviorValue;
#else
    public PllEntityBehavior Behavior => BundleBehavior as PllEntityBehavior
        ?? throw PllBundleSerialization.Missing(nameof(BundleBehavior));
    internal EntityBinding? Binding;
    void OnDisable() => Binding?.Suspend();
    void OnEnable() => Binding?.Resume();
    void OnDestroy() => Binding?.Retire();
#endif
}

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllEntityValueContract
{
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllEntityAuthoring Source)
    {
        if (!Source || !Source.enabled) return "Entity definition must be enabled.";
        return Failure(Source.transform, Source.Id, Source.ModelRoot, Source.InitiallyEnabled, Source.Behavior);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(Transform Owner, string Id, GameObject Model, bool InitiallyEnabled, PllEntityBehavior Behavior)
    {
        if (!Owner || !PllAuthoringValueContract.IsLocalId(Id))
            return "Entity needs an enabled definition with a stable source ID.";
        if (!Model || !TransformOwnership.Contains(Owner, Model.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Model.transform))
            return "Entity model must belong to its definition root.";
        if (Owner.GetComponentInChildren<PllHazardAuthoring>(true))
            return "Entity cannot also own a Hazard definition.";
        if (Behavior is not PllPluginEntityBehavior && Owner.GetComponentInChildren<Rigidbody>(true))
            return "Native Entity uses its prepared native body, not an additional authored Rigidbody.";
        if (Model.GetComponentInChildren<Renderer>(true)
            && (Behavior is PllMushroomZombieBehavior
                || Behavior is PllBeetleBehavior { Appearance: null }
                || Behavior is PllCondorBehavior { Appearance: null }))
            return "Custom Entity renderers need an admitted Beetle or Condor appearance mapping. Zombie uses native appearance only.";
        var Failure = Behavior switch
        {
            PllBeetleBehavior Beetle => PllNativeEntityAppearanceValueContract.Failure(Beetle.Appearance),
            PllMushroomZombieBehavior => null,
            PllCondorBehavior Condor when !PllAuthoringValueContract.CanEncode(Condor.NestOffset.x)
                || !PllAuthoringValueContract.CanEncode(Condor.NestOffset.y)
                || !PllAuthoringValueContract.CanEncode(Condor.NestOffset.z)
                => "Condor nest offset must be finite.",
            PllCondorBehavior Condor => PllNativeEntityAppearanceValueContract.Failure(Condor.Appearance),
            PllPluginEntityBehavior Plugin => PllPluginEntityZoneValueContract.ProviderFailure(
                Plugin.PluginGuid, Plugin.FactoryId, Plugin.Revision)
                ?? PllPluginEntityZoneValueContract.DefinitionFailure(
                    Owner.gameObject, Model, InitiallyEnabled),
            _ => "Entity behavior is unsupported. Select an admitted native recipe or installed factory."
        };
        return Failure ?? PllPluginEntityZoneValueContract.CodefreeFailure(
            Owner.gameObject, Owner.GetComponent<PllEntityAuthoring>(), Model);
    }
}
