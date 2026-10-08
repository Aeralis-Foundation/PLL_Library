using System;
using System.Collections.Generic;

namespace PLL.Library;

internal static class BiomeMetadataLayoutSolver
{
    const int MaximumCandidatesPerToken = 128;
    const int MaximumStates = 32768;
    const long MaximumTransitions = 4194304;

    static int MaximumOrdinaryTokens => NavigationRoles.MaximumOrdinarySegments;

    internal static Sequence CreateStrictSequence(
        LayoutSegmentCatalog Catalog,
        IReadOnlyList<string> Tokens,
        string Description)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Tokens is null) throw new ArgumentNullException(nameof(Tokens));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        var Result = new List<Token>(Tokens.Count);
        for (var i = 0; i < Tokens.Count; i++)
            Result.Add(CreateStrictToken(Catalog, Tokens[i], $"{Description} token {i}"));
        return new Sequence(Result, Description);
    }

    internal static Sequence CreateConfiguredSequence(
        LayoutSegmentCatalog Catalog,
        string Value,
        string Description)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Value is null) throw new ArgumentNullException(nameof(Value));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        var Values = Value.Split(new[] { '>' }, StringSplitOptions.None);
        var Result = new List<Token>(Values.Length);
        for (var i = 0; i < Values.Length; i++)
            Result.Add(CreateStrictToken(Catalog, Values[i], $"{Description} token {i}"));
        return new Sequence(Result, Description);
    }

    internal static Sequence CreateLockedSequence(
        LayoutSegmentCatalog Catalog,
        IReadOnlyList<LayoutSegmentSource> Sources,
        string Description)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Sources is null) throw new ArgumentNullException(nameof(Sources));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        var Result = new List<Token>(Sources.Count);
        for (var i = 0; i < Sources.Count; i++)
            Result.Add(CreateLockedToken(Catalog, Sources[i]));
        return new Sequence(Result, Description);
    }

    internal static Token CreateStrictToken(
        LayoutSegmentCatalog Catalog,
        string Value,
        string Description)
    {
        if (TryParseToken(Catalog, Value, Description, out var Token)) return Token;
        throw new InvalidOperationException($"{Description} references unknown segment token '{Value}'.");
    }

    internal static Token CreateLockedToken(
        LayoutSegmentCatalog Catalog,
        LayoutSegmentSource Source)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Source is null) throw new ArgumentNullException(nameof(Source));
        if (Source is PeakAttachmentSource)
            throw new ArgumentException("A structural attachment cannot be used as a layout token.", nameof(Source));
        RejectGroupPrefix(Catalog, Source);
        return new Token(new[] { CreateLockedCandidate(Catalog, Source) }, null);
    }

    internal static Token CreateOwnedBiomeToken(
        LayoutSegmentCatalog Catalog,
        BiomeId biome,
        string description)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (!biome.IsValid) throw new ArgumentException("A valid biome ID is required.", nameof(biome));
        if (description is null) throw new ArgumentNullException(nameof(description));
        RejectGroupPrefix(Catalog, biome);
        if (biome == NativeBiomeIds.Caldera || biome == NativeBiomeIds.Gloom)
            throw new InvalidOperationException(
                $"{description} uses internal composite biome '{biome}'; use 'peak/{(biome == NativeBiomeIds.Caldera ? "volcano" : "citadel")}'.");
        var Candidates = GatherCandidates(Catalog, biome, description, false);
        if (Candidates.Length == 0)
            throw new InvalidOperationException($"{description} has no registered owner for biome '{biome}'.");
        return new Token(Candidates, biome);
    }


    internal static List<LayoutSegmentSource> Resolve(Sequence SequenceValue)
    {
        if (SequenceValue is null) throw new ArgumentNullException(nameof(SequenceValue));
        var Positions = new Position[SequenceValue.Tokens.Count];
        for (var i = 0; i < SequenceValue.Tokens.Count; i++)
            Positions[i] = new Position(SequenceValue.Tokens[i].Candidates);
        var Result = Solve(Positions, SequenceValue.Description);
        LayoutPlan.Validate(Result, SequenceValue.Description);
        return Result;
    }

    internal static bool TryParseToken(
        LayoutSegmentCatalog Catalog,
        string Value,
        string Description,
        out Token Token)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Value is null) throw new ArgumentNullException(nameof(Value));
        if (Description is null) throw new ArgumentNullException(nameof(Description));
        var Text = Value.Trim();
        try
        {
            LayoutContract.SegmentToken(Text, nameof(Value));
        }
        catch (ArgumentException Exception)
        {
            throw new InvalidOperationException(
                $"{Description} is not a valid layout segment token. {Exception.Message}", Exception);
        }

        var HasName = Catalog.TryGetName(Text, out var NamedSource);
        var HasStableId = Catalog.TryGetStableId(Text, out var IdentifiedSource);
        if (HasName && HasStableId && !ReferenceEquals(NamedSource, IdentifiedSource))
            throw new InvalidOperationException(
                $"{Description} is ambiguous because '{Text}' matches segment name '{NamedSource.Id}' and segment ID '{IdentifiedSource.StableId}'.");
        if (HasName || HasStableId)
        {
            var Source = HasName ? NamedSource : IdentifiedSource;
            if (Source is PeakAttachmentSource)
                throw new InvalidOperationException(
                    $"{Description} uses structural attachment '{Text}' as a layout segment.");
            Token = CreateLockedToken(Catalog, Source);
            return true;
        }
        if (BiomeId.TryParse(Text, out var Biome))
        {
            RejectGroupPrefix(Catalog, Biome);
            var Candidates = GatherCandidates(Catalog, Biome, Description);
            if (Candidates.Length != 0)
            {
                Token = new Token(Candidates, Biome);
                return true;
            }
        }
        Token = null!;
        return false;
    }

    static Candidate[] GatherCandidates(
        LayoutSegmentCatalog Catalog,
        BiomeId target,
        string description,
        bool includeOverwrites = true)
    {
        var Configuration = RunConfigurationResolution.RequireCurrent();
        var result = new List<Candidate>();
        foreach (var Source in Catalog.SelectableValues)
        {
            if (Source is PeakAttachmentSource
                || !Source.TryGetMetadataBiome(out var Biome)) continue;
            var Owns = Biome == target;
            var Metadata = Source is CustomLayoutSegmentSource Custom
                ? BiomeMetadataLayoutSolver.Metadata(Custom, Configuration.Biomes)
                : null;
            if (Configuration.Mode == MapLayoutMode.Generative && Metadata is not null
                && !GenerativeMap.Includes(Metadata, Configuration.Layout.Seed)) continue;
            var Overwrites = includeOverwrites && Metadata is not null && Metadata.OverwritesBiome(target);
            if (!Owns && !Overwrites) continue;
            if (result.Count >= MaximumCandidatesPerToken)
                throw new InvalidOperationException(
                    $"{description} matches more than {MaximumCandidatesPerToken} metadata layout candidates.");
            result.Add(new Candidate(
                CandidateSources(Catalog, Source), Metadata, !Owns, Source.StableId));
        }
        result.Sort(CandidateComparer.Instance);
        return result.ToArray();
    }

    static Candidate CreateLockedCandidate(
        LayoutSegmentCatalog Catalog,
        LayoutSegmentSource Source)
    {
        var Biomes = RunConfigurationResolution.RequireCurrent().Biomes;
        var Metadata = Source is CustomLayoutSegmentSource Custom
            ? BiomeMetadataLayoutSolver.Metadata(Custom, Biomes)
            : null;
        return new Candidate(CandidateSources(Catalog, Source), Metadata, false, Source.StableId);
    }

    static LayoutSegmentSource[] CandidateSources(
        LayoutSegmentCatalog Catalog,
        LayoutSegmentSource Source)
    {
        if (Catalog.TryGetGroupSources(Source, out var Group))
        {
            var Sources = new LayoutSegmentSource[Group.Count];
            for (var Index = 0; Index < Sources.Length; Index++) Sources[Index] = Group[Index];
            return Sources;
        }
        if (Source is not NativeOrdinarySegmentSource Native) return new[] { Source };
        if (Native.NativeRole == Segment.Caldera)
            throw new InvalidOperationException(
                $"Native segment '{Native.Id}' is an internal composite prefix and cannot be selected independently.");
        if (Native.NativeRole != Segment.TheKiln) return new[] { Source };
        var Prefix = Catalog.Native.CompositePrefix(Native);
        if (!Compatible(Prefix, Native))
            throw new InvalidOperationException(
                $"PEAK's native {Prefix.Id}/{Native.Id} composite has an incompatible internal transition.");
        return new LayoutSegmentSource[] { Prefix, Native };
    }


    static List<LayoutSegmentSource> Solve(
        IReadOnlyList<Position> positions,
        string description)
    {
        var Capacity = NavigationRoles.MaximumOrdinarySegments;
        var states = new List<State>();
        var examinedTransitions = 0L;
        var first = positions[0];
        for (var candidateIndex = 0; candidateIndex < first.Candidates.Length; candidateIndex++)
        {
            var candidate = first.Candidates[candidateIndex];
            if (candidate.SlotCost > Capacity) continue;
            first.States[candidateIndex].Add(states.Count);
            states.Add(new State(
                candidate.IsOverwrite ? 1 : 0, 0, 0, -1, candidateIndex, candidate.SlotCost));
        }
        AssignRanks(first, states);

        for (var positionIndex = 1; positionIndex < positions.Count; positionIndex++)
        {
            var previous = positions[positionIndex - 1];
            var current = positions[positionIndex];
            for (var candidateIndex = 0; candidateIndex < current.Candidates.Length; candidateIndex++)
            {
                var candidate = current.Candidates[candidateIndex];
                var byCost = new Dictionary<int, State>();
                for (var previousIndex = 0; previousIndex < previous.Candidates.Length; previousIndex++)
                {
                    if (!Compatible(previous.Candidates[previousIndex].Source, candidate.EntrySource)) continue;
                    ScoreAdjacency(
                        previous.Candidates[previousIndex], candidate, out var adjacency, out var favorites);
                    foreach (var previousStateIndex in previous.States[previousIndex])
                    {
                        if (++examinedTransitions > MaximumTransitions)
                            throw new InvalidOperationException(
                                $"{description} exceeds the {MaximumTransitions} metadata layout transition limit at token {positionIndex}.");
                        var previousState = states[previousStateIndex];
                        var slots = previousState.Slots + candidate.SlotCost;
                        if (slots > Capacity) continue;
                        var candidateState = new State(
                            previousState.OverwriteSubstitutions + (candidate.IsOverwrite ? 1 : 0),
                            previousState.BeforeAfterScore + adjacency,
                            previousState.FavoriteScore + favorites,
                            previousStateIndex,
                            candidateIndex,
                            slots);
                        if (byCost.TryGetValue(slots, out var existing)
                            && !Better(
                                candidateState.OverwriteSubstitutions,
                                candidateState.BeforeAfterScore,
                                candidateState.FavoriteScore,
                                previousState.Rank,
                                previousStateIndex,
                                existing.OverwriteSubstitutions,
                                existing.BeforeAfterScore,
                                existing.FavoriteScore,
                                states[existing.Previous].Rank,
                                existing.Previous)) continue;
                        byCost[slots] = candidateState;
                    }
                }
                var costs = new List<int>(byCost.Keys);
                costs.Sort();
                State? best = null;
                foreach (var cost in costs)
                {
                    var state = byCost[cost];
                    if (best.HasValue && !BetterPath(state, best.Value, states)) continue;
                    if (states.Count >= MaximumStates)
                        throw new InvalidOperationException(
                            $"{description} exceeds the {MaximumStates} capacity-aware metadata layout state limit at token {positionIndex}.");
                    current.States[candidateIndex].Add(states.Count);
                    states.Add(state);
                    best = state;
                }
            }
            AssignRanks(current, states);
        }

        var last = positions[positions.Count - 1];
        var bestState = -1;
        var finalOverwrites = int.MinValue;
        var finalBeforeAfter = int.MinValue;
        var finalFavorites = int.MinValue;
        var finalRank = int.MaxValue;
        for (var candidateIndex = 0; candidateIndex < last.Candidates.Length; candidateIndex++)
            foreach (var stateIndex in last.States[candidateIndex])
            {
                var state = states[stateIndex];
                if (!Better(
                        state.OverwriteSubstitutions, state.BeforeAfterScore, state.FavoriteScore, state.Rank, stateIndex,
                        finalOverwrites, finalBeforeAfter, finalFavorites, finalRank, bestState)) continue;
                bestState = stateIndex;
                finalOverwrites = state.OverwriteSubstitutions;
                finalBeforeAfter = state.BeforeAfterScore;
                finalFavorites = state.FavoriteScore;
                finalRank = state.Rank;
            }
        if (bestState < 0)
            throw new InvalidOperationException(
                $"{description} has no connector-compatible metadata layout within {Capacity} internal navigation slots.");

        var selected = new Candidate[positions.Count];
        for (var positionIndex = positions.Count - 1; positionIndex >= 0; positionIndex--)
        {
            var state = states[bestState];
            selected[positionIndex] = positions[positionIndex].Candidates[state.CandidateIndex];
            bestState = states[bestState].Previous;
        }
        var Result = new List<LayoutSegmentSource>();
        foreach (var Candidate in selected) Result.AddRange(Candidate.Sources);
        return Result;
    }

    internal static bool Compatible(LayoutSegmentSource Previous, LayoutSegmentSource Next) =>
        LayoutTransition.TryResolve(Previous, Next, out _);

    static void ScoreAdjacency(Candidate Previous, Candidate Next, out int BeforeAfter, out int Favorites)
    {
        var HasPreviousBiome = Previous.Source.TryGetMetadataBiome(out var PreviousBiome);
        var HasNextBiome = Next.Source.TryGetMetadataBiome(out var NextBiome);
        BeforeAfter = 0;
        if (HasNextBiome
            && Previous.Metadata is not null && Previous.Metadata.PrefersBefore(NextBiome)) BeforeAfter++;
        if (HasPreviousBiome
            && Next.Metadata is not null && Next.Metadata.PrefersAfter(PreviousBiome)) BeforeAfter++;
        Favorites = 0;
        if (HasNextBiome
            && Previous.Metadata is not null && Previous.Metadata.Favors(NextBiome)) Favorites++;
        if (HasPreviousBiome
            && Next.Metadata is not null && Next.Metadata.Favors(PreviousBiome)) Favorites++;
    }

    static bool Better(
        int overwrites,
        int beforeAfter,
        int favorites,
        int rank,
        int stateIndex,
        int otherOverwrites,
        int otherBeforeAfter,
        int otherFavorites,
        int otherRank,
        int otherStateIndex) =>
        overwrites > otherOverwrites
        || overwrites == otherOverwrites && (beforeAfter > otherBeforeAfter
            || beforeAfter == otherBeforeAfter && (favorites > otherFavorites
                || favorites == otherFavorites && (rank < otherRank
                    || rank == otherRank && stateIndex < otherStateIndex)));

    static bool BetterPath(State state, State other, IReadOnlyList<State> states) =>
        Better(
            state.OverwriteSubstitutions,
            state.BeforeAfterScore,
            state.FavoriteScore,
            states[state.Previous].Rank,
            state.Previous,
            other.OverwriteSubstitutions,
            other.BeforeAfterScore,
            other.FavoriteScore,
            states[other.Previous].Rank,
            other.Previous);

    static void AssignRanks(Position position, List<State> states)
    {
        var keys = new List<RankKey>();
        for (var candidateIndex = 0; candidateIndex < position.Candidates.Length; candidateIndex++)
            foreach (var stateIndex in position.States[candidateIndex])
            {
                var state = states[stateIndex];
                var previousRank = state.Previous >= 0 ? states[state.Previous].Rank : -1;
                keys.Add(new RankKey(
                    stateIndex, previousRank, position.Candidates[state.CandidateIndex].StableSegmentId));
            }
        keys.Sort(RankKeyComparer.Instance);
        var rank = -1;
        var lastPreviousRank = int.MinValue;
        string? lastSegmentId = null;
        foreach (var key in keys)
        {
            if (key.PreviousRank != lastPreviousRank
                || !string.Equals(key.StableSegmentId, lastSegmentId, StringComparison.Ordinal))
            {
                rank++;
                lastPreviousRank = key.PreviousRank;
                lastSegmentId = key.StableSegmentId;
            }
            var state = states[key.StateIndex];
            state.Rank = rank;
            states[key.StateIndex] = state;
        }
    }

    static void RejectGroupPrefix(LayoutSegmentCatalog Catalog, LayoutSegmentSource Source)
    {
        if (!Catalog.TryGetGroupOwner(Source, out var Owner)) return;
        throw InternalGroupMember(
            ((CustomLayoutSegmentSource)Source).MetadataBiome,
            ((CustomLayoutSegmentSource)Owner).MetadataBiome);
    }

    static void RejectGroupPrefix(LayoutSegmentCatalog Catalog, BiomeId Prefix)
    {
        if (!Catalog.TryGetGroupOwner(Prefix, out var Owner)) return;
        throw InternalGroupMember(Prefix, ((CustomLayoutSegmentSource)Owner).MetadataBiome);
    }

    static InvalidOperationException InternalGroupMember(BiomeId Prefix, BiomeId Owner) =>
        new($"Biome '{Prefix}' is an internal member of group '{Owner}'; use '{Owner}'.");

    internal static void ValidateGroupConfiguration(
        LayoutSegmentCatalog Catalog,
        BiomeLayoutConfigSnapshot Biomes)
    {
        if (!Catalog.HasGroups) return;
        foreach (var Source in Catalog.AllValues)
        {
            if (Source is not CustomLayoutSegmentSource Custom) continue;
            var Metadata = BiomeMetadataLayoutSolver.Metadata(Custom, Biomes);
            if (Catalog.TryGetGroupOwner(Source, out _))
                ValidateNeutralPrefix(Metadata, Custom.MetadataBiome);
            ValidateRelationships(Catalog, Metadata);
        }
    }

    static BiomeLayoutConfigValues Metadata(
        CustomLayoutSegmentSource Source,
        BiomeLayoutConfigSnapshot Biomes)
    {
        if (!Biomes.TryGet(Source.MetadataBiome, out var Metadata))
            throw new InvalidOperationException(
                $"Custom segment '{Source.Id}' has no effective biome configuration.");
        return Metadata;
    }

    static void ValidateRelationships(
        LayoutSegmentCatalog Catalog,
        BiomeLayoutConfigValues Metadata)
    {
        Validate(Metadata.Favorites, nameof(PllBiomeDefinition.Favorites));
        Validate(Metadata.Overwrites, nameof(PllBiomeDefinition.Overwrites));
        Validate(Metadata.Before, nameof(PllBiomeDefinition.Before));
        Validate(Metadata.After, nameof(PllBiomeDefinition.After));
        return;

        void Validate(IReadOnlyList<BiomeId> Values, string Name)
        {
            foreach (var Target in Values)
            {
                if (!Catalog.TryGetGroupOwner(Target, out var Owner)) continue;
                throw new InvalidOperationException(
                    $"Biome '{Metadata.Id}' {Name} targets internal group member '{Target}'; "
                    + $"use '{((CustomLayoutSegmentSource)Owner).MetadataBiome}'.");
            }
        }
    }

    static void ValidateNeutralPrefix(BiomeLayoutConfigValues Metadata, BiomeId Prefix)
    {
        if (Metadata.SpawnChance == 100 && Metadata.Favorites.Length == 0
            && Metadata.Overwrites.Length == 0 && Metadata.Before.Length == 0
            && Metadata.After.Length == 0) return;
        throw new InvalidOperationException(
            $"Biome group prefix '{Prefix}' requires SpawnChance 100 and no effective layout relationships.");
    }

    internal sealed class Sequence
    {
        internal Sequence(IReadOnlyList<Token> Tokens, string Description)
        {
            if (Tokens is null) throw new ArgumentNullException(nameof(Tokens));
            if (Description is null) throw new ArgumentNullException(nameof(Description));
            if (Tokens.Count > MaximumOrdinaryTokens)
                throw new InvalidOperationException(
                    $"{Description} contains more than {MaximumOrdinaryTokens} ordinary segments.");
            if (Tokens.Count == 0)
                throw new InvalidOperationException($"{Description} contains no ordinary segments.");
            var OwnedTokens = new Token[Tokens.Count];
            for (var i = 0; i < OwnedTokens.Length; i++)
                OwnedTokens[i] = Tokens[i]
                    ?? throw new ArgumentException($"{Description} contains a missing token at index {i}.", nameof(Tokens));
            this.Tokens = Array.AsReadOnly(OwnedTokens);
            this.Description = Description;
        }

        internal IReadOnlyList<Token> Tokens { get; }
        internal string Description { get; }
    }

    internal sealed class Token
    {
        readonly Candidate[] candidates;
        readonly BiomeId? requestedBiome;

        internal Token(Candidate[] candidates, BiomeId? requestedBiome)
        {
            this.candidates = candidates;
            this.requestedBiome = requestedBiome;
        }

        internal Candidate[] Candidates => candidates;
        internal bool IsNadir
        {
            get
            {
                foreach (var candidate in candidates)
                    if (candidate.Source is not NadirLayoutSegmentSource) return false;
                return true;
            }
        }
        internal string StableKey => requestedBiome.HasValue
            ? $"biome/{requestedBiome.Value.Value}"
            : $"segment/{candidates[0].StableSegmentId}";

        internal bool CanTransitionTo(Token next)
        {
            foreach (var previousCandidate in candidates)
                foreach (var nextCandidate in next.candidates)
                    if (Compatible(previousCandidate.Source, nextCandidate.EntrySource)) return true;
            return false;
        }

        internal bool MatchesAnchor(Token anchor)
        {
            foreach (var candidate in candidates)
                foreach (var anchorCandidate in anchor.candidates)
                    if (ReferenceEquals(candidate.Source, anchorCandidate.Source)) return true;
            return false;
        }
    }

    sealed class Position
    {
        internal Position(Candidate[] candidates)
        {
            Candidates = candidates;
            States = new List<int>[candidates.Length];
            for (var i = 0; i < States.Length; i++) States[i] = new List<int>();
        }

        internal Candidate[] Candidates { get; }
        internal List<int>[] States { get; }
    }

    internal readonly struct Candidate
    {
        internal Candidate(
            LayoutSegmentSource[] Sources,
            BiomeLayoutConfigValues? Metadata,
            bool IsOverwrite,
            string StableSegmentId)
        {
            this.Sources = Sources;
            this.Metadata = Metadata;
            this.IsOverwrite = IsOverwrite;
            this.StableSegmentId = StableSegmentId;
        }

        internal LayoutSegmentSource[] Sources { get; }
        internal LayoutSegmentSource Source => Sources[Sources.Length - 1];
        internal LayoutSegmentSource EntrySource => Sources[0];
        internal int SlotCost => Sources.Length;
        internal BiomeLayoutConfigValues? Metadata { get; }
        internal bool IsOverwrite { get; }
        internal string StableSegmentId { get; }
    }

    struct State
    {
        internal State(
            int overwriteSubstitutions,
            int beforeAfterScore,
            int favoriteScore,
            int previous,
            int candidateIndex,
            int slots)
        {
            OverwriteSubstitutions = overwriteSubstitutions;
            BeforeAfterScore = beforeAfterScore;
            FavoriteScore = favoriteScore;
            Previous = previous;
            CandidateIndex = candidateIndex;
            Slots = slots;
            Rank = 0;
        }

        internal int OverwriteSubstitutions;
        internal int BeforeAfterScore;
        internal int FavoriteScore;
        internal int Previous;
        internal int CandidateIndex;
        internal int Slots;
        internal int Rank;
    }

    readonly struct RankKey
    {
        internal RankKey(int stateIndex, int previousRank, string stableSegmentId)
        {
            StateIndex = stateIndex;
            PreviousRank = previousRank;
            StableSegmentId = stableSegmentId;
        }

        internal int StateIndex { get; }
        internal int PreviousRank { get; }
        internal string StableSegmentId { get; }
    }

    sealed class CandidateComparer : IComparer<Candidate>
    {
        internal static readonly CandidateComparer Instance = new();

        public int Compare(Candidate left, Candidate right)
        {
            var result = StringComparer.Ordinal.Compare(left.StableSegmentId, right.StableSegmentId);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left.Source.Id, right.Source.Id);
        }
    }

    sealed class RankKeyComparer : IComparer<RankKey>
    {
        internal static readonly RankKeyComparer Instance = new();

        public int Compare(RankKey left, RankKey right)
        {
            var result = left.PreviousRank.CompareTo(right.PreviousRank);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.StableSegmentId, right.StableSegmentId);
            return result != 0 ? result : left.StateIndex.CompareTo(right.StateIndex);
        }
    }
}
