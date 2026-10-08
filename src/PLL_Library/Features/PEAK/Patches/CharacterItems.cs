using HarmonyLib;

namespace PLL.Library;

[HarmonyPatch(typeof(global::CharacterItems), "UpdatePocketBehaviors")]
internal static class CharacterItemsPatch
{
    [HarmonyPrefix]
    static bool Prefix(Character ___character) => ___character.player != null;
}
