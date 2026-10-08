using System;
using System.Collections.Generic;
using System.Globalization;

namespace PLL.Library;

internal static class InfiniteMap
{
    sealed class Choice
    {
        internal Choice(BiomeMetadataLayoutSolver.Token token, double weight)
        {
            Token = token;
            Weight = weight;
        }

        internal BiomeMetadataLayoutSolver.Token Token { get; }
        internal double Weight { get; set; }
    }

    internal static BiomeMetadataLayoutSolver.Sequence Apply(
        LayoutSegmentCatalog Catalog,
        BiomeMetadataLayoutSolver.Sequence Layout,
        ResolvedRunConfiguration Configuration)
    {
        if (Catalog is null) throw new ArgumentNullException(nameof(Catalog));
        if (Layout is null) throw new ArgumentNullException(nameof(Layout));
        if (Configuration is null) throw new ArgumentNullException(nameof(Configuration));
        if (Configuration.Mode != MapLayoutMode.Infinite) return Layout;
        var InfiniteConfiguration = Configuration.Layout;

        var Choices = CreateChoices(
            Catalog, InfiniteConfiguration.Repeatable, InfiniteConfiguration.Chances);
        if (Choices.Count == 0)
        {
            Plugin.PLLLog.LogWarning(
                "InfiniteMap: Repeatable had no valid registered segments; no repeatable segments were inserted.");
            if (InfiniteConfiguration.RemoveOld)
                throw new InvalidOperationException(
                    "InfiniteMap cannot remove the configured layout without a valid repeatable segment.");
            return Layout;
        }

        var Target = Math.Max(1, Math.Min(InfiniteConfiguration.Scale, NavigationRoles.MaximumOrdinarySegments));
        var Result = InfiniteConfiguration.RemoveOld
            ? new List<BiomeMetadataLayoutSolver.Token>(Target)
            : new List<BiomeMetadataLayoutSolver.Token>(Layout.Tokens);

        Trim(Result, Target);
        var InsertAt = InsertionIndex(Result, InfiniteConfiguration.BeginIndex);
        var Count = Target - Result.Count;
        if (Count > 0)
        {
            var Chain = CreateChain(
                Choices,
                Count,
                InsertAt > 0 ? Result[InsertAt - 1] : null,
                InsertAt < Result.Count ? Result[InsertAt] : null,
                new FastRandom(CreateSeed(Choices, InfiniteConfiguration.Seed, InfiniteConfiguration.Scale)));
            Result.InsertRange(InsertAt, Chain);
        }

        return new BiomeMetadataLayoutSolver.Sequence(
            Result,
            $"{Layout.Description} after InfiniteMap expansion");
    }
    static List<BiomeMetadataLayoutSolver.Token> CreateChain(
        List<Choice> choices,
        int length,
        BiomeMetadataLayoutSolver.Token? predecessor,
        BiomeMetadataLayoutSolver.Token? successor,
        FastRandom random)
    {
        var count = choices.Count;
        var transitions = new bool[count, count];
        var viable = new bool[length, count];
        for (var i = 0; i < count; i++)
        {
            viable[length - 1, i] = successor is null || choices[i].Token.CanTransitionTo(successor);
            for (var j = 0; j < count; j++)
                transitions[i, j] = choices[i].Token.CanTransitionTo(choices[j].Token);
        }
        for (var position = length - 2; position >= 0; position--)
            for (var i = 0; i < count; i++)
                for (var j = 0; j < count; j++)
                    if (transitions[i, j] && viable[position + 1, j])
                    {
                        viable[position, i] = true;
                        break;
                    }

        var compatible = new bool[count];
        var possible = false;
        for (var i = 0; i < count; i++)
        {
            compatible[i] = predecessor is null || predecessor.CanTransitionTo(choices[i].Token);
            possible |= compatible[i] && viable[0, i];
        }
        if (!possible)
            throw new InvalidOperationException(
                "InfiniteMap could not build a compatible repeatable chain at the configured insertion point.");

        var chain = new List<BiomeMetadataLayoutSolver.Token>(length);
        for (var position = 0; position < length; position++)
        {
            var selected = Select(choices, viable, compatible, position, random);
            chain.Add(choices[selected].Token);
            for (var i = 0; i < count; i++) compatible[i] = transitions[selected, i];
        }
        return chain;
    }

    static int InsertionIndex(IReadOnlyList<BiomeMetadataLayoutSolver.Token> layout, int requested)
    {
        var limit = TerminalGroupStart(layout);
        if (limit < 0) limit = layout.Count;
        if (requested == -1) return limit;

        requested = Math.Max(0, requested);
        return Math.Min(requested, limit);
    }

    static List<Choice> CreateChoices(
        LayoutSegmentCatalog Catalog,
        string repeatable,
        string chances)
    {
        if (repeatable.Length == 0) return new List<Choice>();
        var Names = SplitChoices(repeatable);
        var Weights = SplitChoices(chances);
        var ParsedWeights = new double[Names.Length];
        var ValidWeights = Names.Length == Weights.Length;
        var maximum = 0d;

        if (ValidWeights)
        {
            for (var i = 0; i < Weights.Length; i++)
            {
                ValidWeights = double.TryParse(
                        Weights[i].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out ParsedWeights[i])
                    && ParsedWeights[i] >= 0
                    && !double.IsInfinity(ParsedWeights[i]);
                if (!ValidWeights) break;
                maximum = Math.Max(maximum, ParsedWeights[i]);
            }
            ValidWeights = ValidWeights && maximum > 0;
        }

        if (!ValidWeights)
            Plugin.PLLLog.LogWarning(
                "InfiniteMap: Chances must contain one finite non-negative weight per repeatable segment, with at least one positive value; equal weights were used.");

        var Choices = new List<Choice>(Names.Length);
        var Indices = new Dictionary<string, int>(Names.Length, StringComparer.Ordinal);
        for (var i = 0; i < Names.Length; i++)
        {
            var Token = BiomeMetadataLayoutSolver.CreateStrictToken(
                Catalog, Names[i], $"InfiniteMap repeatable token {i}");
            var Weight = ValidWeights ? ParsedWeights[i] / maximum : 1d;
            if (Indices.TryGetValue(Token.StableKey, out var Index)) Choices[Index].Weight += Weight;
            else
            {
                Indices.Add(Token.StableKey, Choices.Count);
                Choices.Add(new Choice(Token, Weight));
            }
        }
        for (var i = Choices.Count - 1; i >= 0; i--)
            if (Choices[i].Weight <= 0) Choices.RemoveAt(i);
        return Choices;
    }

    static string[] SplitChoices(string Value) =>
        Value.Split(new[] { '>' }, StringSplitOptions.None);
    static void Trim(List<BiomeMetadataLayoutSolver.Token> layout, int target)
    {
        if (layout.Count <= target) return;
        var terminal = TerminalGroupStart(layout);
        if (terminal >= 0)
        {
            var Ending = layout[terminal];
            layout.RemoveAt(terminal);
            TrimPrefix(layout, target - 1);
            layout.Add(Ending);
            return;
        }
        TrimPrefix(layout, target);
    }

    static void TrimPrefix(List<BiomeMetadataLayoutSolver.Token> layout, int ordinaryLimit)
    {
        ordinaryLimit = Math.Max(0, ordinaryLimit);
        if (layout.Count > ordinaryLimit)
            layout.RemoveRange(ordinaryLimit, layout.Count - ordinaryLimit);
    }

    static int TerminalGroupStart(IReadOnlyList<BiomeMetadataLayoutSolver.Token> layout)
    {
        if (layout.Count == 0) return -1;
        return layout[layout.Count - 1].IsNadir ? layout.Count - 1 : -1;
    }

    static int Select(
        List<Choice> choices,
        bool[,] viable,
        bool[] compatible,
        int position,
        FastRandom random)
    {
        var total = 0d;
        for (var i = 0; i < choices.Count; i++)
            if (compatible[i] && viable[position, i]) total += choices[i].Weight;
        var value = random.NextDouble() * total;
        var selected = -1;
        for (var i = 0; i < choices.Count; i++)
        {
            if (!compatible[i] || !viable[position, i]) continue;
            selected = i;
            value -= choices[i].Weight;
            if (value < 0) return i;
        }
        return selected >= 0
            ? selected
            : throw new InvalidOperationException("InfiniteMap could not continue a compatible repeatable chain.");
    }

    static uint CreateSeed(IEnumerable<Choice> Choices, int Seed, int Scale)
    {
        var HashValue = 2166136261u ^ unchecked((uint)Seed);
        foreach (var Choice in Choices)
        {
            HashValue = LayoutEntropy.Append(HashValue, Choice.Token.StableKey);
            HashValue = LayoutEntropy.Append(
                HashValue, Choice.Weight.ToString("R", CultureInfo.InvariantCulture));
        }
        HashValue = (HashValue ^ unchecked((uint)Scale)) * 16777619u;
        return HashValue == 0 ? 0x9E3779B9u : HashValue;
    }
    sealed class FastRandom
    {
        uint state;

        internal FastRandom(uint seed) => state = seed;

        internal double NextDouble()
        {
            var value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return (value >> 8) * (1d / 16777216d);
        }
    }
}
