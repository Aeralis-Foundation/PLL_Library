namespace PLL.Library;

internal static class LayoutEntropy
{
    internal static uint Append(uint Hash, string Value)
    {
        foreach (var Character in Value) Hash = (Hash ^ Character) * 16777619u;
        return Hash;
    }
}
