using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public struct SegmentId : IComparable<SegmentId>, IEquatable<SegmentId>
{
    [SerializeField] string value;

    public SegmentId(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (!QualifiedId.TryParse(value, out _))
            throw new ArgumentException("A segment ID must be a canonical lowercase qualified ID.", nameof(value));
        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool IsEmpty => string.IsNullOrEmpty(value);
    public bool IsValid => QualifiedId.TryParse(value, out _);

    public static SegmentId Beach { get; } = new("peak/segment/beach");
    public static SegmentId Tropics { get; } = new("peak/segment/tropics");
    public static SegmentId Roots { get; } = new("peak/segment/roots");
    public static SegmentId Alpine { get; } = new("peak/segment/alpine");
    public static SegmentId Mesa { get; } = new("peak/segment/mesa");
    public static SegmentId Caldera { get; } = new("peak/segment/caldera");
    public static SegmentId Gloom { get; } = new("peak/segment/gloom");
    public static SegmentId Volcano { get; } = new("peak/segment/volcano");
    public static SegmentId Citadel { get; } = new("peak/segment/citadel");
    public static SegmentId Peak { get; } = new("peak/segment/peak");
    public static SegmentId Nadir { get; } = new("peak/segment/nadir");

    internal static bool IsNative(SegmentId id) =>
        id == Beach || id == Tropics || id == Roots || id == Alpine
        || id == Mesa || id == Caldera || id == Gloom || id == Volcano
        || id == Citadel || id == Peak || id == Nadir;

    public static bool TryParse(string? value, out SegmentId id)
    {
        if (!QualifiedId.TryParse(value, out var Parsed))
        {
            id = default;
            return false;
        }
        id = new SegmentId { value = Parsed.Value };
        return true;
    }

    public int CompareTo(SegmentId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public bool Equals(SegmentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is SegmentId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(SegmentId left, SegmentId right) => left.Equals(right);
    public static bool operator !=(SegmentId left, SegmentId right) => !left.Equals(right);

}

internal readonly struct QualifiedId
{
    const int MaximumLength = 128;

    QualifiedId(string Value) => this.Value = Value;

    internal string Value { get; }

    internal static bool TryParse(string? Candidate, out QualifiedId Id)
    {
        Id = default;
        if (string.IsNullOrEmpty(Candidate) || Candidate.Length > MaximumLength
            || !string.Equals(Candidate, Candidate.ToLowerInvariant(), StringComparison.Ordinal)) return false;
        var Separator = Candidate.IndexOf('/');
        if (Separator <= 0 || Separator == Candidate.Length - 1
            || Candidate.IndexOf("//", StringComparison.Ordinal) >= 0
            || Candidate.IndexOf('\\') >= 0 || Candidate.IndexOf('>') >= 0) return false;
        for (var i = 0; i < Candidate.Length; i++)
        {
            var Character = Candidate[i];
            if (char.IsControl(Character) || char.IsWhiteSpace(Character)) return false;
            if (char.IsHighSurrogate(Character))
            {
                if (++i >= Candidate.Length || !char.IsLowSurrogate(Candidate[i])) return false;
            }
            else if (char.IsLowSurrogate(Character)) return false;
        }
        Id = new QualifiedId(Candidate);
        return true;
    }
}
