using System;

namespace PLL.Library;

internal readonly struct ViewBatchEnvelope
{
    internal enum ParseFailure
    {
        None,
        Shape,
        GenerationEpoch,
        Protocol,
        LayoutRevision,
        Seed,
        ViewIds,
        ViewPathHash,
        ReplayCatalogFingerprint,
        FakeIds,
        FakePathHash,
        ReplaceFakeItems,
        GeneratedNetworkContractHash,
        NextFakeId
    }

    internal const int FieldCount = 12;
    const int ProtocolRevision = 8;

    internal ViewBatchEnvelope(
        string EpochValue,
        int RevisionValue,
        int SeedValue,
        byte[] ViewHash,
        int[] ViewIdentifiers,
        byte[] ReplayFingerprint,
        byte[] FakeHash,
        int[] FakeIdentifiers,
        bool ReplaceFakeRegistry,
        byte[] GeneratedNetworkContractHashValue,
        int NextFakeIdentifier)
    {
        GenerationEpoch = EpochValue;
        LayoutRevision = RevisionValue;
        Seed = SeedValue;
        ViewPathHash = ViewHash;
        ViewIds = ViewIdentifiers;
        ReplayCatalogFingerprint = ReplayFingerprint;
        FakePathHash = FakeHash;
        FakeIds = FakeIdentifiers;
        ReplaceFakeItems = ReplaceFakeRegistry;
        GeneratedNetworkContractHash = GeneratedNetworkContractHashValue;
        NextFakeId = NextFakeIdentifier;
    }

    internal string GenerationEpoch { get; }
    internal int LayoutRevision { get; }
    internal int Seed { get; }
    internal byte[] ViewPathHash { get; }
    internal int[] ViewIds { get; }
    internal byte[] ReplayCatalogFingerprint { get; }
    internal byte[] FakePathHash { get; }
    internal int[] FakeIds { get; }
    internal bool ReplaceFakeItems { get; }
    internal byte[] GeneratedNetworkContractHash { get; }
    internal int NextFakeId { get; }

    internal object[] ToPropertyValue() =>
    [
        GenerationEpoch,
        ProtocolRevision,
        LayoutRevision,
        Seed,
        ViewPathHash,
        ViewIds,
        ReplayCatalogFingerprint,
        FakePathHash,
        FakeIds,
        ReplaceFakeItems,
        GeneratedNetworkContractHash,
        NextFakeId
    ];

    internal static bool TryParse(
        object? Value,
        string EpochValue,
        int RevisionValue,
        int SeedValue,
        out ViewBatchEnvelope Envelope,
        out ParseFailure Failure)
    {
        Envelope = default;
        if (Value is not object[] { Length: FieldCount } Fields)
            return Fail(ParseFailure.Shape, out Failure);
        if (Fields[0] is not string Epoch
            || !string.Equals(Epoch, EpochValue, StringComparison.Ordinal))
            return Fail(ParseFailure.GenerationEpoch, out Failure);
        if (Fields[1] is not int Protocol || Protocol != ProtocolRevision)
            return Fail(ParseFailure.Protocol, out Failure);
        if (Fields[2] is not int Revision || Revision != RevisionValue)
            return Fail(ParseFailure.LayoutRevision, out Failure);
        if (Fields[3] is not int BatchSeed || BatchSeed != SeedValue)
            return Fail(ParseFailure.Seed, out Failure);
        if (Fields[4] is not byte[] { Length: 32 } ViewHash)
            return Fail(ParseFailure.ViewPathHash, out Failure);
        if (Fields[5] is not int[] ViewIdentifiers)
            return Fail(ParseFailure.ViewIds, out Failure);
        if (Fields[6] is not byte[] { Length: 32 } ReplayFingerprint)
            return Fail(ParseFailure.ReplayCatalogFingerprint, out Failure);
        if (Fields[7] is not byte[] { Length: 32 } FakeHash)
            return Fail(ParseFailure.FakePathHash, out Failure);
        if (Fields[8] is not int[] FakeIdentifiers)
            return Fail(ParseFailure.FakeIds, out Failure);
        if (Fields[9] is not bool ReplaceFakeRegistry)
            return Fail(ParseFailure.ReplaceFakeItems, out Failure);
        if (Fields[10] is not byte[] { Length: 32 } GeneratedNetworkContractHashValue)
            return Fail(ParseFailure.GeneratedNetworkContractHash, out Failure);
        if (Fields[11] is not int NextFakeIdentifier || NextFakeIdentifier < 0)
            return Fail(ParseFailure.NextFakeId, out Failure);

        Envelope = new ViewBatchEnvelope(
            Epoch, Revision, BatchSeed, ViewHash, ViewIdentifiers,
            ReplayFingerprint, FakeHash, FakeIdentifiers,
            ReplaceFakeRegistry, GeneratedNetworkContractHashValue,
            NextFakeIdentifier);
        Failure = ParseFailure.None;
        return true;
    }

    static bool Fail(ParseFailure Value, out ParseFailure Failure)
    {
        Failure = Value;
        return false;
    }
}
