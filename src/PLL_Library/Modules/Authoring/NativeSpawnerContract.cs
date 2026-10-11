using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeSpawnerContract
{
    readonly ComponentBinding<PllNativeSpawnerAuthoring> Binding;
    readonly ObjectBinding Native;
    readonly Component NativeSource;
    readonly TransformBinding[] Points;
    readonly TransformBinding? UpTowards;
    NativeSpawnResolution[]? Resolutions;
    GameObject[][]? Families;
    byte[]? PoolFingerprint;
    int TotalWeight;

    NativeSpawnerContract(AuthoringBindingOwner Owner, PllNativeSpawnerAuthoring Source, PllNativeSpawnerSelection Selection)
    {
        this.Owner = Owner; this.Selection = Selection;
        Binding = ComponentBinding<PllNativeSpawnerAuthoring>.Create(Owner.BindingRoot, Source);
        NativeSource = PllNativeSpawnerShape.Require(Source, Selection);
        Native = ObjectBinding.Create(Owner.BindingRoot, NativeSource.gameObject);
        RequireUnbound();
        Points = new TransformBinding[Selection.Points.Count];
        for (var i = 0; i < Points.Length; i++) Points[i] = TransformBinding.Create(Owner.BindingRoot, Selection.Points[i]);
        if (Selection.UpTowards) UpTowards = TransformBinding.Create(Owner.BindingRoot, Selection.UpTowards);
    }

    internal AuthoringBindingOwner Owner { get; }
    internal PllNativeSpawnerSelection Selection { get; }
    internal bool Generated => Owner.Generated;
    internal PllNativeSpawnerAuthoring Source => Binding.Source;

    internal static NativeSpawnerContract[] Capture(Transform Template, Transform Floor, Transform Campfire,
        AuthoringCandidateGraph Graph, BiomeId Id)
    {
        var Entries = AuthoringBindingOwner.GraphComponents<PllNativeSpawnerAuthoring>(Template, Id, Graph, "native spawner");
        var Result = new NativeSpawnerContract[Entries.Count];
        for (var i = 0; i < Result.Length; i++)
        {
            var Entry = Entries[i];
            if (Entry.Owner.Generated && Entry.Value is PllContentSpawnerAuthoring)
                throw new ArgumentException("Content Spawner requires a stable biome zone, not a generated candidate.");
            if (!Entry.Owner.Generated && !TransformOwnership.Contains(Floor, Entry.Value.transform)
                && !TransformOwnership.Contains(Campfire, Entry.Value.transform))
                throw new ArgumentException("Food and Content Spawners must belong to the floor, campfire or a reachable candidate.");
            Result[i] = new(Entry.Owner, Entry.Value, Entry.Value.Capture());
        }
        return Result;
    }

    internal void ResolveExternal()
    {
        var Prepared = new NativeSpawnResolution[Selection.Entries.Count]; var Total = 0;
        var PreparedFamilies = new GameObject[Prepared.Length][];
        for (var i = 0; i < Prepared.Length; i++)
        {
            Prepared[i] = NativeSpawnCatalog.ResolveContent(Selection.Entries[i].Name,
                Selection.Kind == PllNativeSpawnerKind.Props ? PllContentKind.Props : PllContentKind.Items);
            PreparedFamilies[i] = Selection.Kind == PllNativeSpawnerKind.Props
                ? ((NativePropSpawnResolution)Prepared[i]).PropFamily()
                : new[] { ((NativeItemSpawnResolution)Prepared[i]).Prefab() };
            Total = checked(Total + Selection.Entries[i].Weight);
        }
        if (Selection.Pool != SpawnPool.None) PoolFingerprint = NativeSpawnCatalog.ResolveFoodPool(Selection.Pool);
        Resolutions = Prepared; Families = PreparedFamilies; TotalWeight = Total;
        NativeSpawnCatalog.RetainContent(this);
    }

    internal void ClearExternal()
    {
        NativeSpawnCatalog.ReleaseContent(this);
        Resolutions = null; Families = null; PoolFingerprint = null; TotalWeight = 0;
    }

    internal void Validate(Transform Template, AuthoringCandidateGraph Graph, BiomeId Id)
    {
        Owner.Validate(Template, Graph, Source.transform, Id, "native spawner");
        Binding.Validate(Owner.BindingRoot, Id, "native spawner"); Native.Validate(Owner.BindingRoot, Id, "translated native spawner");
        Resolve(Owner.BindingRoot, Id); RequireUnbound();
    }

    internal Component ResolveStable(Transform Root, BiomeId Id)
    {
        if (Generated) throw new InvalidOperationException("Generated native spawners need a candidate instance.");
        return Resolve(Root, Id);
    }

    internal Component ResolveGenerated(Transform Root, BiomeId Id)
    {
        if (!Generated) throw new InvalidOperationException("Stable native spawners need their biome instance.");
        return Resolve(Root, Id);
    }

    Component Resolve(Transform Root, BiomeId Id)
    {
        var Source = Binding.Resolve(Root, Id, "native spawner");
        var Actual = Source.Capture();
        if (!Selection.HasSameSettings(Actual)) throw new InvalidOperationException("Native spawner settings changed after capture.");
        for (var i = 0; i < Points.Length; i++)
            if (Points[i].ResolveValidated(Root, Id.Value, "food spawn point") != Actual.Points[i])
                throw new InvalidOperationException("Food Spawner point bindings changed.");
        if (UpTowards is { } Up ? Up.ResolveValidated(Root, Id.Value, "food up target") != Actual.UpTowards : Actual.UpTowards)
            throw new InvalidOperationException("Food Spawner up target changed.");
        var Resolved = PllNativeSpawnerShape.Require(Source, Actual);
        if (Resolved.gameObject != Native.Resolve(Root, Id, "translated native spawner"))
            throw new InvalidOperationException("Native spawner component binding changed.");
        return Resolved;
    }

    internal void Bind(Component Native)
    {
        var Resolutions = this.Resolutions ?? throw new InvalidOperationException("Native spawner content is not sealed.");
        var Families = this.Families!;
        for (var i = 0; i < Resolutions.Length; i++)
            if (Resolutions[i] is NativeItemSpawnResolution Item) Item.Prefab();
            else ((NativePropSpawnResolution)Resolutions[i]).ValidateFamily(Families[i]);
        if (Native is PllContentPropSpawner Props)
        {
            var Count = 0; foreach (var Family in Families) Count = checked(Count + Family.Length);
            var Prefabs = new GameObject[Count]; var Offset = 0;
            foreach (var Family in Families) { Family.CopyTo(Prefabs, Offset); Offset += Family.Length; }
            Props.props = Prefabs; Props.SelectPrefab = SelectProp;
            return;
        }
        var Spawner = (Spawner)Native;
        if (Selection.Pool == SpawnPool.None)
        {
            var Entries = new List<SpawnEntry>(Families.Length);
            for (var i = 0; i < Families.Length; i++)
                Entries.Add(new SpawnEntry { prefab = Families[i][0], weight = Selection.Entries[i].Weight });
            Spawner.spawns.items = Entries;
        }
    }

    GameObject SelectProp()
    {
        var Pick = UnityEngine.Random.Range(0, TotalWeight);
        for (var i = 0; i < Selection.Entries.Count; i++)
        {
            Pick -= Selection.Entries[i].Weight;
            if (Pick >= 0) continue;
            var Family = Families![i]; return Family[UnityEngine.Random.Range(0, Family.Length)];
        }
        throw new InvalidOperationException("Native prop weights have no selected entry.");
    }

    internal void Unbind(Component Native)
    {
        if (!Native) return;
        if (Native is PllContentPropSpawner Props)
        {
            Props.SelectPrefab = null; Props.props = Array.Empty<GameObject>(); return;
        }
        var Spawner = (Spawner)Native;
        if (Spawner.TryGetComponent<Peak.SpawnedItemTracker>(out var Tracker)) SpawnTrackerTransaction.RemoveSpawnTracker(Tracker);
        Spawner.spawnedObjectPrefab = null!; Spawner.spawnMode = Spawner.SpawnMode.SpawnPool;
        if (Spawner.spawns)
        {
            Spawner.spawns.items.Clear();
            if (Selection.Kind == PllNativeSpawnerKind.GroundFood && Selection.Pool != SpawnPool.None)
                Spawner.spawns.items.Add(new SpawnEntry { weight = 1 });
        }
    }

    void RequireUnbound()
    {
        var Native = NativeSource;
        if (Native is PllContentPropSpawner Props)
        {
            if (Props.props is not { Length: 0 } || Props.SelectPrefab is not null || Props.currentSpawns != 0
                || Props.transform.childCount != 0) throw new ArgumentException("Content Spawner retained native output before binding.");
            return;
        }
        var Spawner = (Spawner)Native;
        if (Spawner.spawnMode != Spawner.SpawnMode.SpawnPool || Spawner.spawnedObjectPrefab
            || Spawner.spawns.items.Count != (Selection.Kind == PllNativeSpawnerKind.GroundFood && Selection.Pool != SpawnPool.None ? 1 : 0)
            || Spawner.spawns.items.Count == 1 && (Spawner.spawns.items[0].prefab || Spawner.spawns.items[0].weight != 1))
            throw new ArgumentException("Food or Content Spawner retained item bindings before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer, bool External)
    {
        Owner.WriteManifest(Writer); Binding.WriteManifest(Writer); Native.WriteManifest(Writer);
        Selection.WriteManifest(Writer); Writer.Write(Points.Length);
        foreach (var Point in Points) Point.WriteManifest(Writer);
        Writer.Write(UpTowards is not null); UpTowards?.WriteManifest(Writer);
        if (!External) return;
        var Resolutions = this.Resolutions ?? throw new InvalidOperationException("Native spawner content is not sealed.");
        foreach (var Resolution in Resolutions) Resolution.WriteFingerprint(Writer);
        if (Selection.Pool != SpawnPool.None)
        {
            var Fingerprint = PoolFingerprint ?? throw new InvalidOperationException("Native food pool is not sealed.");
            Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint);
        }
    }
}
