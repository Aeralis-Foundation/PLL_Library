namespace PLL.Library;

internal enum MapLayoutMode
{
    Base,
    Ordered,
    Generative,
    Infinite
}

internal static class MapLayoutModeResolver
{
    internal static MapLayoutMode Resolve(
        LayoutId selectedLayoutId,
        bool doOrder,
        bool generativeMap,
        bool infiniteMap)
    {
        if (!selectedLayoutId.IsDefault || doOrder) return MapLayoutMode.Ordered;
        if (generativeMap) return MapLayoutMode.Generative;
        return infiniteMap ? MapLayoutMode.Infinite : MapLayoutMode.Base;
    }
}
