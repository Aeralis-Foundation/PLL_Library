using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public struct PllNativeSpawnEntry
{
    public string Name;
    [Min(1)] public int Weight;
}

public enum PllNativeSpawnerKind : byte { AttachedFood, GroundFood, Items, Props }

public abstract class PllNativeSpawnerAuthoring : MonoBehaviour
#if !PLL_CREATOR_SDK
    , ISerializationCallbackReceiver
#endif
{
    [SerializeField, HideInInspector] string[]? BundleNames = null;
    [SerializeField, HideInInspector] int[]? BundleWeights = null;

    public abstract PllNativeSpawnerSelection Capture();

#if PLL_CREATOR_SDK
    public void SealBundleSerialization(PllNativeSpawnerSelection Selection)
    {
        var Names = new string[Selection.Entries.Count];
        var Weights = new int[Names.Length];
        for (var i = 0; i < Names.Length; i++)
        {
            Names[i] = Selection.Entries[i].Name;
            Weights[i] = Selection.Entries[i].Weight;
        }
        BundleNames = Names; BundleWeights = Weights;
    }
#else
    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (BundleNames is null && BundleWeights is null) return;
        if (BundleNames is not { } Names || BundleWeights is not { } Weights || Names.Length != Weights.Length)
            throw new InvalidOperationException("Compiled native spawn names and weights do not match.");
        if (Names.Length > PllNativeSpawnerSelection.MaximumEntries)
            throw new InvalidOperationException(
                $"Compiled native spawn lists cannot exceed {PllNativeSpawnerSelection.MaximumEntries} entries.");
        var Entries = new PllNativeSpawnEntry[Names.Length];
        for (var i = 0; i < Entries.Length; i++)
            Entries[i] = new PllNativeSpawnEntry { Name = Names[i], Weight = Weights[i] };
        switch (this)
        {
            case PllFoodSpawnerAuthoring Food: Food.CustomItems = Entries; break;
            case PllContentSpawnerAuthoring Content: Content.Content = Entries; break;
            default: throw new InvalidOperationException("Compiled native spawn entries have an unsupported owner.");
        }
    }
#endif
}

public sealed class PllNativeSpawnerSelection
{
    internal const int MaximumEntries = 256;

    PllNativeSpawnerSelection(PllNativeSpawnerKind Kind, SpawnPool Pool,
        PllNativeSpawnEntry[] Entries, Transform[] Points, Transform? UpTowards,
        int Minimum, int Maximum, float Bias, float Chance, Vector3 ZoneSize)
    {
        this.Kind = Kind; this.Pool = Pool;
        this.Entries = Array.AsReadOnly(Entries); this.Points = Array.AsReadOnly(Points);
        this.UpTowards = UpTowards; this.Minimum = Minimum; this.Maximum = Maximum;
        this.Bias = Bias; this.Chance = Chance == 0f ? 0f : Chance; this.ZoneSize = ZoneSize;
    }

    public PllNativeSpawnerKind Kind { get; }
    public SpawnPool Pool { get; }
    public IReadOnlyList<PllNativeSpawnEntry> Entries { get; }
    public IReadOnlyList<Transform> Points { get; }
    public Transform? UpTowards { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public float Bias { get; }
    public float Chance { get; }
    public Vector3 ZoneSize { get; }

    internal static PllNativeSpawnerSelection Capture(PllFoodSpawnerAuthoring Source)
    {
        if (!Source.enabled || Source.GetComponents<PllNativeSpawnerAuthoring>().Length != 1
            || Source.SpawnPoints is null || Source.CustomItems is null
            || Source.Placement is not PllFoodPlacement.Attached and not PllFoodPlacement.Ground)
            throw new ArgumentException("Food Spawner needs an enabled component and a supported placement.");
        ValidateCounts(Source.MinimumCount, Source.MaximumCount, Source.Chance);
        if (Source.Placement == PllFoodPlacement.Attached
                && (!PllAuthoringValueContract.CanEncode(Source.CountBias) || Source.CountBias <= 0f)
            || Source.SpawnPoints.Length < Source.MaximumCount || Source.SpawnPoints.Length > 256)
            throw new ArgumentException("Food Spawner needs a positive count bias and enough spawn points for its maximum count (at most 256).");
        var Seen = new HashSet<Transform>();
        foreach (var Point in Source.SpawnPoints)
            if (!Point || !Seen.Add(Point) || !TransformOwnership.Contains(Source.transform, Point))
                throw new ArgumentException("Food Spawner points must be unique children of its GameObject.");
        if (PllAuthoringValueContract.DestroyedReference(Source.UpTowards)
            || Source.UpTowards && (!TransformOwnership.Contains(Source.transform, Source.UpTowards)
            || Source.Placement == PllFoodPlacement.Ground))
            throw new ArgumentException("Food Spawner's up target must belong to an Attached spawner.");
        if (!Source.UseCustomList && Source.NativePool is not (
            PllFoodPool.BeachBerries or PllFoodPool.JungleBerries or PllFoodPool.Mushrooms
            or PllFoodPool.SpikyVine or PllFoodPool.Coconuts or PllFoodPool.JungleWillow
            or PllFoodPool.JungleVine or PllFoodPool.Winterberries or PllFoodPool.Cactus
            or PllFoodPool.Redwood or PllFoodPool.Nest))
            throw new ArgumentException("Food Spawner has an unsupported native food pool.");
        if (Source.UseCustomList) ValidateEntries(Source.CustomItems);
        else if (Source.CustomItems.Length != 0)
            throw new ArgumentException("Food Spawner's custom list must be empty when using a native pool.");
        return new(Source.Placement == PllFoodPlacement.Attached
            ? PllNativeSpawnerKind.AttachedFood : PllNativeSpawnerKind.GroundFood,
            Source.UseCustomList ? SpawnPool.None : (SpawnPool)Source.NativePool,
            (PllNativeSpawnEntry[])Source.CustomItems.Clone(), (Transform[])Source.SpawnPoints.Clone(),
            Source.UpTowards, Source.MinimumCount, Source.MaximumCount,
            Source.Placement == PllFoodPlacement.Attached ? Source.CountBias : 1f, Source.Chance, default);
    }

    internal static PllNativeSpawnerSelection Capture(PllContentSpawnerAuthoring Source)
    {
        if (!Source.enabled || Source.GetComponents<PllNativeSpawnerAuthoring>().Length != 1 || Source.Content is null
            || Source.Kind is not PllContentKind.Items and not PllContentKind.Props)
            throw new ArgumentException("Content Spawner needs an enabled component and a supported content kind.");
        ValidateCounts(Source.MinimumCount, Source.MaximumCount, Source.Chance);
        ValidateEntries(Source.Content);
        if (!PllAuthoringValueContract.CanEncode(Source.ZoneSize.x)
            || !PllAuthoringValueContract.CanEncode(Source.ZoneSize.y)
            || !PllAuthoringValueContract.CanEncode(Source.ZoneSize.z)
            || Source.ZoneSize.x <= 0f || Source.ZoneSize.y <= 0f || Source.ZoneSize.z <= 0f
            || Source.transform.lossyScale != Vector3.one)
            throw new ArgumentException("Content Spawner needs positive zone dimensions and unit world scale.");
        return new(Source.Kind == PllContentKind.Items ? PllNativeSpawnerKind.Items : PllNativeSpawnerKind.Props,
            SpawnPool.None, (PllNativeSpawnEntry[])Source.Content.Clone(), Array.Empty<Transform>(), null,
            Source.MinimumCount, Source.MaximumCount, 1f, Source.Chance, Source.ZoneSize);
    }

    static void ValidateCounts(int Minimum, int Maximum, float Chance)
    {
        if (Minimum < 0 || Maximum < 1 || Maximum > 256 || Minimum > Maximum
            || !PllAuthoringValueContract.CanEncode(Chance) || Chance < 0f || Chance > 1f)
            throw new ArgumentException("Spawner counts must fit 0..256, with a positive maximum and a chance in 0..1.");
    }

    static void ValidateEntries(PllNativeSpawnEntry[] Entries)
    {
        if (Entries.Length == 0 || Entries.Length > MaximumEntries)
            throw new ArgumentException($"A custom spawn list needs 1..{MaximumEntries} entries.");
        var Names = new HashSet<string>(StringComparer.Ordinal); long Total = 0;
        foreach (var Entry in Entries)
        {
            if (!ForceObjectNameContract.IsValid(Entry.Name))
                throw new ArgumentException("Native content names " + ForceObjectNameContract.Requirement + ".");
            if (!Names.Add(Entry.Name) || Entry.Weight < 1)
                throw new ArgumentException("Native content names must be unique, with positive weights.");
            Total += Entry.Weight;
        }
        if (Total > int.MaxValue) throw new ArgumentException("Spawn weights exceed the supported total.");
    }

    public bool HasSameSettings(PllNativeSpawnerSelection Other)
    {
        if (Kind != Other.Kind || Pool != Other.Pool || Minimum != Other.Minimum || Maximum != Other.Maximum
            || Bias != Other.Bias || Chance != Other.Chance || ZoneSize != Other.ZoneSize
            || Entries.Count != Other.Entries.Count || Points.Count != Other.Points.Count) return false;
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Name != Other.Entries[i].Name || Entries[i].Weight != Other.Entries[i].Weight) return false;
        return true;
    }

    public void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write((byte)Kind); Writer.Write((int)Pool); Writer.Write(Minimum); Writer.Write(Maximum);
        Writer.Write(Bias); Writer.Write(Chance); Writer.Write(ZoneSize.x); Writer.Write(ZoneSize.y); Writer.Write(ZoneSize.z);
        Writer.Write(Entries.Count);
        foreach (var Entry in Entries) { Writer.Write(Entry.Name); Writer.Write(Entry.Weight); }
    }
}
