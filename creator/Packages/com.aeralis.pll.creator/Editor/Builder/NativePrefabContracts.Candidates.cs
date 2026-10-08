#nullable enable
using UnityEngine;

namespace PLL.Library
{
    internal static partial class NativePrefabContracts
    {
        internal static GameObject[]? EffectiveProps(PropSpawner Spawner) =>
            Spawner.overrideProps && Spawner.overridePropsList ? Spawner.overridePropsList.gameObjects : Spawner.props;
    }
}
