using HarmonyLib;

namespace PLL.Library;

[HarmonyPatch(typeof(global::Character), nameof(global::Character.Awake))]
internal static class CharacterLookInitializationPatch
{
    [HarmonyPostfix]
    static void Postfix(global::Character __instance) => __instance.RecalculateLookDirections();
}
