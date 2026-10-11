using System;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/NativeModifiers")]
public sealed class NativeModifiers : MonoBehaviour
{
    public bool NightCold;
    public bool MesaHeatstroke;

    public static (bool NightCold, bool MesaHeatstroke) Capture(Transform Root)
    {
        if (!Root) throw new ArgumentNullException(nameof(Root));
        var Sources = Root.GetComponentsInChildren<NativeModifiers>(true);
        if (Sources.Length == 0) return default;
        if (Sources.Length != 1 || Sources[0].transform != Root
            || !Root.GetComponent<CustomSegmentRuntime>())
            throw new ArgumentException(
                "NativeModifiers must occur once on the biome prefab root.", nameof(Root));
        return Sources[0].enabled
            ? (Sources[0].NightCold, Sources[0].MesaHeatstroke) : default;
    }
}
