using System;
using System.Collections.Generic;

namespace PLL.Library;

public static class PLLLibrary
{
    public const string PluginGuid = MyPluginInfo.PLUGIN_GUID;
    public const string PluginName = MyPluginInfo.PLUGIN_NAME;
    public const string PluginVersion = MyPluginInfo.PLUGIN_VERSION;
    public const string StartConnector = PluginGuid + "/connector/start/v1";
    public const string LayoutConnector = PluginGuid + "/connector/layout/v1";

#if !PLL_CREATOR_SDK
    public static string? CurrentSegmentId => Map.CurrentRun?.ReadyCurrentSlot?.Source.Id;
    public static string? CurrentSegmentTypeId => Map.CurrentRun?.ReadyCurrentSlot?.Source.SegmentTypeId;
    public static string? CurrentBiomeId => Map.CurrentRun?.ReadyCurrentSlot?.Source.BiomeId;
    public static int GetOccupiedEffectZoneCount(
        BiomeId BiomeId, string Hook) =>
        AuthoringRuntime.GetOccupiedEffectZoneCount(BiomeId, Hook);
    public static int EmitEffect(string hook) => AuthoringRuntime.Emit(hook);
    public static int StopEffects(string hook) => AuthoringRuntime.Stop(hook);
    public static int EmitStinger(string hook) => AuthoringRuntime.EmitStinger(hook);
    public static int StopStingers(string hook) => AuthoringRuntime.StopStingers(hook);
    public static int EmitClimbSurface(string Hook) =>
        AuthoringRuntime.EmitClimbSurface(Hook);
    public static int EmitClimbSurface(
        string Hook, float StaminaUsageMultiplier,
        float SpeedMultiplier) =>
        AuthoringRuntime.EmitClimbSurface(
            Hook, StaminaUsageMultiplier, SpeedMultiplier);
    public static int StopClimbSurface(string Hook) =>
        AuthoringRuntime.StopClimbSurface(Hook);
    public static void DeployLayout(LayoutDefinition definition) => LayoutRegistry.Register(definition);
    public static void ModifyLayout(LayoutModification modification) => LayoutRegistry.Register(modification);
    public static void RegisterEntityFactory(BepInEx.BaseUnityPlugin Provider,
        string FactoryId, string Revision, IPllPluginEntityFactory Factory) =>
        PluginEntityFactoryRegistry.Register(Provider, FactoryId, Revision, Factory);
    public static BepInEx.Configuration.ConfigFile? GetConfig(string Id) =>
        CustomBiomeConfigRegistry.GetConfig(Id);
    public static IReadOnlyList<PLLHookedProp> HookedProps => HookedPropRuntime.HookedProps;
    public static IReadOnlyList<PLLHookedProp> GetHookedProps(string hook) => HookedPropRuntime.Get(hook);
    public static int RequestPropGeneration(string Hook) =>
        Map.CurrentRun?.HookedGeneration.Request(Hook) ?? 0;

    public static event Action<PllEffectZoneOccupancyChange>?
        EffectZoneOccupancyChanged
    {
        add => AuthoringRuntime.AddEffectZoneOccupancyChanged(value);
        remove => AuthoringRuntime.RemoveEffectZoneOccupancyChanged(value);
    }

    public static event Action<PLLHookedProp>? HookedPropAdded
    {
        add => HookedPropRuntime.Added += value;
        remove => HookedPropRuntime.Added -= value;
    }

    public static event Action<PLLHookedProp>? HookedPropRemoving
    {
        add => HookedPropRuntime.Removing += value;
        remove => HookedPropRuntime.Removing -= value;
    }

    internal static void NotifyHookedPropDestroyed(PLLHookedProp value) => HookedPropRuntime.NotifyDestroyed(value);
#endif
}

#if !PLL_CREATOR_SDK
public readonly struct PllEffectZoneOccupancyChange
{
    internal PllEffectZoneOccupancyChange(
        BiomeId BiomeId, string Hook, int OccupiedZoneCount)
    {
        this.BiomeId = BiomeId;
        this.Hook = Hook;
        this.OccupiedZoneCount = OccupiedZoneCount;
    }

    public BiomeId BiomeId { get; }
    public string Hook { get; }
    public int OccupiedZoneCount { get; }
    public bool IsInside => OccupiedZoneCount != 0;
}
#endif
