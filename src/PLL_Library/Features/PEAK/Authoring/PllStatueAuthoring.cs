using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Props/Statue")]
public sealed class PllStatueAuthoring : MonoBehaviour
{
    [SerializeField] GameObject modelRoot = null!;
    [SerializeField] GameObject? skeletonRoot = null;
    [SerializeField] Transform spawnPoint = null!;
    [SerializeField] string displayName = "";
    [SerializeField, Min(0f)] float openTime = 1.5f;
    [SerializeField] RuntimeAnimatorController? animatorController = null;
    [SerializeField] GameObject[] items = Array.Empty<GameObject>();

    public GameObject ModelRoot => modelRoot;
    public GameObject? SkeletonRoot => skeletonRoot;
    public Transform SpawnPoint => spawnPoint;
    public string DisplayName => displayName ?? "";
    public float OpenTime => openTime;
    public RuntimeAnimatorController? AnimatorController => animatorController;
    public IReadOnlyList<GameObject> Items => items ?? Array.Empty<GameObject>();
}
