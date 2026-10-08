using UnityEngine;

namespace PLL.Library;

public enum PllCollisionMode : byte
{
    Automatic,
    Authored,
    None
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Geometry/Collision")]
public sealed class PllCollisionAuthoring : MonoBehaviour
{
    [SerializeField] PllCollisionMode ModeValue = PllCollisionMode.Automatic;

    public PllCollisionMode Mode => ModeValue;
}
