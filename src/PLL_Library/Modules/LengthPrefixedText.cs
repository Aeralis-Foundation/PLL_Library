using System;
using System.Globalization;
using System.Text;

namespace PLL.Library;

internal static class LengthPrefixedText
{
    internal static void Append(StringBuilder Result, string Value) =>
        Result.Append(Value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Value);

    internal static bool TryRead(
        string Data,
        ref int Offset,
        out string Value,
        int MaximumLength = int.MaxValue)
    {
        Value = string.Empty;
        var Separator = Data.IndexOf(':', Offset);
        if (Separator <= Offset
            || !int.TryParse(
                Data.AsSpan(Offset, Separator - Offset),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var Length)
            || Length < 0 || Length > MaximumLength || Length > Data.Length - Separator - 1) return false;
        Offset = Separator + 1;
        Value = Data.Substring(Offset, Length);
        Offset += Length;
        return true;
    }
}
