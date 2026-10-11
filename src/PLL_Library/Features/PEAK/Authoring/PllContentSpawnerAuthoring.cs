using System;
using UnityEngine;

namespace PLL.Library;

public enum PllContentKind : byte { Items, Props }

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Content Spawner")]
public sealed class PllContentSpawnerAuthoring : PllNativeSpawnerAuthoring
{
    public PllContentKind Kind;
    public PllNativeSpawnEntry[] Content = Array.Empty<PllNativeSpawnEntry>();
    public Vector3 ZoneSize = new(10f, 10f, 10f);
    [Min(0)] public int MinimumCount = 1;
    [Min(1)] public int MaximumCount = 5;
    [Range(0f, 1f)] public float Chance = 1f;

    public override PllNativeSpawnerSelection Capture() => PllNativeSpawnerSelection.Capture(this);

    void OnDrawGizmosSelected()
    {
        var Previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.down * ZoneSize.y * 0.5f, ZoneSize);
        Gizmos.matrix = Previous;
    }
}
