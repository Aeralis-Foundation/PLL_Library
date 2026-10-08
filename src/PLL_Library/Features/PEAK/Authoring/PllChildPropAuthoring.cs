using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllChildPropTiming : byte
{
    Immediate = 0,
    Late = 1
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Child Props")]
public sealed class PllChildPropAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] Transform[] points = Array.Empty<Transform>();
    [SerializeField] GameObject[] props = Array.Empty<GameObject>();
    [SerializeField, Range(0f, 1f)] float chancePerPoint = 1f;
    [SerializeField] PllChildPropTiming timing = PllChildPropTiming.Immediate;

    public GameObject Root => root;
    public IReadOnlyList<Transform> Points => points ?? Array.Empty<Transform>();
    public IReadOnlyList<GameObject> Props => props ?? Array.Empty<GameObject>();
    public float ChancePerPoint => chancePerPoint;
    public PllChildPropTiming Timing => timing;
}

[Serializable]
public sealed class PllSocketTransformModifier : PropSpawnerMod
{
    public override void ModifyObject(GameObject value, PropSpawner.SpawnData data)
    {
        value.transform.SetPositionAndRotation(data.pos, data.spawnerTransform.rotation);
    }
}
