using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeFogHazards
{
    internal static bool IsNativeRisingGhost(LavaRising Rising)
    {
        var Type = Rising.risingFieldType;
        return Type is LavaRising.RisingFieldType.Gloom or LavaRising.RisingFieldType.VoidGhosts;
    }

    internal static GameObject RequireNativeSleepFog(
        Peak.Island.GloomVoiceConfuser Manager,
        Transform Root)
    {
        if (!Manager)
            throw new InvalidOperationException("The native Gloom voice manager component is invalid.");
        if (Manager.fogField is not Peak.StatusFieldGloom FogField
            || !FogField || !TransformOwnership.Contains(Root, FogField.transform))
            throw new InvalidOperationException("The native Gloom voice manager's fog field ownership is invalid.");
        if (FogField.transform == Root)
            throw new InvalidOperationException("The stationary Gloom fog field cannot own the native placement root.");
        return FogField.gameObject;
    }
    internal static void ConfigureNativeFogHazards(
        Transform Root,
        bool IsClone,
        Dictionary<GameObject, bool> Originals,
        RunEnvironmentConfiguration Configuration)
    {
        if (Configuration.NoRisingGhosts)
            foreach (var Rising in Root.GetComponentsInChildren<LavaRising>(true))
            {
                if (!IsNativeRisingGhost(Rising)) continue;
                if (Rising.transform == Root)
                    throw new InvalidOperationException("A rising Gloom or lost-soul field cannot own the native placement root.");
                PlacementExecution.SetPlacedActive(Rising.gameObject, false, IsClone, Originals);
            }

        if (!Configuration.NoSleepFog) return;
        foreach (var Manager in Root.GetComponentsInChildren<Peak.Island.GloomVoiceConfuser>(true))
            PlacementExecution.SetPlacedActive(RequireNativeSleepFog(Manager, Root), false, IsClone, Originals);
    }
}
