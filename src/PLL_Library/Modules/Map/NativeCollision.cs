using UnityEngine;

namespace PLL.Library;

internal static class NativeCollision
{
    internal const int InitialQueryHits = 64;
    internal const int MaximumQueryHits = 1 << 16;

    internal static bool IsGeneratedOutput(Transform root, Transform value)
    {
        if (value == root) return false;
        for (var current = value.parent; current; current = current.parent)
        {
            if (current.GetComponent<PropSpawner>()) return true;
            if (current == root) return false;
        }
        return false;
    }
}
