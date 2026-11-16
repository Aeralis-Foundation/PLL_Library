using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class NativeFrogWater
{
    [HarmonyPatch(typeof(FrogTongue), nameof(FrogTongue.WaterRaycast))]
    static class WaterRaycastPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FrogTongue __instance)
        {
            var Origin = __instance.transform.position + Vector3.up * 0.5f;
            __instance.waterCastHit = HelperFunctions.LineCheck(
                Origin, Origin + Vector3.down * __instance.floatCastLength, HelperFunctions.LayerType.Map);
            var Hit = __instance.waterCastHit.transform;
            if (!Hit)
            {
                __instance.inWater = false;
                __instance.rig.linearDamping = 0f;
                return false;
            }
            if (!__instance.inWater)
            {
                __instance.inWater = true;
                var Zone = Hit.parent ? Hit.parent.GetComponentInChildren<WaterZone>() : null;
                if (Zone)
                {
                    var At = __instance.transform.position
                        + Vector3.ClampMagnitude(__instance.rig.linearVelocity.normalized, 1f);
                    Zone.PlaySplash(At);
                    if (__instance.splashSFX) __instance.splashSFX.Play(At);
                }
            }
            if (Hit.gameObject.CompareTag(__instance._waterTag))
                __instance.rig.AddForce(Vector3.up * __instance.floatForce
                    * (__instance.floatCastLength - __instance.waterCastHit.distance));
            __instance.rig.linearDamping = __instance.floatDrag;
            return false;
        }
    }
}
