using HarmonyLib;

namespace PLL.Library;

[HarmonyPatch(typeof(global::FrogTongue), "OnCollisionEnter", typeof(UnityEngine.Collision))]
internal static class FrogTongueCollisionPatch
{
    [HarmonyPrefix]
    static bool Prefix(global::FrogTongue __instance) => __instance && Character.localCharacter;
}
