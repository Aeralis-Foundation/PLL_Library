using System;
using System.Globalization;
using System.Text;

namespace PLL.Library;

internal static class QuicksaveLayoutIdentity
{
    const string LayoutIdentityRevision = "25";
    const int CanonicalInactiveSpawnChance = 100;

    internal static bool TryUpgradeSpatialIdentity(string Saved, out string Current)
    {
        Current = string.Empty;
        var Offset = 0;
        if (!LengthPrefixedText.TryRead(Saved, ref Offset, out var Revision) || Revision != LayoutIdentityRevision) return false;
        var Writer = new StringBuilder();
        LengthPrefixedText.Append(Writer, Revision);
        string? Last = null;
        while (Offset < Saved.Length)
        {
            if (!LengthPrefixedText.TryRead(Saved, ref Offset, out var Value)) return false;
            if (Last is not null) LengthPrefixedText.Append(Writer, Last);
            Last = Value;
        }
        if (Last is null || !CustomBiomeConfigRegistry.TryParseSnapshot(Last, out var Biomes, LegacySpatial: true)) return false;
        LengthPrefixedText.Append(Writer, Biomes.Serialized);
        Current = Writer.ToString();
        return true;
    }

    readonly struct EffectiveLayoutIdentityConfiguration
    {
        internal EffectiveLayoutIdentityConfiguration(
            LayoutId SelectedLayout,
            string ConfiguredOrder,
            bool OrderEnabled,
            bool GenerativeEnabled,
            bool InfiniteEnabled,
            string RepeatableOrder,
            string ChanceList,
            int MapScale,
            int StartingIndex,
            bool RemoveOldEnabled,
            bool WaterFollowsBeachEnabled,
            bool VoidWaterFollowsNadirEnabled,
            bool RelocatePlaneEnabled,
            bool NoWaterEnabled,
            bool NoVoidWaterEnabled,
            bool NoPlaneEnabled,
            BiomeLayoutConfigSnapshot BiomeConfiguration)
        {
            var Mode = MapLayoutModeResolver.Resolve(
                SelectedLayout, OrderEnabled, GenerativeEnabled, InfiniteEnabled);
            LayoutOrder = Mode == MapLayoutMode.Ordered ? ConfiguredOrder : string.Empty;
            DoOrder = Mode == MapLayoutMode.Ordered;
            GenerativeMap = Mode == MapLayoutMode.Generative;
            InfiniteMap = Mode == MapLayoutMode.Infinite;
            Repeatable = InfiniteMap ? RepeatableOrder : string.Empty;
            Chances = InfiniteMap ? ChanceList : string.Empty;
            Scale = InfiniteMap ? MapScale : 0;
            BeginIndex = InfiniteMap ? StartingIndex : 0;
            RemoveOld = InfiniteMap && RemoveOldEnabled;
            WaterFollowsBeach = !NoWaterEnabled && WaterFollowsBeachEnabled;
            VoidWaterFollowsNadir = !NoVoidWaterEnabled && VoidWaterFollowsNadirEnabled;
            RelocatePlane = !NoPlaneEnabled && RelocatePlaneEnabled;
            BiomeConfigs = GenerativeMap ? BiomeConfiguration : NormalizeInactiveSpawnChances(BiomeConfiguration);
        }

        internal string LayoutOrder { get; }
        internal bool DoOrder { get; }
        internal bool GenerativeMap { get; }
        internal bool InfiniteMap { get; }
        internal string Repeatable { get; }
        internal string Chances { get; }
        internal int Scale { get; }
        internal int BeginIndex { get; }
        internal bool RemoveOld { get; }
        internal bool WaterFollowsBeach { get; }
        internal bool VoidWaterFollowsNadir { get; }
        internal bool RelocatePlane { get; }
        internal BiomeLayoutConfigSnapshot BiomeConfigs { get; }
    }

    internal static string RuntimeLayoutIdentity(ResolvedRunConfiguration Configuration)
    {
        if (Configuration is null) throw new ArgumentNullException(nameof(Configuration));
        SegmentRegistry.SealStartup();
        return LayoutIdentity(
            Configuration.Layout.SelectedLayoutId,
            Configuration.Layout.IsLibraryBase,
            Configuration.Layout.Seed,
            Configuration.Layout.ReplaceGenerated,
            Configuration.Layout.Order,
            Configuration.Layout.OrderEnabled,
            Configuration.Layout.Generative,
            Configuration.Layout.Infinite,
            Configuration.Layout.Repeatable,
            Configuration.Layout.Chances,
            Configuration.Layout.Scale,
            Configuration.Layout.BeginIndex,
            Configuration.Layout.RemoveOld,
            Configuration.Terminal.Winnable,
            Configuration.Terminal.PeakAtEnd,
            Configuration.Environment.WaterFollowsBeach,
            Configuration.Environment.VoidWaterFollowsNadir,
            Configuration.Environment.RelocatePlane,
            Configuration.Environment.RelocateStart,
            Configuration.LazyGeneration,
            Configuration.Environment.Fogless,
            Configuration.Environment.NoWater,
            Configuration.Environment.NoVoidWater,
            Configuration.Environment.NoPlane,
            Configuration.Environment.RemoveOldZones,
            Configuration.Terminal.GemlessNadir,
            Configuration.Terminal.ValidNadir,
            Configuration.Environment.NoRisingGhosts,
            Configuration.Environment.NoSleepFog,
            Configuration.Environment.NoSkyProps,
            Configuration.Biomes);
    }
    static string? CreateLocalLayoutIdentity(
        CapturedLayoutSelection Selection,
        int MapSeed,
        BiomeLayoutConfigSnapshot Biomes)
    {
        var Repeatable = FileManager.InfiniteMapRepeatable.Value;
        var Chances = FileManager.InfiniteMapChances.Value;
        if (!LayoutChoiceEncoding.IsValid(Selection.RawOrder)
            || !LayoutChoiceEncoding.IsValid(Repeatable) || !LayoutChoiceEncoding.IsValid(Chances)) return null;
        SegmentRegistry.SealStartup();
        return LayoutIdentity(Selection.Id, Selection.IsLibraryBase,
            MapSeed, FileManager.ReplaceGenerated.Value, Selection.RawOrder, Selection.DoOrder,
            FileManager.GenerativeMap.Value, FileManager.InfiniteMap.Value,
            Repeatable, Chances,
            FileManager.InfiniteMapScale.Value, FileManager.InfiniteMapBeginIndex.Value,
            FileManager.InfiniteMapRemoveOld.Value, FileManager.Winnable.Value,
            FileManager.PeakAtEnd.Value, FileManager.WaterFollowsBeach.Value,
            FileManager.VoidWaterFollowsNadir.Value, FileManager.RelocatePlane.Value,
            FileManager.RelocateStart.Value, FileManager.LazyGeneration.Value, FileManager.Fogless.Value,
            FileManager.NoWater.Value, FileManager.NoVoidWater.Value, FileManager.NoPlane.Value,
            FileManager.RemoveOld.Value, FileManager.GemlessNadir.Value, FileManager.ValidNadir.Value,
            FileManager.NoRisingGhosts.Value, FileManager.NoSleepFog.Value,
            FileManager.NoSkyProps.Value, Biomes);
    }

    internal static MapLayoutMode LocalMode(LayoutId SelectedLayout, bool? OrderEnabledOverride = null)
    {
        var EffectiveOrder = !SelectedLayout.IsDefault
            || (OrderEnabledOverride ?? TerminalLayoutSelection.IsOrderEnabled(FileManager.DoOrder.Value));
        return MapLayoutModeResolver.Resolve(
            SelectedLayout, EffectiveOrder, FileManager.GenerativeMap.Value, FileManager.InfiniteMap.Value);
    }

    static string LayoutIdentity(
        LayoutId SelectedLayoutId,
        bool IsLibraryBase,
        int Seed,
        bool ReplaceGenerated,
        string LayoutOrder,
        bool DoOrder,
        bool GenerativeMap,
        bool InfiniteMap,
        string Repeatable,
        string Chances,
        int Scale,
        int BeginIndex,
        bool RemoveOld,
        bool Winnable,
        bool PeakAtEnd,
        bool WaterFollowsBeach,
        bool VoidWaterFollowsNadir,
        bool RelocatePlane,
        bool RelocateStart,
        bool LazyGeneration,
        bool Fogless,
        bool NoWater,
        bool NoVoidWater,
        bool NoPlane,
        bool RemoveOldZones,
        bool GemlessNadir,
        bool ValidNadir,
        bool NoRisingGhosts,
        bool NoSleepFog,
        bool NoSkyProps,
        BiomeLayoutConfigSnapshot BiomeConfigs)
    {
        var Effective = new EffectiveLayoutIdentityConfiguration(
            SelectedLayoutId, LayoutOrder, DoOrder, GenerativeMap, InfiniteMap,
            Repeatable, Chances, Scale, BeginIndex, RemoveOld,
            WaterFollowsBeach, VoidWaterFollowsNadir, RelocatePlane,
            NoWater, NoVoidWater, NoPlane, BiomeConfigs);
        LayoutOrder = Effective.LayoutOrder;
        DoOrder = Effective.DoOrder;
        GenerativeMap = Effective.GenerativeMap;
        InfiniteMap = Effective.InfiniteMap;
        Repeatable = Effective.Repeatable;
        Chances = Effective.Chances;
        Scale = Effective.Scale;
        BeginIndex = Effective.BeginIndex;
        RemoveOld = Effective.RemoveOld;
        WaterFollowsBeach = Effective.WaterFollowsBeach;
        VoidWaterFollowsNadir = Effective.VoidWaterFollowsNadir;
        RelocatePlane = Effective.RelocatePlane;
        BiomeConfigs = Effective.BiomeConfigs;

        var Result = new StringBuilder();
        LengthPrefixedText.Append(Result, LayoutIdentityRevision);
        LengthPrefixedText.Append(Result, SegmentRegistry.StartupManifestRevision);
        LengthPrefixedText.Append(Result, LayoutRegistry.ManifestRevision);
        LengthPrefixedText.Append(Result, SelectedLayoutId.Value);
        LengthPrefixedText.Append(Result, IsLibraryBase ? "1" : "0");
        LengthPrefixedText.Append(Result, Seed.ToString(CultureInfo.InvariantCulture));
        LengthPrefixedText.Append(Result, ReplaceGenerated ? "1" : "0");
        LengthPrefixedText.Append(Result, LayoutOrder);
        LengthPrefixedText.Append(Result, DoOrder ? "1" : "0");
        LengthPrefixedText.Append(Result, GenerativeMap ? "1" : "0");
        LengthPrefixedText.Append(Result, InfiniteMap ? "1" : "0");
        LengthPrefixedText.Append(Result, Repeatable);
        LengthPrefixedText.Append(Result, Chances);
        LengthPrefixedText.Append(Result, Scale.ToString(CultureInfo.InvariantCulture));
        LengthPrefixedText.Append(Result, BeginIndex.ToString(CultureInfo.InvariantCulture));
        LengthPrefixedText.Append(Result, RemoveOld ? "1" : "0");
        LengthPrefixedText.Append(Result, Winnable ? "1" : "0");
        LengthPrefixedText.Append(Result, PeakAtEnd ? "1" : "0");
        LengthPrefixedText.Append(Result, WaterFollowsBeach ? "1" : "0");
        LengthPrefixedText.Append(Result, VoidWaterFollowsNadir ? "1" : "0");
        LengthPrefixedText.Append(Result, RelocatePlane ? "1" : "0");
        LengthPrefixedText.Append(Result, RelocateStart ? "1" : "0");
        LengthPrefixedText.Append(Result, LazyGeneration ? "1" : "0");
        LengthPrefixedText.Append(Result, Fogless ? "1" : "0");
        LengthPrefixedText.Append(Result, NoWater ? "1" : "0");
        LengthPrefixedText.Append(Result, NoVoidWater ? "1" : "0");
        LengthPrefixedText.Append(Result, NoPlane ? "1" : "0");
        LengthPrefixedText.Append(Result, RemoveOldZones ? "1" : "0");
        LengthPrefixedText.Append(Result, GemlessNadir ? "1" : "0");
        LengthPrefixedText.Append(Result, ValidNadir ? "1" : "0");
        LengthPrefixedText.Append(Result, NoRisingGhosts ? "1" : "0");
        LengthPrefixedText.Append(Result, NoSleepFog ? "1" : "0");
        LengthPrefixedText.Append(Result, NoSkyProps ? "1" : "0");
        LengthPrefixedText.Append(Result, BiomeConfigs.Serialized);
        return Result.ToString();
    }

    static BiomeLayoutConfigSnapshot NormalizeInactiveSpawnChances(BiomeLayoutConfigSnapshot Source)
    {
        var Values = Source.Values;
        var RequiresNormalization = false;
        for (var I = 0; I < Values.Count; I++)
            if (Values[I].SpawnChance != CanonicalInactiveSpawnChance)
            {
                RequiresNormalization = true;
                break;
            }
        if (!RequiresNormalization) return Source;

        var Normalized = new BiomeLayoutConfigValues[Values.Count];
        for (var I = 0; I < Normalized.Length; I++)
        {
            var Value = Values[I];
            Normalized[I] = new BiomeLayoutConfigValues(
                Value.Id, Value.Favorites, Value.Overwrites, Value.Before, Value.After,
                CanonicalInactiveSpawnChance, Value.Variations);
        }
        return new BiomeLayoutConfigSnapshot(Normalized, Source.NativeVariations, Source.SpatialValues);
    }

    internal static bool Matches(
        string SavedIdentity,
        CapturedLayoutSelection Selection,
        int MapSeed,
        BiomeLayoutConfigSnapshot Biomes)
    {
        var Current = CreateLocalLayoutIdentity(Selection, MapSeed, Biomes);
        return string.Equals(SavedIdentity, Current, StringComparison.Ordinal);
    }
}
