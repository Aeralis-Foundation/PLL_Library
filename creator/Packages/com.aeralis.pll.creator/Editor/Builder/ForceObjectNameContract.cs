#nullable enable

namespace PLL.Library
{
    internal static class ForceObjectNameContract
    {
        internal const string Requirement = "must be 1 to 256 UTF-16 code units, contain a non-whitespace character, "
            + "contain no control characters, and use only well-formed surrogate pairs";

        internal static bool IsValid(string? Value)
        {
            if (Value is null || Value.Length is < 1 or > 256 || string.IsNullOrWhiteSpace(Value)) return false;
            for (var Index = 0; Index < Value.Length; Index++)
            {
                var Character = Value[Index];
                if (char.IsControl(Character)) return false;
                if (char.IsHighSurrogate(Character))
                {
                    if (++Index >= Value.Length || !char.IsLowSurrogate(Value[Index])) return false;
                }
                else if (char.IsLowSurrogate(Character)) return false;
            }
            return true;
        }
    }
}