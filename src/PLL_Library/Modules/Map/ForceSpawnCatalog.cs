using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class ItemDatabaseCatalog
{
    internal readonly struct Entry
    {
        internal Entry(ushort key, Item item)
        {
            Key = key;
            Item = item;
        }

        internal ushort Key { get; }
        internal Item Item { get; }
    }

    readonly ItemDatabase database;
    readonly Dictionary<ushort, Entry> byId;
    readonly Dictionary<string, Entry[]> byName;

    ItemDatabaseCatalog(
        ItemDatabase database,
        Dictionary<ushort, Entry> byId,
        Dictionary<string, Entry[]> byName)
    {
        this.database = database;
        this.byId = byId;
        this.byName = byName;
    }

    internal static ItemDatabaseCatalog Capture()
    {
        var database = ItemDatabase.Instance;
        if (!database || database.itemLookup is null)
            throw new InvalidOperationException("PEAK's live ItemDatabase catalog is unavailable.");
        var ordered = new List<KeyValuePair<ushort, Item>>(database.itemLookup);
        ordered.Sort((left, right) => left.Key.CompareTo(right.Key));
        var byId = new Dictionary<ushort, Entry>(ordered.Count);
        var grouped = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        foreach (var pair in ordered)
        {
            var item = pair.Value;
            if (!item || !item.gameObject) continue;
            if (item.itemID != pair.Key)
                throw new InvalidOperationException(
                    $"ItemDatabase identity {pair.Key} points to item ID {item.itemID} on '{item.gameObject.name}'.");
            var components = item.gameObject.GetComponentsInChildren<Item>(true);
            if (components.Length != 1 || !NativeFixtures.SameObject(components[0], item))
                throw new InvalidOperationException(
                    $"ItemDatabase identity {pair.Key} has an ambiguous live item-prefab contract.");
            var entry = new Entry(pair.Key, item);
            byId.Add(pair.Key, entry);
            var name = item.gameObject.name;
            if (!grouped.TryGetValue(name, out var matches))
                grouped.Add(name, matches = new List<Entry>());
            matches.Add(entry);
        }
        var byName = new Dictionary<string, Entry[]>(grouped.Count, StringComparer.Ordinal);
        foreach (var pair in grouped) byName.Add(pair.Key, pair.Value.ToArray());
        return new ItemDatabaseCatalog(database, byId, byName);
    }

    internal GameObject Resolve(ushort key, string name, string type, string context)
    {
        if (!byId.TryGetValue(key, out var entry))
            throw new InvalidOperationException($"{context} item '{name}' ({key}) is absent from PEAK's live ItemDatabase.");
        Validate(entry, name, type, context);
        return entry.Item.gameObject;
    }

    internal bool TryGet(string name, out Entry[] entries) => byName.TryGetValue(name, out entries!);

    internal void ProjectItems(List<NativeContentEntry> Entries, HashSet<string> Names)
    {
        foreach (var Entry in byId.Values)
        {
            var Item = Entry.Item;
            var Name = Item.gameObject.name;
            var Type = Item.GetType();
            Names.Add(Name);
            var Projected = new NativeContentEntry
            {
                Kind = NativeContentPickKind.Item,
                Id = Entry.Key.ToString(CultureInfo.InvariantCulture), Name = Name,
                ItemId = Entry.Key, ComponentType = Type.FullName ?? Type.Name,
                ComponentAssembly = Type.Assembly.GetName().Name!,
                CandidateCount = 1,
                ImageAbsence = NativeContentTools.ThumbnailAbsence
            };
            NativeContentTools.CaptureItemIcon(Projected, Item.UIData?.icon);
            Entries.Add(Projected);
        }
    }

    void Validate(Entry entry, string name, string type, string context)
    {
        var item = entry.Item;
        if (!database || !NativeFixtures.SameObject(ItemDatabase.Instance, database)
            || database.itemLookup is null
            || !database.itemLookup.TryGetValue(entry.Key, out var current)
            || !NativeFixtures.SameObject(current, item) || !item || item.itemID != entry.Key
            || !item.gameObject || !string.Equals(item.gameObject.name, name, StringComparison.Ordinal)
            || !string.Equals(item.GetType().FullName ?? item.GetType().Name, type, StringComparison.Ordinal))
            throw new InvalidOperationException($"{context} item '{name}' ({entry.Key}) changed its live ItemDatabase mapping.");
    }
}

internal abstract class ForceSpawnResolution
{
    protected ForceSpawnResolution(string name)
    {
        Name = name;
    }

    internal string Name { get; }
    internal abstract void WriteFingerprint(BinaryWriter writer);
}

internal sealed class ItemForceSpawnResolution : ForceSpawnResolution
{
    readonly ushort key;
    readonly Item item;
    readonly GameObject prefab;
    readonly string type;
    readonly byte[] fingerprint;

    internal ItemForceSpawnResolution(string name, ushort key, Item item) : base(name)
    {
        this.key = key;
        this.item = item;
        prefab = item.gameObject;
        type = item.GetType().FullName ?? item.GetType().Name;
        fingerprint = DeterministicObjectIdentity.HashObjectHierarchy(prefab.transform, "PLL/ForceItemCandidate/v1");
    }

    internal GameObject Prefab()
    {
        var database = ItemDatabase.Instance;
        if (!database || database.itemLookup is null || !database.itemLookup.TryGetValue(key, out var current)
            || !NativeFixtures.SameObject(current, item) || !item || item.itemID != key
            || !NativeFixtures.SameObject(item.gameObject, prefab)
            || !prefab || !string.Equals(prefab.name, Name, StringComparison.Ordinal)
            || !string.Equals(item.GetType().FullName ?? item.GetType().Name, type, StringComparison.Ordinal))
            throw new InvalidOperationException($"Forced item '{Name}' changed its sealed ItemDatabase identity {key}.");
        var items = prefab.GetComponentsInChildren<Item>(true);
        if (items.Length != 1 || !NativeFixtures.SameObject(items[0], item))
            throw new InvalidOperationException($"Forced item '{Name}' changed its live item-prefab contract.");
        var currentFingerprint = DeterministicObjectIdentity.HashObjectHierarchy(
            prefab.transform, "PLL/ForceItemCandidate/v1");
        if (!DeterministicObjectIdentity.SameHash(fingerprint, currentFingerprint))
            throw new InvalidOperationException($"Forced item '{Name}' changed its sealed prefab fingerprint.");
        return prefab;
    }

    internal override void WriteFingerprint(BinaryWriter writer)
    {
        writer.Write(Name); writer.Write((byte)0); writer.Write(key); writer.Write(item.itemID); writer.Write(type);
        writer.Write(fingerprint.Length); writer.Write(fingerprint);
    }
}

internal sealed class PropForceSpawnResolution : ForceSpawnResolution
{
    readonly PropEntry[] props;

    internal PropForceSpawnResolution(string name, PropEntry[] props) : base(name)
    {
        this.props = props;
    }

    internal GameObject[] PropFamily()
    {
        var Result = new GameObject[props.Length];
        for (var Index = 0; Index < props.Length; Index++) Result[Index] = props[Index].Candidate;
        ValidateFamily(Result);
        return Result;
    }

    internal void ValidateFamily(GameObject[] Expected)
    {
        if (Expected.Length != props.Length)
            throw new InvalidOperationException($"Forced native prop '{Name}' changed its sealed family size.");
        for (var Index = 0; Index < props.Length; Index++)
        {
            props[Index].Validate(Name);
            if (!ReferenceEquals(Expected[Index], props[Index].Candidate))
                throw new InvalidOperationException(
                    $"Forced native prop '{Name}' replaced its sealed candidate at index {Index}.");
        }
    }

    internal string StableKey(GameObject candidate)
    {
        var CandidateInstanceId = candidate.GetInstanceID();
        foreach (var value in props)
        {
            if (value.Candidate.GetInstanceID() != CandidateInstanceId) continue;
            value.Validate(Name);
            return value.StableKey;
        }
        throw new InvalidOperationException($"Forced native prop '{Name}' selected a candidate outside its sealed family.");
    }

    internal bool RequiresNativeSource(int SourceInstanceId)
    {
        foreach (var Value in props)
            if (Value.RequiresNativeSource(SourceInstanceId)) return true;
        return false;
    }

    internal override void WriteFingerprint(BinaryWriter writer)
    {
        writer.Write(Name); writer.Write((byte)1);
        writer.Write(props.Length);
        foreach (var value in props) value.WriteFingerprint(writer);
    }

    internal sealed class PropEntry
    {
        readonly string stableKey;
        readonly byte[] fingerprint;
        readonly int[] NativeSourceInstanceIds;

        internal PropEntry(GameObject Candidate, string StableKey, int[] NativeSourceInstanceIds)
        {
            this.Candidate = Candidate;
            this.stableKey = StableKey;
            this.NativeSourceInstanceIds = NativeSourceInstanceIds;
            fingerprint = DeterministicObjectIdentity.HashObjectHierarchy(
                Candidate.transform, "PLL/ForcePropCandidate/v1");
        }

        internal GameObject Candidate { get; }
        internal string StableKey => stableKey;

        internal void Validate(string name)
        {
            if (!Candidate || !string.Equals(Candidate.name, name, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Forced native prop '{name}' changed its sealed candidate '{StableKey}'.");
            ForceSpawnCatalog.ValidateCandidate(Candidate, name, StableKey);
            var current = DeterministicObjectIdentity.HashObjectHierarchy(
                Candidate.transform, "PLL/ForcePropCandidate/v1");
            if (!DeterministicObjectIdentity.SameHash(fingerprint, current))
                throw new InvalidOperationException(
                    $"Forced native prop '{name}' changed its candidate fingerprint at '{StableKey}'.");
        }

        internal bool RequiresNativeSource(int SourceInstanceId)
        {
            foreach (var Value in NativeSourceInstanceIds)
                if (Value == SourceInstanceId) return true;
            return false;
        }

        internal void WriteFingerprint(BinaryWriter writer)
        {
            writer.Write(StableKey); writer.Write(fingerprint.Length); writer.Write(fingerprint);
        }
    }
}

internal static class ForceSpawnCatalog
{
    enum SealPhase
    {
        Inactive,
        BuildingEmpty,
        BuildingContent,
        Sealed
    }

    sealed class PropCandidate
    {
        internal readonly int[] NativeSourceInstanceIds;

        internal PropCandidate(GameObject Candidate, string StableKey, int[] NativeSourceInstanceIds)
        {
            this.Candidate = Candidate;
            this.StableKey = StableKey;
            this.NativeSourceInstanceIds = NativeSourceInstanceIds;
        }

        internal GameObject Candidate { get; }
        internal string StableKey { get; private set; }

        internal void ReplaceSite(string StableKey) => this.StableKey = StableKey;
    }

    static readonly Dictionary<string, ForceSpawnResolution> Resolved = new(StringComparer.Ordinal);
    static readonly List<FinalForceAuthoringContract> SealedContracts = new();
    static ItemDatabaseCatalog? ItemCatalog;
    static Dictionary<string, List<PropCandidate>>? PropCandidates;
    static SealPhase Phase;

    internal static void BeginEmptySeal()
    {
        Clear();
        Phase = SealPhase.BuildingEmpty;
    }

    internal static ItemDatabaseCatalog BeginContentSeal(
        IReadOnlyDictionary<string, LayoutSegmentSource> Sources,
        bool ScanNativeProps)
    {
        if (Sources is null) throw new ArgumentNullException(nameof(Sources));
        Clear();
        try
        {
            var Items = ItemDatabaseCatalog.Capture();
            ItemCatalog = Items;
            PropCandidates = ScanNativeProps
                ? CreateProps(Sources)
                : new Dictionary<string, List<PropCandidate>>(StringComparer.Ordinal);
            Phase = SealPhase.BuildingContent;
            return Items;
        }
        catch
        {
            Clear();
            throw;
        }
    }

    internal static void SealBatch(IReadOnlyList<FinalForceAuthoringContract> contracts)
    {
        if (contracts is null) throw new ArgumentNullException(nameof(contracts));
        if (Phase != SealPhase.BuildingContent)
            throw new InvalidOperationException("The live content force-spawn seal is not open.");
        var items = ItemCatalog!;
        var props = PropCandidates!;
        var sites = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var contract in contracts)
            foreach (var name in contract.ForceNames)
            {
                if (!sites.TryGetValue(name, out var contexts))
                    sites.Add(name, contexts = new List<string>());
                contexts.Add(contract.DescribeForceName(name));
            }
        var orderedNames = new List<string>(sites.Keys);
        orderedNames.Sort(StringComparer.Ordinal);
        foreach (var name in orderedNames)
        {
            if (Resolved.ContainsKey(name)) continue;
            try { Resolved.Add(name, Resolve(name, items, props)); }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"{string.Join("; ", sites[name])} failed live classification. {exception.Message}", exception);
            }
        }
        var sealedCount = 0;
        try
        {
            for (; sealedCount < contracts.Count; sealedCount++) contracts[sealedCount].SealExternal(Resolved);
            for (var i = 0; i < contracts.Count; i++)
                if (!SealedContracts.Contains(contracts[i])) SealedContracts.Add(contracts[i]);
        }
        catch
        {
            for (var i = 0; i < sealedCount; i++) contracts[i].ClearExternal();
            throw;
        }
    }

    internal static void CompleteSeal()
    {
        if (Phase == SealPhase.BuildingEmpty)
        {
            Phase = SealPhase.Sealed;
            return;
        }
        if (Phase != SealPhase.BuildingContent)
            throw new InvalidOperationException("The live force-spawn seal is not open.");
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in SealedContracts)
            foreach (var name in contract.ForceNames) usedNames.Add(name);
        var unusedNames = new List<string>();
        foreach (var name in Resolved.Keys)
            if (!usedNames.Contains(name)) unusedNames.Add(name);
        foreach (var name in unusedNames) Resolved.Remove(name);
        ItemCatalog = null;
        PropCandidates = null;
        Phase = SealPhase.Sealed;
    }

    internal static void ProjectContent(List<NativeContentEntry> Entries)
    {
        if (Phase != SealPhase.BuildingContent)
            throw new InvalidOperationException("Native content export requires the open force-spawn capture.");
        var Names = new HashSet<string>(StringComparer.Ordinal);
        ItemCatalog!.ProjectItems(Entries, Names);
        foreach (var Name in PropCandidates!.Keys) Names.Add(Name);
        foreach (var Name in Names)
        {
            PropCandidates.TryGetValue(Name, out var Family);
            var Entry = new NativeContentEntry
            {
                Kind = NativeContentPickKind.ForceName, Id = Name, Name = Name,
                CandidateCount = Family?.Count ?? 1,
                ImageAbsence = NativeContentTools.ThumbnailAbsence
            };
            Entries.Add(Entry);
            ForceSpawnResolution Resolution;
            try
            {
                Resolution = Resolved.TryGetValue(Name, out var Existing)
                    ? Existing : Resolve(Name, ItemCatalog, PropCandidates);
            }
            catch (InvalidOperationException Failure)
            {
                Entry.SelectionFailure = Failure.Message; continue;
            }
            Entry.Fingerprint = NativeContentTools.Fingerprint(Resolution.WriteFingerprint);
        }
    }

    internal static void UnsealBatch(IReadOnlyList<FinalForceAuthoringContract> contracts)
    {
        if (contracts is null) throw new ArgumentNullException(nameof(contracts));
        foreach (var contract in contracts)
        {
            contract.ClearExternal();
            SealedContracts.Remove(contract);
        }
    }

    internal static void Clear()
    {
        ForceSpawnTarget.ClearCaptures();
        foreach (var contract in SealedContracts) contract.ClearExternal();
        SealedContracts.Clear();
        ItemCatalog = null;
        PropCandidates = null;
        Resolved.Clear();
        Phase = SealPhase.Inactive;
    }

    internal static bool RequiresNativeSource(Transform SourceRoot)
    {
        if (!SourceRoot) throw new ArgumentNullException(nameof(SourceRoot));
        var SourceInstanceId = SourceRoot.GetInstanceID();
        foreach (var Resolution in Resolved.Values)
            if (Resolution is PropForceSpawnResolution Props
                && Props.RequiresNativeSource(SourceInstanceId)) return true;
        return false;
    }

    static Dictionary<string, List<PropCandidate>> CreateProps(
        IReadOnlyDictionary<string, LayoutSegmentSource> Sources)
    {
        var NativeRoots = new Dictionary<int, (Transform Root, string SourcePrefix)>();
        foreach (var Source in Sources.Values)
        {
            if (Source is CustomLayoutSegmentSource) continue;
            var Root = Source.Container;
            if (!Root) throw new InvalidOperationException($"Native source '{Source.Id}' lost its catalog root.");
            var RootId = Root.GetInstanceID();
            var SourcePrefix = Source.Id + ":";
            if (!NativeRoots.TryGetValue(RootId, out var Existing)
                || StringComparer.Ordinal.Compare(SourcePrefix, Existing.SourcePrefix) < 0)
                NativeRoots[RootId] = (Root, SourcePrefix);
        }
        var Candidates = new Dictionary<int, PropCandidate>();
        foreach (var NativeRoot in NativeRoots)
        {
            var Root = NativeRoot.Value.Root;
            foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
            {
                if (!Spawner || Spawner.transform.GetInstanceID() != NativeRoot.Key
                    && !Spawner.transform.IsChildOf(Root)) continue;
                var Values = NativePrefabContracts.EffectiveProps(Spawner);
                if (Values is null) continue;
                var Path = DeterministicObjectIdentity.RelativeComponentPath(Spawner, Root, 0);
                for (var i = 0; i < Values.Length; i++)
                {
                    var Candidate = Values[i];
                    if (!Candidate) continue;
                    var CandidateInstanceId = Candidate.GetInstanceID();
                    var Key = $"{NativeRoot.Value.SourcePrefix}{Path}:{i:X8}";
                    if (!Candidates.TryGetValue(CandidateInstanceId, out var current))
                    {
                        var NativeSourceInstanceIds = new List<int>();
                        foreach (var DependencyRoot in NativeRoots)
                        {
                            if (Candidate.transform.GetInstanceID() != DependencyRoot.Key
                                && !Candidate.transform.IsChildOf(DependencyRoot.Value.Root)) continue;
                            NativeSourceInstanceIds.Add(DependencyRoot.Key);
                        }
                        NativeSourceInstanceIds.Sort();
                        Candidates.Add(CandidateInstanceId, new PropCandidate(
                            Candidate, Key, NativeSourceInstanceIds.ToArray()));
                        continue;
                    }
                    if (StringComparer.Ordinal.Compare(Key, current.StableKey) < 0)
                        current.ReplaceSite(Key);
                }
            }
        }

        var OrderedCandidates = new List<PropCandidate>(Candidates.Values);
        OrderedCandidates.Sort((Left, Right) => StringComparer.Ordinal.Compare(
            Left.StableKey, Right.StableKey));
        var result = new Dictionary<string, List<PropCandidate>>(StringComparer.Ordinal);
        foreach (var Candidate in OrderedCandidates)
        {
            var Name = Candidate.Candidate.name;
            if (!result.TryGetValue(Name, out var family))
            {
                family = new List<PropCandidate>();
                result.Add(Name, family);
            }
            family.Add(Candidate);
        }
        return result;
    }

    static ForceSpawnResolution Resolve(
        string name,
        ItemDatabaseCatalog items,
        IReadOnlyDictionary<string, List<PropCandidate>> props)
    {
        items.TryGet(name, out var itemMatches);
        props.TryGetValue(name, out var propMatches);
        var itemCount = itemMatches?.Length ?? 0;
        var propCount = propMatches?.Count ?? 0;
        if (itemCount > 1)
            throw new InvalidOperationException(
                $"Forced object name '{name}' matches distinct ItemDatabase identities "
                    + $"[{string.Join(", ", Array.ConvertAll(itemMatches!, value => value.Key.ToString(CultureInfo.InvariantCulture)))}] "
                + $"and native candidates [{string.Join(", ", propMatches?.ConvertAll(value => value.StableKey) ?? new List<string>())}] "
                + "and cannot be classified.");
        if (itemCount != 0 && propCount != 0)
            throw new InvalidOperationException(
                $"Forced object name '{name}' is ambiguous between ItemDatabase identity {itemMatches![0].Key} "
                + $"and native candidates [{string.Join(", ", propMatches!.ConvertAll(value => value.StableKey))}].");
        if (itemCount == 0 && propCount == 0)
            throw new InvalidOperationException(
                $"Forced object name '{name}' is absent from the live item and native-prop catalogs.");
        if (itemCount == 1)
        {
            var match = itemMatches![0];
            return new ItemForceSpawnResolution(name, match.Key, match.Item);
        }

        var family = new PropForceSpawnResolution.PropEntry[propCount];
        for (var i = 0; i < family.Length; i++)
        {
            var candidate = propMatches![i];
            ValidateCandidate(candidate.Candidate, name, candidate.StableKey);
            family[i] = new PropForceSpawnResolution.PropEntry(
                candidate.Candidate, candidate.StableKey, candidate.NativeSourceInstanceIds);
        }
        return new PropForceSpawnResolution(name, family);
    }

    internal static void ValidateCandidate(GameObject candidate, string name, string key)
    {
        if (!candidate || !string.Equals(candidate.name, name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Native force candidate '{key}' lost exact name '{name}'.");
        foreach (var transform in candidate.GetComponentsInChildren<Transform>(true))
        {
            if (transform.GetComponents<Peak.SpawnedItemTracker>().Length > 1)
                throw new InvalidOperationException(
                    $"Native force candidate '{key}' contains duplicate SpawnedItemTracker components on '{transform.name}'.");
            foreach (var component in transform.GetComponents<Component>())
                if (!component)
                    throw new InvalidOperationException(
                        $"Native force candidate '{key}' contains a missing script at '{transform.name}'.");
            if (transform.GetComponent<global::Campfire>() || transform.GetComponent<RespawnChest>()
                || transform.GetComponent<SegmentPort>() || transform.GetComponent<CustomSegmentRuntime>()
                || transform.GetComponent<PllBiomePrefabAnchors>() || transform.GetComponent<PLLFinalGeneration>()
                || transform.GetComponent<PLLForceSpawner>())
                throw new InvalidOperationException(
                    $"Native force candidate '{key}' contains a component reserved for an occurrence-owned map role.");
        }
        foreach (var view in candidate.GetComponentsInChildren<PhotonView>(true))
            if (view && view.ViewID != 0)
                throw new InvalidOperationException(
                    $"Native force candidate '{key}' contains an assigned PhotonView.");
    }
}
