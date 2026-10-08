using System;

namespace PLL.Library;

[Serializable]
public readonly struct LayoutId : IEquatable<LayoutId>, IComparable<LayoutId>
{
    const int MaximumLength = 128;
    const string DefaultValue = "default";
    readonly string? value;

    public LayoutId(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (!IsCanonical(value))
            throw new ArgumentException(
                $"A layout ID must be '{DefaultValue}' or a canonical lowercase qualified ID.",
                nameof(value));
        this.value = value;
    }

    public static LayoutId Default { get; } = new(DefaultValue);
    public string Value => value ?? string.Empty;
    public bool IsDefault => string.Equals(Value, DefaultValue, StringComparison.Ordinal);
    public bool IsEmpty => string.IsNullOrEmpty(value);
    public bool IsValid => IsCanonical(value);

    public static bool TryParse(string? value, out LayoutId id)
    {
        if (!IsCanonical(value))
        {
            id = default;
            return false;
        }
        id = new LayoutId(value!);
        return true;
    }

    public int CompareTo(LayoutId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public bool Equals(LayoutId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is LayoutId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(LayoutId left, LayoutId right) => left.Equals(right);
    public static bool operator !=(LayoutId left, LayoutId right) => !left.Equals(right);

    static bool IsCanonical(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaximumLength) return false;
        if (string.Equals(candidate, DefaultValue, StringComparison.Ordinal)) return true;
        if (!string.Equals(candidate, candidate.ToLowerInvariant(), StringComparison.Ordinal)) return false;
        var separator = candidate.IndexOf('/');
        if (separator <= 0 || separator == candidate.Length - 1
            || candidate.IndexOf("//", StringComparison.Ordinal) >= 0
            || candidate.IndexOf('\\') >= 0 || candidate.IndexOf('>') >= 0) return false;
        for (var i = 0; i < candidate.Length; i++)
        {
            var character = candidate[i];
            if (char.IsControl(character) || char.IsWhiteSpace(character)) return false;
            if (char.IsHighSurrogate(character))
            {
                if (++i >= candidate.Length || !char.IsLowSurrogate(candidate[i])) return false;
            }
            else if (char.IsLowSurrogate(character)) return false;
        }
        return true;
    }
}
