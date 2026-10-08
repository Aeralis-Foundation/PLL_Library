using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Hooked Prop")]
public sealed class PLLHookedProp : MonoBehaviour
{
    [SerializeField] string hook = "";

    public string Hook => hook ?? "";

#if !PLL_CREATOR_SDK
    void OnDestroy()
    {
        if (Application.isPlaying) PLLLibrary.NotifyHookedPropDestroyed(this);
    }
#endif
}
