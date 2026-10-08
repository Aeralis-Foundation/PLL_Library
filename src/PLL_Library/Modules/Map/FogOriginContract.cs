using UnityEngine;

namespace PLL.Library;

internal static class FogOriginContract
{
    internal static bool IsNativeTemplate(FogSphereOrigin? Candidate, bool DisablesFog)
    {
        if (!Candidate || !Candidate.enabled || !Candidate.gameObject.activeSelf
            || Candidate.disableFog != DisablesFog
            || Candidate.gameObject.GetComponents<FogSphereOrigin>().Length != 1) return false;
        var HasNext = false;
        var HasPrevious = false;
        foreach (var Value in Candidate.GetComponentsInChildren<Transform>(true))
        {
            if (Value.name == "Fogwall_Next")
            {
                if (HasNext) return false;
                HasNext = true;
            }
            else if (Value.name == "Fogwall_Prev")
            {
                if (HasPrevious) return false;
                HasPrevious = true;
            }
        }
        return HasNext && HasPrevious;
    }
}
