using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class CustomBiomeDefinition
{
    internal CustomBiomeDefinition(
        string Id,
        string Revision,
        DayNightProfile DayNightProfile,
        PllBiomeBehavior Behavior)
    {
        this.Id = Id ?? throw new ArgumentNullException(nameof(Id));
        this.Revision = Revision ?? throw new ArgumentNullException(nameof(Revision));
        this.DayNightProfile = DayNightProfile ? DayNightProfile : throw new ArgumentNullException(nameof(DayNightProfile));
        this.Behavior = Behavior;
    }

    internal string Id { get; }
    internal string Revision { get; }
    internal DayNightProfile DayNightProfile { get; }
    internal PllBiomeBehavior Behavior { get; }
}

internal sealed class CustomSegmentDefinition
{
    internal CustomSegmentDefinition(
        string Id,
        string Revision,
        string SegmentTypeId,
        string BiomeId,
        GameObject Template,
        CustomSegmentRuntime Runtime,
        PllBiomePrefabAnchors? AuthoringAnchors)
    {
        this.Id = Id ?? throw new ArgumentNullException(nameof(Id));
        this.Revision = Revision ?? throw new ArgumentNullException(nameof(Revision));
        this.SegmentTypeId = SegmentTypeId ?? throw new ArgumentNullException(nameof(SegmentTypeId));
        this.BiomeId = BiomeId ?? throw new ArgumentNullException(nameof(BiomeId));
        this.Template = Template ? Template : throw new ArgumentNullException(nameof(Template));
        this.Runtime = Runtime ? Runtime : throw new ArgumentNullException(nameof(Runtime));
        this.AuthoringAnchors = AuthoringAnchors;
    }

    internal string Id { get; }
    internal string Revision { get; }
    internal string SegmentTypeId { get; }
    internal string BiomeId { get; }
    internal GameObject Template { get; }
    internal CustomSegmentRuntime Runtime { get; }
    internal GameObject SegmentParent => Runtime.SegmentParent;
    internal GameObject SegmentCampfire => Runtime.SegmentCampfire;
    internal FogSphereOrigin FogOrigin => Runtime.FogOrigin;
    internal FogCutoutZone FogCutout => Runtime.FogCutout;
    internal MountainProgressHandler.ProgressPoint ProgressPoint => Runtime.ProgressPoint;
    internal PllBiomePrefabAnchors? AuthoringAnchors { get; }
}

internal sealed class SegmentRegistrationBatch
{
    internal SegmentRegistrationBatch(
        RegisteredBiome[] Biomes,
        RegisteredSegment[] Segments,
        RegisteredBiomeGroup[] Groups)
    {
        this.Biomes = Biomes;
        this.Segments = Segments;
        this.Groups = Groups;
    }

    internal RegisteredBiome[] Biomes { get; }
    internal RegisteredSegment[] Segments { get; }
    internal RegisteredBiomeGroup[] Groups { get; }

    internal void WriteGroupManifest(BinaryWriter Writer) =>
        SegmentRegistry.WriteGroupManifest(Writer, Groups);
}

internal static class SegmentRegistry
{
    internal const string Protocol = PLLLibrary.PluginGuid + "/segment-registry/v18";
    const string StartupProtocol = PLLLibrary.PluginGuid + "/segment-registry/startup/v12";
    const int MaximumTokenLength = 128;
    static readonly Dictionary<string, RegisteredBiome> PendingBiomes = new(StringComparer.Ordinal);
    static readonly Dictionary<string, RegisteredSegment> Pending = new(StringComparer.Ordinal);
    static readonly Dictionary<string, RegisteredBiomeGroup> PendingGroups = new(StringComparer.Ordinal);
    static SegmentRegistrySnapshot? Published;
    static string? PublishedRevision;
    static string? StartupRevision;

    internal static string ManifestRevision =>
        PublishedRevision ?? throw new InvalidOperationException("The segment registry has not been sealed.");

    internal static string StartupManifestRevision =>
        StartupRevision ?? throw new InvalidOperationException("The startup segment registry has not been sealed.");

    internal static SegmentRegistrationBatch RegisterBatch(
        IReadOnlyList<CustomBiomeDefinition> BiomeDefinitions,
        IReadOnlyList<CustomSegmentDefinition> SegmentDefinitions,
        IReadOnlyList<PllBiomeGroupContract> GroupDefinitions)
    {
        if (BiomeDefinitions is null) throw new ArgumentNullException(nameof(BiomeDefinitions));
        if (SegmentDefinitions is null) throw new ArgumentNullException(nameof(SegmentDefinitions));
        if (GroupDefinitions is null) throw new ArgumentNullException(nameof(GroupDefinitions));
        foreach (var Definition in BiomeDefinitions) ValidateBiomeDefinition(Definition);
        foreach (var Definition in SegmentDefinitions) ValidateSegmentDefinition(Definition);

        if (StartupRevision != null) throw new InvalidOperationException("The startup segment registry is sealed.");
        var Identifiers = IdentifierOwners();
        foreach (var Definition in BiomeDefinitions) ClaimIdentifier(Identifiers, Definition.Id, Definition.Id);
        foreach (var Definition in SegmentDefinitions) ClaimIdentifier(Identifiers, Definition.Id, Definition.BiomeId);
        foreach (var Definition in SegmentDefinitions)
            ValidateTypeIdentifier(Identifiers, Definition.SegmentTypeId, Definition.BiomeId);
        var Biomes = new Dictionary<string, RegisteredBiome>(PendingBiomes, StringComparer.Ordinal);
        var NewBiomes = new RegisteredBiome[BiomeDefinitions.Count];
        var BatchBiomes = new Dictionary<string, RegisteredBiome>(StringComparer.Ordinal);
        for (var i = 0; i < BiomeDefinitions.Count; i++)
        {
            var Definition = BiomeDefinitions[i];
            if (Biomes.ContainsKey(Definition.Id))
                throw new InvalidOperationException($"Biome '{Definition.Id}' is already registered.");
            var Biome = RegisteredBiome.Create(Definition);
            Biomes.Add(Definition.Id, Biome);
            BatchBiomes.Add(Definition.Id, Biome);
            NewBiomes[i] = Biome;
        }

        var Ids = new HashSet<string>(Pending.Keys, StringComparer.Ordinal);
        var Snapshots = new RegisteredSegment[SegmentDefinitions.Count];
        var NewGroups = new RegisteredBiomeGroup[GroupDefinitions.Count];
        var FrozenCount = 0;
        try
        {
            for (var i = 0; i < SegmentDefinitions.Count; i++)
            {
                var Definition = SegmentDefinitions[i];
                if (!Ids.Add(Definition.Id))
                    throw new InvalidOperationException($"Segment '{Definition.Id}' is already registered.");
                if (!Biomes.TryGetValue(Definition.BiomeId, out var Biome))
                    throw new InvalidOperationException(
                        $"Biome '{Definition.BiomeId}' must be registered before segment '{Definition.Id}'.");
                var Snapshot = RegisteredSegment.Create(Definition, Biome);
                Snapshot.FreezePorts();
                Snapshots[i] = Snapshot;
                FrozenCount++;
            }

            var BatchSegments = UniqueGroupMemberSegments(Snapshots, GroupDefinitions);
            for (var GroupIndex = 0; GroupIndex < GroupDefinitions.Count; GroupIndex++)
            {
                var Definition = GroupDefinitions[GroupIndex];
                var OwnerId = Definition.Owner.BiomeId.Value;
                if (!BatchBiomes.ContainsKey(OwnerId))
                    throw new InvalidOperationException(
                        $"Biome group owner '{OwnerId}' does not belong to its registration batch.");
                var Owner = BatchSegments[OwnerId];
                var Prefixes = new RegisteredSegment[Definition.Prefixes.Count];
                for (var PrefixIndex = 0; PrefixIndex < Prefixes.Length; PrefixIndex++)
                {
                    var PrefixId = Definition.Prefixes[PrefixIndex].BiomeId.Value;
                    if (!BatchBiomes.ContainsKey(PrefixId))
                        throw new InvalidOperationException(
                            $"Biome group '{OwnerId}' prefix '{PrefixId}' does not belong to its registration batch.");
                    Prefixes[PrefixIndex] = BatchSegments[PrefixId];
                }
                NewGroups[GroupIndex] = new RegisteredBiomeGroup(Definition, Owner, Prefixes);
            }
            foreach (var Biome in NewBiomes) Biome.Validate();
            foreach (var Snapshot in Snapshots) Snapshot.ValidateTemplate();
            foreach (var Biome in NewBiomes) PendingBiomes.Add(Biome.Id, Biome);
            foreach (var Snapshot in Snapshots) Pending.Add(Snapshot.Id, Snapshot);
            foreach (var Group in NewGroups) PendingGroups.Add(Group.Owner.BiomeId, Group);
            return new SegmentRegistrationBatch(NewBiomes, Snapshots, NewGroups);
        }
        catch
        {
            foreach (var Group in NewGroups)
                if (Group is not null && PendingGroups.TryGetValue(Group.Owner.BiomeId, out var Current)
                    && ReferenceEquals(Current, Group)) PendingGroups.Remove(Group.Owner.BiomeId);
            foreach (var Biome in NewBiomes) PendingBiomes.Remove(Biome.Id);
            foreach (var Snapshot in Snapshots)
                if (Snapshot is not null) Pending.Remove(Snapshot.Id);
            for (var i = 0; i < FrozenCount; i++) Snapshots[i].UnfreezePorts();
            throw;
        }
    }

    internal static void UnregisterBatch(SegmentRegistrationBatch Batch)
    {
        if (Batch is null) throw new ArgumentNullException(nameof(Batch));
        if (Published is not null) throw new InvalidOperationException("The segment registry is sealed.");
        foreach (var Biome in Batch.Biomes)
            if (!PendingBiomes.TryGetValue(Biome.Id, out var Current) || !ReferenceEquals(Current, Biome))
                throw new InvalidOperationException($"Biome '{Biome.Id}' is not owned by the registration batch.");
        foreach (var Segment in Batch.Segments)
            if (!Pending.TryGetValue(Segment.Id, out var Current) || !ReferenceEquals(Current, Segment))
                throw new InvalidOperationException($"Segment '{Segment.Id}' is not owned by the registration batch.");
        foreach (var Group in Batch.Groups)
            if (!PendingGroups.TryGetValue(Group.Owner.BiomeId, out var Current) || !ReferenceEquals(Current, Group))
                throw new InvalidOperationException(
                    $"Biome group '{Group.Owner.BiomeId}' is not owned by the registration batch.");
        foreach (var Group in Batch.Groups) PendingGroups.Remove(Group.Owner.BiomeId);
        foreach (var Segment in Batch.Segments)
        {
            Pending.Remove(Segment.Id);
            Segment.UnfreezePorts();
        }
        foreach (var Biome in Batch.Biomes) PendingBiomes.Remove(Biome.Id);
    }

    internal static void ValidateBatch(SegmentRegistrationBatch Batch)
    {
        if (Batch is null) throw new ArgumentNullException(nameof(Batch));
        foreach (var Biome in Batch.Biomes)
        {
            if (!PendingBiomes.TryGetValue(Biome.Id, out var Current) || !ReferenceEquals(Current, Biome))
                throw new InvalidOperationException($"Biome '{Biome.Id}' is not owned by the registration batch.");
            Biome.Validate();
        }
        foreach (var Segment in Batch.Segments)
        {
            if (!Pending.TryGetValue(Segment.Id, out var Current) || !ReferenceEquals(Current, Segment))
                throw new InvalidOperationException($"Segment '{Segment.Id}' is not owned by the registration batch.");
            Segment.ValidateTemplate();
        }
        foreach (var Group in Batch.Groups)
        {
            if (!PendingGroups.TryGetValue(Group.Owner.BiomeId, out var Current) || !ReferenceEquals(Current, Group))
                throw new InvalidOperationException(
                    $"Biome group '{Group.Owner.BiomeId}' is not owned by the registration batch.");
            Group.Validate();
        }
    }

    static Dictionary<string, RegisteredSegment> UniqueGroupMemberSegments(
        IReadOnlyList<RegisteredSegment> Segments,
        IReadOnlyList<PllBiomeGroupContract> Groups)
    {
        var Required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var Group in Groups)
        {
            Required.Add(Group.Owner.BiomeId.Value);
            foreach (var Prefix in Group.Prefixes) Required.Add(Prefix.BiomeId.Value);
        }
        var Result = new Dictionary<string, RegisteredSegment>(Required.Count, StringComparer.Ordinal);
        foreach (var Segment in Segments)
        {
            if (!Required.Contains(Segment.BiomeId)) continue;
            if (!Result.TryAdd(Segment.BiomeId, Segment))
                throw new InvalidOperationException(
                    $"Biome group member '{Segment.BiomeId}' owns more than one registered segment.");
        }
        foreach (var Id in Required)
            if (!Result.ContainsKey(Id))
                throw new InvalidOperationException(
                    $"Biome group member '{Id}' has no registered segment in its content bundle.");
        return Result;
    }

    static Dictionary<string, string> IdentifierOwners()
    {
        var Result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var Biome in PendingBiomes.Values) ClaimIdentifier(Result, Biome.Id, Biome.Id);
        foreach (var Segment in Pending.Values)
            ClaimIdentifier(Result, Segment.Id, Segment.BiomeId);
        return Result;
    }

    static void ClaimIdentifier(Dictionary<string, string> Owners, string Identifier, string BiomeId)
    {
        if (Owners.TryGetValue(Identifier, out var Owner))
        {
            if (!string.Equals(Owner, BiomeId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Identifier '{Identifier}' belongs to both biome '{Owner}' and biome '{BiomeId}'.");
            return;
        }
        foreach (var Segment in Pending.Values)
            if (string.Equals(Segment.SegmentTypeId, Identifier, StringComparison.Ordinal)
                && !string.Equals(Segment.BiomeId, BiomeId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Exact identifier '{Identifier}' conflicts with a segment-type alias owned by biome '{Segment.BiomeId}'.");
        Owners.Add(Identifier, BiomeId);
    }

    static void ValidateTypeIdentifier(Dictionary<string, string> Owners, string Identifier, string BiomeId)
    {
        if (Owners.TryGetValue(Identifier, out var Owner)
            && !string.Equals(Owner, BiomeId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Segment-type alias '{Identifier}' conflicts with an exact identifier owned by biome '{Owner}'.");
    }

    internal static void SealStartup()
    {
        if (StartupRevision is not null) return;
        StartupRevision = CreateManifestRevision(
            StartupProtocol,
            OrderedBiomes(),
            OrderedSegments(),
            OrderedGroups(),
            false,
            Array.Empty<byte[]>());
    }

    internal static void PublishFinal(IReadOnlyList<byte[]> FileFingerprints)
    {
        if (FileFingerprints is null) throw new ArgumentNullException(nameof(FileFingerprints));
        if (Published is not null) throw new InvalidOperationException("The segment registry is already sealed.");
        if (StartupRevision is null) throw new InvalidOperationException("The startup segment registry is not sealed.");
        var Biomes = OrderedBiomes();
        var Items = OrderedSegments();
        var Groups = OrderedGroups();
        var Revision = CreateManifestRevision(
            Protocol, Biomes, Items, Groups, true, FileFingerprints);
        var Snapshot = new SegmentRegistrySnapshot(
            new ReadOnlyCollection<RegisteredSegment>(Items),
            new ReadOnlyCollection<RegisteredBiomeGroup>(Groups));
        PublishedRevision = Revision;
        Published = Snapshot;
    }

    internal static SegmentRegistrySnapshot Snapshot() =>
        Published
        ?? throw new InvalidOperationException(
            "The segment registry has not completed live compatibility sealing.");

    static RegisteredBiome[] OrderedBiomes()
    {
        var Result = new RegisteredBiome[PendingBiomes.Count];
        PendingBiomes.Values.CopyTo(Result, 0);
        Array.Sort(Result, (Left, Right) => StringComparer.Ordinal.Compare(Left.Id, Right.Id));
        return Result;
    }

    static RegisteredSegment[] OrderedSegments()
    {
        var Result = new RegisteredSegment[Pending.Count];
        Pending.Values.CopyTo(Result, 0);
        Array.Sort(Result, (Left, Right) => StringComparer.Ordinal.Compare(Left.Id, Right.Id));
        return Result;
    }

    static RegisteredBiomeGroup[] OrderedGroups()
    {
        var Result = new RegisteredBiomeGroup[PendingGroups.Count];
        PendingGroups.Values.CopyTo(Result, 0);
        OrderGroups(Result);
        return Result;
    }

    static void OrderGroups(RegisteredBiomeGroup[] Groups) =>
        Array.Sort(
            Groups,
            (Left, Right) => StringComparer.Ordinal.Compare(
                Left.Owner.BiomeId, Right.Owner.BiomeId));

    internal static void ValidateToken(string Value, string Parameter)
    {
        if (Value == null) throw new ArgumentNullException(Parameter);
        if (Value.Length == 0 || Value.Length > MaximumTokenLength)
            throw new ArgumentException($"The value must contain 1 to {MaximumTokenLength} characters.", Parameter);
        for (var i = 0; i < Value.Length; i++)
        {
            if (char.IsControl(Value[i]) || char.IsWhiteSpace(Value[i]))
                throw new ArgumentException("The value cannot contain whitespace or control characters.", Parameter);
            if (char.IsHighSurrogate(Value[i]))
            {
                if (++i >= Value.Length || !char.IsLowSurrogate(Value[i]))
                    throw new ArgumentException("The value contains malformed UTF-16.", Parameter);
            }
            else if (char.IsLowSurrogate(Value[i]))
                throw new ArgumentException("The value contains malformed UTF-16.", Parameter);
        }
    }

    static void ValidateQualifiedId(string Id, string Parameter)
    {
        if (!SegmentId.TryParse(Id, out _))
            throw new ArgumentException("A stable ID must be a canonical lowercase qualified path.", Parameter);
    }

    static void ValidateBiomeId(string Id, string Parameter)
    {
        if (!BiomeId.TryParse(Id, out _))
            throw new ArgumentException("A biome ID must be a canonical lowercase qualified ID.", Parameter);
    }

    static void ValidateBiomeDefinition(CustomBiomeDefinition Definition)
    {
        if (Definition == null) throw new ArgumentNullException(nameof(Definition));
        ValidateBiomeId(Definition.Id, nameof(Definition.Id));
        RejectReservedLayoutIdentity(Definition.Id, nameof(Definition.Id));
        ValidateToken(Definition.Revision, nameof(Definition.Revision));
        if (!Definition.DayNightProfile)
            throw new ArgumentException("The custom biome day/night profile was destroyed.", nameof(Definition));
    }
    static void ValidateSegmentDefinition(CustomSegmentDefinition Definition)
    {
        if (Definition == null) throw new ArgumentNullException(nameof(Definition));
        ValidateQualifiedId(Definition.Id, nameof(Definition.Id));
        if (SegmentId.IsNative(new SegmentId(Definition.Id)))
            throw new ArgumentException(
                $"Segment ID '{Definition.Id}' is reserved for a native PEAK segment.", nameof(Definition.Id));
        RejectReservedLayoutIdentity(Definition.Id, nameof(Definition.Id));
        ValidateQualifiedId(Definition.SegmentTypeId, nameof(Definition.SegmentTypeId));
        RejectReservedLayoutIdentity(Definition.SegmentTypeId, nameof(Definition.SegmentTypeId));
        ValidateBiomeId(Definition.BiomeId, nameof(Definition.BiomeId));
        RejectReservedLayoutIdentity(Definition.BiomeId, nameof(Definition.BiomeId));
        ValidateToken(Definition.Revision, nameof(Definition.Revision));
        if (!Definition.Template)
            throw new ArgumentException("The custom segment template was destroyed.", nameof(Definition));
        if (!Definition.Runtime)
            throw new ArgumentException("The custom segment runtime was destroyed.", nameof(Definition));
        if (Definition.AuthoringAnchors is not null && !Definition.AuthoringAnchors)
            throw new ArgumentException("The custom segment authoring anchors were destroyed.", nameof(Definition));
    }

    static void RejectReservedLayoutIdentity(string Value, string Parameter)
    {
        if (LayoutContract.IsPeakToken(Value))
            throw new ArgumentException("Peak's structural identity is reserved.", Parameter);
        if (LayoutContract.IsCalderaToken(Value))
            throw new ArgumentException("Caldera's internal composite identity is reserved.", Parameter);
        if (LayoutContract.IsGloomToken(Value))
            throw new ArgumentException("Gloom's internal composite identity is reserved.", Parameter);
    }

    internal static void WriteGroupManifest(
        BinaryWriter Writer,
        IReadOnlyList<RegisteredBiomeGroup> Groups)
    {
        var OrderedGroups = new RegisteredBiomeGroup[Groups.Count];
        for (var Index = 0; Index < OrderedGroups.Length; Index++)
            OrderedGroups[Index] = Groups[Index];
        OrderGroups(OrderedGroups);
        Writer.Write(OrderedGroups.Length);
        foreach (var Group in OrderedGroups) Group.WriteManifest(Writer);
    }

    static string CreateManifestRevision(
        string Protocol,
        RegisteredBiome[] Biomes,
        RegisteredSegment[] Items,
        RegisteredBiomeGroup[] Groups,
        bool External,
        IReadOnlyList<byte[]> FileFingerprints)
    {
        return ManifestEncoding.CreateRevision(Writer =>
        {
            Writer.Write(Protocol);
            Writer.Write(Biomes.Length);
            foreach (var Biome in Biomes) Biome.WriteManifest(Writer);
            Writer.Write(Items.Length);
            foreach (var Item in Items) Item.WriteManifest(Writer);
            Writer.Write(Groups.Length);
            foreach (var Group in Groups) Group.WriteManifest(Writer);
            if (External)
            {
                BiomeAuthoringCatalog.WriteManifest(Writer);
                Writer.Write(Items.Length);
                foreach (var Item in Items) Item.FinalForce.WriteExternalFingerprint(Writer);
                Writer.Write(FileFingerprints.Count);
                foreach (var Fingerprint in FileFingerprints)
                {
                    if (Fingerprint is null) throw new ArgumentException("A file fingerprint is missing.", nameof(FileFingerprints));
                    Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint);
                }
            }
            else BiomeAuthoringCatalog.WriteSourceManifest(Writer);
        });
    }
}

internal sealed class RegisteredBiome
{
    readonly CustomBiomeDefinition Definition;

    RegisteredBiome(CustomBiomeDefinition Definition)
    {
        this.Definition = Definition;
        Id = Definition.Id;
        Revision = Definition.Revision;
        DayNightProfile = Definition.DayNightProfile;
        Behavior = Definition.Behavior;
    }

    internal string Id { get; }
    internal string Revision { get; }
    internal DayNightProfile DayNightProfile { get; }
    internal PllBiomeBehavior Behavior { get; }

    internal static RegisteredBiome Create(CustomBiomeDefinition Definition) => new(Definition);

    internal void Validate()
    {
        if (!Definition.DayNightProfile || Definition.DayNightProfile != DayNightProfile
            || Definition.Id != Id || Definition.Revision != Revision
            || Definition.Behavior != Behavior)
            throw new InvalidOperationException($"Registered biome '{Id}' changed before the registry was sealed.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Id); Writer.Write(Revision); Writer.Write((byte)Behavior);
    }
}
