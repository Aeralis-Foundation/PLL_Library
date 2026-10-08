using System;
using UnityEngine;

namespace PLL.Library;

public enum PllFluidBehavior : byte { Water = 0, Lava = 1, VisualOnly = 2 }
public enum PllFluidMaterialMode : byte { NativeDefault = 0, NativeOverrides = 1, Custom = 2 }
public enum PllFlowOutput : byte { Legacy = 0, UseFluidDefault = 1, Native = 2, Custom = 3, None = 4 }
public enum PllFluidContactMode : byte { Automatic = 0, Authored = 1 }

[Serializable]
public abstract class PllFluidInteraction
{
    private protected PllFluidInteraction() { }
    public abstract PllFluidBehavior Behavior { get; }
}

[Serializable]
public sealed class PllWaterInteraction : PllFluidInteraction
{
    public override PllFluidBehavior Behavior => PllFluidBehavior.Water;
    public float MovementShallow = DefaultMovementShallow, MovementDeep = DefaultMovementDeep;
    public float AnimationShallow = DefaultAnimationShallow, AnimationDeep = DefaultAnimationDeep;
    public float MovementJumping = 1f, JumpMultiplier = 1f;
    public bool DisableSprintJump;
    public const float DefaultMovementShallow = 0.6f, DefaultMovementDeep = 0.3f;
    public const float DefaultAnimationShallow = 0.7f, DefaultAnimationDeep = 0.25f;
}

[Serializable]
public sealed class PllLavaInteraction : PllFluidInteraction
{
    public override PllFluidBehavior Behavior => PllFluidBehavior.Lava;
    public float HeatRate = DefaultHeatRate, Heat = DefaultHeat;
    public bool IsKiln;
    public const float DefaultHeatRate = 0.1f, DefaultHeat = 0.008f;
}

[Serializable]
public sealed class PllVisualFluidInteraction : PllFluidInteraction
{
    public override PllFluidBehavior Behavior => PllFluidBehavior.VisualOnly;
}

[CreateAssetMenu(menuName = "PLL/Effects/Fluid Definition", fileName = "Fluid")]
public sealed class PllFluidDefinition : ScriptableObject
{
    [SerializeField] string DisplayNameValue = "Fluid";
#if PLL_CREATOR_SDK
    [SerializeReference] PllFluidInteraction InteractionValue = new PllWaterInteraction();
#endif
    [SerializeField, HideInInspector] PllFluidBehavior BundleBehavior = default;
    [SerializeField, HideInInspector] float BundleMovementShallow = 0f, BundleMovementDeep = 0f;
    [SerializeField, HideInInspector] float BundleAnimationShallow = 0f, BundleAnimationDeep = 0f;
    [SerializeField, HideInInspector] float BundleMovementJumping = 0f, BundleJumpMultiplier = 0f;
    [SerializeField, HideInInspector] bool BundleDisableSprintJump = false;
    [SerializeField, HideInInspector] float BundleHeatRate = 0f, BundleHeat = 0f;
    [SerializeField, HideInInspector] bool BundleIsKiln = false;
    [SerializeField] PllFluidMaterialMode ZoneMaterialModeValue = PllFluidMaterialMode.NativeDefault;
    [SerializeField] Material? ZoneMaterialValue = null;
    [SerializeField] PllFluidMaterialMode FlowMaterialModeValue = PllFluidMaterialMode.NativeDefault;
    [SerializeField] Material? FlowMaterialValue = null;
    [SerializeField] PllFlowOutput LandingValue = PllFlowOutput.Native;
    [SerializeField] GameObject? LandingPrefabValue = null;
    [SerializeField] PllFlowOutput SplashValue = PllFlowOutput.Native;
    [SerializeField] GameObject? SplashPrefabValue = null;
    [SerializeField] PllFlowOutput SamplesValue = PllFlowOutput.Native;
    [SerializeField] GameObject? SamplesPrefabValue = null;
    [SerializeField] PllFlowOutput DetailsValue = PllFlowOutput.Native;
    [SerializeField] GameObject[] DetailsPrefabsValue = Array.Empty<GameObject>();

    public string DisplayName => DisplayNameValue;
#if PLL_CREATOR_SDK
    public PllFluidBehavior Behavior => InteractionValue.Behavior;
    public float MovementShallow => (InteractionValue as PllWaterInteraction)?.MovementShallow ?? 0f;
    public float MovementDeep => (InteractionValue as PllWaterInteraction)?.MovementDeep ?? 0f;
    public float AnimationShallow => (InteractionValue as PllWaterInteraction)?.AnimationShallow ?? 0f;
    public float AnimationDeep => (InteractionValue as PllWaterInteraction)?.AnimationDeep ?? 0f;
    public float MovementJumping => (InteractionValue as PllWaterInteraction)?.MovementJumping ?? 0f;
    public float JumpMultiplier => (InteractionValue as PllWaterInteraction)?.JumpMultiplier ?? 0f;
    public bool DisableSprintJump => (InteractionValue as PllWaterInteraction)?.DisableSprintJump ?? false;
    public float HeatRate => (InteractionValue as PllLavaInteraction)?.HeatRate ?? 0f;
    public float Heat => (InteractionValue as PllLavaInteraction)?.Heat ?? 0f;
    public bool IsKiln => (InteractionValue as PllLavaInteraction)?.IsKiln ?? false;

    internal void SealBundleSerialization()
    {
        Validate(); BundleBehavior = Behavior;
        BundleMovementShallow = MovementShallow; BundleMovementDeep = MovementDeep;
        BundleAnimationShallow = AnimationShallow; BundleAnimationDeep = AnimationDeep;
        BundleMovementJumping = MovementJumping; BundleJumpMultiplier = JumpMultiplier;
        BundleDisableSprintJump = DisableSprintJump;
        BundleHeatRate = HeatRate; BundleHeat = Heat; BundleIsKiln = IsKiln;
    }

    public void ValidateBundleSerialization()
    {
        if (BundleBehavior != Behavior || BundleMovementShallow != MovementShallow
            || BundleMovementDeep != MovementDeep || BundleAnimationShallow != AnimationShallow
            || BundleAnimationDeep != AnimationDeep || BundleMovementJumping != MovementJumping
            || BundleJumpMultiplier != JumpMultiplier || BundleDisableSprintJump != DisableSprintJump
            || BundleHeatRate != HeatRate || BundleHeat != Heat || BundleIsKiln != IsKiln)
            throw new InvalidOperationException("Fluid Definition compiled interaction changed.");
    }
#else
    public PllFluidBehavior Behavior => BundleBehavior;
    public float MovementShallow => BundleMovementShallow;
    public float MovementDeep => BundleMovementDeep;
    public float AnimationShallow => BundleAnimationShallow;
    public float AnimationDeep => BundleAnimationDeep;
    public float MovementJumping => BundleMovementJumping;
    public float JumpMultiplier => BundleJumpMultiplier;
    public bool DisableSprintJump => BundleDisableSprintJump;
    public float HeatRate => BundleHeatRate;
    public float Heat => BundleHeat;
    public bool IsKiln => BundleIsKiln;
#endif
    public PllFluidMaterialMode ZoneMaterialMode => ZoneMaterialModeValue;
    public Material? ZoneMaterial => ZoneMaterialValue;
    public PllFluidMaterialMode FlowMaterialMode => FlowMaterialModeValue;
    public Material? FlowMaterial => FlowMaterialValue;
    public PllFlowOutput Landing => LandingValue;
    public GameObject? LandingPrefab => LandingPrefabValue;
    public PllFlowOutput Splash => SplashValue;
    public GameObject? SplashPrefab => SplashPrefabValue;
    public PllFlowOutput Samples => SamplesValue;
    public GameObject? SamplesPrefab => SamplesPrefabValue;
    public PllFlowOutput Details => DetailsValue;
    public GameObject[] DetailsPrefabs => DetailsPrefabsValue;

    public void Validate()
    {
#if PLL_CREATOR_SDK
        if (InteractionValue is not (PllWaterInteraction or PllLavaInteraction or PllVisualFluidInteraction))
            throw new ArgumentException("Fluid Definition has no supported interaction profile.");
#endif
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 128
            || Behavior is not (PllFluidBehavior.Water or PllFluidBehavior.Lava or PllFluidBehavior.VisualOnly))
            throw new ArgumentException("Fluid Definition name or behavior is invalid.");
        ValidateMaterial(ZoneMaterialMode, ZoneMaterial); ValidateMaterial(FlowMaterialMode, FlowMaterial);
        ValidateOutput(Landing, LandingPrefab); ValidateOutput(Splash, SplashPrefab);
        ValidateOutput(Samples, SamplesPrefab);
        if (DetailsPrefabs is null || Details is not (PllFlowOutput.Native or PllFlowOutput.Custom or PllFlowOutput.None)
            || Details == PllFlowOutput.Custom && DetailsPrefabs is not { Length: > 0 and <= 256 }
            || Details != PllFlowOutput.Custom && DetailsPrefabs.Length != 0)
            throw new ArgumentException("Fluid Definition details selection has a conflicting or missing payload.");
        foreach (var Prefab in DetailsPrefabs)
            if (!Prefab) throw new ArgumentException("Fluid Definition detail prefab is missing.");
        if (Behavior == PllFluidBehavior.VisualOnly
            && (ZoneMaterialMode != PllFluidMaterialMode.Custom || FlowMaterialMode != PllFluidMaterialMode.Custom
                || Landing == PllFlowOutput.Native || Splash == PllFlowOutput.Native
                || Samples == PllFlowOutput.Native || Details == PllFlowOutput.Native))
            throw new ArgumentException("Visual Only fluid requires Custom materials and Custom or None presentation.");
        if (Behavior == PllFluidBehavior.Water
            && (!Nonnegative(MovementShallow) || !Nonnegative(MovementDeep)
                || !Nonnegative(AnimationShallow) || !Nonnegative(AnimationDeep)
                || !Nonnegative(MovementJumping) || !Nonnegative(JumpMultiplier)))
            throw new ArgumentException("Fluid Definition water movement or animation value is invalid.");
        if (Behavior == PllFluidBehavior.Lava && (!Nonnegative(Heat) || !Nonnegative(HeatRate) || HeatRate == 0f))
            throw new ArgumentException("Fluid Definition lava heat values are invalid.");
    }

    static void ValidateMaterial(PllFluidMaterialMode Mode, Material? Material)
    {
        if (Mode is not (PllFluidMaterialMode.NativeDefault or PllFluidMaterialMode.NativeOverrides or PllFluidMaterialMode.Custom)
            || Mode == PllFluidMaterialMode.NativeDefault && Material
            || Mode != PllFluidMaterialMode.NativeDefault && (!Material || !Material.shader))
            throw new ArgumentException("Fluid Definition material selection has a conflicting or missing payload.");
        if (Material) PllNativeFlowSettings.ValidateMaterialOverridesPayload(Material);
    }

    static void ValidateOutput(PllFlowOutput Choice, GameObject? Prefab)
    {
        if (Choice is not (PllFlowOutput.Native or PllFlowOutput.Custom or PllFlowOutput.None)
            || Choice == PllFlowOutput.Custom && !Prefab || Choice != PllFlowOutput.Custom && Prefab)
            throw new ArgumentException("Fluid Definition output selection has a conflicting or missing payload.");
    }

    static bool Nonnegative(float Value) => Value >= 0f && PllAuthoringValueContract.CanEncode(Value);
}
