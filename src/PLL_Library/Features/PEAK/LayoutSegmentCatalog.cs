using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PLL.Library;

internal sealed class LayoutSegmentCatalog
{
    readonly IReadOnlyDictionary<string, LayoutSegmentSource> SourcesByName;
    readonly IReadOnlyDictionary<string, LayoutSegmentSource> SourcesByStableId;
    readonly IReadOnlyList<LayoutSegmentSource> SelectableSources;
    readonly Dictionary<LayoutSegmentSource, IReadOnlyList<LayoutSegmentSource>> GroupSourcesByOwner;
    readonly Dictionary<LayoutSegmentSource, LayoutSegmentSource> GroupOwnersByPrefix;
    readonly Dictionary<BiomeId, LayoutSegmentSource> GroupOwnersByPrefixBiome;

    LayoutSegmentCatalog(
        NativeSegmentCatalog Native,
        Dictionary<string, LayoutSegmentSource> SourcesByName,
        Dictionary<string, LayoutSegmentSource> SourcesByStableId,
        LayoutSegmentSource[] SelectableSources,
        Dictionary<LayoutSegmentSource, IReadOnlyList<LayoutSegmentSource>> GroupSourcesByOwner,
        Dictionary<LayoutSegmentSource, LayoutSegmentSource> GroupOwnersByPrefix,
        Dictionary<BiomeId, LayoutSegmentSource> GroupOwnersByPrefixBiome)
    {
        this.Native = Native;
        this.SourcesByName = new ReadOnlyDictionary<string, LayoutSegmentSource>(SourcesByName);
        this.SourcesByStableId = new ReadOnlyDictionary<string, LayoutSegmentSource>(SourcesByStableId);
        this.SelectableSources = new ReadOnlyCollection<LayoutSegmentSource>(SelectableSources);
        this.GroupSourcesByOwner = GroupSourcesByOwner;
        this.GroupOwnersByPrefix = GroupOwnersByPrefix;
        this.GroupOwnersByPrefixBiome = GroupOwnersByPrefixBiome;
    }

    internal NativeSegmentCatalog Native { get; }
    internal IEnumerable<LayoutSegmentSource> AllValues => SourcesByName.Values;
    internal IEnumerable<LayoutSegmentSource> SelectableValues => SelectableSources;
    internal bool HasGroups => GroupSourcesByOwner.Count != 0;

    internal bool TryGetName(string Name, out LayoutSegmentSource Source) =>
        SourcesByName.TryGetValue(Name, out Source!);

    internal bool TryGetStableId(string StableId, out LayoutSegmentSource Source) =>
        SourcesByStableId.TryGetValue(StableId, out Source!);

    internal bool TryGetGroupSources(
        LayoutSegmentSource Owner,
        out IReadOnlyList<LayoutSegmentSource> Sources) =>
        GroupSourcesByOwner.TryGetValue(Owner, out Sources!);

    internal bool TryGetGroupOwner(
        LayoutSegmentSource Prefix,
        out LayoutSegmentSource Owner) =>
        GroupOwnersByPrefix.TryGetValue(Prefix, out Owner!);

    internal bool TryGetGroupOwner(BiomeId Prefix, out LayoutSegmentSource Owner) =>
        GroupOwnersByPrefixBiome.TryGetValue(Prefix, out Owner!);

    internal static LayoutSegmentCatalog Create(NativeSegmentCatalog Native)
    {
        if (Native is null) throw new ArgumentNullException(nameof(Native));
        var ByName = new Dictionary<string, LayoutSegmentSource>(StringComparer.Ordinal);
        var ByStableId = new Dictionary<string, LayoutSegmentSource>(StringComparer.Ordinal);
        var CustomSources = new Dictionary<RegisteredSegment, CustomLayoutSegmentSource>();
        foreach (var Source in Native.Sources.Values) Add(Source);
        var Registry = SegmentRegistry.Snapshot();
        foreach (var Registration in Registry.Segments)
        {
            var Source = new CustomLayoutSegmentSource(Registration);
            CustomSources.Add(Registration, Source);
            Add(Source);
        }

        var GroupSourcesByOwner = new Dictionary<LayoutSegmentSource, IReadOnlyList<LayoutSegmentSource>>();
        var GroupOwnersByPrefix = new Dictionary<LayoutSegmentSource, LayoutSegmentSource>();
        var GroupOwnersByPrefixBiome = new Dictionary<BiomeId, LayoutSegmentSource>();
        foreach (var Group in Registry.Groups)
        {
            var Owner = CustomSources[Group.Owner];
            var Sources = new LayoutSegmentSource[Group.Prefixes.Count + 1];
            for (var Index = 0; Index < Group.Prefixes.Count; Index++)
                Sources[Index] = CustomSources[Group.Prefixes[Index]];
            Sources[Sources.Length - 1] = Owner;
            GroupSourcesByOwner.Add(Owner, new ReadOnlyCollection<LayoutSegmentSource>(Sources));
            for (var Index = 0; Index < Sources.Length - 1; Index++)
            {
                var Prefix = Sources[Index];
                GroupOwnersByPrefix.Add(Prefix, Owner);
                GroupOwnersByPrefixBiome.Add(
                    ((CustomLayoutSegmentSource)Prefix).MetadataBiome,
                    Owner);
            }
        }

        var Selectable = new List<LayoutSegmentSource>(ByName.Count - GroupOwnersByPrefix.Count);
        foreach (var Source in ByName.Values)
            if (!GroupOwnersByPrefix.ContainsKey(Source)) Selectable.Add(Source);
        return new LayoutSegmentCatalog(
            Native,
            ByName,
            ByStableId,
            Selectable.ToArray(),
            GroupSourcesByOwner,
            GroupOwnersByPrefix,
            GroupOwnersByPrefixBiome);

        void Add(LayoutSegmentSource Source)
        {
            if (!ByName.TryAdd(Source.Id, Source))
                throw new InvalidOperationException($"Layout segment name '{Source.Id}' is registered more than once.");
            if (!ByStableId.TryAdd(Source.StableId, Source))
                throw new InvalidOperationException(
                    $"Layout segment ID '{Source.StableId}' is registered more than once.");
        }
    }
}
