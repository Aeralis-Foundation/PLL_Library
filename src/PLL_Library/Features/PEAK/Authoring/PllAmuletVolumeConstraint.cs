using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public sealed class PllAmuletVolumeConstraint : PropSpawnerConstraint
{
    [SerializeField] PllAmuletSpawnerAuthoring Source;

    public PllAmuletVolumeConstraint(PllAmuletSpawnerAuthoring Source)
    {
        if (!Source) throw new ArgumentNullException(nameof(Source));
        this.Source = Source;
    }

    internal PllAmuletSpawnerAuthoring Owner => Source;

    public override bool CheckConstraint(PropSpawner.SpawnData spawnData) =>
        PllZoneMath.Contains(Source.transform, Source.Volume, spawnData.pos);
}
