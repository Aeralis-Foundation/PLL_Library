using System;
using System.Collections.Generic;

namespace PLL.Library;

internal static class GenerativeMap
{
    const int MaximumSearchSteps = 4_194_304;
    static int MaximumOrdinaryCandidates => NavigationRoles.MaximumOrdinarySegments;

    sealed class Candidate
    {
        internal Candidate(
            BiomeId id,
            BiomeMetadataLayoutSolver.Token token,
            BiomeLayoutConfigValues? metadata,
            uint rank)
        {
            Id = id;
            Token = token;
            Metadata = metadata;
            Rank = rank;
        }

        internal BiomeId Id { get; }
        internal BiomeMetadataLayoutSolver.Token Token { get; }
        internal BiomeLayoutConfigValues? Metadata { get; }
        internal uint Rank { get; }
    }

    sealed class Search
    {
        readonly IReadOnlyList<Candidate> candidates;
        readonly bool[,] transitions;
        readonly bool[] remaining;
        readonly int[][] options;
        readonly bool[][] viable;
        readonly List<BiomeMetadataLayoutSolver.Token> tokens;
        int steps;
        bool limitReached;

        internal Search(IReadOnlyList<Candidate> candidates)
        {
            this.candidates = candidates;
            remaining = new bool[candidates.Count];
            Array.Fill(remaining, true);
            transitions = new bool[candidates.Count, candidates.Count];
            options = new int[candidates.Count][];
            viable = new bool[candidates.Count][];
            tokens = new List<BiomeMetadataLayoutSolver.Token>(candidates.Count);
            var maximumSources = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                maximumSources = Math.Max(maximumSources, candidates[i].Token.Candidates.Length);
                options[i] = new int[candidates.Count];
                for (var next = 0; next < candidates.Count; next++)
                    transitions[i, next] = candidates[i].Token.CanTransitionTo(candidates[next].Token);
            }
            for (var i = 0; i < viable.Length; i++) viable[i] = new bool[maximumSources];
        }

        internal List<BiomeMetadataLayoutSolver.Token> Arrange()
        {
            if (Arrange(-1, 0)) return tokens;
            throw new InvalidOperationException(limitReached
                ? $"Generative Map exceeded the {MaximumSearchSteps} candidate-placement search limit."
                : "Generative Map has no connector-compatible candidate ordering.");
        }

        bool Arrange(int previous, int depth)
        {
            if (depth == candidates.Count) return true;
            var choices = options[depth];
            var choiceCount = 0;
            for (var i = 0; i < candidates.Count; i++)
                if (remaining[i] && (previous < 0 || transitions[previous, i])) choices[choiceCount++] = i;
            Sort(choices, choiceCount, previous);

            for (var choice = 0; choice < choiceCount; choice++)
            {
                if (++steps > MaximumSearchSteps)
                {
                    limitReached = true;
                    return false;
                }
                var selected = choices[choice];
                if (!Advance(previous, selected, depth)) continue;
                remaining[selected] = false;
                tokens.Add(candidates[selected].Token);
                if (Arrange(selected, depth + 1)) return true;
                tokens.RemoveAt(tokens.Count - 1);
                remaining[selected] = true;
                if (limitReached) return false;
            }
            return false;
        }

        bool Advance(int previous, int selected, int depth)
        {
            var current = viable[depth];
            Array.Clear(current, 0, current.Length);
            var sources = candidates[selected].Token.Candidates;
            if (previous < 0)
            {
                for (var i = 0; i < sources.Length; i++) current[i] = true;
                return true;
            }

            var priorSources = candidates[previous].Token.Candidates;
            var prior = viable[depth - 1];
            var any = false;
            for (var i = 0; i < sources.Length; i++)
                for (var j = 0; j < priorSources.Length; j++)
                    if (prior[j] && BiomeMetadataLayoutSolver.Compatible(
                            priorSources[j].Source, sources[i].EntrySource))
                    {
                        current[i] = true;
                        any = true;
                        break;
                    }
            return any;
        }

        void Sort(int[] values, int count, int previous)
        {
            for (var i = 1; i < count; i++)
            {
                var value = values[i];
                var position = i;
                while (position > 0 && Compare(value, values[position - 1], previous) < 0)
                {
                    values[position] = values[position - 1];
                    position--;
                }
                values[position] = value;
            }
        }

        int Compare(int left, int right, int previous)
        {
            var result = AdjacencyScore(previous, right).CompareTo(AdjacencyScore(previous, left));
            if (result != 0) return result;
            result = FavoriteScore(previous, right).CompareTo(FavoriteScore(previous, left));
            if (result != 0) return result;
            result = candidates[left].Rank.CompareTo(candidates[right].Rank);
            return result != 0 ? result
                : StringComparer.Ordinal.Compare(candidates[left].Id.Value, candidates[right].Id.Value);
        }

        int AdjacencyScore(int previous, int candidate)
        {
            if (previous < 0) return 0;
            return (candidates[previous].Metadata?.PrefersBefore(candidates[candidate].Id) == true ? 1 : 0)
                + (candidates[candidate].Metadata?.PrefersAfter(candidates[previous].Id) == true ? 1 : 0);
        }

        int FavoriteScore(int previous, int candidate)
        {
            if (previous < 0) return 0;
            return (candidates[previous].Metadata?.Favors(candidates[candidate].Id) == true ? 1 : 0)
                + (candidates[candidate].Metadata?.Favors(candidates[previous].Id) == true ? 1 : 0);
        }
    }

    internal static BiomeMetadataLayoutSolver.Sequence CreateSequence(
        LayoutSegmentCatalog Catalog,
        BiomeLayoutConfigSnapshot configurations,
        int seed)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (configurations is null) throw new ArgumentNullException(nameof(configurations));
        var included = new Dictionary<BiomeId, BiomeLayoutConfigValues?>();
        foreach (var Source in Catalog.SelectableValues)
        {
            if (Source is PeakAttachmentSource) continue;
            if (!Source.TryGetMetadataBiome(out var Biome)) continue;

            if (Source is not CustomLayoutSegmentSource)
            {
                if (!included.ContainsKey(Biome)) included.Add(Biome, null);
                continue;
            }
            if (!configurations.TryGet(Biome, out var Metadata))
                throw new InvalidOperationException($"Custom biome '{Biome}' has no effective configuration.");
            if (!Includes(Metadata, seed)) continue;
            included[Biome] = Metadata;
        }

        if (included.Count > MaximumOrdinaryCandidates)
            throw new InvalidOperationException(
                $"Generative Map contains {included.Count} included ordinary candidates; the maximum is {MaximumOrdinaryCandidates}.");
        var candidates = new List<Candidate>(included.Count);
        foreach (var pair in included)
        {
            var token = BiomeMetadataLayoutSolver.CreateOwnedBiomeToken(
                Catalog, pair.Key, $"Generative Map candidate '{pair.Key}'");
            candidates.Add(new Candidate(pair.Key, token, pair.Value, Entropy(seed, pair.Key, "order")));
        }
        candidates.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        var Tokens = new Search(candidates).Arrange();
        return new BiomeMetadataLayoutSolver.Sequence(Tokens, "Generative Map layout");
    }

    internal static bool Includes(BiomeLayoutConfigValues metadata, int seed)
    {
        if (metadata.SpawnChance <= 0) return false;
        if (metadata.SpawnChance >= 100) return true;
        var value = Entropy(seed, metadata.Id, "include");
        return (int)(((ulong)value * 100) >> 32) < metadata.SpawnChance;
    }

    static uint Entropy(int seed, BiomeId id, string domain)
    {
        var hash = 2166136261u;
        hash = (hash ^ unchecked((byte)seed)) * 16777619u;
        hash = (hash ^ unchecked((byte)(seed >> 8))) * 16777619u;
        hash = (hash ^ unchecked((byte)(seed >> 16))) * 16777619u;
        hash = (hash ^ unchecked((byte)(seed >> 24))) * 16777619u;
        hash = LayoutEntropy.Append(hash, "PLL/generative/v1/");
        hash = LayoutEntropy.Append(hash, domain);
        hash = LayoutEntropy.Append(hash, id.Value);
        return hash;
    }
}
