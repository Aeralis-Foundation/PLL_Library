using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public struct BiomeId : IEquatable<BiomeId>
{
    [SerializeField] string value;

    public BiomeId(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (!IsCanonical(value))
            throw new ArgumentException("A biome ID must be a canonical lowercase qualified ID.", nameof(value));
        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool IsEmpty => string.IsNullOrEmpty(value);
    public bool IsValid => IsCanonical(value);

    public static bool TryParse(string? value, out BiomeId id)
    {
        if (!IsCanonical(value))
        {
            id = default;
            return false;
        }
        id = new BiomeId { value = value! };
        return true;
    }

    public bool Equals(BiomeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is BiomeId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(BiomeId left, BiomeId right) => left.Equals(right);
    public static bool operator !=(BiomeId left, BiomeId right) => !left.Equals(right);

    static bool IsCanonical(string? Candidate)
    {
        if (!QualifiedId.TryParse(Candidate, out _)) return false;
        var Separator = Candidate!.IndexOf('/');
        return Candidate.IndexOf('/', Separator + 1) < 0;
    }
}

internal static class NativeBiomeIds
{
    internal static BiomeId Peak { get; } = new("peak/peak");
    internal static BiomeId Caldera { get; } = new("peak/caldera");
    internal static BiomeId Gloom { get; } = new("peak/gloom");
}
