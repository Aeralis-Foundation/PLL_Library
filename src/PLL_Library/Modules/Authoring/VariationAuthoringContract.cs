using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class VariationAuthoringContract
{
    readonly VariationContract[] Variations;

    VariationAuthoringContract(VariationContract[] Variations) => this.Variations = Variations;

    internal static VariationAuthoringContract Create(
        PllBiomeDefinition Source,
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph) =>
        new(VariationContract.CreateAll(Source, Template, Id, Graph));

    internal void Validate(PllBiomeDefinition Source, Transform Template, BiomeId Id)
    {
        if (Source.VariationDefinitions.Count != Variations.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its variation catalog before sealing.");
        for (var i = 0; i < Variations.Length; i++) Variations[i].Validate(Source.VariationDefinitions[i], Template, Id);
        VariationContract.ValidateHierarchy(Variations, Template, Id, false);
    }

    internal IReadOnlyList<VariationContract> Groups => Variations;

    internal VariationOccurrence Prepare(
        Transform Root, BiomeId Id, Func<string, string, float> EffectiveWeight)
    {
        var Selectors = new List<VariantObjectSelector>();
        var Groups = new VariationOccurrence.Group[Variations.Length];
        for (var i = 0; i < Groups.Length; i++)
        {
            Groups[i] = Variations[i].Prepare(Root, Id, EffectiveWeight, out var Selector);
            if (!Selectors.Contains(Selector)) Selectors.Add(Selector);
        }
        return new VariationOccurrence(Root, Id, Selectors.ToArray(), Groups);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Variations.Length);
        foreach (var Variation in Variations) Variation.WriteManifest(Writer);
    }
}

internal sealed class VariationOccurrence
{
    readonly Transform Root;
    readonly BiomeId BiomeId;
    readonly Group[] Groups;

    internal VariationOccurrence(
        Transform Root, BiomeId BiomeId,
        VariantObjectSelector[] Selectors, Group[] Groups)
    {
        this.Root = Root;
        this.BiomeId = BiomeId;
        this.Selectors = Selectors;
        this.Groups = Groups;
    }

    internal VariantObjectSelector[] Selectors { get; }

    internal VariationSelection[] CaptureSelections()
    {
        var Result = new VariationSelection[Groups.Length];
        for (var i = 0; i < Result.Length; i++) Result[i] = Groups[i].Capture(Root, BiomeId);
        return Result;
    }

    internal sealed class Group
    {
        readonly string Id;
        readonly int NativeGroup;
        readonly VariantObject[] Entries;
        readonly float[] EffectiveWeights;

        internal Group(string Id, int NativeGroup, VariantObject[] Entries, float[] EffectiveWeights)
        {
            this.Id = Id;
            this.NativeGroup = NativeGroup;
            this.Entries = Entries;
            this.EffectiveWeights = EffectiveWeights;
        }

        internal VariationSelection Capture(Transform Root, BiomeId BiomeId)
        {
            GameObject? Selected = null;
            for (var i = 0; i < Entries.Length; i++)
            {
                var Entry = Entries[i];
                if (!Entry || !Entry.enabled || (int)Entry.group != NativeGroup
                    || !Entry.spawnChance.Equals(EffectiveWeights[i]))
                    throw new InvalidOperationException($"Biome '{BiomeId}' variation '{Id}' changed its effective native entry.");
                if (!Entry.gameObject.activeSelf) continue;
                if (EffectiveWeights[i] <= 0f || Selected)
                    throw new InvalidOperationException($"Biome '{BiomeId}' variation '{Id}' selected a disabled entry or more than one entry.");
                Selected = Entry.gameObject;
            }
            if (!Selected)
                throw new InvalidOperationException($"Biome '{BiomeId}' variation '{Id}' selected no entry root.");
            return new VariationSelection(
                Id, DeterministicObjectIdentity.RelativeComponentPath(Selected.transform, Root, 0));
        }
    }
}

internal sealed class VariationContract
{
    readonly ComponentBinding<PllVariationAuthoring> Component;
    readonly ComponentBinding<VariantObjectSelector> Selector;
    readonly ObjectBinding[] Roots;
    readonly ComponentBinding<VariantObject>[] Variants;
    readonly float[] Weights;
    readonly string[] EntryIds;
    readonly int Group;

    internal string Id { get; }
    internal int EntryCount => Roots.Length;
    internal string EntryId(int Index) => EntryIds[Index];
    internal string EntryTitle(int Index) => Roots[Index].Source.name;
    internal float DefaultWeight(int Index) => Weights[Index];

    VariationContract(
        ComponentBinding<PllVariationAuthoring> Component,
        ComponentBinding<VariantObjectSelector> Selector,
        ObjectBinding[] Roots,
        ComponentBinding<VariantObject>[] Variants,
        float[] Weights,
        string[] EntryIds,
        string Id,
        int Group)
    {
        this.Component = Component;
        this.Selector = Selector;
        this.Roots = Roots;
        this.Variants = Variants;
        this.Weights = Weights;
        this.EntryIds = EntryIds;
        this.Id = Id;
        this.Group = Group;
    }

    internal static VariationContract[] CreateAll(
        PllBiomeDefinition Definition,
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph)
    {
        foreach (var Candidate in Graph.Ordered)
            if (Candidate.GetComponentsInChildren<PllVariationAuthoring>(true).Length != 0)
                throw new ArgumentException($"Biome '{Id}' places variation authoring inside a generated candidate.", nameof(Definition));
        var Discovered = Template.GetComponentsInChildren<PllVariationAuthoring>(true);
        if (Discovered.Length != Definition.VariationDefinitions.Count)
            throw new ArgumentException($"Biome '{Id}' has an incomplete staged variation catalog.", nameof(Definition));
        var Result = new VariationContract[Definition.VariationDefinitions.Count];
        var Seen = new HashSet<PllVariationAuthoring>();
        for (var i = 0; i < Result.Length; i++)
        {
            var Source = Definition.VariationDefinitions[i];
            if (!Source || !Seen.Add(Source) || Array.IndexOf(Discovered, Source) < 0)
                throw new ArgumentException($"Biome '{Id}' variation catalog contains a missing, duplicate, or foreign component.", nameof(Definition));
            Result[i] = Create(Template, Id, Source);
        }
        ValidateHierarchy(Result, Template, Id, true);
        return Result;
    }

    internal void Validate(PllVariationAuthoring Expected, Transform Template, BiomeId BiomeId)
    {
        Component.Validate(Template, BiomeId, "variation authoring");
        Selector.Validate(Template, BiomeId, "translated VariantObjectSelector");
        if (!ReferenceEquals(Expected, Component.Source))
            throw new InvalidOperationException($"Biome '{BiomeId}' reordered its variation catalog before sealing.");
        ValidateCore(Template, BiomeId);
    }

    internal VariationOccurrence.Group Prepare(
        Transform RuntimeRoot, BiomeId BiomeId,
        Func<string, string, float> EffectiveWeight,
        out VariantObjectSelector RuntimeSelector)
    {
        RuntimeSelector = ResolveSelector(RuntimeRoot, BiomeId, out var Source);
        var Native = new VariantObject[Roots.Length];
        var Effective = new float[Roots.Length];
        for (var i = 0; i < Roots.Length; i++)
        {
            var RuntimeEntry = Roots[i].Resolve(RuntimeRoot, BiomeId, "variation entry root");
            var RuntimeVariant = Variants[i].Resolve(RuntimeRoot, BiomeId, "translated VariantObject");
            var Entry = Source.Entries[i];
            if (Entry is null || Entry.EntryId != EntryIds[i]
                || Entry.Root != RuntimeEntry || !Entry.Weight.Equals(Weights[i])
                || RuntimeEntry.transform.parent != Source.transform || RuntimeEntry.GetComponents<VariantObject>().Length != 1
                || RuntimeVariant.gameObject != RuntimeEntry || !RuntimeVariant.enabled
                || (int)RuntimeVariant.group != Group || !RuntimeVariant.spawnChance.Equals(Weights[i]))
                throw new InvalidOperationException($"Biome '{BiomeId}' variation '{Id}' lost its sealed runtime entry binding.");
            Native[i] = RuntimeVariant;
            Effective[i] = EffectiveWeight(Id, EntryIds[i]);
        }
        for (var i = 0; i < Native.Length; i++) Native[i].spawnChance = Effective[i];
        return new VariationOccurrence.Group(Id, Group, Native, Effective);
    }

    VariantObjectSelector ResolveSelector(
        Transform RuntimeRoot,
        BiomeId BiomeId,
        out PllVariationAuthoring Source)
    {
        Source = Component.Resolve(RuntimeRoot, BiomeId, "variation authoring");
        var RuntimeSelector = Selector.Resolve(RuntimeRoot, BiomeId, "translated VariantObjectSelector");
        if (!Source.enabled || Source.Id != Id || Source.Entries.Count != Roots.Length)
            throw new InvalidOperationException($"Biome '{BiomeId}' variation '{Id}' changed in its runtime occurrence.");
        var Authored = Source.GetComponents<PllVariationAuthoring>();
        if (Authored.Length <= Group || !ReferenceEquals(Authored[Group], Source)
            || Source.GetComponents<VariantObjectSelector>().Length != 1
            || !ReferenceEquals(Source.GetComponent<VariantObjectSelector>(), RuntimeSelector)
            || !RuntimeSelector.enabled)
            throw new InvalidOperationException($"Biome '{BiomeId}' variation '{Id}' changed in its runtime occurrence.");
        return RuntimeSelector;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Component.WriteManifest(Writer); Selector.WriteManifest(Writer);
        Writer.Write(Id); Writer.Write(Group); Writer.Write(Roots.Length);
        for (var i = 0; i < Roots.Length; i++)
        {
            Roots[i].WriteManifest(Writer); Variants[i].WriteManifest(Writer);
            ManifestEncoding.WriteScalar(Writer, Weights[i]);
            Writer.Write(EntryIds[i]);
        }
    }

    static VariationContract Create(Transform Template, BiomeId BiomeId, PllVariationAuthoring Source)
    {
        var Components = Source.GetComponents<PllVariationAuthoring>();
        var Group = Array.IndexOf(Components, Source);
        var Selectors = Source.GetComponents<VariantObjectSelector>();
        if (!Source.enabled || !PllAuthoringValueContract.IsLocalId(Source.Id) || Group is < 0 or > 3
            || Selectors.Length != 1 || !Selectors[0].enabled
            || Source.Entries.Count < 2)
            throw new ArgumentException($"Biome '{BiomeId}' variation '{Source.name}' has an invalid translated owner.", nameof(Source));
        var Roots = new ObjectBinding[Source.Entries.Count];
        var Variants = new ComponentBinding<VariantObject>[Roots.Length];
        var Weights = new float[Roots.Length];
        var EntryIds = new string[Roots.Length];
        var SeenRoots = new HashSet<GameObject>();
        var SeenIds = new HashSet<string>(StringComparer.Ordinal);
        var Positive = false;
        for (var i = 0; i < Roots.Length; i++)
        {
            var Entry = Source.Entries[i];
            var Root = Entry?.Root;
            var Native = Root ? Root.GetComponents<VariantObject>() : Array.Empty<VariantObject>();
            if (!Root || !SeenRoots.Add(Root) || Root.transform.parent != Source.transform || Root.activeSelf
                || !PllAuthoringValueContract.IsVariationWeight(Entry!.Weight)
                || !PllAuthoringValueContract.IsVariationEntryId(Entry.EntryId)
                || !SeenIds.Add(Entry.EntryId) || Native.Length != 1
                || !Native[0].enabled || (int)Native[0].group != Group || !Native[0].spawnChance.Equals(Entry!.Weight))
                throw new ArgumentException($"Biome '{BiomeId}' variation '{Source.Id}' has an invalid translated entry.", nameof(Source));
            Positive |= Entry.Weight > 0f;
            Roots[i] = ObjectBinding.Create(Template, Root);
            Variants[i] = ComponentBinding<VariantObject>.Create(Template, Native[0]);
            Weights[i] = Entry.Weight;
            EntryIds[i] = Entry.EntryId;
        }
        if (!Positive)
            throw new ArgumentException($"Biome '{BiomeId}' variation '{Source.Id}' has no enabled entry. Set at least one variation weight above zero.", nameof(Source));
        var Contract = new VariationContract(
            ComponentBinding<PllVariationAuthoring>.Create(Template, Source),
            ComponentBinding<VariantObjectSelector>.Create(Template, Selectors[0]),
            Roots, Variants, Weights, EntryIds, Source.Id, Group);
        return Contract;
    }

    void ValidateCore(Transform Template, BiomeId BiomeId)
    {
        var Source = Component.Source;
        if (!Source) throw Failure(BiomeId);
        if (!Source.enabled
            || Source.Id != Id || Source.Entries.Count != Roots.Length)
            throw Failure(BiomeId);
        var Authored = Source.GetComponents<PllVariationAuthoring>();
        if (Authored.Length <= Group || !ReferenceEquals(Authored[Group], Source)
            || Source.GetComponents<VariantObjectSelector>().Length != 1
            || !ReferenceEquals(Source.GetComponent<VariantObjectSelector>(), Selector.Source)
            || !Selector.Source.enabled)
            throw Failure(BiomeId);
        for (var i = 0; i < Roots.Length; i++)
        {
            Roots[i].Validate(Template, BiomeId, "variation entry root");
            Variants[i].Validate(Template, BiomeId, "translated VariantObject");
            var Entry = Source.Entries[i];
            if (Entry is null || Entry.Root != Roots[i].Source || Entry.EntryId != EntryIds[i]
                || !Entry.Weight.Equals(Weights[i]) || Entry.Root.transform.parent != Source.transform
                || Roots[i].Source.activeSelf || Variants[i].Source.gameObject != Roots[i].Source
                || !Variants[i].Source.enabled || Entry.Root.GetComponents<VariantObject>().Length != 1
                || (int)Variants[i].Source.group != Group || !Variants[i].Source.spawnChance.Equals(Weights[i]))
                throw Failure(BiomeId);
        }
    }

    internal static void ValidateHierarchy(
        IReadOnlyList<VariationContract> Contracts,
        Transform Template,
        BiomeId BiomeId,
        bool Argument)
    {
        var Ids = new HashSet<string>(StringComparer.Ordinal);
        var Roots = new HashSet<GameObject>();
        var Owners = new Dictionary<Transform, List<VariationContract>>();
        foreach (var Contract in Contracts)
        {
            var Source = Contract.Component.Source;
            if (!Source || !Ids.Add(Source.Id)) throw HierarchyFailure(BiomeId, Argument);
            if (!Owners.TryGetValue(Source.transform, out var Groups))
                Owners.Add(Source.transform, Groups = new List<VariationContract>());
            Groups.Add(Contract);
            foreach (var Root in Contract.Roots)
                if (!Roots.Add(Root.Source)) throw HierarchyFailure(BiomeId, Argument);
        }

        var OwnerArray = new Transform[Owners.Count];
        Owners.Keys.CopyTo(OwnerArray, 0);
        for (var i = 0; i < OwnerArray.Length; i++)
        {
            var Owner = OwnerArray[i];
            var Groups = Owners[Owner];
            var Authored = Owner.GetComponents<PllVariationAuthoring>();
            var Selectors = Owner.GetComponentsInChildren<VariantObjectSelector>(true);
            var Variants = Owner.GetComponentsInChildren<VariantObject>(true);
            var ExpectedVariants = new HashSet<VariantObject>();
            foreach (var Group in Groups)
                foreach (var Variant in Group.Variants) ExpectedVariants.Add(Variant.Source);
            if (Authored.Length != Groups.Count || Authored.Length is < 1 or > 4
                || Owner.GetComponentsInChildren<PllVariationAuthoring>(true).Length != Authored.Length
                || Selectors.Length != 1 || Selectors[0].transform != Owner || !Selectors[0].enabled
                || Owner.GetComponentsInChildren<VariationSwapper>(true).Length != 0
                || Variants.Length != ExpectedVariants.Count)
                throw HierarchyFailure(BiomeId, Argument);
            foreach (var Variant in Variants)
                if (!ExpectedVariants.Remove(Variant)) throw HierarchyFailure(BiomeId, Argument);
            foreach (var Group in Groups)
                if (Group.Group >= Authored.Length || !ReferenceEquals(Authored[Group.Group], Group.Component.Source))
                    throw HierarchyFailure(BiomeId, Argument);
            for (var j = i + 1; j < OwnerArray.Length; j++)
                if (OwnerArray[j].IsChildOf(Owner) || Owner.IsChildOf(OwnerArray[j]))
                    throw HierarchyFailure(BiomeId, Argument);
        }
    }

    static Exception HierarchyFailure(BiomeId Id, bool Argument) => Argument
        ? new ArgumentException($"Biome '{Id}' has an invalid staged variation hierarchy.", "definition")
        : new InvalidOperationException($"Biome '{Id}' changed its variation hierarchy before sealing.");

    Exception Failure(BiomeId Id) =>
        new InvalidOperationException($"Biome '{Id}' variation '{this.Id}' changed before sealing.");
}
