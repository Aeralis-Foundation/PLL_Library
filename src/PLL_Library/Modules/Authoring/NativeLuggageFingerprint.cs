using System;
using System.IO;

namespace PLL.Library;

internal sealed class NativeLuggageFingerprint : IEquatable<NativeLuggageFingerprint>
{
    const string Protocol = "pll/native-luggage-fingerprint/v2";
    readonly byte[] Data;
    readonly int Hash;

    NativeLuggageFingerprint(byte[] Data)
    {
        this.Data = Data;
        unchecked
        {
            var Value = (int)2166136261;
            foreach (var Item in Data) Value = (Value ^ Item) * 16777619;
            Hash = Value;
        }
    }

    internal static NativeLuggageFingerprint Create(global::Luggage Value)
    {
        if (!NativeLuggageSelection.TryCapture(Value, out var Selection, out var Reason))
            throw new InvalidOperationException($"Native luggage cannot be fingerprinted: {Reason}.");
        return Create(Selection);
    }

    internal static NativeLuggageFingerprint Create(NativeLuggageSelection Selection)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write(Protocol);
            Writer.Write(Selection.DisplayName); Writer.Write(Selection.TimeToOpen);
            Writer.Write(Selection.PlayersInRoomRequirement); Writer.Write(Selection.BelowAscentRequirement);
            Writer.Write(Selection.NormalRunsOnly); Writer.Write((int)Selection.SpawnMode);
            Writer.Write(Selection.BaseSpawnChance);
            WritePrefab(Writer, Selection.SpawnedObjectPrefab); WriteSpawnList(Writer, Selection.Spawns);
            Writer.Write((int)Selection.SpawnPool); Writer.Write(Selection.CanRepeatSpawns);
            Writer.Write(Selection.SpawnTransformIsSpawnerTransform); Writer.Write(Selection.SpawnAwayFromUpTarget);
            Writer.Write(Selection.CenterItemsVisually); Writer.Write(Selection.SpawnOnStart);
            Writer.Write(Selection.IsKinematic);
            if (!Selection.HasHeights) Writer.Write(-1);
            else
            {
                var HeightCount = Selection.HeightCount;
                Writer.Write(HeightCount);
                for (var Index = 0; Index < HeightCount; Index++)
                {
                    var Height = Selection.HeightAt(Index);
                    Writer.Write(true);
                    Writer.Write((int)Height.SpawnPool); Writer.Write(Height.MinimumHeight); Writer.Write(Height.MinimumZ);
                    Writer.Write(Height.HasBiomeRequirement); Writer.Write((int)Height.BiomeRequirement);
                }
            }
            WritePrefab(Writer, Selection.FallbackSpawn);
        }
        return new NativeLuggageFingerprint(Stream.ToArray());
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Data.Length); Writer.Write(Data);
    }

    public bool Equals(NativeLuggageFingerprint? Other)
    {
        if (Other is null || Hash != Other.Hash || Data.Length != Other.Data.Length) return false;
        for (var Index = 0; Index < Data.Length; Index++)
            if (Data[Index] != Other.Data[Index]) return false;
        return true;
    }

    public override bool Equals(object? Value) => Value is NativeLuggageFingerprint Other && Equals(Other);
    public override int GetHashCode() => Hash;

    static void WritePrefab(BinaryWriter Writer, NativePrefabSelection Value)
    {
        Writer.Write(Value.Present);
        if (!Value.Present) return;
        Writer.Write(Value.Name);
        var ItemCount = Value.ItemCount;
        Writer.Write(ItemCount);
        for (var Index = 0; Index < ItemCount; Index++)
        {
            var Item = Value.ItemAt(Index);
            Writer.Write(Item.Name); Writer.Write(Item.ItemId); Writer.Write(Item.TypeName);
        }
    }

    static void WriteSpawnList(BinaryWriter Writer, NativeSpawnList? Value)
    {
        Writer.Write(Value is not null);
        if (Value is null) return;
        Writer.Write(Value.Name);
        if (!Value.HasEntries)
        {
            Writer.Write(-1);
            return;
        }
        var EntryCount = Value.EntryCount;
        Writer.Write(EntryCount);
        for (var Index = 0; Index < EntryCount; Index++)
        {
            var Entry = Value.EntryAt(Index);
            Writer.Write(Entry is not null);
            if (Entry is null) continue;
            WritePrefab(Writer, Entry.Prefab); Writer.Write(Entry.Weight); Writer.Write(Entry.PercentageOdds);
        }
    }
}
