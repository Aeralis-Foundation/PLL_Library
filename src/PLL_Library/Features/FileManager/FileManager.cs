using System.IO;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace PLL.Library;

public static class FileManager
{
    static ConfigFile Config = null!;
    static IReadOnlyDictionary<string, string[]>? NativeVariationChoices;
    internal static readonly Dictionary<string, ConfigEntry<string>> NativeVariations = new(StringComparer.Ordinal);
    internal static IReadOnlyList<BiomeSpatialConfig> NativeSpatial { get; private set; } = Array.Empty<BiomeSpatialConfig>();
    internal const string DefaultLayoutOrder = "Beach > Tropics > Roots > Alpine > Mesa > Volcano > Citadel > Nadir";
    internal const string DefaultRepeatable = "";
    internal const string DefaultChances = "";

    public static ConfigEntry<string> LayoutOrder { get; private set; } = null!;
    public static ConfigEntry<string> Seed { get; private set; } = null!;
    public static ConfigEntry<bool> ReplaceGenerated { get; private set; } = null!;
    internal static ConfigEntry<bool> GetNote { get; private set; } = null!;
    public static ConfigEntry<bool> AdvertiseLayout { get; private set; } = null!;
    public static ConfigEntry<bool> DisplayProgress { get; private set; } = null!;
    public static ConfigEntry<int> LightMapMinimum { get; private set; } = null!;
    public static ConfigEntry<bool> DoOrder { get; private set; } = null!;
    public static ConfigEntry<bool> GenerativeMap { get; private set; } = null!;
    public static ConfigEntry<bool> InfiniteMap { get; private set; } = null!;
    public static ConfigEntry<string> InfiniteMapRepeatable { get; private set; } = null!;
    public static ConfigEntry<string> InfiniteMapChances { get; private set; } = null!;
    public static ConfigEntry<int> InfiniteMapScale { get; private set; } = null!;
    public static ConfigEntry<int> InfiniteMapBeginIndex { get; private set; } = null!;
    public static ConfigEntry<bool> InfiniteMapRemoveOld { get; private set; } = null!;
    public static ConfigEntry<bool> Winnable { get; private set; } = null!;
    public static ConfigEntry<bool> PeakAtEnd { get; private set; } = null!;
    public static ConfigEntry<bool> GemlessNadir { get; private set; } = null!;
    public static ConfigEntry<bool> WaterFollowsBeach { get; private set; } = null!;
    public static ConfigEntry<bool> VoidWaterFollowsNadir { get; private set; } = null!;
    public static ConfigEntry<bool> RelocatePlane { get; private set; } = null!;
    public static ConfigEntry<bool> RelocateStart { get; private set; } = null!;
    public static ConfigEntry<bool> ValidNadir { get; private set; } = null!;
    public static ConfigEntry<bool> LazyGeneration { get; private set; } = null!;
    public static ConfigEntry<bool> Fogless { get; private set; } = null!;
    public static ConfigEntry<bool> NoRisingGhosts { get; private set; } = null!;
    public static ConfigEntry<bool> NoSleepFog { get; private set; } = null!;
    public static ConfigEntry<bool> NoWater { get; private set; } = null!;
    public static ConfigEntry<bool> NoVoidWater { get; private set; } = null!;
    public static ConfigEntry<bool> NoPlane { get; private set; } = null!;
    public static ConfigEntry<bool> NoSkyProps { get; private set; } = null!;
    public static ConfigEntry<bool> RemoveOld { get; private set; } = null!;

    internal static void Initialize(BepInPlugin OwnerMetadata)
    {
        var DirectoryPath = Path.Combine(Paths.ConfigPath, "AF", "PLL_Library");
        Directory.CreateDirectory(DirectoryPath);
        CustomBiomeConfigRegistry.Initialize(OwnerMetadata, Path.Combine(DirectoryPath, "Custom Segments"));

        var Bindings = ConfigDocument.Open(Path.Combine(DirectoryPath, "config.cfg"), OwnerMetadata);
        Config = Bindings.Config;
        NativeSpatial = Bindings.NativeSpatial;
        foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
            NativeVariations.Add(Biome, Bindings.NativeVariations[Biome]);
        LayoutOrder = Bindings.LayoutOrder;
        Seed = Bindings.Seed;
        ReplaceGenerated = Bindings.ReplaceGenerated;
        GetNote = Bindings.GetNote;
        AdvertiseLayout = Bindings.AdvertiseLayout;
        DisplayProgress = Bindings.DisplayProgress;
        LightMapMinimum = Bindings.LightMapMinimum;
        DoOrder = Bindings.DoOrder;
        GenerativeMap = Bindings.GenerativeMap;
        InfiniteMap = Bindings.InfiniteMap;
        InfiniteMapRepeatable = Bindings.InfiniteMapRepeatable;
        InfiniteMapChances = Bindings.InfiniteMapChances;
        InfiniteMapScale = Bindings.InfiniteMapScale;
        InfiniteMapBeginIndex = Bindings.InfiniteMapBeginIndex;
        InfiniteMapRemoveOld = Bindings.InfiniteMapRemoveOld;
        Winnable = Bindings.Winnable;
        PeakAtEnd = Bindings.PeakAtEnd;
        GemlessNadir = Bindings.GemlessNadir;
        WaterFollowsBeach = Bindings.WaterFollowsBeach;
        VoidWaterFollowsNadir = Bindings.VoidWaterFollowsNadir;
        RelocatePlane = Bindings.RelocatePlane;
        RelocateStart = Bindings.RelocateStart;
        ValidNadir = Bindings.ValidNadir;
        LazyGeneration = Bindings.LazyGeneration;
        Fogless = Bindings.Fogless;
        NoRisingGhosts = Bindings.NoRisingGhosts;
        NoSleepFog = Bindings.NoSleepFog;
        NoWater = Bindings.NoWater;
        NoVoidWater = Bindings.NoVoidWater;
        NoPlane = Bindings.NoPlane;
        NoSkyProps = Bindings.NoSkyProps;
        RemoveOld = Bindings.RemoveOld;
    }

    internal static void BindNativeVariationChoices(IReadOnlyDictionary<string, string[]> ChoicesByBiome)
    {
        NativeVariationChoices = ChoicesByBiome;
        Config.SaveOnConfigSet = false;
        try { BindNativeVariationDescriptions(ChoicesByBiome); }
        finally { Config.SaveOnConfigSet = true; }
        try { ConfigDocument.PublishNativeVariationDescriptions(Config.ConfigFilePath, ChoicesByBiome); }
        catch (Exception Failure) when (Failure is IOException or UnauthorizedAccessException or DecoderFallbackException
            || Failure is AggregateException Aggregate && Aggregate.InnerExceptions.All(
                Cause => Cause is IOException or UnauthorizedAccessException or DecoderFallbackException))
        {
            Plugin.PLLLog.LogWarning(("Native variation choices could not be written. "
                + (Failure is DecoderFallbackException
                    ? $"Save '{Config.ConfigFilePath}' as UTF-8. "
                    : $"Check write access to '{Config.ConfigFilePath}'. "), Failure));
        }
        var Host = RunConfigurationResolution.RequireCurrent().Biomes.NativeVariations;
        foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
            if (Array.IndexOf(ChoicesByBiome[Biome], Host[Biome]) < 0)
                throw new InvalidDataException($"Native biome '{Biome}' has an unknown variation '{Host[Biome]}'. Select Random or a listed variation.");
    }

    static void BindNativeVariationDescriptions(IReadOnlyDictionary<string, string[]> ChoicesByBiome)
    {
        foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
        {
            var Choices = ChoicesByBiome[Biome];
            var Local = NativeVariations[Biome].Value;
            if (Array.IndexOf(Choices, Local) < 0)
            {
                var DisplayChoices = new string[Choices.Length + 1];
                Array.Copy(Choices, DisplayChoices, Choices.Length);
                DisplayChoices[Choices.Length] = Local;
                Choices = DisplayChoices;
            }
            Config.Remove(NativeVariations[Biome].Definition);
            NativeVariations[Biome] = Config.Bind("Variations", Biome, "Random", new ConfigDescription(
                "Selects a native variation for every occurrence; Random keeps PEAK's selection.",
                new AcceptableValueList<string>(Choices)));
            NativeVariations[Biome].Value = Local;
        }
    }

    internal static IReadOnlyDictionary<string, string> ReloadRunConfiguration(
        IReadOnlyDictionary<string, string>? RestoredNativeVariations)
    {
        var Document = ConfigDocument.Read(Config.ConfigFilePath, RestoredNativeVariations is not null);
        var Native = RestoredNativeVariations;
        if (Native is null)
        {
            var Raw = Document.VariationSettings();
            var Captured = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
            {
                var Selected = Raw.TryGetValue("Variations\n" + Biome, out var Value) ? Value : "Random";
                if (Selected.Length == 0 || Selected.IndexOf('\n') >= 0 || Selected.IndexOf('\r') >= 0)
                    throw new InvalidDataException($"Native biome '{Biome}' has an invalid variation name.");
                Captured.Add(Biome, Selected);
            }
            Native = Captured;
        }
        Config.SaveOnConfigSet = false;
        try
        {
            foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
            {
                Config.Remove(NativeVariations[Biome].Definition);
                NativeVariations[Biome] = Config.Bind("Variations", Biome, "Random",
                    "Selects a native variation; available choices are fetched when WilIsland loads.");
            }
            Document.Apply(Config);
            if (NativeVariationChoices is { } Choices)
            {
                BindNativeVariationDescriptions(Choices);
                if (RestoredNativeVariations is null) Config.Save();
            }
        }
        finally { Config.SaveOnConfigSet = true; }
        return Native;
    }

}
