using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class StatueContract
{
    readonly ComponentBinding<PllStatueAuthoring> AuthoringBinding;
    readonly ComponentBinding<RespawnChest> ChestBinding;
    readonly ComponentBinding<Photon.Pun.PhotonView> ViewBinding;
    readonly ComponentBinding<Animator> AnimatorBinding;
    readonly ObjectBinding RootBinding;
    readonly ObjectBinding ModelRootBinding;
    readonly ObjectBinding SkeletonRootBinding;
    readonly TransformBinding SpawnPointBinding;
    readonly ItemContract[] Items;
    readonly NativeLuggageFingerprint Fingerprint;
    readonly string DisplayName;
    readonly float OpenTime;
    readonly RuntimeAnimatorController? AuthoredAnimatorController;
    readonly RuntimeAnimatorController AnimatorController;
    bool ExternalResolved;

    StatueContract(
        ComponentBinding<PllStatueAuthoring> AuthoringBinding,
        ComponentBinding<RespawnChest> ChestBinding,
        ComponentBinding<Photon.Pun.PhotonView> ViewBinding,
        ComponentBinding<Animator> AnimatorBinding,
        ObjectBinding RootBinding,
        ObjectBinding ModelRootBinding,
        ObjectBinding SkeletonRootBinding,
        TransformBinding SpawnPointBinding,
        ItemContract[] Items,
        NativeLuggageFingerprint Fingerprint,
        string DisplayName,
        float OpenTime,
        RuntimeAnimatorController? AuthoredAnimatorController,
        RuntimeAnimatorController AnimatorController)
    {
        this.AuthoringBinding = AuthoringBinding; this.ChestBinding = ChestBinding;
        this.ViewBinding = ViewBinding; this.AnimatorBinding = AnimatorBinding;
        this.RootBinding = RootBinding; this.ModelRootBinding = ModelRootBinding;
        this.SkeletonRootBinding = SkeletonRootBinding; this.SpawnPointBinding = SpawnPointBinding;
        this.Items = Items; this.Fingerprint = Fingerprint;
        this.DisplayName = DisplayName; this.OpenTime = OpenTime;
        this.AuthoredAnimatorController = AuthoredAnimatorController;
        this.AnimatorController = AnimatorController;
    }

    internal static StatueContract? CreateOptional(Transform Template, GameObject? Anchor, BiomeId BiomeId)
    {
        var Sources = Template.GetComponentsInChildren<PllStatueAuthoring>(true);
        if (Sources.Length == 0) return null;
        if (Sources.Length != 1 || !Sources[0])
            throw new ArgumentException($"Biome '{BiomeId}' must contain at most one valid PllStatueAuthoring.", "template");
        var Source = Sources[0];
        var Wrapper = Source.gameObject;
        var Model = Source.ModelRoot;
        var Skeleton = Source.SkeletonRoot ? Source.SkeletonRoot : Model;
        var Point = Source.SpawnPoint;
        if (!Source.enabled || Anchor != Wrapper || Wrapper.transform == Template
            || !TransformOwnership.DiscoverableWhenActivated(Template, Wrapper.transform)
            || TransformOwnership.HasPropSpawnerInPath(Template, Wrapper.transform)
            || !Model || !Skeleton || !Point || Model == Wrapper || Skeleton == Wrapper
            || Model == Point.gameObject || !TransformOwnership.Contains(Wrapper.transform, Model.transform)
            || !TransformOwnership.Contains(Wrapper.transform, Skeleton.transform)
            || !TransformOwnership.Contains(Wrapper.transform, Point)
            || !FinalForceAuthoringContract.ActiveBelow(Model.transform, Wrapper.transform)
            || !FinalForceAuthoringContract.ActiveBelow(Skeleton.transform, Wrapper.transform)
            || !FinalForceAuthoringContract.ActiveBelow(Point, Wrapper.transform)
            || !NativePrefabContracts.HasPresentation(Model) || string.IsNullOrEmpty(Source.DisplayName)
            || !ManifestEncoding.CanEncode(Source.OpenTime) || Source.OpenTime <= 0f)
            throw new ArgumentException($"Biome '{BiomeId}' custom statue source is incomplete or outside its stable anchored wrapper.", "template");
        var Chests = Wrapper.GetComponents<RespawnChest>();
        var LuggageComponents = Wrapper.GetComponents<global::Luggage>();
        var Views = Wrapper.GetComponents<Photon.Pun.PhotonView>();
        var Animators = Wrapper.GetComponents<Animator>();
        if (Chests.Length != 1 || LuggageComponents.Length != 1 || Chests[0].GetType() != typeof(RespawnChest)
            || !Chests[0].enabled || Chests[0].IsOpen || Chests[0].skeleton != Skeleton
            || Views.Length != 1 || !Views[0] || !Views[0].enabled || Views[0].ViewID != 0
            || Animators.Length != 1 || !Animators[0] || !Animators[0].enabled
            || !Animators[0].runtimeAnimatorController
            || Source.AnimatorController && Animators[0].runtimeAnimatorController != Source.AnimatorController
            || Chests[0].displayName != Source.DisplayName || !Chests[0].timeToOpen.Equals(Source.OpenTime)
            || !ValidSpawner(Chests[0], Point))
            throw new ArgumentException($"Biome '{BiomeId}' custom statue does not match its staged RespawnChest contract.", "template");
        var Items = new ItemContract[Source.Items.Count];
        var SeenItems = new HashSet<GameObject>();
        for (var i = 0; i < Items.Length; i++)
        {
            var ItemPrefab = Source.Items[i];
            if (!ItemPrefab || !SeenItems.Add(ItemPrefab))
                throw new ArgumentException($"Biome '{BiomeId}' custom statue has a missing or duplicate item.", "template");
            Items[i] = ItemContract.Create(ItemPrefab, BiomeId, "statue");
        }
        return new StatueContract(
            ComponentBinding<PllStatueAuthoring>.Create(Template, Source),
            ComponentBinding<RespawnChest>.Create(Template, Chests[0]),
            ComponentBinding<Photon.Pun.PhotonView>.Create(Template, Views[0]),
            ComponentBinding<Animator>.Create(Template, Animators[0]),
            ObjectBinding.Create(Template, Wrapper), ObjectBinding.Create(Template, Model),
            ObjectBinding.Create(Template, Skeleton), TransformBinding.Create(Template, Point),
            Items, NativeLuggageFingerprint.Create(Chests[0]), Source.DisplayName, Source.OpenTime,
            Source.AnimatorController, Animators[0].runtimeAnimatorController);
    }

    internal void ResolveExternal(ItemDatabaseCatalog Catalog, BiomeId BiomeId)
    {
        foreach (var Item in Items) Item.ResolveDatabase(Catalog, BiomeId, "statue");
        ExternalResolved = true;
    }

    internal void ClearExternal()
    {
        foreach (var Item in Items) Item.ClearExternal();
        ExternalResolved = false;
    }

    internal ResolvedStatue Resolve(Transform Template, BiomeId BiomeId)
    {
        if (!ExternalResolved) throw new InvalidOperationException($"Biome '{BiomeId}' statue item compatibility was not sealed.");
        var Source = AuthoringBinding.Resolve(Template, BiomeId, "statue authoring");
        var RuntimeRoot = RootBinding.Resolve(Template, BiomeId, "statue root");
        var RuntimeModel = ModelRootBinding.Resolve(Template, BiomeId, "statue model root");
        var RuntimeSkeleton = SkeletonRootBinding.Resolve(Template, BiomeId, "statue skeleton root");
        var RuntimePoint = SpawnPointBinding.ResolveValidated(Template, BiomeId.Value, "statue spawn point");
        var RuntimeChest = ChestBinding.Resolve(Template, BiomeId, "statue RespawnChest");
        var RuntimeView = ViewBinding.Resolve(Template, BiomeId, "statue PhotonView");
        var RuntimeAnimator = AnimatorBinding.Resolve(Template, BiomeId, "statue Animator");
        if (Source.gameObject != RuntimeRoot || Source.ModelRoot != RuntimeModel
            || (Source.SkeletonRoot ? Source.SkeletonRoot : Source.ModelRoot) != RuntimeSkeleton
            || Source.SpawnPoint != RuntimePoint || Source.DisplayName != DisplayName
            || !Source.OpenTime.Equals(OpenTime) || Source.AnimatorController != AuthoredAnimatorController
            || RuntimeChest.gameObject != RuntimeRoot || RuntimeChest.skeleton != RuntimeSkeleton
            || !RuntimeChest.enabled || RuntimeChest.IsOpen || RuntimeChest.displayName != DisplayName
            || !RuntimeChest.timeToOpen.Equals(OpenTime) || !ValidSpawner(RuntimeChest, RuntimePoint)
            || RuntimeView.gameObject != RuntimeRoot || !RuntimeView.enabled
            || RuntimeAnimator.gameObject != RuntimeRoot || !RuntimeAnimator.enabled
            || RuntimeAnimator.runtimeAnimatorController != AnimatorController
            || !NativePrefabContracts.HasPresentation(RuntimeModel) || !NativeLuggageFingerprint.Create(RuntimeChest).Equals(Fingerprint)
            || Source.Items.Count != Items.Length)
            throw new InvalidOperationException($"Biome '{BiomeId}' runtime custom statue changed its sealed contract.");
        var ResolvedItems = new GameObject[Items.Length];
        for (var i = 0; i < Items.Length; i++)
        {
            if (Source.Items[i] != Items[i].Source)
                throw new InvalidOperationException($"Biome '{BiomeId}' runtime custom statue changed its item catalog.");
            ResolvedItems[i] = Items[i].ResolveRuntime(BiomeId, "statue");
        }
        return new ResolvedStatue(Source, RuntimeChest, ResolvedItems);
    }

    internal void Validate(Transform Template, BiomeId BiomeId)
    {
        AuthoringBinding.Validate(Template, BiomeId, "statue authoring");
        ChestBinding.Validate(Template, BiomeId, "statue RespawnChest");
        ViewBinding.Validate(Template, BiomeId, "statue PhotonView");
        AnimatorBinding.Validate(Template, BiomeId, "statue Animator");
        RootBinding.Validate(Template, BiomeId, "statue root");
        ModelRootBinding.Validate(Template, BiomeId, "statue model root");
        SkeletonRootBinding.Validate(Template, BiomeId, "statue skeleton root");
        SpawnPointBinding.Validate(Template, BiomeId.Value, "statue spawn point");
        var Source = AuthoringBinding.Source;
        if (!Source.enabled || Source.gameObject != RootBinding.Source || Source.ModelRoot != ModelRootBinding.Source
            || (Source.SkeletonRoot ? Source.SkeletonRoot : Source.ModelRoot) != SkeletonRootBinding.Source
            || Source.SpawnPoint != SpawnPointBinding.Source || Source.DisplayName != DisplayName
            || !Source.OpenTime.Equals(OpenTime) || Source.AnimatorController != AuthoredAnimatorController
            || ChestBinding.Source.gameObject != RootBinding.Source || ChestBinding.Source.skeleton != SkeletonRootBinding.Source
            || !ChestBinding.Source.enabled || ChestBinding.Source.IsOpen || ChestBinding.Source.displayName != DisplayName
            || !ChestBinding.Source.timeToOpen.Equals(OpenTime) || !ValidSpawner(ChestBinding.Source, SpawnPointBinding.Source)
            || !ViewBinding.Source.enabled || ViewBinding.Source.ViewID != 0 || !AnimatorBinding.Source.enabled
            || AnimatorBinding.Source.runtimeAnimatorController != AnimatorController
            || !NativePrefabContracts.HasPresentation(ModelRootBinding.Source)
            || !NativeLuggageFingerprint.Create(ChestBinding.Source).Equals(Fingerprint)
            || Source.Items.Count != Items.Length)
            throw new InvalidOperationException($"Biome '{BiomeId}' changed its custom statue before sealing.");
        for (var i = 0; i < Items.Length; i++)
        {
            Items[i].ValidateSource(BiomeId, "statue");
            if (Source.Items[i] != Items[i].Source)
                throw new InvalidOperationException($"Biome '{BiomeId}' changed its custom statue item catalog before sealing.");
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        AuthoringBinding.WriteManifest(Writer); ChestBinding.WriteManifest(Writer);
        ViewBinding.WriteManifest(Writer); AnimatorBinding.WriteManifest(Writer);
        RootBinding.WriteManifest(Writer); ModelRootBinding.WriteManifest(Writer);
        SkeletonRootBinding.WriteManifest(Writer); SpawnPointBinding.WriteManifest(Writer);
        Fingerprint.WriteManifest(Writer); Writer.Write(DisplayName); Writer.Write(OpenTime);
        Writer.Write(AuthoredAnimatorController ? AuthoredAnimatorController.name : "");
        Writer.Write(AnimatorController ? AnimatorController.name : "");
        Writer.Write(Items.Length);
        foreach (var Item in Items) Item.WriteManifest(Writer);
    }

    static bool ValidSpawner(RespawnChest Value, Transform Point) =>
        NativeLuggageSelection.MatchesStagedSpawner(Value, false, 1) && Value.spawnSpots[0] == Point;
}

internal readonly struct ResolvedStatue
{
    internal ResolvedStatue(PllStatueAuthoring Authoring, RespawnChest Chest, GameObject[] Items)
    {
        this.Authoring = Authoring; this.Chest = Chest; this.Items = Items;
    }

    internal PllStatueAuthoring Authoring { get; }
    internal RespawnChest Chest { get; }
    internal GameObject[] Items { get; }
}
