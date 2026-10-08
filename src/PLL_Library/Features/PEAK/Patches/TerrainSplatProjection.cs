using System;
using HarmonyLib;

namespace PLL.Library;

internal static class TerrainSplatProjection
{
    static TerrainSplat? Owner;
    static MapSlot[] ProjectedSlots = Array.Empty<MapSlot>();
    static bool Started;
    static bool Projected;

    internal static void Capture()
    {
        var Terrain = UnityEngine.Object.FindAnyObjectByType<TerrainSplat>();
        if (!Terrain || Owner == Terrain) return;
        Owner = Terrain;
        ProjectedSlots = Array.Empty<MapSlot>();
        Started = false;
        Projected = false;
    }

    internal static void Bind()
    {
        if (!Map.HasPublishedLayout)
        {
            Release(true);
            return;
        }
        Capture();
        if (!Owner || !Started || ReferenceEquals(ProjectedSlots, Map.Slots)) return;
        Owner.Generate(TerrainBrush.BrushType.All);
        ProjectedSlots = Map.Slots;
        Projected = true;
    }

    internal static void Refresh()
    {
        ProjectedSlots = Array.Empty<MapSlot>();
        Bind();
    }

    internal static void Release(bool RestoreNative)
    {
        var Terrain = Owner;
        var Regenerate = RestoreNative && Terrain && Started && Projected;
        Owner = null;
        ProjectedSlots = Array.Empty<MapSlot>();
        Started = false;
        Projected = false;
        if (Regenerate) Terrain!.Generate(TerrainBrush.BrushType.All);
    }

    [HarmonyPatch(typeof(TerrainSplat), "Start")]
    static class StartPatch
    {
        [HarmonyPostfix]
        static void Postfix(TerrainSplat __instance)
        {
            Owner = __instance;
            Started = true;
            Projected = Map.HasPublishedLayout;
            ProjectedSlots = Projected ? Map.Slots : Array.Empty<MapSlot>();
        }
    }
}
