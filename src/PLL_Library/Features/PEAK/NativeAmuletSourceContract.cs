using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeAmuletSourceContract
{
    const int AmuletTypeCount = 4;
    const int FingerprintVersion = 1;

    readonly int[] SemanticTypes;
    readonly GameObject[] CandidateValues;
    readonly ushort[] ItemIds;
    readonly string[] ItemNames;

    NativeAmuletSourceContract(Peak.PropSpawner_AmuletStatues Source, Transform Container,
        int[] SemanticTypes, ushort[] ItemIds, string[] ItemNames)
    {
        this.Source = Source;
        Category = Source.statueIndex;
        this.SemanticTypes = SemanticTypes;
        CandidateValues = (GameObject[])Source.props.Clone();
        this.ItemIds = ItemIds; this.ItemNames = ItemNames;
        SourceBindings = Array.AsReadOnly(new[]
        {
            "native:" + DeterministicObjectIdentity.RelativeComponentStructuralPath(Source, Container)
        });
    }

    internal Peak.PropSpawner_AmuletStatues Source { get; }
    internal int Category { get; }
    internal int CoverageBit => 1 << Category;
    internal int CandidateCount => CandidateValues.Length;
    internal GameObject Candidate(int Index) => CandidateValues[Index];
    internal System.Collections.Generic.IReadOnlyList<string> SourceBindings { get; }

    internal Peak.PropSpawner_AmuletStatues PlacedGenerator(Transform Container) =>
        TransformOwnership.Contains(Container, Source.transform) ? Source
            : Container.GetComponentInChildren<Peak.PropSpawner_AmuletStatues>(true)
                ?? throw new InvalidOperationException("A placed native segment lost its sealed amulet generator.");

    internal static NativeAmuletSourceContract Capture(Transform Container)
    {
        if (!Container) throw new ArgumentNullException(nameof(Container));
        var Spawners = Container.GetComponentsInChildren<Peak.PropSpawner_AmuletStatues>(true);
        if (Spawners.Length != 1 || !Spawners[0]
            || Spawners[0].GetType() != typeof(Peak.PropSpawner_AmuletStatues))
            throw new InvalidOperationException(
                "A native segment container must own exactly one PEAK amulet-statue spawner.");
        var Spawner = Spawners[0];
        if (Spawner.statueIndex is < 0 or >= AmuletTypeCount || Spawner.props is not { Length: AmuletTypeCount })
            throw new InvalidOperationException(
                "PEAK's native amulet-statue spawner has an invalid category or prop catalog.");

        var SemanticTypes = new int[AmuletTypeCount];
        var ItemIds = new ushort[AmuletTypeCount];
        var ItemNames = new string[AmuletTypeCount];
        var Coverage = 0;
        for (var i = 0; i < SemanticTypes.Length; i++)
        {
            var Prop = Spawner.props[i];
            var FakeItems = Prop ? Prop.GetComponentsInChildren<FakeItem>(true) : Array.Empty<FakeItem>();
            if (FakeItems.Length != 1 || !FakeItems[0] || !FakeItems[0].realItemPrefab)
                throw new InvalidOperationException(
                    "A native amulet-statue prop must own exactly one real amulet item binding.");
            var Amulets = FakeItems[0].realItemPrefab.GetComponents<Peak.AmuletBase>();
            if (Amulets.Length != 1 || !Amulets[0]
                || Amulets[0].amuletIndex is < 0 or >= AmuletTypeCount
                || (Coverage & 1 << Amulets[0].amuletIndex) != 0)
                throw new InvalidOperationException(
                    "PEAK's native amulet-statue prop catalog is not a four-type semantic bijection.");
            SemanticTypes[i] = Amulets[0].amuletIndex;
            ItemIds[i] = FakeItems[0].realItemPrefab.itemID;
            ItemNames[i] = FakeItems[0].itemName ?? string.Empty;
            Coverage |= 1 << SemanticTypes[i];
        }
        return new NativeAmuletSourceContract(Spawner, Container, SemanticTypes, ItemIds, ItemNames);
    }

    internal bool HasSameSemanticBijection(NativeAmuletSourceContract Other)
    {
        if (Other is null) throw new ArgumentNullException(nameof(Other));
        for (var i = 0; i < SemanticTypes.Length; i++)
            if (SemanticTypes[i] != Other.SemanticTypes[i]) return false;
        return true;
    }

    internal void WriteFingerprint(BinaryWriter Writer)
    {
        if (Writer is null) throw new ArgumentNullException(nameof(Writer));
        Writer.Write(FingerprintVersion);
        Writer.Write(Category);
        Writer.Write(SemanticTypes.Length);
        foreach (var Type in SemanticTypes) Writer.Write(Type);
    }

    internal void WriteFactoryFingerprint(BinaryWriter Writer)
    {
        WriteFingerprint(Writer);
        for (var Index = 0; Index < ItemIds.Length; Index++)
        {
            Writer.Write(ItemIds[Index]); Writer.Write(ItemNames[Index]);
        }
    }
}
