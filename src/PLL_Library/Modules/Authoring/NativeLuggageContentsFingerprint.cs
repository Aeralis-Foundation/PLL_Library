using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeLuggageContentsFingerprint : IEquatable<NativeLuggageContentsFingerprint>
{
    const string Protocol = "pll/native-luggage-contents/v1";
    readonly byte[] Data;
    readonly int Hash;

    NativeLuggageContentsFingerprint(byte[] Data)
    {
        this.Data = Data;
        unchecked
        {
            var Value = (int)2166136261;
            foreach (var Item in Data) Value = (Value ^ Item) * 16777619;
            Hash = Value;
        }
    }

    internal static NativeLuggageContentsFingerprint Create(NativeLuggageContents Contents)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, Encoding.UTF8, true))
        {
            Writer.Write(Protocol);
            Writer.Write(Contents.Variant);
            switch (Contents.Variant)
            {
                case 0:
                    Contents.SpawnedObjectPrefab.Write(Writer);
                    break;
                case 1:
                    Writer.Write((int)Contents.SpawnPool);
                    Contents.FallbackSpawn.Write(Writer);
                    break;
                case 2:
                    Writer.Write(Contents.HeightCount);
                    for (var Index = 0; Index < Contents.HeightCount; Index++)
                    {
                        var Height = Contents.HeightAt(Index);
                        Writer.Write((int)Height.SpawnPool);
                        Writer.Write(Height.MinimumHeight);
                        Writer.Write(Height.MinimumZ);
                        Writer.Write(Height.HasBiomeRequirement);
                        Writer.Write((int)Height.BiomeRequirement);
                    }
                    Contents.FallbackSpawn.Write(Writer);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Native luggage contents variant {Contents.Variant} is outside the sealed model.");
            }
        }
        return new NativeLuggageContentsFingerprint(Stream.ToArray());
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Data.Length);
        Writer.Write(Data);
    }

    public bool Equals(NativeLuggageContentsFingerprint? Other)
    {
        if (Other is null || Hash != Other.Hash || Data.Length != Other.Data.Length) return false;
        for (var Index = 0; Index < Data.Length; Index++)
            if (Data[Index] != Other.Data[Index]) return false;
        return true;
    }

    public override bool Equals(object? Value) =>
        Value is NativeLuggageContentsFingerprint Other && Equals(Other);
    public override int GetHashCode() => Hash;
}

internal sealed class NativeLuggageContents
{
    readonly NativeHeightSelection[] Heights;

    NativeLuggageContents(
        byte Variant,
        NativeLuggageContentsPrefab SpawnedObjectPrefab,
        SpawnPool SpawnPool,
        NativeHeightSelection[] Heights,
        NativeLuggageContentsPrefab FallbackSpawn)
    {
        this.Variant = Variant;
        this.SpawnedObjectPrefab = SpawnedObjectPrefab;
        this.SpawnPool = SpawnPool;
        this.Heights = Heights;
        this.FallbackSpawn = FallbackSpawn;
        Fingerprint = NativeLuggageContentsFingerprint.Create(this);
    }

    internal byte Variant { get; }
    internal NativeLuggageContentsPrefab SpawnedObjectPrefab { get; }
    internal SpawnPool SpawnPool { get; }
    internal int HeightCount => Heights.Length;
    internal bool HeightBased => Variant == 2;
    internal NativeLuggageContentsPrefab FallbackSpawn { get; }
    internal NativeLuggageContentsFingerprint Fingerprint { get; }

    internal NativeHeightSelection HeightAt(int Index) => Heights[Index];

    internal static bool TryCreate(
        global::Luggage Source,
        out NativeLuggageContents Contents,
        out string Reason)
    {
        Contents = null!;
        switch (Source.spawnMode)
        {
            case Spawner.SpawnMode.SingleItem:
                if (!NativeLuggageContentsPrefab.TryCreate(
                        Source.spawnedObjectPrefab,
                        "spawnedObjectPrefab",
                        out var SpawnedObjectPrefab,
                        out Reason)) return false;
                Contents = new NativeLuggageContents(
                    0, SpawnedObjectPrefab, default, Array.Empty<NativeHeightSelection>(),
                    NativeLuggageContentsPrefab.Missing);
                return true;
            case Spawner.SpawnMode.SpawnPool:
                if (!NativeLuggageContentsPrefab.TryCreate(
                        Source.fallbackSpawn,
                        "fallbackSpawn",
                        out var PoolFallback,
                        out Reason)) return false;
                Contents = new NativeLuggageContents(
                    1, NativeLuggageContentsPrefab.Missing, Source.spawnPool,
                    Array.Empty<NativeHeightSelection>(), PoolFallback);
                return true;
            case Spawner.SpawnMode.HeightBasedSpawnPools:
                if (Source.heightBasedSpawnPools is not { Count: > 0 } SourceHeights)
                {
                    Reason = "heightBasedSpawnPools is null or empty";
                    return false;
                }
                var Heights = new NativeHeightSelection[SourceHeights.Count];
                for (var Index = 0; Index < Heights.Length; Index++)
                {
                    var Height = SourceHeights[Index];
                    if (Height is null)
                    {
                        Reason = "heightBasedSpawnPools contains a null entry";
                        return false;
                    }
                    Heights[Index] = new NativeHeightSelection(Height);
                }
                if (!NativeLuggageContentsPrefab.TryCreate(
                        Source.fallbackSpawn,
                        "fallbackSpawn",
                        out var HeightFallback,
                        out Reason)) return false;
                Contents = new NativeLuggageContents(
                    2, NativeLuggageContentsPrefab.Missing, default, Heights, HeightFallback);
                return true;
            case Spawner.SpawnMode.Guidebook:
                Reason = "Guidebook content selection is unsupported";
                return false;
            default:
                Reason = $"spawn mode {(int)Source.spawnMode} is outside the supported content model";
                return false;
        }
    }

    internal void ApplyTo(global::Luggage Target)
    {
        Target.spawnMode = Spawner.SpawnMode.SingleItem;
        Target.spawnedObjectPrefab = null;
        Target.spawns = null;
        Target.spawnPool = default;
        Target.heightBasedSpawnPools = new List<Spawner.HeightBasedSpawnListEntry>();
        Target.fallbackSpawn = null;
        switch (Variant)
        {
            case 0:
                Target.spawnedObjectPrefab = SpawnedObjectPrefab.Asset;
                return;
            case 1:
                Target.spawnMode = Spawner.SpawnMode.SpawnPool;
                Target.spawnPool = SpawnPool;
                Target.fallbackSpawn = FallbackSpawn.Asset;
                return;
            case 2:
                Target.spawnMode = Spawner.SpawnMode.HeightBasedSpawnPools;
                foreach (var Height in Heights) Target.heightBasedSpawnPools.Add(Height.Create());
                Target.fallbackSpawn = FallbackSpawn.Asset;
                return;
            default:
                throw new InvalidOperationException(
                    $"Native luggage contents variant {Variant} is outside the sealed model.");
        }
    }
}

internal sealed class NativeLuggageContentsPrefab
{
    internal static NativeLuggageContentsPrefab Missing { get; } = new(
        null, false, "", Array.Empty<NativeLuggageContentsItem>());

    readonly NativeLuggageContentsItem[] Items;

    NativeLuggageContentsPrefab(
        GameObject? Asset,
        bool Present,
        string Name,
        NativeLuggageContentsItem[] Items)
    {
        this.Asset = Asset;
        this.Present = Present;
        this.Name = Name;
        this.Items = Items;
    }

    internal GameObject? Asset { get; }
    bool Present { get; }
    string Name { get; }

    internal static bool TryCreate(
        GameObject? Source,
        string Field,
        out NativeLuggageContentsPrefab Result,
        out string Reason)
    {
        if (Source is null || !Source)
        {
            Result = Missing;
            Reason = "";
            return true;
        }
        if (Source.scene.IsValid())
        {
            Result = null!;
            Reason = $"{Field} references scene GameObject '{Source.name}'";
            return false;
        }
        var Components = Source.GetComponentsInChildren<Item>(true);
        var Items = new NativeLuggageContentsItem[Components.Length];
        for (var Index = 0; Index < Items.Length; Index++)
        {
            if (!Components[Index])
            {
                Result = null!;
                Reason = $"{Field} contains a destroyed Item component";
                return false;
            }
            Items[Index] = new NativeLuggageContentsItem(Components[Index]);
        }
        Array.Sort(Items, NativeLuggageContentsItem.Compare);
        for (var Index = 1; Index < Items.Length; Index++)
            if (Items[Index - 1].Equals(Items[Index]))
            {
                Result = null!;
                Reason = $"{Field} contains duplicate Item identity '{Items[Index].Name}'";
                return false;
            }
        Result = new NativeLuggageContentsPrefab(Source, true, Source.name ?? "", Items);
        Reason = "";
        return true;
    }

    internal void Write(BinaryWriter Writer)
    {
        Writer.Write(Present);
        if (!Present) return;
        Writer.Write(Name);
        Writer.Write(Items.Length);
        foreach (var Item in Items) Item.Write(Writer);
    }
}

internal readonly struct NativeLuggageContentsItem : IEquatable<NativeLuggageContentsItem>
{
    internal NativeLuggageContentsItem(Item Source)
    {
        Id = Source.itemID;
        Name = Source.gameObject.name ?? "";
        var Type = Source.GetType();
        TypeName = Type.FullName ?? Type.Name;
    }

    internal ushort Id { get; }
    internal string Name { get; }
    internal string TypeName { get; }

    internal static int Compare(
        NativeLuggageContentsItem Left,
        NativeLuggageContentsItem Right)
    {
        var Result = Left.Id.CompareTo(Right.Id);
        if (Result != 0) return Result;
        Result = StringComparer.Ordinal.Compare(Left.Name, Right.Name);
        return Result != 0 ? Result : StringComparer.Ordinal.Compare(Left.TypeName, Right.TypeName);
    }

    internal void Write(BinaryWriter Writer)
    {
        Writer.Write(Id);
        Writer.Write(Name);
        Writer.Write(TypeName);
    }

    public bool Equals(NativeLuggageContentsItem Other) =>
        Id == Other.Id && string.Equals(Name, Other.Name, StringComparison.Ordinal)
        && string.Equals(TypeName, Other.TypeName, StringComparison.Ordinal);
    public override bool Equals(object? Value) =>
        Value is NativeLuggageContentsItem Other && Equals(Other);
    public override int GetHashCode() => HashCode.Combine(Id, Name, TypeName);
}
