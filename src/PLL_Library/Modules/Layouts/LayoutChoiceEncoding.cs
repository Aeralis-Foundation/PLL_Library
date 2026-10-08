using System;

namespace PLL.Library;

internal static class LayoutChoiceEncoding
{
    internal const int MaximumFieldLength = short.MaxValue;
    internal const int MaximumCount = byte.MaxValue;

    internal static bool IsValid(string Value)
    {
        if (Value.Length > MaximumFieldLength) return false;
        var Count = 0;
        var Start = 0;
        for (var i = 0; i <= Value.Length; i++)
        {
            if (i < Value.Length && Value[i] != '>') continue;
            if (i > Start && ++Count > MaximumCount) return false;
            Start = i + 1;
        }
        return true;
    }

    internal static void RequireValid(string Order, string Repeatable, string Chances)
    {
        if (IsValid(Order) && IsValid(Repeatable) && IsValid(Chances)) return;
        throw new InvalidOperationException(
            $"LayoutOrder, InfiniteMap: Repeatable, and InfiniteMap: Chances must each contain at most {MaximumCount} non-empty entries and {MaximumFieldLength} characters.");
    }
}
