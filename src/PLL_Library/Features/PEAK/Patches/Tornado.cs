using HarmonyLib;

namespace PLL.Library;

[HarmonyPatch(typeof(global::Tornado), "TargetSelection")]
internal static class TornadoTargetSelectionPatch
{
    [HarmonyPrefix]
    static bool Prefix(global::Tornado __instance) => __instance && __instance.targetParent;
}
