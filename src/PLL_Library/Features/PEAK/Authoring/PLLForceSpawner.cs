using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Force Spawner")]
public sealed class PLLForceSpawner : MonoBehaviour
{
    [SerializeField] private string objectName = "";

    public string ObjectName => objectName ?? "";
}
