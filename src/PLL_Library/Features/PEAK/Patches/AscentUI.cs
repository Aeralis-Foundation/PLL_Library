using System;
using HarmonyLib;

namespace PLL.Library;

[HarmonyPatch(typeof(global::AscentUI), "Start")]
internal static class AscentUIStartPatch
{
    static global::AscentUI? Current;
    static string? Ascent;
    static bool LabelApplied;
    static ResolvedRunConfiguration? ResolvedConfiguration;

    [HarmonyPostfix]
    static void Postfix(global::AscentUI __instance)
    {
        StopAdvertising();
        if (!FileManager.AdvertiseLayout.Value) return;
        Current = __instance;
        Ascent = __instance.text.text;
        TerminalLayoutSelection.Changed += RefreshSelection;
        if (ResolvedConfiguration is { } Configuration) ApplyResolved(Configuration);
        else ApplyLocal();
    }

    internal static void RefreshResolved(ResolvedRunConfiguration Configuration)
    {
        ResolvedConfiguration = Configuration ?? throw new ArgumentNullException(nameof(Configuration));
        ApplyResolved(Configuration);
    }

    internal static void ResetResolved()
    {
        ResolvedConfiguration = null;
        ApplyLocal();
    }

    static void RefreshSelection()
    {
        if (!FileManager.AdvertiseLayout.Value)
        {
            StopAdvertising();
            return;
        }
        if (ResolvedConfiguration is null) ApplyLocal();
    }

    static void ApplyResolved(ResolvedRunConfiguration Configuration) =>
        Refresh(Configuration.Mode, Configuration.Layout.ReplaceGenerated);

    static void ApplyLocal() => Refresh(
        MapLayoutModeResolver.Resolve(
            TerminalLayoutSelection.SelectedId,
            TerminalLayoutSelection.IsOrderEnabled(FileManager.DoOrder.Value),
            FileManager.GenerativeMap.Value,
            FileManager.InfiniteMap.Value),
        FileManager.ReplaceGenerated.Value);

    static void Refresh(MapLayoutMode Mode, bool ReplaceGenerated)
    {
        if (!FileManager.AdvertiseLayout.Value)
        {
            StopAdvertising();
            return;
        }
        if (Current is not { } Ui || !Ui || Ascent is null) return;
        var Label = Mode switch
        {
            MapLayoutMode.Ordered => "Modified",
            MapLayoutMode.Generative => "Generative",
            MapLayoutMode.Infinite => "Infinite",
            MapLayoutMode.Base when ReplaceGenerated => "Randomized",
            MapLayoutMode.Base => null,
            _ => throw new InvalidOperationException($"Unknown map layout mode {(int)Mode}.")
        };
        if (Label is null)
        {
            RestoreLabel();
            return;
        }
        var Title = !global::RunSettings.IsCustomRun && global::Ascents.currentAscent == 0
            ? global::AscentData.Instance.ascents[2].localizedTitle
            : Ascent;
        Ui.text.text = $"{Title}\n<size=75%>{Label}</size>";
        LabelApplied = true;
    }

    static void RestoreLabel()
    {
        if (!LabelApplied) return;
        if (Current is { } Ui && Ui && Ascent is not null) Ui.text.text = Ascent;
        LabelApplied = false;
    }

    static void StopAdvertising()
    {
        RestoreLabel();
        TerminalLayoutSelection.Changed -= RefreshSelection;
        Current = null;
        Ascent = null;
    }
}
