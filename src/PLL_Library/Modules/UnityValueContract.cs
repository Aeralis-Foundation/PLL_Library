using UnityEngine;

namespace PLL.Library;

internal static class UnityValueContract
{
    internal static bool Finite(Vector3 Value) =>
        float.IsFinite(Value.x) && float.IsFinite(Value.y)
        && float.IsFinite(Value.z);

    internal static bool Finite(Quaternion Value) =>
        float.IsFinite(Value.x) && float.IsFinite(Value.y)
        && float.IsFinite(Value.z) && float.IsFinite(Value.w);

    internal static bool Finite(Color Value) =>
        float.IsFinite(Value.r) && float.IsFinite(Value.g)
        && float.IsFinite(Value.b) && float.IsFinite(Value.a);
}
