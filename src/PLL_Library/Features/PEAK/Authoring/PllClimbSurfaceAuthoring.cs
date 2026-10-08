using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllClimbSurfaceMode : byte
{
    Always,
    EmitHook
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Geometry/Climb Surface")]
public sealed class PllClimbSurfaceAuthoring : MonoBehaviour
{
    [SerializeField] PllClimbSurfaceMode ModeValue = PllClimbSurfaceMode.Always;
    [SerializeField] string EmitHookValue = "";
    [SerializeField, Min(0f)] float StaminaUsageMultiplierValue = 1f;
    [SerializeField, Min(0f)] float SpeedMultiplierValue = 1f;

    public PllClimbSurfaceMode Mode => ModeValue;
    public string EmitHook => EmitHookValue ?? "";
    public float StaminaUsageMultiplier => StaminaUsageMultiplierValue;
    public float SpeedMultiplier => SpeedMultiplierValue;
}

[Serializable]
public sealed class PllClimbSurfaceMaterialRule
{
    [SerializeField] PhysicsMaterial PhysicsMaterialValue = null!;
    [SerializeField] PllClimbSurfaceMode ModeValue = PllClimbSurfaceMode.Always;
    [SerializeField] string EmitHookValue = "";
    [SerializeField, Min(0f)] float StaminaUsageMultiplierValue = 1f;
    [SerializeField, Min(0f)] float SpeedMultiplierValue = 1f;

    public PhysicsMaterial PhysicsMaterial => PhysicsMaterialValue;
    public PllClimbSurfaceMode Mode => ModeValue;
    public string EmitHook => EmitHookValue ?? "";
    public float StaminaUsageMultiplier => StaminaUsageMultiplierValue;
    public float SpeedMultiplier => SpeedMultiplierValue;
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Geometry/Climb Material Rules")]
public sealed class PllClimbSurfaceMaterialAuthoring : MonoBehaviour
#if !PLL_CREATOR_SDK
    , ISerializationCallbackReceiver
#endif
{
#if PLL_CREATOR_SDK
    [SerializeField] PllClimbSurfaceMaterialRule[] RulesValue =
        Array.Empty<PllClimbSurfaceMaterialRule>();
#else
    PllClimbSurfaceMaterialRule[]? PreparedRules;
#endif
    [SerializeReference, HideInInspector] object[]? BundleRules = null;

#if PLL_CREATOR_SDK
    public IReadOnlyList<PllClimbSurfaceMaterialRule> Rules =>
        RulesValue ?? Array.Empty<PllClimbSurfaceMaterialRule>();

    internal void SealBundleSerialization() => BundleRules =
        PllBundleSerialization.SealArray(RulesValue ?? Array.Empty<PllClimbSurfaceMaterialRule>());
#else
    public IReadOnlyList<PllClimbSurfaceMaterialRule> Rules => PreparedRules ??=
        PllBundleSerialization.ReadArray<PllClimbSurfaceMaterialRule>(BundleRules, "climb material rules");

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize() => PreparedRules = null;
#endif
}
