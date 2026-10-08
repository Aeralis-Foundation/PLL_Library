using System;
using System.Globalization;

namespace PLL.Library;

internal readonly struct GeneratedBatchKey : IEquatable<GeneratedBatchKey>, IComparable<GeneratedBatchKey>
{
    internal enum Owner : byte { Slot, Hooked }
    GeneratedBatchKey(Owner Kind, int RequestIndex, int TargetIndex)
    {
        this.Kind = Kind;
        this.RequestIndex = RequestIndex;
        this.TargetIndex = TargetIndex;
    }

    internal Owner Kind { get; }
    internal int RequestIndex { get; }
    internal int TargetIndex { get; }
    internal static GeneratedBatchKey Slot(int Index) => new(Owner.Slot, Index, -1);
    internal static GeneratedBatchKey Hooked(int RequestIndex, int TargetIndex) => new(Owner.Hooked, RequestIndex, TargetIndex);
    public bool Equals(GeneratedBatchKey Other) => Kind == Other.Kind
        && RequestIndex == Other.RequestIndex && TargetIndex == Other.TargetIndex;
    public override bool Equals(object? Other) => Other is GeneratedBatchKey Key && Equals(Key);
    public override int GetHashCode() => HashCode.Combine((byte)Kind, RequestIndex, TargetIndex);
    public int CompareTo(GeneratedBatchKey Other)
    {
        var Order = Kind.CompareTo(Other.Kind);
        if (Order == 0) Order = RequestIndex.CompareTo(Other.RequestIndex);
        return Order == 0 ? TargetIndex.CompareTo(Other.TargetIndex) : Order;
    }
    public static bool operator ==(GeneratedBatchKey Left, GeneratedBatchKey Right) => Left.Equals(Right);
    public static bool operator !=(GeneratedBatchKey Left, GeneratedBatchKey Right) => !Left.Equals(Right);
    public override string ToString() => Kind == Owner.Slot
        ? "slot/" + RequestIndex.ToString("D3", CultureInfo.InvariantCulture)
        : "hooked/" + RequestIndex.ToString("D3", CultureInfo.InvariantCulture) + "/"
            + TargetIndex.ToString("D3", CultureInfo.InvariantCulture);
}
