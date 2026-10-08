using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace PLL.Library;

internal sealed class ConfigDocument
{
    const string LegacyPublicSchemaRevision = "17";
    const string PublicSchemaRevision20 = "20";
    const string PublicSchemaRevision21 = "21";
    const string LightMapCategorySchemaRevision = "22";
    const string PublicSchemaRevision23 = "23";
    const string PublicSchemaRevision24 = "24";
    const string PreviousPublicSchemaRevision = "25";
    const string PublicSchemaRevision26 = "26";
    const string NativeSpatialSchemaRevision = "27";
    const string CurrentSchemaRevision = "28";
    static readonly ConfigDefinition LegacyPeakCampfire = new("Behaviors", "Treat Peak As Normal Campfire");
    static readonly ConfigDefinition LegacyDynamicLightMap = new("Optimizations", "Dynamic Light Map");
    static readonly ConfigDefinition LegacyLightMapMinimum = new("PLL", "Light Map Minimum");

    readonly string Path;
    readonly bool Exists;
    readonly Dictionary<string, Dictionary<string, RawSetting>> RawSettings = new(StringComparer.Ordinal);

    ConfigDocument(string Path, bool Exists, IEnumerable<string> Lines, bool IgnoreNativeVariationDuplicates = false)
    {
        this.Path = Path;
        this.Exists = Exists;
        Parse(Lines, IgnoreNativeVariationDuplicates);
        SchemaRevision = !Exists ? CurrentSchemaRevision
            : TryGet("Version", "SchemaRevision", out var Schema) ? Schema.Value
            : string.Empty;
    }

    string SchemaRevision { get; }

    internal static ConfigBindings Open(string Path, BepInPlugin OwnerMetadata)
    {
        var Document = Read(Path);
        if (Document.Exists
            && Document.SchemaRevision is not (LegacyPublicSchemaRevision
                or PublicSchemaRevision20 or PublicSchemaRevision21
                or LightMapCategorySchemaRevision
                or PublicSchemaRevision23 or PublicSchemaRevision24 or PreviousPublicSchemaRevision
                or PublicSchemaRevision26 or NativeSpatialSchemaRevision or CurrentSchemaRevision))
            throw new InvalidDataException(
                $"Configuration schema '{Document.SchemaRevision}' is unsupported; expected '{CurrentSchemaRevision}'.");
        var Bindings = ConfigBindings.Bind(Path, OwnerMetadata, CurrentSchemaRevision);
        Document.ApplySchemaMigration(Bindings);
        var MismatchedSettings = Document.FindMismatchedSettings(Bindings);
        var Refresh = Document.SchemaRevision != CurrentSchemaRevision
            || MismatchedSettings.Count != 0;

        foreach (var Setting in MismatchedSettings)
            if (Setting.Migratable) Setting.Entry.BoxedValue = Setting.Entry.DefaultValue;

        if (Refresh) Bindings = Document.Replace(Bindings, OwnerMetadata);
        else if (!Document.Exists) Bindings.Config.Save();
        Bindings.Config.SaveOnConfigSet = true;
        return Bindings;
    }

    internal static ConfigDocument Read(string Path, bool IgnoreNativeVariationDuplicates = false)
    {
        var Exists = File.Exists(Path);
        return new ConfigDocument(Path, Exists,
            Exists ? File.ReadLines(Path, new UTF8Encoding(false, true)) : Array.Empty<string>(), IgnoreNativeVariationDuplicates);
    }

    internal void RequireCustomBiomeOwner(string Id)
    {
        if (!TryGet("Biome", "ID", out var Owner)
            || (string)TomlTypeConverter.ConvertToValue(Owner.Value, typeof(string)) != Id)
            throw new InvalidDataException($"Configuration '{Path}' does not belong to biome '{Id}'. Move the file before registering this biome.");
    }

    internal static Dictionary<string, string> ReadVariationSettings(string Path)
        => Read(Path).VariationSettings();

    internal bool HasSpatialSettings(string Section) => TryGet(Section, "Wall Boxes", out _) && TryGet(Section, "Fog Cutout", out _);

    internal Dictionary<string, string> VariationSettings()
    {
        var Result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var Section in RawSettings)
        {
            if (Section.Key != "Variations" && !Section.Key.StartsWith("Variations.", StringComparison.Ordinal)) continue;
            foreach (var Setting in Section.Value)
                if (Section.Key == "Variations" || Setting.Key == "Selected"
                    || Setting.Key.StartsWith("Weight.", StringComparison.Ordinal)
                    || Setting.Key.StartsWith(VariationGroupConfig.WeightPrefix, StringComparison.Ordinal))
                {
                    Result.Add(Section.Key + "\n" + Setting.Key, Setting.Value.Value);
                    if (Setting.Value.VariationEntryId is { } EntryId)
                        Result.Add(Section.Key + "\n" + VariationGroupConfig.IdentityPrefix
                            + Setting.Key.Substring(VariationGroupConfig.WeightPrefix.Length), EntryId);
                }
        }
        return Result;
    }

    internal static void PublishNativeVariationDescriptions(
        string Path, IReadOnlyDictionary<string, string[]> ChoicesByBiome)
    {
        string Source;
        Encoding Encoding;
        using var OriginalFile = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using (var Detection = new StreamReader(OriginalFile, new UTF8Encoding(false, true), true, 1024, true))
        {
            Detection.Peek();
            Encoding = (Encoding)Detection.CurrentEncoding.Clone();
            Encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            Encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            OriginalFile.Position = Encoding.GetPreamble().Length;
            using var Reader = new StreamReader(OriginalFile, Encoding, false, 1024, true);
            Source = Reader.ReadToEnd();
        }
        var Result = new StringBuilder(Source.Length);
        var PendingLines = new List<(int Start, int ContentLength, int Length)>();
        var InVariations = false;
        var NewLine = Environment.NewLine;
        for (var Position = 0; Position < Source.Length;)
        {
            var Start = Position;
            while (Position < Source.Length && Source[Position] is not ('\r' or '\n')) Position++;
            var ContentLength = Position - Start;
            if (Position < Source.Length)
            {
                var End = Position++;
                if (Source[End] == '\r' && Position < Source.Length && Source[Position] == '\n') Position++;
                NewLine = Source.Substring(End, Position - End);
            }
            var Length = Position - Start;
            var Line = Source.Substring(Start, ContentLength).Trim();
            if (Line.StartsWith("[", StringComparison.Ordinal) && Line.EndsWith("]", StringComparison.Ordinal))
            {
                AppendPending(null);
                InVariations = Line == "[Variations]";
                Result.Append(Source, Start, Length);
                continue;
            }
            if (!InVariations)
            {
                Result.Append(Source, Start, Length);
                continue;
            }
            var Separator = Line.IndexOf('=');
            if (Separator < 0 || Line.StartsWith("#", StringComparison.Ordinal))
            {
                PendingLines.Add((Start, ContentLength, Length));
                continue;
            }
            AppendPending(ChoicesByBiome.TryGetValue(Line.Substring(0, Separator).Trim(), out var Choices)
                ? new AcceptableValueList<string>(Choices).ToDescriptionString() : null);
            Result.Append(Source, Start, Length);
        }
        AppendPending(null);
        var Published = Result.ToString();
        if (Published == Source) return;

        var StagingPath = Path + ".variations." + Guid.NewGuid().ToString("N") + ".new";
        var StagingFile = new FileStream(StagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (StagingFile)
            using (var Writer = new StreamWriter(StagingFile, Encoding, 1024, true)) Writer.Write(Published);
            File.Replace(StagingPath, Path, null);
        }
        catch (Exception Failure)
        {
            try { File.Delete(StagingPath); }
            catch (Exception Cleanup)
            {
                throw new AggregateException(
                    "Native variation descriptions could not publish or discard their staged file.", Failure, Cleanup);
            }
            throw;
        }

        void AppendPending(string? Description)
        {
            foreach (var Pending in PendingLines)
            {
                if (Description is not null && Source.Substring(Pending.Start, Pending.ContentLength)
                        .TrimStart().StartsWith("# Acceptable values:", StringComparison.Ordinal)) continue;
                Result.Append(Source, Pending.Start, Pending.Length);
            }
            if (Description is not null) Result.Append(Description).Append(NewLine);
            PendingLines.Clear();
        }
    }
    internal void Apply(ConfigFile Configuration)
    {
        CopyOrphans(Configuration, false);
        foreach (var Entry in ((IDictionary<ConfigDefinition, ConfigEntryBase>)Configuration).Values.ToArray())
            if (TryGet(Entry.Definition.Section, Entry.Definition.Key, out var Setting))
                Entry.SetSerializedValue(Setting.Value);
            else Entry.BoxedValue = Entry.DefaultValue;
    }

    void CopyOrphans(ConfigFile Configuration, bool Migrating)
    {
        var Orphans = Configuration.OrphanedEntries;
        Orphans.Clear();
        foreach (var Section in RawSettings.Values)
            foreach (var Setting in Section.Values)
            {
                if (Configuration.ContainsKey(Setting.Definition)
                    || Migrating && (Setting.Definition.Equals(LegacyPeakCampfire)
                        || Setting.Definition.Equals(LegacyDynamicLightMap)
                        || Setting.Definition.Equals(LegacyLightMapMinimum))) continue;
                Orphans.Add(Setting.Definition, Setting.Value);
            }
    }

    void Parse(IEnumerable<string> Lines, bool IgnoreNativeVariationDuplicates)
    {
        var Section = string.Empty;
        string? SettingType = null;
        string? PendingEntryId = null;

        foreach (var RawLine in Lines)
        {
            var Line = RawLine.Trim();
            if (Line.StartsWith("[", StringComparison.Ordinal) && Line.EndsWith("]", StringComparison.Ordinal))
            {
                if (PendingEntryId is not null)
                    throw new InvalidDataException("Variation entry marker has no weight setting.");
                Section = Line.Substring(1, Line.Length - 2);
                SettingType = null;
                continue;
            }

            if (Line.StartsWith("## " + VariationGroupConfig.EntryIdComment, StringComparison.Ordinal))
            {
                if (PendingEntryId is not null)
                    throw new InvalidDataException("Variation entry marker is repeated before its weight setting.");
                PendingEntryId = Line.Substring(3 + VariationGroupConfig.EntryIdComment.Length).Trim();
                if (!Section.StartsWith("Variations.", StringComparison.Ordinal)
                    || !PllAuthoringValueContract.IsVariationEntryId(PendingEntryId))
                    throw new InvalidDataException("Variation entry marker is invalid or outside a variation group.");
                continue;
            }

            const string TypePrefix = "# Setting type:";
            if (Line.StartsWith(TypePrefix, StringComparison.Ordinal))
            {
                SettingType = Line.Substring(TypePrefix.Length).Trim();
                continue;
            }

            var Separator = Line.IndexOf('=');
            if (Separator < 0 || Line.StartsWith("#", StringComparison.Ordinal)) continue;
            var Key = Line.Substring(0, Separator).Trim();
            if (PendingEntryId is not null && !Key.StartsWith(VariationGroupConfig.WeightPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Variation entry marker does not describe a weight setting.");
            if (Section.StartsWith("Variations.", StringComparison.Ordinal)
                && Key.StartsWith(VariationGroupConfig.WeightPrefix, StringComparison.Ordinal) && PendingEntryId is null)
                throw new InvalidDataException("Variation weight has no entry marker.");
            var Value = Line.Substring(Separator + 1).Trim();
            if (!RawSettings.TryGetValue(Section, out var SectionSettings))
            {
                SectionSettings = new Dictionary<string, RawSetting>(StringComparer.Ordinal);
                RawSettings.Add(Section, SectionSettings);
            }
            if ((Section == "Variations" && !IgnoreNativeVariationDuplicates
                    || Section.StartsWith("Variations.", StringComparison.Ordinal))
                && SectionSettings.ContainsKey(Key))
                throw new InvalidDataException($"Variation setting '{Key}' is duplicated.");
            if ((Section.StartsWith("Biome.", StringComparison.Ordinal) && Key is "Wall Boxes" or "Fog Cutout"
                    || Section == "Boundaries" && Key.EndsWith(" Wall Boxes", StringComparison.Ordinal)
                    || Section == "Fog" && Key.EndsWith(" Fog Cutout", StringComparison.Ordinal))
                && SectionSettings.TryGetValue(Key, out var Previous)
                && (Previous.Value != Value || Previous.SettingType != SettingType))
                throw new InvalidDataException($"Spatial setting '{Key}' has conflicting duplicate values or types.");
            SectionSettings[Key] = new RawSetting(
                new ConfigDefinition(Section, Key), SettingType, Value, PendingEntryId);
            SettingType = null;
            PendingEntryId = null;
        }
        if (PendingEntryId is not null)
            throw new InvalidDataException("Variation entry marker has no weight setting.");
    }

    List<BoundConfigSetting> FindMismatchedSettings(ConfigBindings Bindings)
    {
        var Mismatched = new List<BoundConfigSetting>();
        foreach (var Setting in Bindings.Settings)
        {
            if (!TryGet(Setting.Entry.Definition.Section, Setting.Entry.Definition.Key, out var Raw)) continue;
            if ((Raw.SettingType is null || Raw.SettingType == Setting.Kind.TypeName())
                && Setting.Kind.ValueMatches(Raw.Value)) continue;
            Mismatched.Add(Setting);
        }
        return Mismatched;
    }

    void ApplySchemaMigration(ConfigBindings Bindings)
    {
        if (SchemaRevision == LightMapCategorySchemaRevision
            && TryGet(LegacyLightMapMinimum.Section, LegacyLightMapMinimum.Key, out var Setting)
            && (Setting.SettingType is null || Setting.SettingType == typeof(int).Name)
            && int.TryParse(
                Setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var LightMapMinimum)) Bindings.LightMapMinimum.Value = LightMapMinimum;
        for (var Index = 0; Index < Bindings.NativeSpatial.Count; Index++)
        {
            var Section = "Biome." + BiomeLayoutConfigSnapshot.SpatialBiomeNames[Index];
            if (!RawSettings.TryGetValue(Section, out var LegacySettings)) continue;
            if (SchemaRevision != NativeSpatialSchemaRevision)
                throw new InvalidDataException($"Native spatial section '{Section}' requires schema '{NativeSpatialSchemaRevision}' migration.");
            foreach (var Legacy in LegacySettings.Values)
            {
                var Target = Legacy.Definition.Key switch
                {
                    "Wall Boxes" => Bindings.NativeSpatial[Index].Boxes,
                    "Fog Cutout" => Bindings.NativeSpatial[Index].FogCutout,
                    _ => throw new InvalidDataException($"Native spatial section '{Section}' has unknown setting '{Legacy.Definition.Key}'.")
                };
                if (Legacy.SettingType is not null && Legacy.SettingType != typeof(string).Name)
                    throw new InvalidDataException($"Native spatial setting '{Legacy.Definition.Key}' has type '{Legacy.SettingType}'; expected 'String'.");
                if (TryGet(Target.Definition.Section, Target.Definition.Key, out var Current)
                    && (Current.Value != Legacy.Value
                        || Current.SettingType is not null && Current.SettingType != typeof(string).Name))
                    throw new InvalidDataException($"Native spatial setting '{Target.Definition.Key}' conflicts with its old biome section.");
                Target.SetSerializedValue(Legacy.Value);
            }
            RawSettings.Remove(Section);
        }
    }

    bool TryGet(string Section, string Key, out RawSetting Setting)
    {
        if (RawSettings.TryGetValue(Section, out var SectionSettings)
            && SectionSettings.TryGetValue(Key, out Setting)) return true;
        Setting = default;
        return false;
    }

    ConfigBindings Replace(ConfigBindings Source, BepInPlugin OwnerMetadata)
    {
        var Values = new Dictionary<ConfigDefinition, object>();
        foreach (var Setting in Source.Settings)
            if (Setting.Migratable) Values.Add(Setting.Entry.Definition, Setting.Entry.BoxedValue);

        var StagingPath = Path + ".new";
        if (File.Exists(StagingPath)) File.Delete(StagingPath);
        try
        {
            var Staged = ConfigBindings.Bind(StagingPath, OwnerMetadata, CurrentSchemaRevision);
            CopyOrphans(Staged.Config, SchemaRevision != CurrentSchemaRevision);
            foreach (var Setting in Staged.Settings)
                if (Setting.Migratable) Setting.Entry.BoxedValue = Values[Setting.Entry.Definition];
            Staged.Config.Save();
            File.Replace(StagingPath, Path, null);
        }
        finally
        {
            if (File.Exists(StagingPath)) File.Delete(StagingPath);
        }
        return ConfigBindings.Bind(Path, OwnerMetadata, CurrentSchemaRevision);
    }

    readonly struct RawSetting
    {
        internal RawSetting(ConfigDefinition Definition, string? SettingType, string Value, string? VariationEntryId)
        {
            this.Definition = Definition;
            this.SettingType = SettingType;
            this.Value = Value;
            this.VariationEntryId = VariationEntryId;
        }

        internal string? SettingType { get; }
        internal ConfigDefinition Definition { get; }
        internal string Value { get; }
        internal string? VariationEntryId { get; }
    }
}

internal sealed class ConfigBindings
{
    const string DefaultSeed = "";
    const int DefaultScale = 8;
    readonly List<BoundConfigSetting> BoundSettings = new();

    ConfigBindings(string Path, BepInPlugin OwnerMetadata, string SchemaRevision)
    {
        Config = new ConfigFile(Path, false, OwnerMetadata) { SaveOnConfigSet = false };
        Bind("Version", "SchemaRevision", SchemaRevision, "Tracks the configuration schema.", Migratable: false);
        Seed = Bind("Map", "Seed", DefaultSeed, "Empty chooses a new random world seed; any text produces a deterministic seed.");
        ReplaceGenerated = Bind("Map", "Replace Generated", true, "Regenerates PEAK's already generated native segment props with the selected seed.");
        GetNote = Bind("PLL", "Get Note", true, "Fetches the Aeralis Foundation developer note when PEAK's main menu loads.");
        AdvertiseLayout = Bind("PLL", "Advertise Layout", false, "Shows one configured PLL layout label beneath PEAK's ascent name.");
        DisplayProgress = Bind("PLL", "Display Progress", false, "Shows the active map-processing operation and estimated remaining time above PEAK's loading text.");
        LightMapMinimum = Bind("Map", "Light Map Minimum", 25, new ConfigDescription(
            "Sets the local per-occurrence light-volume page limit in MiB.",
            new AcceptableValueRange<int>(25, 500)));
        LayoutOrder = Bind("Map", "LayoutOrder", FileManager.DefaultLayoutOrder, "Defines an ordered list of public native or custom ordinary layout segments. Volcano and Citadel each include their native lower half.");
        DoOrder = Bind("Map", "DoOrder", true, "Enables LayoutOrder for the map.");
        GenerativeMap = Bind("Map", "Generative Map", false, "Builds one deterministic layout from registered native and custom biome candidates when Layout Order is disabled.");
        InfiniteMap = Bind("Map", "InfiniteMap", false, "Enables infinite map generation.");
        InfiniteMapRepeatable = Bind("Fields", "InfiniteMap: Repeatable", FileManager.DefaultRepeatable, "Up to 255 public segment names eligible for repetition, separated by >.");
        InfiniteMapChances = Bind("Fields", "InfiniteMap: Chances", FileManager.DefaultChances, "Up to 255 non-negative weights mapped by position to InfiniteMap: Repeatable, separated by >.");
        InfiniteMapScale = Bind("Fields", "InfiniteMap: Scale", DefaultScale, new ConfigDescription("Requested total public segment count; configuration accepts 1 through 255, current PEAK navigation resolves at most 254 ordinary internal slots, and an online shared Photon room may separately reject a content-dependent generated object batch when it lacks free room ViewIDs.", new AcceptableValueRange<int>(1, byte.MaxValue)));
        InfiniteMapBeginIndex = Bind("Fields", "InfiniteMap: Begin Index", -1, "-1 inserts at the final eligible public boundary (before a terminal Nadir); other values are clamped zero-based public insertion boundaries.");
        InfiniteMapRemoveOld = Bind("Fields", "InfiniteMap: Remove Old", false, "Removes the default layout so only InfiniteMap: Repeatable segments are used.");
        Winnable = Bind("Behaviors", "Winnable", true, "Enables the configured final-zone win behavior.");
        PeakAtEnd = Bind("Behaviors", "Peak at end", false, "Attaches full Peak after the actual final public ordinary segment; terminal Volcano and Citadel retain contextual Peak.");
        GemlessNadir = Bind("Behaviors", "Gemless Nadir", false, "Allows every Nadir occurrence to load without Scout's Honor.");
        WaterFollowsBeach = Bind("Behaviors", "Water follows Beach", true, "Activates the water floor when Beach is reached; otherwise enables it beneath the first segment from the start.");
        VoidWaterFollowsNadir = Bind("Behaviors", "Void Water follows Nadir", true, "Activates PEAK's Void Water when its Nadir occurrence is reached; otherwise enables it from the start.");
        RelocatePlane = Bind("Prerequisites", "Relocate Plane", true, "Moves PEAK's original crashed plane to the first segment's explicit Start port.");
        RelocateStart = Bind("Prerequisites", "Relocate Start", true, "Moves PEAK's active four-player spawn group to the first segment's explicit Start port.");
        ValidNadir = Bind("Prerequisites", "Valid Nadir", true, "Requires preceding native or custom sources to provide all four distinct amulet opportunity categories before Nadir may require Scout's Honor; otherwise Nadir is gemless.");
        LazyGeneration = Bind("Optimizations", "Lazy Generation", true, "Defers procedural prop generation until its prerequisite zone is reached.");
        Fogless = Bind("Optimizations", "Fogless", false, "Disables the fog sphere and its relocation behavior.");
        NoRisingGhosts = Bind("Optimizations", "No Rising Ghosts", false, "Disables Nadir's rising lost-soul fog and Citadel's rising Gloom.");
        NoSleepFog = Bind("Optimizations", "No Sleep Fog", false, "Disables Gloom's stationary sleep fog field.");
        NoWater = Bind("Optimizations", "No Water", false, "Disables Beach water and all water relocation behavior.");
        NoVoidWater = Bind("Optimizations", "No Void Water", false, "Disables PEAK's Nadir-local Void Water surface and WaterZone.");
        NoPlane = Bind("Optimizations", "No Plane", false, "Removes the crashed plane and its affiliates except spawn points.");
        NoSkyProps = Bind("Optimizations", "No Sky Props", true, "Restricts each native raycast-position prop spawner to its declared collision domain and exact registered external support. Citadel treasure spawners include their temple floor; foreign domains and the Peak bridge stay excluded, and PEAK's effective global trigger behavior stays unchanged.");
        RemoveOld = Bind("Optimizations", "Remove Old", true, "Destroys map zones older than the immediately previous zone after progression.");
        foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
            NativeVariations.Add(Biome, Bind("Variations", Biome, "Random",
                "Selects a native variation; available choices are fetched when WilIsland loads. Random keeps PEAK's selection."));
        var Spatial = new BiomeSpatialConfig[BiomeLayoutConfigSnapshot.SpatialBiomeNames.Count];
        for (var Index = 0; Index < Spatial.Length; Index++)
        {
            var Name = BiomeLayoutConfigSnapshot.SpatialBiomeNames[Index];
            Spatial[Index] = new BiomeSpatialConfig(Config, BiomeLayoutConfigSnapshot.NativeSpatialId(Name),
                new ConfigDefinition("Boundaries", Name + " Wall Boxes"), new ConfigDefinition("Fog", Name + " Fog Cutout"));
            BoundSettings.Add(new BoundConfigSetting(Spatial[Index].Boxes, ConfigValueKind.String, true));
            BoundSettings.Add(new BoundConfigSetting(Spatial[Index].FogCutout, ConfigValueKind.String, true));
        }
        NativeSpatial = Array.AsReadOnly(Spatial);
    }

    internal ConfigFile Config { get; }
    internal Dictionary<string, ConfigEntry<string>> NativeVariations { get; } = new(StringComparer.Ordinal);
    internal IReadOnlyList<BiomeSpatialConfig> NativeSpatial { get; }
    internal IReadOnlyList<BoundConfigSetting> Settings => BoundSettings;
    internal ConfigEntry<string> LayoutOrder { get; }
    internal ConfigEntry<string> Seed { get; }
    internal ConfigEntry<bool> ReplaceGenerated { get; }
    internal ConfigEntry<bool> GetNote { get; }
    internal ConfigEntry<bool> AdvertiseLayout { get; }
    internal ConfigEntry<bool> DisplayProgress { get; }
    internal ConfigEntry<int> LightMapMinimum { get; }
    internal ConfigEntry<bool> DoOrder { get; }
    internal ConfigEntry<bool> GenerativeMap { get; }
    internal ConfigEntry<bool> InfiniteMap { get; }
    internal ConfigEntry<string> InfiniteMapRepeatable { get; }
    internal ConfigEntry<string> InfiniteMapChances { get; }
    internal ConfigEntry<int> InfiniteMapScale { get; }
    internal ConfigEntry<int> InfiniteMapBeginIndex { get; }
    internal ConfigEntry<bool> InfiniteMapRemoveOld { get; }
    internal ConfigEntry<bool> Winnable { get; }
    internal ConfigEntry<bool> PeakAtEnd { get; }
    internal ConfigEntry<bool> GemlessNadir { get; }
    internal ConfigEntry<bool> WaterFollowsBeach { get; }
    internal ConfigEntry<bool> VoidWaterFollowsNadir { get; }
    internal ConfigEntry<bool> RelocatePlane { get; }
    internal ConfigEntry<bool> RelocateStart { get; }
    internal ConfigEntry<bool> ValidNadir { get; }
    internal ConfigEntry<bool> LazyGeneration { get; }
    internal ConfigEntry<bool> Fogless { get; }
    internal ConfigEntry<bool> NoRisingGhosts { get; }
    internal ConfigEntry<bool> NoSleepFog { get; }
    internal ConfigEntry<bool> NoWater { get; }
    internal ConfigEntry<bool> NoVoidWater { get; }
    internal ConfigEntry<bool> NoPlane { get; }
    internal ConfigEntry<bool> NoSkyProps { get; }
    internal ConfigEntry<bool> RemoveOld { get; }

    internal static ConfigBindings Bind(string Path, BepInPlugin OwnerMetadata, string SchemaRevision) =>
        new(Path, OwnerMetadata, SchemaRevision);

    ConfigEntry<string> Bind(string Section, string Key, string DefaultValue, string Description, bool Migratable = true) =>
        Add(Config.Bind(Section, Key, DefaultValue, Description), ConfigValueKind.String, Migratable);

    ConfigEntry<bool> Bind(string Section, string Key, bool DefaultValue, string Description) =>
        Add(Config.Bind(Section, Key, DefaultValue, Description), ConfigValueKind.Boolean, true);

    ConfigEntry<int> Bind(string Section, string Key, int DefaultValue, string Description) =>
        Add(Config.Bind(Section, Key, DefaultValue, Description), ConfigValueKind.Int32, true);

    ConfigEntry<int> Bind(string Section, string Key, int DefaultValue, ConfigDescription Description) =>
        Add(Config.Bind(Section, Key, DefaultValue, Description), ConfigValueKind.Int32, true);

    ConfigEntry<T> Add<T>(ConfigEntry<T> Entry, ConfigValueKind Kind, bool Migratable)
    {
        BoundSettings.Add(new BoundConfigSetting(Entry, Kind, Migratable));
        return Entry;
    }
}

internal readonly struct BoundConfigSetting
{
    internal BoundConfigSetting(ConfigEntryBase Entry, ConfigValueKind Kind, bool Migratable)
    {
        this.Entry = Entry;
        this.Kind = Kind;
        this.Migratable = Migratable;
    }

    internal ConfigEntryBase Entry { get; }
    internal ConfigValueKind Kind { get; }
    internal bool Migratable { get; }
}

internal enum ConfigValueKind
{
    String,
    Boolean,
    Int32
}

internal static class ConfigValueKindOperations
{
    internal static string TypeName(this ConfigValueKind Kind) => Kind switch
    {
        ConfigValueKind.String => typeof(string).Name,
        ConfigValueKind.Boolean => typeof(bool).Name,
        ConfigValueKind.Int32 => typeof(int).Name,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };

    internal static bool ValueMatches(this ConfigValueKind Kind, string Value) => Kind switch
    {
        ConfigValueKind.String => true,
        ConfigValueKind.Boolean => bool.TryParse(Value, out _),
        ConfigValueKind.Int32 => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };
}
