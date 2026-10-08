using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class NativeBridgeCollision
{
    [HarmonyPatch]
    static class AddCollisionModifiersPatch
    {
        static MethodBase TargetMethod() => AccessTools.DeclaredMethod(
            typeof(BreakableBridge), nameof(BreakableBridge.AddCollisionModifiers), Type.EmptyTypes)
            ?? throw new MissingMethodException("BreakableBridge.AddCollisionModifiers was not found.");

        [HarmonyPrefix]
        static bool Prefix(BreakableBridge __instance)
        {
            if (!NativeFeatureGeneration.IsActiveDescendant(__instance.transform)) return true;
            Action<Character, CollisionModifier, Collision, Bodypart> Callback = __instance.OnBridgeCollision;
            foreach (var Collider in __instance.GetComponentsInChildren<Collider>())
            {
                var Modifier = Collider.gameObject.AddComponent<CollisionModifier>();
                Modifier.applyEffects = false;
                Modifier.onCollide += Callback;
            }
            return false;
        }
    }
}
