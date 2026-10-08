using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class ItemSpawnerContract
{
    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PllItemSpawnerAuthoring> Component;
    readonly ComponentBinding<Spawner> SpawnerBinding;
    readonly ComponentBinding<Peak.SpawnedItemTracker> TrackerBinding;
    readonly ObjectBinding RootBinding;
    readonly TransformBinding[] SpawnPoints;
    readonly ItemContract Item;
    readonly float Chance;
    readonly int Players;
    readonly int BelowAscent;
    readonly bool Kinematic;
    readonly bool Center;
    readonly string OrderKey;

    ItemSpawnerContract(AuthoringBindingOwner Owner, ResolvedItemSpawner Authored, BiomeId Id)
    {
        this.Owner = Owner;
        OrderKey = Authored.OrderKey;
        Chance = Authored.Authoring.Chance;
        Players = Authored.Authoring.PlayersInRoomRequirement;
        BelowAscent = Authored.Authoring.BelowAscentRequirement;
        Kinematic = Authored.Authoring.IsKinematic;
        Center = Authored.Authoring.CenterItemsVisually;
        SpawnPoints = new TransformBinding[Authored.Authoring.SpawnPoints.Count];
        Item = ItemContract.Create(Authored.Item, Id, OwnerName);
        ValidateCore(Authored, Owner.BindingRoot, Id, false);
        Component = ComponentBinding<PllItemSpawnerAuthoring>.Create(Owner.BindingRoot, Authored.Authoring);
        SpawnerBinding = ComponentBinding<Spawner>.Create(Owner.BindingRoot, Authored.Spawner);
        TrackerBinding = ComponentBinding<Peak.SpawnedItemTracker>.Create(Owner.BindingRoot, Authored.Tracker);
        RootBinding = ObjectBinding.Create(Owner.BindingRoot, Authored.Root);
        for (var i = 0; i < SpawnPoints.Length; i++)
            SpawnPoints[i] = TransformBinding.Create(Owner.BindingRoot, Authored.Authoring.SpawnPoints[i]);
    }

    internal bool Generated => Owner.Generated;

    internal static ItemSpawnerContract[] CreateAll(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        var Values = new List<(AuthoringBindingOwner Owner, string Key, PllItemSpawnerAuthoring Value)>();
        Add(Template.gameObject);
        foreach (var Candidate in Graph.Ordered) Add(Candidate);
        Values.Sort((Left, Right) =>
        {
            var OwnerOrder = Left.Owner.CandidateIndex.CompareTo(Right.Owner.CandidateIndex);
            return OwnerOrder != 0 ? OwnerOrder : StringComparer.Ordinal.Compare(Left.Key, Right.Key);
        });
        for (var i = 0; i < Values.Count; i++)
            for (var j = i + 1; j < Values.Count; j++)
                if (ReferenceEquals(Values[i].Owner.SourceRoot, Values[j].Owner.SourceRoot)
                    && (TransformOwnership.Contains(Values[i].Value.transform, Values[j].Value.transform)
                        || TransformOwnership.Contains(Values[j].Value.transform, Values[i].Value.transform)))
                    throw new ArgumentException($"Biome '{Id}' item-spawner roots overlap.", "template");
        var Result = new ItemSpawnerContract[Values.Count];
        for (var i = 0; i < Result.Length; i++) Result[i] = Create(Floor, Campfire, Id, Values[i]);
        return Result;

        void Add(GameObject GraphRoot)
        {
            foreach (var Value in GraphRoot.GetComponentsInChildren<PllItemSpawnerAuthoring>(true))
            {
                if (!Value) throw new ArgumentException($"Biome '{Id}' contains a destroyed item-spawner component.", "template");
                var BindingOwner = AuthoringBindingOwner.Create(Template, Graph, Value.transform, Id, "item spawner");
                if (!ReferenceEquals(BindingOwner.SourceRoot, GraphRoot)) continue;
                Values.Add((BindingOwner,
                    DeterministicObjectIdentity.RelativeComponentPath(Value, BindingOwner.BindingRoot, 0), Value));
            }
        }
    }

    internal void ResolveExternal(ItemDatabaseCatalog Catalog, BiomeId Id) =>
        Item.ResolveDatabase(Catalog, Id, OwnerName);

    internal void ClearExternal() => Item.ClearExternal();

    internal ResolvedItemSpawner ResolveStable(Transform RuntimeRoot, BiomeId Id)
    {
        if (Generated) throw new InvalidOperationException($"Biome '{Id}' cannot resolve a generated item spawner as stable authoring.");
        return ResolveCore(RuntimeRoot, null, Id, OrderKey);
    }

    internal bool TryResolveGenerated(
        PllItemSpawnerAuthoring Source,
        BiomeId Id,
        out ResolvedItemSpawner Resolved)
    {
        Resolved = default;
        if (!Component.TryMatchGenerated(
                Owner, Source, Id, "generated item spawner", out var GeneratedRoot)) return false;
        Resolved = ResolveCore(GeneratedRoot, Source, Id,
            Owner.CandidateIndex.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "/"
            + FinalForceAuthoringContract.Key(GeneratedRoot, Source));
        return true;
    }

    internal void Validate(
        Transform Template,
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        Owner.Validate(Template, Graph, Component.Source.transform, Id, "item spawner");
        var BindingRoot = Owner.BindingRoot;
        Component.Validate(BindingRoot, Id, "item-spawner authoring");
        SpawnerBinding.Validate(BindingRoot, Id, "translated item Spawner");
        TrackerBinding.Validate(BindingRoot, Id, "translated SpawnedItemTracker");
        RootBinding.Validate(BindingRoot, Id, "item-spawner root");
        if (!ValidFeatureOwner(Component.Source, Owner, Floor, Campfire))
            throw new InvalidOperationException($"Biome '{Id}' changed an item-spawner owner before sealing.");
        ValidateCore(new ResolvedItemSpawner(
            Component.Source, RootBinding.Source, SpawnerBinding.Source, TrackerBinding.Source, Item.Source, OrderKey),
            BindingRoot, Id, true);
        Item.ValidateSource(Id, OwnerName);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Owner.WriteManifest(Writer);
        Component.WriteManifest(Writer); RootBinding.WriteManifest(Writer);
        SpawnerBinding.WriteManifest(Writer); TrackerBinding.WriteManifest(Writer);
        Writer.Write(OrderKey);
        ManifestEncoding.WriteScalar(Writer, Chance);
        Writer.Write(Players); Writer.Write(BelowAscent); Writer.Write(Kinematic); Writer.Write(Center);
        Writer.Write(SpawnPoints.Length);
        foreach (var Point in SpawnPoints) Point.WriteManifest(Writer);
        Item.WriteManifest(Writer);
    }

    static ItemSpawnerContract Create(
        Transform Floor,
        Transform Campfire,
        BiomeId Id,
        (AuthoringBindingOwner Owner, string Key, PllItemSpawnerAuthoring Value) Entry)
    {
        var Source = Entry.Value;
        if (!ValidFeatureOwner(Source, Entry.Owner, Floor, Campfire))
            throw new ArgumentException($"Biome '{Id}' item spawner '{Source.name}' has an invalid authoring root.", "template");
        return new ItemSpawnerContract(Entry.Owner, new ResolvedItemSpawner(
            Source,
            Source.Root,
            Source.GetComponentInChildren<Spawner>(true),
            Source.GetComponentInChildren<Peak.SpawnedItemTracker>(true),
            Source.Item,
            Entry.Key), Id);
    }

    ResolvedItemSpawner ResolveCore(
        Transform BindingRoot,
        PllItemSpawnerAuthoring? Expected,
        BiomeId Id,
        string RuntimeOrderKey)
    {
        var Source = Component.Resolve(BindingRoot, Id, "item-spawner authoring");
        if (Expected is not null && !ReferenceEquals(Source, Expected))
            throw new InvalidOperationException($"Biome '{Id}' generated item-spawner binding changed.");
        var Resolved = new ResolvedItemSpawner(
            Source,
            RootBinding.Resolve(BindingRoot, Id, "item-spawner root"),
            SpawnerBinding.Resolve(BindingRoot, Id, "translated item Spawner"),
            TrackerBinding.Resolve(BindingRoot, Id, "translated SpawnedItemTracker"),
            Item.Source,
            RuntimeOrderKey);
        ValidateCore(Resolved, BindingRoot, Id, true);
        return new ResolvedItemSpawner(
            Resolved.Authoring, Resolved.Root, Resolved.Spawner, Resolved.Tracker,
            Item.ResolveRuntime(Id, OwnerName), Resolved.OrderKey);
    }

    void ValidateCore(ResolvedItemSpawner Resolved, Transform BindingRoot, BiomeId Id, bool Runtime)
    {
        var Source = Resolved.Authoring;
        if (!Source) throw Failure(Id, Runtime, "was destroyed");
        if (!Source.enabled || Source.GetComponents<PllItemSpawnerAuthoring>().Length != 1
            || !Resolved.Root || Source.Root != Source.gameObject || Source.Root != Resolved.Root
            || TransformOwnership.HasPropSpawnerInPath(BindingRoot, Source.transform)
            || !ValidRoleSeparation(Source, BindingRoot) || Source.Chance < 0f || Source.Chance > 1f
            || !ManifestEncoding.CanEncode(Source.Chance) || Source.PlayersInRoomRequirement < 0
            || Source.BelowAscentRequirement < -1)
            throw Runtime
                ? Failure(Id, Runtime, "changed its sealed fields or translated native shape")
                : new ArgumentException($"Biome '{Id}' item spawner '{Source.name}' has an invalid authoring root.", "template");

        var NativeSpawners = Source.GetComponentsInChildren<Spawner>(true);
        var Trackers = Source.GetComponentsInChildren<Peak.SpawnedItemTracker>(true);
        var AllSpawners = Source.GetComponentsInChildren<ISpawner>(true);
        var Views = Source.GetComponentsInChildren<PhotonView>(true);
        if (!Resolved.Spawner || !Resolved.Tracker
            || NativeSpawners.Length != 1 || !ReferenceEquals(NativeSpawners[0], Resolved.Spawner)
            || Trackers.Length != 1 || !ReferenceEquals(Trackers[0], Resolved.Tracker)
            || Resolved.Spawner.gameObject != Resolved.Root || Resolved.Tracker.gameObject != Resolved.Root
            || AllSpawners.Length != 1 || !ReferenceEquals(AllSpawners[0], Resolved.Spawner)
            || Views.Length != 0)
            throw Runtime
                ? Failure(Id, Runtime, "changed its sealed fields or translated native shape")
                : new ArgumentException($"Biome '{Id}' item spawner '{Source.name}' has an invalid translated native shape.", "template");

        if (Source.Item != Item.Source || !Source.Chance.Equals(Chance)
            || Source.PlayersInRoomRequirement != Players || Source.BelowAscentRequirement != BelowAscent
            || Source.IsKinematic != Kinematic || Source.CenterItemsVisually != Center
            || Source.SpawnPoints.Count != SpawnPoints.Length || !ValidSpawner(Resolved.Spawner, Id, Runtime))
            throw Failure(Id, Runtime, "changed its sealed fields or translated native shape");

        var Seen = new HashSet<Transform>();
        for (var i = 0; i < SpawnPoints.Length; i++)
        {
            var Point = Runtime
                ? SpawnPoints[i].ResolveValidated(BindingRoot, Id.Value, "item-spawner spawn point")
                : Source.SpawnPoints[i];
            if (Point && Seen.Add(Point) && TransformOwnership.Contains(Source.transform, Point)
                && Source.SpawnPoints[i] == Point && Resolved.Spawner.spawnSpots[i] == Point) continue;
            throw Runtime
                ? Failure(Id, Runtime, "changed its sealed spawn-point order")
                : new ArgumentException(
                    $"Biome '{Id}' item spawner '{Source.name}' has a missing, duplicate, or unowned spawn point.",
                    "template");
        }
    }

    bool ValidSpawner(Spawner Value, BiomeId Id, bool Runtime) =>
        Value.enabled && Value.spawnMode == Spawner.SpawnMode.SingleItem
        && Value.spawnPointMode == Spawner.SpawnPointMode.SingleList
        && (!Value.spawnedObjectPrefab || Runtime && Value.spawnedObjectPrefab == Item.ResolveRuntime(Id, OwnerName))
        && !Value.spawns && (int)Value.spawnPool == 0 && Value.spawnSpots is { Count: > 0 }
        && Value.spawnSpots.Count == SpawnPoints.Length && Value.weightedSpawnSpots is { Count: 0 }
        && !Value.spawnUpTowardsTarget && !Value.spawnTransformIsSpawnerTransform && !Value.spawnAwayFromUpTarget
        && Value.centerItemsVisually == Center && Value.spawnOnStart && Value.isKinematic == Kinematic
        && Value.baseSpawnChance.Equals(Chance) && Value.playersInRoomRequirement == Players
        && Value.belowAscentRequirement == BelowAscent && !Value.normalRunsOnly && !Value.canRepeatSpawns
        && Value.heightBasedSpawnPools is { Count: 0 } && !Value.fallbackSpawn;

    static bool ValidFeatureOwner(
        PllItemSpawnerAuthoring Source,
        AuthoringBindingOwner Owner,
        Transform Floor,
        Transform Campfire) => Owner.Generated
        ? TransformOwnership.Contains(Owner.BindingRoot, Source.transform)
        : Source.transform != Floor && Source.transform != Campfire
            && (TransformOwnership.Contains(Floor, Source.transform)
                || TransformOwnership.Contains(Campfire, Source.transform));

    static bool ValidRoleSeparation(PllItemSpawnerAuthoring Source, Transform BindingRoot)
    {
        var ItemRoot = Source.Root ? Source.Root.transform : null;
        if (!ItemRoot) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllItemSpawnerAuthoring>(true))
            if (Value && Value != Source && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllLuggageAuthoring>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllStatueAuthoring>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllCampfireAuthoring>(true))
            if (Value && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllEffectAuthoring>(true))
            if (Value && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllEffectZoneAuthoring>(true))
            if (Value && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllVisualAuthoring>(true))
            if (Value && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllSoundEmitterAuthoring>(true))
            if (Value && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PllBiomeStingerAuthoring>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PLLNativeResolver>(true))
            if (Value && Value.Root && TransformOwnership.Overlaps(ItemRoot, Value.Root.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PLLForceSpawner>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PLLFinalGeneration>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<PLLNoPropSpawn>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        foreach (var Value in BindingRoot.GetComponentsInChildren<SegmentPort>(true))
            if (Value && TransformOwnership.Overlaps(ItemRoot, Value.transform)) return false;
        var Runtime = BindingRoot.GetComponent<CustomSegmentRuntime>();
        if (Runtime && (TransformOwnership.Overlaps(ItemRoot, Runtime.FogOrigin.transform)
            || TransformOwnership.Overlaps(ItemRoot, Runtime.FogCutout.transform)
            || TransformOwnership.Overlaps(ItemRoot, Runtime.ProgressPoint.transform))) return false;
        var Anchors = BindingRoot.GetComponent<PllBiomePrefabAnchors>();
        return !Anchors || (!Anchors.StatueRoot || !TransformOwnership.Overlaps(ItemRoot, Anchors.StatueRoot.transform))
            && !TransformOwnership.Overlaps(ItemRoot, Anchors.StartAnchor)
            && (!Anchors.PlayerFormationRoot || !TransformOwnership.Overlaps(ItemRoot, Anchors.PlayerFormationRoot.transform))
            && !TransformOwnership.Overlaps(ItemRoot, Anchors.PlaneAnchor)
            && !TransformOwnership.Overlaps(ItemRoot, Anchors.FogBeginAnchor)
            && !TransformOwnership.Overlaps(ItemRoot, Anchors.FogCutoffAnchor);
    }

    string OwnerName => $"item spawner '{OrderKey}'";

    Exception Failure(BiomeId Id, bool Runtime, string Reason) => Runtime
        ? new InvalidOperationException($"Biome '{Id}' {OwnerName} {Reason}.")
        : new ArgumentException($"Biome '{Id}' {OwnerName} {Reason}.", "source");
}

internal readonly struct ResolvedItemSpawner
{
    internal ResolvedItemSpawner(
        PllItemSpawnerAuthoring Authoring,
        GameObject Root,
        Spawner Spawner,
        Peak.SpawnedItemTracker Tracker,
        GameObject Item,
        string OrderKey)
    {
        this.Authoring = Authoring;
        this.Root = Root;
        this.Spawner = Spawner;
        this.Tracker = Tracker;
        this.Item = Item;
        this.OrderKey = OrderKey;
    }

    internal PllItemSpawnerAuthoring Authoring { get; }
    internal GameObject Root { get; }
    internal Spawner Spawner { get; }
    internal Peak.SpawnedItemTracker Tracker { get; }
    internal GameObject Item { get; }
    internal string OrderKey { get; }
    internal ResolvedItemSpawner WithOrderPrefix(string Prefix) =>
        new(Authoring, Root, Spawner, Tracker, Item, Prefix + OrderKey);
}
