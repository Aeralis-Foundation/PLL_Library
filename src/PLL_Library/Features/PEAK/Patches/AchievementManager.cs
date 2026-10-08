using HarmonyLib;

namespace PLL.Library;

[HarmonyPatch(typeof(global::AchievementManager), nameof(global::AchievementManager.InitRunBasedValues), typeof(global::SerializableRunBasedValues))]
internal static class AchievementManagerInitRunBasedValuesPatch
{
    [HarmonyPrefix]
    static void Prefix(global::AchievementManager __instance, global::SerializableRunBasedValues initialValues) =>
        __instance.runBasedValueData = initialValues;
}
