#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace PLL.Library
{
    internal sealed class PllBiomeGroupContract
    {
        readonly GameObject Root;
        readonly PllBiomeGroupAuthoring? SourceCarrier;
        readonly PllBiomeDefinition[] PrefixValues;
        readonly IReadOnlyList<PllBiomeDefinition> ReadOnlyPrefixes;

        PllBiomeGroupContract(
            PllBiomeDefinition Owner, GameObject Root, PllBiomeGroupAuthoring? Carrier,
            PllBiomeDefinition[] Prefixes)
        {
            this.Owner = Owner;
            this.Root = Root;
            SourceCarrier = Carrier;
            PrefixValues = Prefixes;
            ReadOnlyPrefixes = new ReadOnlyCollection<PllBiomeDefinition>(
                PrefixValues);
        }

        internal PllBiomeDefinition Owner { get; }
        internal PllBiomeGroupAuthoring Carrier => SourceCarrier
            ?? throw new InvalidOperationException("A proposed biome group has no source carrier.");
        internal IReadOnlyList<PllBiomeDefinition> Prefixes =>
            ReadOnlyPrefixes;

        internal static PllBiomeGroupContract? Capture(
            PllBiomeDefinition Owner)
        {
            if (!Owner || !Owner.RootPrefab)
                throw new InvalidOperationException(
                    "Biome group capture requires a definition with a root prefab.");
            var Root = Owner.RootPrefab;
            var Carriers = Root.GetComponentsInChildren<
                PllBiomeGroupAuthoring>(true);
            if (Carriers.Length == 0) return null;
            var Carrier = RequireCarrier(Owner, Root, Carriers);
            return new PllBiomeGroupContract(
                Owner, Root, Carrier, RequirePrefixes(Owner, Carrier.Prefixes));
        }

        internal static PllBiomeGroupContract Prepare(
            PllBiomeDefinition Owner, GameObject Root,
            IReadOnlyList<PllBiomeDefinition> Prefixes)
        {
            var Carriers = Root.GetComponentsInChildren<PllBiomeGroupAuthoring>(true);
            if (Carriers.Length != 0) RequireCarrier(Owner, Root, Carriers);
            else RequireOwnerLink(Owner, Root);
            return new PllBiomeGroupContract(
                Owner, Root, null, RequirePrefixes(Owner, Prefixes));
        }

        static PllBiomeDefinition[] RequirePrefixes(
            PllBiomeDefinition Owner, IReadOnlyList<PllBiomeDefinition> Values)
        {
            if (Values.Count == 0)
                throw new InvalidOperationException(
                    "Biome group '" + Owner.BiomeId
                    + "' must contain at least one prefix.");
            var SegmentCount = (long)Values.Count + 1;
            if (SegmentCount > PllLayoutCapacityContract.MaximumOrdinarySegments)
                throw new InvalidOperationException(
                    "Biome group '" + Owner.BiomeId + "' contains "
                    + SegmentCount + " ordinary segments; the maximum is "
                    + PllLayoutCapacityContract.MaximumOrdinarySegments + ".");
            var Prefixes = new PllBiomeDefinition[Values.Count];
            var Seen = new HashSet<int>();
            for (var Index = 0; Index < Prefixes.Length; Index++)
            {
                var Prefix = Values[Index];
                if (!Prefix)
                    throw new InvalidOperationException(
                        "Biome group '" + Owner.BiomeId + "' prefix "
                        + Index + " is missing.");
                if (ReferenceEquals(Prefix, Owner))
                    throw new InvalidOperationException(
                        "Biome group '" + Owner.BiomeId
                        + "' cannot contain its owner as a prefix.");
                if (!Seen.Add(Prefix.GetInstanceID()))
                    throw new InvalidOperationException(
                        "Biome group '" + Owner.BiomeId
                        + "' contains duplicate prefix '" + Prefix.BiomeId
                        + "'.");
                Prefixes[Index] = Prefix;
            }
            return Prefixes;
        }

        internal static PllBiomeGroupContract[] ValidateScope(
            IReadOnlyList<PllBiomeDefinition> Definitions,
            IReadOnlyList<GameObject> RootAssets,
            IReadOnlyList<PllBiomeGroupContract> Groups,
            PllBiomeGroupContract? EditedGroup = null)
        {
            var DefinitionsByObject = new Dictionary<int,
                PllBiomeDefinition>(Definitions.Count);
            var DefinitionsByRoot = new Dictionary<int,
                PllBiomeDefinition>(Definitions.Count);
            foreach (var Definition in Definitions)
            {
                if (!Definition || !Definition.RootPrefab
                    || !DefinitionsByObject.TryAdd(
                        Definition.GetInstanceID(), Definition))
                    throw new InvalidOperationException(
                        "Biome group scope contains duplicate or missing definition ownership.");
            }

            var GroupsByOwner = new Dictionary<int,
                PllBiomeGroupContract>(Groups.Count);
            foreach (var Group in Groups)
            {
                if (Group == null || !Group.Owner
                    || !DefinitionsByObject.TryGetValue(
                        Group.Owner.GetInstanceID(), out var ScopedOwner)
                    || !ReferenceEquals(ScopedOwner, Group.Owner)
                    || !GroupsByOwner.TryAdd(
                        Group.Owner.GetInstanceID(), Group))
                    throw new InvalidOperationException(
                        "Biome group scope contains a duplicate or foreign owner.");
            }

            foreach (var Definition in Definitions)
            {
                var Root = EditedGroup != null && ReferenceEquals(EditedGroup.Owner, Definition)
                    ? EditedGroup.Root : Definition.RootPrefab;
                if (!DefinitionsByRoot.TryAdd(Root.GetInstanceID(), Definition))
                    throw new InvalidOperationException(
                        "Biome group scope contains duplicate root ownership.");
            }

            var SeenRoots = new HashSet<int>();
            foreach (var Root in RootAssets)
            {
                if (!Root)
                    throw new InvalidOperationException(
                        "Biome group scope contains a missing root asset.");
                if (!SeenRoots.Add(Root.GetInstanceID())) continue;
                var Carriers = Root.GetComponentsInChildren<
                    PllBiomeGroupAuthoring>(true);
                if (!DefinitionsByRoot.TryGetValue(
                        Root.GetInstanceID(), out var Owner))
                {
                    if (Carriers.Length != 0)
                        throw new InvalidOperationException(
                            "Non-biome root '" + Root.name
                            + "' cannot contain PllBiomeGroupAuthoring.");
                    continue;
                }
                GroupsByOwner.TryGetValue(
                    Owner.GetInstanceID(), out var Expected);
                if (Expected == null && Carriers.Length == 0) continue;
                if (Expected == null)
                    throw new InvalidOperationException(
                        "Biome group '" + Owner.BiomeId
                        + "' changed its owner-root carrier binding.");
                if (!ReferenceEquals(Expected, EditedGroup))
                    Expected.RequireCurrent(Root, Carriers);
            }
            foreach (var Definition in Definitions)
                if (!SeenRoots.Contains(
                        (EditedGroup != null && ReferenceEquals(EditedGroup.Owner, Definition)
                            ? EditedGroup.Root : Definition.RootPrefab).GetInstanceID()))
                    throw new InvalidOperationException(
                        "Biome group scope omits root prefab for biome '"
                        + Definition.BiomeId + "'.");

            var Participation = new Dictionary<int,
                PllBiomeGroupContract>();
            var PrefixIds = new HashSet<BiomeId>();
            foreach (var Group in Groups)
            {
                Claim(Group.Owner, Group);
                foreach (var Prefix in Group.PrefixValues)
                {
                    if (!DefinitionsByObject.TryGetValue(
                            Prefix.GetInstanceID(), out var ScopedPrefix)
                        || !ReferenceEquals(ScopedPrefix, Prefix))
                        throw new InvalidOperationException(
                            "Biome group '" + Group.Owner.BiomeId
                            + "' prefix '" + Prefix.BiomeId
                            + "' is outside the bundle definition scope.");
                    Claim(Prefix, Group);
                    RequireNeutralPrefix(Prefix);
                    PrefixIds.Add(Prefix.BiomeId);
                }
            }
            foreach (var Definition in Definitions)
            {
                RejectPrefixTargets(
                    Definition, Definition.Favorites, PrefixIds,
                    nameof(PllBiomeDefinition.Favorites));
                RejectPrefixTargets(
                    Definition, Definition.Overwrites, PrefixIds,
                    nameof(PllBiomeDefinition.Overwrites));
                RejectPrefixTargets(
                    Definition, Definition.Before, PrefixIds,
                    nameof(PllBiomeDefinition.Before));
                RejectPrefixTargets(
                    Definition, Definition.After, PrefixIds,
                    nameof(PllBiomeDefinition.After));
            }

            var Result = new PllBiomeGroupContract[Groups.Count];
            for (var Index = 0; Index < Result.Length; Index++)
                Result[Index] = Groups[Index];
            Array.Sort(Result, (Left, Right) =>
                StringComparer.Ordinal.Compare(
                    Left.Owner.BiomeId.Value,
                    Right.Owner.BiomeId.Value));
            return Result;

            void Claim(
                PllBiomeDefinition Member, PllBiomeGroupContract Group)
            {
                if (Participation.TryAdd(Member.GetInstanceID(), Group))
                    return;
                throw new InvalidOperationException(
                    "Biome '" + Member.BiomeId
                    + "' participates in both group '"
                    + Participation[Member.GetInstanceID()].Owner.BiomeId
                    + "' and group '" + Group.Owner.BiomeId + "'.");
            }
        }

        internal static void RequireNoCarrier(
            GameObject Root, string Context)
        {
            if (Root.GetComponentsInChildren<
                    PllBiomeGroupAuthoring>(true).Length != 0)
                throw new InvalidOperationException(
                    Context + " cannot contain PllBiomeGroupAuthoring.");
        }

        internal void ValidateSource()
        {
            var Root = Owner.RootPrefab;
            if (!Root)
                throw new InvalidOperationException(
                    "Biome group '" + Owner.BiomeId
                    + "' lost its owner root before sealing.");
            RequireCurrent(
                Root, Root.GetComponentsInChildren<
                    PllBiomeGroupAuthoring>(true));
        }

        void RequireCurrent(
            GameObject Root, PllBiomeGroupAuthoring[] Carriers)
        {
            var Current = RequireCarrier(Owner, Root, Carriers);
            var Values = Current.Prefixes;
            if (!ReferenceEquals(Current, Carrier)
                || Values.Count != PrefixValues.Length)
                throw new InvalidOperationException(
                    "Biome group '" + Owner.BiomeId
                    + "' changed before sealing.");
            for (var Index = 0; Index < PrefixValues.Length; Index++)
                if (!Values[Index]
                    || !ReferenceEquals(Values[Index], PrefixValues[Index]))
                    throw new InvalidOperationException(
                        "Biome group '" + Owner.BiomeId + "' prefix "
                        + Index + " changed before sealing.");
        }

        static PllBiomeGroupAuthoring RequireCarrier(
            PllBiomeDefinition Owner, GameObject Root,
            PllBiomeGroupAuthoring[] Carriers)
        {
            if (Carriers.Length != 1 || !Carriers[0]
                || !Carriers[0].enabled
                || Carriers[0].gameObject != Root)
                throw new InvalidOperationException(
                    "Biome '" + Owner.BiomeId
                    + "' must own one enabled direct PllBiomeGroupAuthoring.");
            RequireOwnerLink(Owner, Root);
            return Carriers[0];
        }

        static void RequireOwnerLink(PllBiomeDefinition Owner, GameObject Root)
        {
            var Links = Root.GetComponents<PllBiomeRoot>();
            if (Links.Length != 1
                || !ReferenceEquals(Links[0].Definition, Owner))
                throw new InvalidOperationException(
                    "Biome group '" + Owner.BiomeId
                    + "' requires one direct PllBiomeRoot linked to its owner definition.");
        }

        static void RequireNeutralPrefix(PllBiomeDefinition Prefix)
        {
            if (Prefix.SpawnChance == 100
                && Prefix.Favorites.Count == 0
                && Prefix.Overwrites.Count == 0
                && Prefix.Before.Count == 0
                && Prefix.After.Count == 0) return;
            throw new InvalidOperationException(
                "Biome group prefix '" + Prefix.BiomeId
                + "' must have Spawn Chance 100 and no layout relationships.");
        }

        static void RejectPrefixTargets(
            PllBiomeDefinition Owner,
            IReadOnlyList<BiomeId> Relationships,
            HashSet<BiomeId> PrefixIds, string Field)
        {
            foreach (var Target in Relationships)
                if (PrefixIds.Contains(Target))
                    throw new InvalidOperationException(
                        "Biome '" + Owner.BiomeId + "' " + Field
                        + " targets internal group member '" + Target
                        + "'.");
        }
    }
}
