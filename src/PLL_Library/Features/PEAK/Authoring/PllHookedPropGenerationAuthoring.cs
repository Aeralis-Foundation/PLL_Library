using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
public sealed class PllHookedPropGenerationAuthoring : MonoBehaviour
{
    [SerializeField] bool GenerateOnLoadValue = true;
    [SerializeField] string EmitHookValue = "";

    public bool GenerateOnLoad => GenerateOnLoadValue;
    public string EmitHook => EmitHookValue ?? "";
}
