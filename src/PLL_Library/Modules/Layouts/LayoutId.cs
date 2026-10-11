using System;

namespace PLL.Library;

[Serializable]
public readonly struct LayoutId : IEquatable<LayoutId>, IComparable<LayoutId>
{
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

    LayoutId(QualifiedId Id) => value = Id.Value;

    public static LayoutId Default { get; } = new(DefaultValue);
    public string Value => value ?? string.Empty;
    public bool IsDefault => string.Equals(Value, DefaultValue, StringComparison.Ordinal);
    public bool IsEmpty => string.IsNullOrEmpty(value);
    public bool IsValid => IsCanonical(value);

    public static bool TryParse(string? value, out LayoutId id)
    {
        if (string.Equals(value, DefaultValue, StringComparison.Ordinal))
        {
            id = Default;
            return true;
        }
        if (!QualifiedId.TryParse(value, out var Parsed))
        {
            id = default;
            return false;
        }
        id = new LayoutId(Parsed);
        return true;
    }

    public int CompareTo(LayoutId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public bool Equals(LayoutId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is LayoutId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(LayoutId left, LayoutId right) => left.Equals(right);
    public static bool operator !=(LayoutId left, LayoutId right) => !left.Equals(right);

    static bool IsCanonical(string? Candidate) =>
        string.Equals(Candidate, DefaultValue, StringComparison.Ordinal)
        || QualifiedId.TryParse(Candidate, out _);
}
