using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class ItemContract
{
    readonly GameObject SourceObject;
    readonly Item ItemComponent;
    readonly ushort ItemId;
    readonly string ItemName;
    readonly string ItemType;
    GameObject? ResolvedPrefab;

    ItemContract(GameObject SourceObject, Item ItemComponent)
    {
        this.SourceObject = SourceObject;
        this.ItemComponent = ItemComponent;
        ItemId = ItemComponent.itemID;
        ItemName = ItemComponent.gameObject.name;
        ItemType = ItemComponent.GetType().FullName ?? ItemComponent.GetType().Name;
    }

    internal GameObject Source => SourceObject;

    internal static ItemContract Create(GameObject Source, BiomeId BiomeId, string Owner)
    {
        var Values = Source ? Source.GetComponentsInChildren<Item>(true) : Array.Empty<Item>();
        if (!Source || Values.Length != 1 || !Values[0])
            throw new ArgumentException($"Biome '{BiomeId}' {Owner} has a missing or ambiguous PEAK item prefab.", "source");
        return new ItemContract(Source, Values[0]);
    }

    internal void ResolveDatabase(ItemDatabaseCatalog Catalog, BiomeId BiomeId, string Owner)
    {
        if (Catalog is null) throw new ArgumentNullException("catalog");
        if (!ItemComponent || ItemComponent.itemID != ItemId || ItemComponent.gameObject.name != ItemName
            || (ItemComponent.GetType().FullName ?? ItemComponent.GetType().Name) != ItemType)
            throw new InvalidOperationException($"Biome '{BiomeId}' {Owner} changed an authored item before sealing.");
        ResolvedPrefab = Catalog.Resolve(ItemId, ItemName, ItemType, $"Biome '{BiomeId}' {Owner}");
    }

    internal GameObject ResolveRuntime(BiomeId BiomeId, string Owner)
    {
        if (!ResolvedPrefab) throw new InvalidOperationException($"Biome '{BiomeId}' {Owner} item compatibility was not sealed.");
        if (!ItemDatabase.TryGetItem(ItemId, out var Database) || !Database || Database.gameObject != ResolvedPrefab
            || Database.itemID != ItemId || Database.gameObject.name != ItemName
            || (Database.GetType().FullName ?? Database.GetType().Name) != ItemType)
            throw new InvalidOperationException($"Biome '{BiomeId}' {Owner} ItemDatabase mapping changed after sealing.");
        return ResolvedPrefab;
    }

    internal void ClearExternal() => ResolvedPrefab = null;

    internal void ValidateSource(BiomeId BiomeId, string Owner)
    {
        if (!SourceObject || !ItemComponent || ItemComponent.itemID != ItemId || ItemComponent.gameObject.name != ItemName
            || (ItemComponent.GetType().FullName ?? ItemComponent.GetType().Name) != ItemType)
            throw new InvalidOperationException($"Biome '{BiomeId}' {Owner} changed an authored item before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(ItemId); Writer.Write(ItemName); Writer.Write(ItemType);
    }
}
