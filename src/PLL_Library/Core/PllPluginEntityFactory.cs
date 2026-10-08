using System;
using UnityEngine;

namespace PLL.Library;

public interface IPllPluginEntityFactory
{
    void ConfigurePrototype(GameObject Prototype);
    IPllPluginEntityInstance Bind(GameObject Entity, PllPluginEntitySpawn Spawn);
}

public interface IPllPluginEntityInstance : IDisposable
{
    void Admit();
}

public readonly struct PllPluginEntitySpawn
{
    internal PllPluginEntitySpawn(string SegmentId, int OccurrenceIndex,
        string ZoneId, int SpawnOrdinal, int Seed)
    {
        this.SegmentId = SegmentId; this.OccurrenceIndex = OccurrenceIndex;
        this.ZoneId = ZoneId; this.SpawnOrdinal = SpawnOrdinal; this.Seed = Seed;
    }

    public string SegmentId { get; }
    public int OccurrenceIndex { get; }
    public string ZoneId { get; }
    public int SpawnOrdinal { get; }
    public int Seed { get; }
}
