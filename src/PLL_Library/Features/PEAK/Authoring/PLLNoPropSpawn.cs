using System;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Geometry/No Prop Spawn")]
public sealed class PLLNoPropSpawn : MonoBehaviour
{
}

[Serializable]
public sealed class PllNoPropSpawnConstraint : PropSpawnerConstraint
{
    public override bool CheckConstraint(PropSpawner.SpawnData data)
    {
        var collider = data.hit.collider;
        return collider == null || collider.GetComponentInParent<PLLNoPropSpawn>() == null;
    }
}
