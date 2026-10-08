using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Item Spawner")]
public sealed class PllItemSpawnerAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] GameObject item = null!;
    [SerializeField] Transform[] spawnPoints = Array.Empty<Transform>();
    [SerializeField, Range(0f, 1f)] float chance = 1f;
    [SerializeField, Min(0)] int playersInRoomRequirement = 0;
    [SerializeField] int belowAscentRequirement = -1;
    [SerializeField] bool isKinematic = false;
    [SerializeField] bool centerItemsVisually = false;

    public GameObject Root => root;
    public GameObject Item => item;
    public IReadOnlyList<Transform> SpawnPoints => spawnPoints ?? Array.Empty<Transform>();
    public float Chance => chance;
    public int PlayersInRoomRequirement => playersInRoomRequirement;
    public int BelowAscentRequirement => belowAscentRequirement;
    public bool IsKinematic => isKinematic;
    public bool CenterItemsVisually => centerItemsVisually;
}
