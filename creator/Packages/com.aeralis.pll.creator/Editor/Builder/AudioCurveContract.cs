using UnityEngine;

namespace PLL.Library
{
    internal static class AudioCurveContract
    {
        internal static bool IsConstantZero(Keyframe[] Keys)
        {
            foreach (var Key in Keys)
                if (!Key.value.Equals(0f)) return false;
            for (var i = 0; i + 1 < Keys.Length; i++)
                if (!IsZeroOrStep(Keys[i].outTangent) || !IsZeroOrStep(Keys[i + 1].inTangent)) return false;
            return true;
        }

        internal static bool IsZeroOrStep(float Value) => Value.Equals(0f) || float.IsInfinity(Value);
    }
}
