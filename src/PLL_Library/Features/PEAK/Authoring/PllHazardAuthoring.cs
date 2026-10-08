using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public abstract class PllHazardBehavior
{
    private protected PllHazardBehavior() { }
}

[Serializable]
public sealed class PllContactInjuryBehavior : PllHazardBehavior
{
    public Collider Contact = null!;
    [Min(0f)] public float InjuryAmount = 0.15f;
    [Min(0f)] public float CooldownSeconds = 1f;
    [Min(0f)] public float Knockback = 0f;
    [Min(0f)] public float RagdolledKnockback = 0f;
    [Range(0f, 1f)] public float ForwardBias = 0f;
    public bool KnockbackOnlyWhenStanding = false;
    public bool PreventFallDamage = true;
}

[Serializable]
public sealed class PllPluginHazardBehavior : PllHazardBehavior
{
    public string PluginGuid = string.Empty;
    public string FactoryId = string.Empty;
    public string Revision = string.Empty;
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Hazard")]
public sealed class PllHazardAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] string IdValue = string.Empty;
    [SerializeField] GameObject ModelRootValue = null!;
    [SerializeField] bool InitiallyEnabledValue = true;
#if PLL_CREATOR_SDK
    [SerializeReference] PllHazardBehavior BehaviorValue = null!;
#endif
    [SerializeReference, HideInInspector] object BundleBehavior = null!;

    public string Id => IdValue;
    public GameObject ModelRoot => ModelRootValue;
    public bool InitiallyEnabled => InitiallyEnabledValue;
#if PLL_CREATOR_SDK
    public PllHazardBehavior Behavior => BehaviorValue;
    internal void SealBundleSerialization() => BundleBehavior = BehaviorValue;
#else
    public PllHazardBehavior Behavior => BundleBehavior as PllHazardBehavior
        ?? throw PllBundleSerialization.Missing(nameof(BundleBehavior));
    internal HazardInstance? Binding;
    void OnDisable() => Binding?.Suspend();
    void OnEnable() => Binding?.Resume();
    void OnDestroy() => Binding?.Retire();
#endif
}

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class PllHazardContact : MonoBehaviour
{
    [SerializeField, HideInInspector] PllHazardAuthoring DefinitionValue = null!;
    public PllHazardAuthoring Definition => DefinitionValue;
#if !PLL_CREATOR_SDK
    internal HazardInstance? Binding;
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
static class PllHazardValueContract
{
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllHazardAuthoring Source)
    {
        if (!Source || !Source.enabled) return "Hazard definition must be enabled.";
        return Failure(Source.transform, Source.Id, Source.ModelRoot, Source.InitiallyEnabled, Source.Behavior);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(Transform Owner, string Id, GameObject Model, bool InitiallyEnabled, PllHazardBehavior Behavior)
    {
        if (!Owner || !PllAuthoringValueContract.IsLocalId(Id))
            return "Hazard needs an enabled definition with a stable local ID.";
        if (!Model || !TransformOwnership.Contains(Owner, Model.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Model.transform))
            return "Hazard needs an active owned model.";
        if (Owner.GetComponentInChildren<PllEntityAuthoring>(true))
            return "Hazard cannot also own an Entity definition.";
        if (Behavior is PllPluginHazardBehavior && (!InitiallyEnabled || Model == Owner.gameObject))
            return "Plugin Hazard needs an initially enabled definition and a separate owned model child.";
        return Behavior switch
        {
            PllContactInjuryBehavior Contact => ContactFailure(Owner, Model, Contact),
            PllArrowShooterBehavior or PllMovingSawBehavior or PllSpikeTrapBehavior => PllMechanicalHazardValueContract.Failure(Owner, Model, Behavior),
            PllPluginHazardBehavior Plugin => PllPluginEntityZoneValueContract.ProviderFailure(Plugin.PluginGuid, Plugin.FactoryId, Plugin.Revision)
                ?? PllPluginEntityZoneValueContract.DefinitionFailure(Owner.gameObject, Model, InitiallyEnabled),
            _ => "Hazard behavior is unsupported. Select an admitted profile."
        };
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? ContactFailure(Transform Owner, GameObject Model, PllContactInjuryBehavior Behavior)
    {
        if (!Owner || !Model || !TransformOwnership.Contains(Owner, Model.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Model.transform))
            return "Hazard needs an active owned model.";
        var Contact = Behavior.Contact;
        if (!Contact || !Contact.enabled || Contact.isTrigger || Contact.attachedRigidbody
            || !TransformOwnership.Contains(Owner, Contact.transform)
            || !TransformOwnership.DiscoverableWhenActivated(Owner, Contact.transform)
            || Contact.GetComponents<Collider>().Length != 1)
            return "Contact Injury needs one enabled owned solid collider without a Rigidbody.";
        if (Contact is not (BoxCollider or SphereCollider or CapsuleCollider or MeshCollider)
            || Contact is MeshCollider Mesh && !Mesh.sharedMesh)
            return "Contact Injury collider must be a Box, Sphere, Capsule or static Mesh collider.";
        if (Owner.GetComponentInChildren<Rigidbody>(true)
            || Owner.GetComponentInChildren<Animator>(true)
            || Owner.GetComponentInChildren<Animation>(true)
            || Owner.GetComponentInChildren<SkinnedMeshRenderer>(true))
            return "Contact Injury supports stationary rigid geometry only.";
        foreach (var Component in Contact.GetComponents<MonoBehaviour>())
            if (Component is ICollisionModifierEvent)
                return "Contact Injury collider must not contain native contact callbacks.";
        if (!Nonnegative(Behavior.InjuryAmount) || !Nonnegative(Behavior.CooldownSeconds)
            || !Nonnegative(Behavior.Knockback) || !Nonnegative(Behavior.RagdolledKnockback)
            || !PllAuthoringValueContract.CanEncode(Behavior.ForwardBias)
            || Behavior.ForwardBias < 0f || Behavior.ForwardBias > 1f)
            return "Contact Injury amounts must be finite and nonnegative; Forward Bias must be between zero and one.";
        foreach (var Renderer in Model.GetComponentsInChildren<MeshRenderer>(true))
            if (Renderer.enabled && TransformOwnership.DiscoverableWhenActivated(Model.transform, Renderer.transform)
                && Renderer.TryGetComponent<MeshFilter>(out var Filter) && Filter.sharedMesh) return null;
        return "Contact Injury model needs an enabled rigid mesh renderer.";
    }

    static bool Nonnegative(float Value) => PllAuthoringValueContract.CanEncode(Value) && Value >= 0f;
}
