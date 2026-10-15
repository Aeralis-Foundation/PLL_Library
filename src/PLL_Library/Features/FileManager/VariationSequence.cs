using System;
using System.IO;

namespace PLL.Library;

internal sealed class VariationSequence
{
    internal const string RandomChoice = "Random", PatternChoice = "Pattern";
    internal static readonly string PatternDescription =
        "When Pattern is selected, cycles through variation names separated by > "
        + $"for successive copies of this biome. Leave empty to use Random. Accepts up to {byte.MaxValue} entries.";
    readonly string[] Entries;
    internal static VariationSequence Random { get; } = new(Array.Empty<string>());

    VariationSequence(string[] Entries)
    {
        this.Entries = Entries;
        Serialized = string.Join(">", Entries);
    }

    internal string Serialized { get; }
    internal bool IsRandom => Entries.Length == 0;
    internal int Count => Entries.Length;
    internal string At(int OccurrenceIndex) => IsRandom ? string.Empty : Entries[OccurrenceIndex % Entries.Length];

    internal static VariationSequence Capture(
        string SelectionId, string Selected, string Pattern, Func<string, string>? ResolveEntry = null)
    {
        if (Selected == RandomChoice || Selected == PatternChoice && string.IsNullOrWhiteSpace(Pattern)) return Random;
        if (Selected != PatternChoice && Selected.IndexOf('>') >= 0
            || !TryParse(Selected == PatternChoice ? Pattern : Selected, out var Sequence, ResolveEntry)
            || Sequence.IsRandom)
            throw new InvalidDataException($"Variation selection '{SelectionId}' is invalid. Use a listed variation or a pattern of up to {byte.MaxValue} names separated by >.");
        return Sequence;
    }

    internal static bool TryParse(
        string Value, out VariationSequence Sequence, Func<string, string>? ResolveEntry = null)
    {
        Sequence = Random;
        if (Value.Length == 0 || Value == RandomChoice) return true;
        if (Value.Length > RunConfigurationCodec.MaximumPayloadBytes) return false;
        var Count = 1;
        foreach (var Character in Value)
            if (Character is '\n' or '\r' || Character == '>' && ++Count > byte.MaxValue) return false;
        var Entries = Value.Split('>');
        for (var i = 0; i < Entries.Length; i++)
        {
            Entries[i] = Entries[i].Trim();
            if (Entries[i].Length == 0 || Entries[i] is RandomChoice or PatternChoice) return false;
            if (ResolveEntry is not null) Entries[i] = ResolveEntry(Entries[i]);
        }
        Sequence = new VariationSequence(Entries);
        return true;
    }
}
