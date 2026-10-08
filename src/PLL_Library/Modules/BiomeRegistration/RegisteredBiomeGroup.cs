using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace PLL.Library;

internal sealed class RegisteredBiomeGroup
{
    readonly PllBiomeGroupContract Contract;

    internal RegisteredBiomeGroup(
        PllBiomeGroupContract Contract,
        RegisteredSegment Owner,
        RegisteredSegment[] Prefixes)
    {
        ValidateConnectors(Owner, Prefixes);
        this.Contract = Contract;
        this.Owner = Owner;
        this.Prefixes = new ReadOnlyCollection<RegisteredSegment>(Prefixes);
    }

    internal RegisteredSegment Owner { get; }
    internal IReadOnlyList<RegisteredSegment> Prefixes { get; }

    internal void Validate() => Contract.ValidateSource();

    static void ValidateConnectors(RegisteredSegment Owner, RegisteredSegment[] Prefixes)
    {
        var Previous = Prefixes[0];
        for (var Index = 1; Index <= Prefixes.Length; Index++)
        {
            var Next = Index == Prefixes.Length ? Owner : Prefixes[Index];
            PllConnectorContract.Require(
                Owner.BiomeId,
                Previous.Id,
                Previous.Exit.Connector,
                Previous.TemplatePose.rotation * Previous.Exit.LocalPose.rotation,
                Next.Id,
                Next.Entry.Connector,
                Next.TemplatePose.rotation * Next.Entry.LocalPose.rotation);
            Previous = Next;
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Owner.BiomeId);
        Writer.Write(Prefixes.Count);
        foreach (var Prefix in Prefixes) Writer.Write(Prefix.BiomeId);
    }
}

internal sealed class SegmentRegistrySnapshot
{
    internal SegmentRegistrySnapshot(
        IReadOnlyList<RegisteredSegment> Segments,
        IReadOnlyList<RegisteredBiomeGroup> Groups)
    {
        this.Segments = Segments;
        this.Groups = Groups;
    }

    internal IReadOnlyList<RegisteredSegment> Segments { get; }
    internal IReadOnlyList<RegisteredBiomeGroup> Groups { get; }
}
