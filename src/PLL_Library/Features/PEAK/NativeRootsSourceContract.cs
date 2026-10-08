using System;
using UnityEngine;

namespace PLL.Library;

internal static class NativeRootsSourceContract
{
    const int AuthoredSpawnCount = 100200;
    const int CorrectedSpawnCount = 100;

    internal static void Normalize(Transform SegmentParent)
    {
        if (!SegmentParent || !string.Equals(SegmentParent.name, "Roots Segment", StringComparison.Ordinal))
            throw new InvalidOperationException("PEAK's native Roots segment identity changed.");
        var Plateau = RequireDirectChild(SegmentParent, "PlateauProps");
        var Cave = RequireDirectChild(Plateau, "- Cave Mania Variant");
        var Target = RequireDirectChild(Cave, "SporeShrooms (1)");
        var Spawners = Target.GetComponents<PropSpawner>();
        if (Spawners.Length != 1 || Spawners[0].GetType() != typeof(PropSpawner))
            throw new InvalidOperationException(
                "PEAK's native Roots SporeShrooms (1) target must own one direct PropSpawner.");
        if (Spawners[0].nrOfSpawns != AuthoredSpawnCount)
            throw new InvalidOperationException(
                $"PEAK's native Roots SporeShrooms (1) count changed from {AuthoredSpawnCount} to {Spawners[0].nrOfSpawns}.");
        Spawners[0].nrOfSpawns = CorrectedSpawnCount;
    }

    static Transform RequireDirectChild(Transform Parent, string Name)
    {
        Transform? Result = null;
        for (var i = 0; i < Parent.childCount; i++)
        {
            var Child = Parent.GetChild(i);
            if (!string.Equals(Child.name, Name, StringComparison.Ordinal)) continue;
            if (Result)
                throw new InvalidOperationException(
                    $"PEAK's native Roots source owns multiple direct '{Name}' children.");
            Result = Child;
        }
        return Result
            ? Result
            : throw new InvalidOperationException(
                $"PEAK's native Roots source is missing its direct '{Name}' child.");
    }
}
