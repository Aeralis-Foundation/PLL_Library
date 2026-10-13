using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace PLL.Library;

internal sealed class CustomBiomeConfig
{
    internal CustomBiomeConfig(
        BiomeId biomeId,
        string configPath,
        ConfigFile file,
        ConfigEntry<string> favorites,
        ConfigEntry<string> overwrites,
        ConfigEntry<string> before,
        ConfigEntry<string> after,
        ConfigEntry<int> spawnChance,
        VariationGroupConfig[] Variations,
        BiomeSpatialConfig Spatial,
        bool RequiresCommitSave,
        string? LegacyPath = null)
    {
        BiomeId = biomeId;
        ConfigPath = configPath;
        File = file;
        Favorites = favorites;
        Overwrites = overwrites;
        Before = before;
        After = after;
        SpawnChance = spawnChance;
        this.Variations = Variations;
        this.Spatial = Spatial;
        this.RequiresCommitSave = RequiresCommitSave;
        this.LegacyPath = LegacyPath;
    }

    internal BiomeId BiomeId { get; }
    internal string ConfigPath { get; }
    internal ConfigEntry<string> Favorites { get; }
    internal ConfigEntry<string> Overwrites { get; }
    internal ConfigEntry<string> Before { get; }
    internal ConfigEntry<string> After { get; }
    internal ConfigEntry<int> SpawnChance { get; }
    internal ConfigFile File { get; }
    internal IReadOnlyList<VariationGroupConfig> Variations { get; }
    internal BiomeSpatialConfig Spatial { get; }
    internal bool RequiresCommitSave { get; }
    internal string? LegacyPath { get; }
}

internal readonly struct CustomBiomeConfigDefaults
{
    CustomBiomeConfigDefaults(
        BiomeId Id,
        string DisplayTitle,
        string ContentRevision,
        BiomeId[] Favorites,
        BiomeId[] Overwrites,
        BiomeId[] Before,
        BiomeId[] After,
        int SpawnChance)
    {
        this.Id = Id;
        this.DisplayTitle = DisplayTitle;
        this.ContentRevision = ContentRevision;
        this.Favorites = Favorites;
        this.Overwrites = Overwrites;
        this.Before = Before;
        this.After = After;
        this.SpawnChance = SpawnChance;
    }

    internal BiomeId Id { get; }
    internal string DisplayTitle { get; }
    internal string ContentRevision { get; }
    internal BiomeId[] Favorites { get; }
    internal BiomeId[] Overwrites { get; }
    internal BiomeId[] Before { get; }
    internal BiomeId[] After { get; }
    internal int SpawnChance { get; }

    internal static CustomBiomeConfigDefaults Create(PllBiomeDefinition definition)
    {
        if (!definition) throw new ArgumentNullException(nameof(definition));
        var id = definition.BiomeId;
        var chance = definition.SpawnChance;
        if (chance < 0 || chance > 100)
            throw new ArgumentOutOfRangeException(
                nameof(definition), chance, $"Biome '{id}' SpawnChance must be between 0 and 100.");
        return new CustomBiomeConfigDefaults(
            id,
            definition.DisplayTitle,
            definition.ContentRevision,
            CopyIds(definition.Favorites, id, nameof(PllBiomeDefinition.Favorites)),
            CopyIds(definition.Overwrites, id, nameof(PllBiomeDefinition.Overwrites),
                rejectReservedNonLayout: true),
            CopyIds(definition.Before, id, nameof(PllBiomeDefinition.Before)),
            CopyIds(definition.After, id, nameof(PllBiomeDefinition.After)),
            chance);
    }

    static BiomeId[] CopyIds(
        IReadOnlyList<BiomeId> source,
        BiomeId owner,
        string name,
        bool rejectReservedNonLayout = false)
    {
        var result = new BiomeId[source.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var value = source[i];
            if (!value.IsValid || value == owner)
                throw new ArgumentException($"Biome '{owner}' contains an invalid {name} entry.", nameof(source));
            if (rejectReservedNonLayout && BiomeLayoutConfigSnapshot.IsReservedNonLayoutBiome(value))
                throw new ArgumentException(
                    $"Biome '{owner}' cannot overwrite reserved non-layout biome '{value}'.", nameof(source));
            result[i] = value;
        }
        Array.Sort(result, CompareIds);
        for (var i = 1; i < result.Length; i++)
            if (result[i - 1] == result[i])
                throw new ArgumentException($"Biome '{owner}' contains duplicate {name} entry '{result[i]}'.", nameof(source));
        return result;
    }

    static int CompareIds(BiomeId left, BiomeId right) =>
        StringComparer.Ordinal.Compare(left.Value, right.Value);
}

internal sealed class BiomeLayoutConfigValues
{
    internal BiomeLayoutConfigValues(
        BiomeId id,
        BiomeId[] favorites,
        BiomeId[] overwrites,
        BiomeId[] before,
        BiomeId[] after,
        int spawnChance,
        IReadOnlyList<VariationGroupValues>? Variations = null)
    {
        Id = id;
        Favorites = favorites;
        Overwrites = overwrites;
        Before = before;
        After = after;
        SpawnChance = spawnChance;
        this.Variations = Variations ?? Array.Empty<VariationGroupValues>();
    }

    internal BiomeId Id { get; }
    internal BiomeId[] Favorites { get; }
    internal BiomeId[] Overwrites { get; }
    internal BiomeId[] Before { get; }
    internal BiomeId[] After { get; }
    internal int SpawnChance { get; }
    internal IReadOnlyList<VariationGroupValues> Variations { get; }
    internal float EffectiveVariationWeight(string GroupId, string EntryId, int OccurrenceIndex)
    {
        foreach (var Group in Variations)
            if (Group.Id == GroupId) return Group.EffectiveWeight(EntryId, OccurrenceIndex);
        throw new InvalidOperationException($"Biome '{Id}' has no captured variation group '{GroupId}'.");
    }
    internal bool Favors(BiomeId id) => Contains(Favorites, id);
    internal bool OverwritesBiome(BiomeId id) => Contains(Overwrites, id);
    internal bool PrefersBefore(BiomeId id) => Contains(Before, id);
    internal bool PrefersAfter(BiomeId id) => Contains(After, id);

    static bool Contains(IReadOnlyList<BiomeId> values, BiomeId id)
    {
        for (var i = 0; i < values.Count; i++)
            if (values[i] == id) return true;
        return false;
    }
}

internal sealed class BiomeLayoutConfigSnapshot
{
    const string SpatialRevision = "1";
    internal static readonly IReadOnlyList<string> SpatialBiomeNames = Array.AsReadOnly(
        NativeSegmentCatalog.ConfigBiomeNames.Where(Name => Name != "Peak").ToArray());
    static readonly Dictionary<string, BiomeId> NativeSpatialIds = CreateNativeSpatialIds();
    internal static readonly BiomeLayoutConfigSnapshot Empty = new(Array.Empty<BiomeLayoutConfigValues>());
    readonly Dictionary<BiomeId, BiomeLayoutConfigValues> byId;
    readonly Dictionary<string, BiomeSpatialSettings> NativeSpatial;
    readonly Dictionary<BiomeId, BiomeSpatialSettings> CustomSpatial;

    internal BiomeLayoutConfigSnapshot(BiomeLayoutConfigValues[] values, IReadOnlyDictionary<string, VariationSequence>? NativeVariations = null,
        IReadOnlyList<BiomeSpatialSettings>? Spatial = null)
    {
        Values = values;
        byId = new Dictionary<BiomeId, BiomeLayoutConfigValues>(values.Length);
        foreach (var value in values) byId.Add(value.Id, value);
        this.NativeVariations = NativeVariations ?? new Dictionary<string, VariationSequence>(StringComparer.Ordinal);
        var SpatialValues = new BiomeSpatialSettings[NativeSpatialIds.Count + values.Length];
        if (Spatial is null)
        {
            var Ids = SpatialIds(values);
            for (var Index = 0; Index < Ids.Length; Index++) SpatialValues[Index] = BiomeSpatialSettings.Empty(Ids[Index]);
        }
        else
        {
            if (Spatial.Count != SpatialValues.Length) throw new ArgumentException("Spatial settings do not match the biome catalog.", nameof(Spatial));
            for (var Index = 0; Index < SpatialValues.Length; Index++) SpatialValues[Index] = Spatial[Index];
        }
        NativeSpatial = new Dictionary<string, BiomeSpatialSettings>(NativeSpatialIds.Count, StringComparer.Ordinal);
        CustomSpatial = new Dictionary<BiomeId, BiomeSpatialSettings>(values.Length);
        for (var Index = 0; Index < SpatialBiomeNames.Count; Index++)
            NativeSpatial.Add(SpatialBiomeNames[Index], SpatialValues[Index]);
        for (var Index = 0; Index < values.Length; Index++)
            CustomSpatial.Add(values[Index].Id, SpatialValues[NativeSpatialIds.Count + Index]);
        this.SpatialValues = Array.AsReadOnly(SpatialValues);
        var Writer = new StringBuilder();
        WriteBase(Writer, values, this.NativeVariations);
        LengthPrefixedText.Append(Writer, SpatialRevision);
        LengthPrefixedText.Append(Writer, SpatialValues.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var Value in SpatialValues) Value.Write(Writer);
        Serialized = Writer.ToString();
    }

    internal IReadOnlyList<BiomeLayoutConfigValues> Values { get; }
    internal string Serialized { get; }
    internal IReadOnlyDictionary<string, VariationSequence> NativeVariations { get; }
    internal IReadOnlyList<BiomeSpatialSettings> SpatialValues { get; }
    internal BiomeSpatialSettings SpatialFor(LayoutSegmentSource Source) => Source is CustomLayoutSegmentSource Custom
        ? CustomSpatial[Custom.MetadataBiome] : NativeSpatial[Source.Id];
    internal static BiomeId NativeSpatialId(string Name) => NativeSpatialIds[Name];

    static BiomeId[] SpatialIds(IReadOnlyList<BiomeLayoutConfigValues> Values)
    {
        var Ids = new BiomeId[NativeSpatialIds.Count + Values.Count];
        var Index = 0;
        foreach (var Name in SpatialBiomeNames) Ids[Index++] = NativeSpatialIds[Name];
        foreach (var Value in Values) Ids[Index++] = Value.Id;
        return Ids;
    }

    static Dictionary<string, BiomeId> CreateNativeSpatialIds()
    {
        var Result = new Dictionary<string, BiomeId>(StringComparer.Ordinal);
        foreach (var Name in SpatialBiomeNames) Result.Add(Name, new BiomeId("peak/" + Name.ToLowerInvariant()));
        return Result;
    }
    internal bool HasForcedNativeVariation
    {
        get
        {
            foreach (var Selected in NativeVariations.Values) if (!Selected.IsRandom) return true;
            return false;
        }
    }
    internal bool TryGet(BiomeId id, out BiomeLayoutConfigValues values) => byId.TryGetValue(id, out values!);
    internal BiomeLayoutConfigValues this[BiomeId Id] => byId[Id];

    static void WriteBase(StringBuilder Result, IReadOnlyList<BiomeLayoutConfigValues> Values, IReadOnlyDictionary<string, VariationSequence> NativeVariations)
    {
        LengthPrefixedText.Append(Result, Values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var Value in Values)
        {
            LengthPrefixedText.Append(Result, Value.Id.Value);
            LengthPrefixedText.Append(Result, FormatIds(Value.Favorites));
            LengthPrefixedText.Append(Result, FormatIds(Value.Overwrites));
            LengthPrefixedText.Append(Result, FormatIds(Value.Before));
            LengthPrefixedText.Append(Result, FormatIds(Value.After));
            LengthPrefixedText.Append(Result, Value.SpawnChance.ToString(CultureInfo.InvariantCulture));
            VariationGroupValues.Write(Result, Value.Variations);
        }
        LengthPrefixedText.Append(Result, NativeVariations.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var Biome in NativeSegmentCatalog.ConfigBiomeNames)
            if (NativeVariations.TryGetValue(Biome, out var Selected))
            {
                LengthPrefixedText.Append(Result, Biome);
                LengthPrefixedText.Append(Result, Selected.IsRandom ? VariationSequence.RandomChoice : Selected.Serialized);
            }
    }

    internal static bool TryDeserialize(
        string data,
        IReadOnlyList<CustomBiomeConfig> expected,
        out BiomeLayoutConfigSnapshot snapshot,
        bool LegacySpatial = false)
    {
        snapshot = Empty;
        if (Encoding.UTF8.GetByteCount(data) > RunConfigurationCodec.MaximumPayloadBytes) return false;
        var offset = 0;
        if (!LengthPrefixedText.TryRead(data, ref offset, out var CountValue)
            || !int.TryParse(CountValue, NumberStyles.None, CultureInfo.InvariantCulture, out var Count)
            || Count != expected.Count) return false;
        var Values = new BiomeLayoutConfigValues[Count];
        for (var i = 0; i < Values.Length; i++)
        {
            var owner = expected[i].BiomeId;
            if (!LengthPrefixedText.TryRead(data, ref offset, out var Id) || !string.Equals(Id, owner.Value, StringComparison.Ordinal)
                || !LengthPrefixedText.TryRead(data, ref offset, out var Favorites) || !TryParseCanonicalIds(Favorites, owner, out var FavoriteIds)
                || !LengthPrefixedText.TryRead(data, ref offset, out var Overwrites)
                || !TryParseCanonicalIds(
                    Overwrites, owner, out var OverwriteIds, rejectReservedNonLayout: true)
                || !LengthPrefixedText.TryRead(data, ref offset, out var Before) || !TryParseCanonicalIds(Before, owner, out var BeforeIds)
                || !LengthPrefixedText.TryRead(data, ref offset, out var After) || !TryParseCanonicalIds(After, owner, out var AfterIds)
                || !LengthPrefixedText.TryRead(data, ref offset, out var ChanceValue)
                || !int.TryParse(ChanceValue, NumberStyles.None, CultureInfo.InvariantCulture, out var Chance)
                || Chance < 0 || Chance > 100
                || !VariationGroupValues.TryRead(data, ref offset, expected[i].Variations, out var Variations)) return false;
            Values[i] = new BiomeLayoutConfigValues(
                owner, FavoriteIds, OverwriteIds, BeforeIds, AfterIds, Chance, Variations);
        }
        if (!LengthPrefixedText.TryRead(data, ref offset, out var NativeCountValue)
            || !int.TryParse(NativeCountValue, NumberStyles.None, CultureInfo.InvariantCulture, out var NativeCount)
            || NativeCount != NativeSegmentCatalog.ConfigBiomeNames.Count) return false;
        var NativeVariations = new Dictionary<string, VariationSequence>(NativeCount, StringComparer.Ordinal);
        foreach (var ExpectedBiome in NativeSegmentCatalog.ConfigBiomeNames)
        {
            if (!LengthPrefixedText.TryRead(data, ref offset, out var Biome) || Biome != ExpectedBiome
                || !LengthPrefixedText.TryRead(data, ref offset, out var Selected)
                || Selected.Length == 0 || !VariationSequence.TryParse(Selected, out var Sequence)) return false;
            NativeVariations.Add(Biome, Sequence);
        }
        if (LegacySpatial)
        {
            if (offset != data.Length) return false;
            var Writer = new StringBuilder();
            WriteBase(Writer, Values, NativeVariations);
            if (Writer.ToString() != data) return false;
            snapshot = new BiomeLayoutConfigSnapshot(Values, NativeVariations);
            return true;
        }
        var SpatialIds = BiomeLayoutConfigSnapshot.SpatialIds(Values);
        if (!LengthPrefixedText.TryRead(data, ref offset, out var ParsedSpatialRevision) || ParsedSpatialRevision != SpatialRevision
            || !LengthPrefixedText.TryRead(data, ref offset, out var SpatialCount)
            || !int.TryParse(SpatialCount, NumberStyles.None, CultureInfo.InvariantCulture, out var CountSpatial)
            || CountSpatial != SpatialIds.Length) return false;
        var Spatial = new BiomeSpatialSettings[CountSpatial];
        for (var Index = 0; Index < Spatial.Length; Index++)
        {
            var Id = SpatialIds[Index];
            if (!LengthPrefixedText.TryRead(data, ref offset, out var RawId) || RawId != Id.Value
                || !LengthPrefixedText.TryRead(data, ref offset, out var Boxes)
                || !LengthPrefixedText.TryRead(data, ref offset, out var Fog)
                || !BiomeSpatialSettings.TryParse(Id, Boxes, Fog, false, out Spatial[Index])) return false;
        }
        if (offset != data.Length) return false;
        snapshot = new BiomeLayoutConfigSnapshot(Values, NativeVariations, Spatial);
        return string.Equals(snapshot.Serialized, data, StringComparison.Ordinal);
    }

    internal static string FormatIds(IReadOnlyList<BiomeId> values)
    {
        if (values.Count == 0) return string.Empty;
        var result = new StringBuilder(values.Count * 16);
        for (var i = 0; i < values.Count; i++)
        {
            if (i != 0) result.Append('>');
            result.Append(values[i].Value);
        }
        return result.ToString();
    }

    internal static BiomeId[] ParseConfiguredIds(
        string? value,
        BiomeId owner,
        bool rejectReservedNonLayout = false)
    {
        if (string.IsNullOrEmpty(value)) return Array.Empty<BiomeId>();
        var result = new List<BiomeId>();
        var seen = new HashSet<BiomeId>();
        foreach (var token in value.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries))
            if (BiomeId.TryParse(token.Trim(), out var id) && id != owner)
            {
                if (rejectReservedNonLayout && IsReservedNonLayoutBiome(id))
                    throw new InvalidOperationException(
                        $"Biome '{owner}' cannot overwrite reserved non-layout biome '{id}'.");
                if (seen.Add(id)) result.Add(id);
            }
        result.Sort(CompareIds);
        return result.ToArray();
    }

    internal static bool IsReservedNonLayoutBiome(BiomeId id) =>
        id == NativeBiomeIds.Peak || id == NativeBiomeIds.Caldera || id == NativeBiomeIds.Gloom;

    static bool TryParseCanonicalIds(
        string value,
        BiomeId owner,
        out BiomeId[] result,
        bool rejectReservedNonLayout = false)
    {
        result = Array.Empty<BiomeId>();
        if (value.Length == 0) return true;
        var tokens = value.Split('>');
        result = new BiomeId[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!BiomeId.TryParse(tokens[i], out var id) || id == owner
                || rejectReservedNonLayout && IsReservedNonLayoutBiome(id)
                || i != 0 && StringComparer.Ordinal.Compare(result[i - 1].Value, id.Value) >= 0)
            {
                result = Array.Empty<BiomeId>();
                return false;
            }
            result[i] = id;
        }
        return true;
    }

    static int CompareIds(BiomeId left, BiomeId right) =>
        StringComparer.Ordinal.Compare(left.Value, right.Value);

}

internal enum CustomBiomeConfigBatchState
{
    Pending,
    Materialized,
    Committed,
    Unregistered
}

internal readonly struct CustomBiomeConfigFileSnapshot
{
    internal CustomBiomeConfigFileSnapshot(string Path, byte[] Contents)
    {
        this.Path = Path;
        this.Contents = Contents;
    }

    internal string Path { get; }
    internal byte[] Contents { get; }
}

internal sealed class CustomBiomeConfigBatch
{
    internal CustomBiomeConfigBatch(BiomeId[] Ids) => this.Ids = Ids;
    internal BiomeId[] Ids { get; }
    internal CustomBiomeConfigBatchState State { get; private set; }
    internal CustomBiomeConfig[] Configurations { get; private set; } = Array.Empty<CustomBiomeConfig>();
    internal CustomBiomeConfigFileSnapshot[] ExistingFiles { get; private set; } = Array.Empty<CustomBiomeConfigFileSnapshot>();
    internal string[] CreatedFiles { get; private set; } = Array.Empty<string>();

    internal void CompleteMaterialization(
        CustomBiomeConfig[] Configurations,
        CustomBiomeConfigFileSnapshot[] ExistingFiles,
        string[] CreatedFiles)
    {
        if (State != CustomBiomeConfigBatchState.Pending)
            throw new InvalidOperationException("The custom biome configuration batch is not pending.");
        this.Configurations = Configurations;
        this.ExistingFiles = ExistingFiles;
        this.CreatedFiles = CreatedFiles;
        State = CustomBiomeConfigBatchState.Materialized;
    }

    internal void CompleteCommit()
    {
        if (State != CustomBiomeConfigBatchState.Materialized)
            throw new InvalidOperationException("The custom biome configuration batch is not materialized.");
        ExistingFiles = Array.Empty<CustomBiomeConfigFileSnapshot>();
        CreatedFiles = Array.Empty<string>();
        State = CustomBiomeConfigBatchState.Committed;
    }

    internal void CompleteUnregister()
    {
        Configurations = Array.Empty<CustomBiomeConfig>();
        ExistingFiles = Array.Empty<CustomBiomeConfigFileSnapshot>();
        CreatedFiles = Array.Empty<string>();
        State = CustomBiomeConfigBatchState.Unregistered;
    }
}

internal static class CustomBiomeConfigRegistry
{
    const string Section = "Generative Map";
    const int ReadableStemLength = 48;
    static readonly Dictionary<BiomeId, CustomBiomeConfigDefaults> Pending = new();
    static readonly Dictionary<BiomeId, CustomBiomeConfig> Configurations = new();
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    static BepInPlugin? ownerMetadata;
    static string? directory;
    static CustomBiomeConfig[]? StartupConfigurations;
    static bool registrySealed;

    internal static void Initialize(BepInPlugin metadata, string configDirectory)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        if (configDirectory is null) throw new ArgumentNullException(nameof(configDirectory));
        var resolvedDirectory = Path.GetFullPath(configDirectory);
        if (ownerMetadata is not null)
        {
            if (ReferenceEquals(ownerMetadata, metadata)
                && string.Equals(directory, resolvedDirectory, StringComparison.Ordinal)) return;
            throw new InvalidOperationException("The custom biome configuration registry is already initialized.");
        }
        ownerMetadata = metadata;
        directory = resolvedDirectory;
    }

    internal static CustomBiomeConfigBatch RegisterDefaults(
        IReadOnlyList<CustomBiomeConfigDefaults> defaults)
    {
        if (defaults is null) throw new ArgumentNullException(nameof(defaults));
        if (StartupConfigurations is not null)
            throw new InvalidOperationException("The startup custom biome configuration registry is sealed.");
        var incoming = new HashSet<BiomeId>();
        var ids = new BiomeId[defaults.Count];
        for (var i = 0; i < defaults.Count; i++)
        {
            var id = defaults[i].Id;
            if (!id.IsValid || !incoming.Add(id) || Pending.ContainsKey(id) || Configurations.ContainsKey(id))
                throw new InvalidOperationException($"Biome '{id}' already has a custom configuration.");
            ids[i] = id;
        }
        foreach (var value in defaults) Pending.Add(value.Id, value);
        return new CustomBiomeConfigBatch(ids);
    }

    internal static void Unregister(CustomBiomeConfigBatch Batch)
    {
        if (Batch is null) throw new ArgumentNullException(nameof(Batch));
        if (registrySealed) throw new InvalidOperationException("The custom biome configuration registry is sealed.");
        if (Batch.State == CustomBiomeConfigBatchState.Unregistered)
            throw new InvalidOperationException("The custom biome configuration batch is already unregistered.");
        var Committed = Batch.State == CustomBiomeConfigBatchState.Committed;
        foreach (var Id in Batch.Ids)
        {
            var Owned = Committed ? Configurations.ContainsKey(Id) : Pending.ContainsKey(Id);
            if (!Owned)
                throw new InvalidOperationException($"Biome '{Id}' has no custom configuration owned by the batch.");
        }
        if (Batch.State == CustomBiomeConfigBatchState.Materialized)
        {
            var Failures = DeleteCreatedFiles(Batch.CreatedFiles);
            if (Failures is not null)
                throw new AggregateException(
                    "Custom biome configuration unregister cleanup failed.", Failures);
        }
        foreach (var Id in Batch.Ids)
            if (Committed) Configurations.Remove(Id);
            else Pending.Remove(Id);
        Batch.CompleteUnregister();
    }

    internal static void Materialize(CustomBiomeConfigBatch Batch)
    {
        if (Batch is null) throw new ArgumentNullException(nameof(Batch));
        if (Batch.State != CustomBiomeConfigBatchState.Pending)
            throw new InvalidOperationException("The custom biome configuration batch is not pending.");
        var Defaults = new List<CustomBiomeConfigDefaults>(Batch.Ids.Length);
        foreach (var Id in Batch.Ids)
            if (Pending.TryGetValue(Id, out var Value)) Defaults.Add(Value);
            else throw new InvalidOperationException($"Biome '{Id}' has no pending custom configuration.");
        Defaults.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Id.Value, Right.Id.Value));
        var Prepared = Prepare(Defaults, out var ExistingFiles, out var CreatedFiles);
        Batch.CompleteMaterialization(Prepared, ExistingFiles, CreatedFiles);
    }

    internal static void Commit(CustomBiomeConfigBatch Batch)
    {
        if (Batch is null) throw new ArgumentNullException(nameof(Batch));
        if (Batch.State != CustomBiomeConfigBatchState.Materialized)
            throw new InvalidOperationException("The custom biome configuration batch is not materialized.");
        var Added = 0;
        var Archived = new List<string>();
        var StagedFiles = new List<(string StagingPath, string Destination)>();
        try
        {
            foreach (var Configuration in Batch.Configurations)
            {
                if (!Configuration.RequiresCommitSave) continue;
                var StagingPath = Configuration.ConfigPath + "." + Guid.NewGuid().ToString("N") + ".new";
                StagedFiles.Add((StagingPath, Configuration.ConfigPath));
                var Staged = new ConfigFile(StagingPath, false, ownerMetadata) { SaveOnConfigSet = false };
                foreach (var Entry in Configuration.File.Entries) Staged.Entries.Add(Entry.Key, Entry.Value);
                foreach (var Orphan in Configuration.File.OrphanedEntries)
                    Staged.OrphanedEntries.Add(Orphan.Key, Orphan.Value);
                Staged.Save();
            }
            foreach (var Staged in StagedFiles)
                if (File.Exists(Staged.Destination)) File.Replace(Staged.StagingPath, Staged.Destination, null);
                else File.Move(Staged.StagingPath, Staged.Destination);
            for (; Added < Batch.Configurations.Length; Added++)
                Configurations.Add(Batch.Configurations[Added].BiomeId, Batch.Configurations[Added]);
            foreach (var Configuration in Batch.Configurations)
                if (Configuration.LegacyPath is { } LegacyPath)
                {
                    File.Move(LegacyPath, LegacyPath + ".bak");
                    Archived.Add(LegacyPath);
                }
            foreach (var Configuration in Batch.Configurations)
                Configuration.File.SaveOnConfigSet = true;
            foreach (var Id in Batch.Ids) Pending.Remove(Id);
            Batch.CompleteCommit();
        }
        catch (Exception Failure)
        {
            for (var Index = 0; Index < Added; Index++)
                Configurations.Remove(Batch.Configurations[Index].BiomeId);
            List<Exception>? Cleanup = null;
            foreach (var LegacyPath in Archived)
                try { File.Move(LegacyPath + ".bak", LegacyPath); }
                catch (Exception Exception) { (Cleanup ??= new List<Exception>()).Add(Exception); }
            foreach (var Existing in Batch.ExistingFiles)
                try { File.WriteAllBytes(Existing.Path, Existing.Contents); }
                catch (Exception Exception) { (Cleanup ??= new List<Exception>()).Add(Exception); }
            var StagingFailures = DeleteCreatedFiles(StagedFiles.Select(Staged => Staged.StagingPath));
            if (StagingFailures is not null) (Cleanup ??= new List<Exception>()).AddRange(StagingFailures);
            var DeleteFailures = DeleteCreatedFiles(Batch.CreatedFiles);
            if (DeleteFailures is not null)
                (Cleanup ??= new List<Exception>()).AddRange(DeleteFailures);
            if (Cleanup is not null)
            {
                Cleanup.Insert(0, Failure);
                throw new AggregateException(
                    "Custom biome configuration commit and rollback both failed.", Cleanup);
            }
            throw;
        }
    }

    internal static void SealStartup()
    {
        if (StartupConfigurations is not null) return;
        if (Pending.Count != 0)
            throw new InvalidOperationException(
                $"The startup custom biome configuration registry has {Pending.Count} uncommitted entries.");
        var Ordered = Configurations.Values.ToArray();
        Array.Sort(Ordered, static (Left, Right) => StringComparer.Ordinal.Compare(Left.BiomeId.Value, Right.BiomeId.Value));
        StartupConfigurations = Ordered;
    }

    internal static void SealRegistry() => registrySealed = true;

    internal static ConfigFile? GetConfig(string Id) =>
        BiomeId.TryParse(Id, out var Biome)
        && Configurations.TryGetValue(Biome, out var Configuration)
            ? Configuration.File
            : null;

    internal static BiomeLayoutConfigSnapshot Capture(IReadOnlyDictionary<string, VariationSequence> NativeVariations)
    {
        var OrderedConfigurations = StartupConfigurations!;
        var Values = new BiomeLayoutConfigValues[OrderedConfigurations.Length];
        for (var i = 0; i < Values.Length; i++)
        {
            var Configuration = OrderedConfigurations[i];
            var VariationValues = VariationGroupConfig.Capture(Configuration);
            var id = Configuration.BiomeId;
            Values[i] = new BiomeLayoutConfigValues(
                id,
                BiomeLayoutConfigSnapshot.ParseConfiguredIds(Configuration.Favorites.Value, id),
                BiomeLayoutConfigSnapshot.ParseConfiguredIds(
                    Configuration.Overwrites.Value, id, rejectReservedNonLayout: true),
                BiomeLayoutConfigSnapshot.ParseConfiguredIds(Configuration.Before.Value, id),
                BiomeLayoutConfigSnapshot.ParseConfiguredIds(Configuration.After.Value, id),
                Math.Max(0, Math.Min(100, Configuration.SpawnChance.Value)), VariationValues);
        }
        var Spatial = new BiomeSpatialSettings[FileManager.NativeSpatial.Count + OrderedConfigurations.Length];
        var SpatialIndex = 0;
        foreach (var Configuration in FileManager.NativeSpatial) Spatial[SpatialIndex++] = Configuration.Capture();
        foreach (var Configuration in OrderedConfigurations) Spatial[SpatialIndex++] = Configuration.Spatial.Capture();
        return new BiomeLayoutConfigSnapshot(Values, NativeVariations, Spatial);
    }

    internal static bool TryParseSnapshot(string data, out BiomeLayoutConfigSnapshot snapshot, bool LegacySpatial = false)
    {
        if (data is null)
        {
            snapshot = BiomeLayoutConfigSnapshot.Empty;
            return false;
        }
        return BiomeLayoutConfigSnapshot.TryDeserialize(
            data, StartupConfigurations!, out snapshot, LegacySpatial);
    }

    static CustomBiomeConfig[] Prepare(
        IReadOnlyList<CustomBiomeConfigDefaults> Defaults,
        out CustomBiomeConfigFileSnapshot[] ExistingFiles,
        out string[] CreatedFiles)
    {
        var Metadata = ownerMetadata
            ?? throw new InvalidOperationException("The custom biome configuration registry is not initialized.");
        var TargetDirectory = directory!;
        var Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var Configuration in Configurations.Values) Paths.Add(Configuration.ConfigPath);
        var PlannedPaths = new string[Defaults.Count];
        for (var Index = 0; Index < Defaults.Count; Index++)
        {
            var Id = Defaults[Index].Id;
            var Path = System.IO.Path.Combine(TargetDirectory, FileName(Id));
            if (!Paths.Add(Path))
                throw new InvalidOperationException($"Biome '{Id}' collides with another custom configuration path.");
            PlannedPaths[Index] = Path;
        }

        if (Defaults.Count != 0) Directory.CreateDirectory(TargetDirectory);
        var Existing = new List<CustomBiomeConfigFileSnapshot>();
        var Created = new List<string>();
        var Prepared = new CustomBiomeConfig[Defaults.Count];
        try
        {
            for (var Index = 0; Index < Prepared.Length; Index++)
            {
                var Path = PlannedPaths[Index];
                var Existed = File.Exists(Path);
                string? LegacyPath = System.IO.Path.Combine(TargetDirectory, FileName(Defaults[Index].Id, true));
                if (Existed || !File.Exists(LegacyPath)) LegacyPath = null;
                else if (File.Exists(LegacyPath + ".bak"))
                    throw new IOException($"Configuration backup '{LegacyPath}.bak' already exists.");
                byte[]? Contents = null;
                if (Existed) Contents = File.ReadAllBytes(Path);
                var RequiresCommitSave = !Existed || !HasContentRevision(Contents!, Defaults[Index].ContentRevision);
                Prepared[Index] = Bind(Path, Metadata, Defaults[Index], RequiresCommitSave, LegacyPath);
                if (Existed) Existing.Add(new CustomBiomeConfigFileSnapshot(Path, Contents!));
                else Created.Add(Path);
            }
            ExistingFiles = Existing.ToArray();
            CreatedFiles = Created.ToArray();
            return Prepared;
        }
        catch (Exception Failure)
        {
            var Cleanup = DeleteCreatedFiles(Created);
            if (Cleanup is not null)
            {
                Cleanup.Insert(0, Failure);
                throw new AggregateException(
                    "Custom biome configuration materialization and rollback both failed.", Cleanup);
            }
            throw;
        }
    }

    static List<Exception>? DeleteCreatedFiles(IEnumerable<string> Paths)
    {
        List<Exception>? Failures = null;
        foreach (var Path in Paths)
            try { File.Delete(Path); }
            catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
        return Failures;
    }

    static CustomBiomeConfig Bind(
        string Path,
        BepInPlugin Metadata,
        CustomBiomeConfigDefaults Defaults,
        bool RequiresCommitSave,
        string? LegacyPath = null)
    {
        var Source = ConfigDocument.Read(LegacyPath ?? Path);
        if (LegacyPath is null && File.Exists(Path)) Source.RequireCustomBiomeOwner(Defaults.Id.Value);
        var Config = new ConfigFile(Path, false, Metadata) { SaveOnConfigSet = false };
        if (LegacyPath is not null) Source.Apply(Config);
        Config.Bind("Biome", "ID", Defaults.Id.Value,
            "Stable biome ID used in LayoutOrder and biome relationships. Do not change this value.");
        var BiomeTitle = Config.Bind("Biome", "Name", Defaults.DisplayTitle,
            "Biome name from the loaded bundle. This value updates when the author renames the biome.");
        RequiresCommitSave |= BiomeTitle.Value != Defaults.DisplayTitle;
        BiomeTitle.Value = Defaults.DisplayTitle;
        var ContentRevision = Config.Bind(
            "Version", "Content Revision", Defaults.ContentRevision,
            "Identifies the biome content in the loaded bundle.");
        if (!string.Equals(ContentRevision.Value, Defaults.ContentRevision, StringComparison.Ordinal))
            ContentRevision.Value = Defaults.ContentRevision;
        var Favorites = Config.Bind(Section, "Favorites", FormatDefaults(Defaults.Favorites),
            "Prefers these neighboring biomes when PLL chooses a layout. Separate IDs with >; leave empty for no preference.");
        var Overwrites = Config.Bind(Section, "Overwrites", FormatDefaults(Defaults.Overwrites),
            "Allows this biome to replace the listed biome IDs. Names and segment IDs keep their selected source. Separate IDs with >; leave empty to disable replacement.");
        var Before = Config.Bind(Section, "Before", FormatDefaults(Defaults.Before),
            "IDs of biomes this biome should come immediately before. Separate IDs with >; leave empty for no preference.");
        var After = Config.Bind(Section, "After", FormatDefaults(Defaults.After),
            "IDs of biomes this biome should come immediately after. Separate IDs with >; leave empty for no preference.");
        var SpawnChance = Config.Bind(Section, "Spawn Chance", Defaults.SpawnChance,
            new ConfigDescription("Generative Map only: chance from 0 to 100 to include this biome. Ordered layouts do not use this chance.",
                new AcceptableValueRange<int>(0, 100)));
        if (!BiomeAuthoringCatalog.TryGet($"{Defaults.Id.Value}/segment", out var Authoring))
            throw new InvalidOperationException($"Biome '{Defaults.Id}' has no registered authoring contract.");
        var RawVariations = Source.VariationSettings();
        var Variations = new VariationGroupConfig[Authoring.VariationGroups.Count];
        for (var i = 0; i < Variations.Length; i++)
        {
            Variations[i] = new VariationGroupConfig(Config, Authoring.VariationGroups[i], RawVariations);
            RequiresCommitSave |= Variations[i].RequiresSave;
        }
        Array.Sort(Variations, (Left, Right) => string.CompareOrdinal(Left.Id, Right.Id));
        foreach (var Definition in Config.OrphanedEntries.Keys.Where(Definition =>
            Definition.Section.StartsWith("Variations.", StringComparison.Ordinal)).ToArray())
        {
            Config.OrphanedEntries.Remove(Definition);
            RequiresCommitSave = true;
        }
        var Spatial = new BiomeSpatialConfig(Config, Defaults.Id,
            new ConfigDefinition("Spatial", "Wall Boxes"), new ConfigDefinition("Spatial", "Fog Cutout"));
        RequiresCommitSave |= !Source.HasSpatialSettings("Spatial");
        return new CustomBiomeConfig(
            Defaults.Id, Path, Config, Favorites, Overwrites, Before, After, SpawnChance,
            Variations, Spatial, RequiresCommitSave, LegacyPath);
    }

    static bool HasContentRevision(byte[] Contents, string Revision)
    {
        using var Reader = new StreamReader(
            new MemoryStream(Contents, false), Encoding.UTF8, true);
        var InVersionSection = false;
        string? PersistedRevision = null;
        string? RawLine;
        while ((RawLine = Reader.ReadLine()) is not null)
        {
            var Line = RawLine.Trim();
            if (Line.StartsWith('[') && Line.EndsWith(']'))
            {
                InVersionSection = string.Equals(
                    Line.Substring(1, Line.Length - 2), "Version", StringComparison.Ordinal);
                continue;
            }
            if (!InVersionSection || Line.StartsWith('#')) continue;
            var Separator = Line.IndexOf('=');
            if (Separator < 0
                || !string.Equals(
                    Line.Substring(0, Separator).Trim(), "Content Revision", StringComparison.Ordinal)) continue;
            PersistedRevision = Line.Substring(Separator + 1).Trim();
        }
        return string.Equals(PersistedRevision, Revision, StringComparison.Ordinal);
    }

    static string FormatDefaults(IReadOnlyList<BiomeId> values)
    {
        if (values.Count == 0) return string.Empty;
        var result = new StringBuilder(values.Count * 18);
        for (var i = 0; i < values.Count; i++)
        {
            if (i != 0) result.Append(" > ");
            result.Append(values[i].Value);
        }
        return result.ToString();
    }

    static string FileName(BiomeId id, bool Legacy = false)
    {
        var stem = new StringBuilder(Math.Min(id.Value.Length, ReadableStemLength));
        var separated = false;
        foreach (var character in id.Value)
        {
            var safe = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';
            if (safe)
            {
                if (stem.Length >= ReadableStemLength) break;
                stem.Append(character);
                separated = false;
            }
            else if (!separated && stem.Length != 0 && stem.Length < ReadableStemLength)
            {
                stem.Append('-');
                separated = true;
            }
        }
        while (stem.Length != 0 && stem[stem.Length - 1] == '-') stem.Length--;
        if (stem.Length == 0) stem.Append("biome");
        using var algorithm = SHA256.Create();
        var hash = BitConverter.ToString(algorithm.ComputeHash(StrictUtf8.GetBytes(id.Value))).Replace("-", string.Empty);
        return $"{stem}-{(Legacy ? hash : hash.Substring(0, 16))}.cfg";
    }
}

internal sealed class VariationGroupConfig
{
    internal const string EntryIdComment = "PLL variation entry: ";
    internal const string WeightPrefix = "Weight - ";
    internal const string IdentityPrefix = "Entry ID - ";
    readonly struct Entry
    {
        internal readonly string Id, Title;
        internal readonly float DefaultWeight;
        internal Entry(string Id, string Title, float DefaultWeight)
        { this.Id = Id; this.Title = Title; this.DefaultWeight = DefaultWeight; }
    }

    readonly Entry[] Entries;
    readonly int[] EntryOrder;
    readonly string Section;

    internal VariationGroupConfig(ConfigFile File, VariationContract Contract, Dictionary<string, string> Raw)
    {
        Id = Contract.Id;
        Entries = new Entry[Contract.EntryCount];
        for (var i = 0; i < Entries.Length; i++)
            Entries[i] = new Entry(Contract.EntryId(i), Contract.EntryTitle(i), Contract.DefaultWeight(i));
        Section = "Variations." + Uri.EscapeDataString(Id);
        EntryOrder = new int[Entries.Length];
        var Labels = new string[EntryOrder.Length + 2];
        Labels[0] = VariationSequence.RandomChoice;
        Labels[Labels.Length - 1] = VariationSequence.PatternChoice;
        var Titles = new string[EntryOrder.Length];
        var Counts = new Dictionary<string, int>(StringComparer.Ordinal)
            { [VariationSequence.RandomChoice] = 1, [VariationSequence.PatternChoice] = 1 };
        for (var i = 0; i < Titles.Length; i++)
        {
            var Title = new StringBuilder();
            foreach (var Character in Entries[i].Title)
                Title.Append(char.IsControl(Character) || "=\\\"'[],>".IndexOf(Character) >= 0 ? ' ' : Character);
            Titles[i] = Title.ToString().Trim();
            if (Titles[i].Length == 0) Titles[i] = "Variation";
            Counts.TryGetValue(Titles[i], out var Count); Counts[Titles[i]] = Count + 1;
        }
        var Reserved = new HashSet<string>(Counts.Keys, StringComparer.Ordinal);
        for (var i = 0; i < Titles.Length; i++)
        {
            var Label = Titles[i];
            if (Counts[Label] > 1)
                for (var Length = 1; Length <= Entries[i].Id.Length; Length++)
                {
                    var Prefix = Entries[i].Id.Substring(0, Length);
                    var Unique = true;
                    for (var j = 0; j < Titles.Length; j++)
                        if (i != j && Titles[j] == Titles[i] && Entries[j].Id.StartsWith(Prefix, StringComparison.Ordinal))
                        { Unique = false; break; }
                    if (!Unique) continue;
                    while (!Reserved.Add(Titles[i] + " (" + Prefix + ")")) Prefix += "-";
                    Label = Titles[i] + " (" + Prefix + ")";
                    break;
                }
            Labels[i + 1] = Label;
        }
        var WeightSettings = ReadWeights(Raw);
        for (var i = 0; i < EntryOrder.Length; i++)
        {
            EntryOrder[i] = i;
            var Key = WeightPrefix + Labels[i + 1];
            var Weight = File.Bind(Section, Key, Entries[i].DefaultWeight, new ConfigDescription(
                "Random weight for " + Labels[i + 1] + ". Weights are relative, not percentages. Zero prevents random selection but still allows manual selection.\n"
                    + EntryIdComment + Entries[i].Id,
                new AcceptableValueRange<float>(0f, 100f)));
            Weight.Value = ReadWeight(WeightSettings, i);
            RequiresSave |= !Raw.TryGetValue(Section + "\n" + IdentityPrefix + Labels[i + 1], out var PersistedId)
                || PersistedId != Entries[i].Id;
        }
        Array.Sort(EntryOrder, (Left, Right) => string.CompareOrdinal(Entries[Left].Id, Entries[Right].Id));
        var Selected = ReadSelection(Raw, out var PatternSelected);
        var Selection = File.Bind(Section, "Selected", VariationSequence.RandomChoice, new ConfigDescription(
            "Selects one entry for every occurrence, even with zero weight. Random uses relative weights; Pattern follows this group's pattern.",
            new AcceptableValueList<string>(Labels)));
        if (!Selected.IsRandom && !PatternSelected)
            Selection.Value = FindLabel(Selected.At(0));
        var Pattern = File.Bind(Section, VariationSequence.PatternChoice, "", VariationSequence.PatternDescription);
        if (PatternSelected && !Selected.IsRandom)
        {
            var Canonical = new StringBuilder();
            for (var i = 0; i < Selected.Count; i++)
            {
                var Label = FindLabel(Selected.At(i));
                if (Canonical.Length + Label.Length + (i == 0 ? 0 : 3) > RunConfigurationCodec.MaximumPayloadBytes)
                    throw new InvalidDataException($"Variation pattern '{Id}' exceeds {RunConfigurationCodec.MaximumPayloadBytes} characters. Use fewer entries.");
                if (i != 0) Canonical.Append(" > ");
                Canonical.Append(Label);
            }
            Pattern.Value = Canonical.ToString();
        }
        RequiresSave |= !Raw.TryGetValue(Section + "\n" + VariationSequence.PatternChoice, out var PersistedPattern)
            || PersistedPattern != Pattern.Value;
        RequiresSave |= !Raw.TryGetValue(Section + "\nSelected", out var PersistedSelection)
            || PersistedSelection != Selection.Value;

        string FindLabel(string EntryId)
        {
            for (var i = 0; i < Entries.Length; i++)
                if (Entries[i].Id == EntryId) return Labels[i + 1];
            throw new InvalidOperationException($"Variation group '{Id}' has no entry '{EntryId}'.");
        }
    }

    internal string Id { get; }
    internal bool RequiresSave { get; }
    internal int EntryCount => EntryOrder.Length;
    internal string EntryId(int Index) => Entries[EntryOrder[Index]].Id;

    internal VariationGroupValues Capture(Dictionary<string, string> Raw, BiomeId Biome)
    {
        var WeightSettings = ReadWeights(Raw);
        var Weights = new float[EntryOrder.Length];
        var EntryIds = new string[EntryOrder.Length];
        var Enabled = false;
        for (var i = 0; i < Weights.Length; i++)
        {
            EntryIds[i] = EntryId(i);
            Weights[i] = ReadWeight(WeightSettings, EntryOrder[i]);
            Enabled |= Weights[i] > 0f;
        }
        var Selected = ReadSelection(Raw, out _);
        if (!Enabled && Selected.IsRandom)
            throw new InvalidDataException($"Biome '{Biome}' variation group '{Id}' has no positive weight. Set at least one variation weight above zero.");
        return new VariationGroupValues(Id, Selected, EntryIds, Weights);
    }

    Dictionary<string, string> ReadWeights(Dictionary<string, string> Raw)
    {
        var Weights = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var Setting in Raw)
        {
            if (!Setting.Key.StartsWith(Section + "\nWeight.", StringComparison.Ordinal)
                && !Setting.Key.StartsWith(Section + "\n" + WeightPrefix, StringComparison.Ordinal)) continue;
            var Identity = WeightIdentity(Setting.Key, Raw);
            if (!Weights.TryAdd(Identity, Setting.Value))
                throw new InvalidDataException($"Variation group '{Id}' repeats weight entry '{Identity}'. Remove the duplicate weight.");
        }
        return Weights;
    }

    string WeightIdentity(string Key, Dictionary<string, string> Raw)
    {
        string Identity;
        var Prefix = Section + "\n" + WeightPrefix;
        if (Key.StartsWith(Prefix, StringComparison.Ordinal))
            Identity = Raw[Section + "\n" + IdentityPrefix + Key.Substring(Prefix.Length)];
        else
        {
            Identity = Key.Substring(Key.LastIndexOf('.') + 1);
            if (!PllAuthoringValueContract.IsVariationEntryId(Identity))
                throw new InvalidDataException($"Variation group '{Id}' has an invalid weight entry ID '{Identity}'.");
        }
        return Identity;
    }

    float ReadWeight(Dictionary<string, string> Weights, int Index)
    {
        if (!Weights.TryGetValue(Entries[Index].Id, out var Value)) return Entries[Index].DefaultWeight;
        if (!float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var Weight)
            || !PllAuthoringValueContract.IsVariationWeight(Weight))
            throw new InvalidDataException($"Variation group '{Id}' entry '{Entries[Index].Title}' has an invalid weight '{Value}'.");
        return Weight == 0f ? 0f : Weight;
    }

    VariationSequence ReadSelection(Dictionary<string, string> Raw, out bool PatternSelected)
    {
        var Selected = Raw.TryGetValue(Section + "\nSelected", out var Value) ? Value : VariationSequence.RandomChoice;
        var HasPattern = Raw.TryGetValue(Section + "\n" + VariationSequence.PatternChoice, out var Pattern);
        PatternSelected = Selected == VariationSequence.PatternChoice
            && (HasPattern || !Raw.ContainsKey(Section + "\n" + IdentityPrefix + Selected));
        if (!PatternSelected)
            return Selected == VariationSequence.RandomChoice ? VariationSequence.Random
                : VariationSequence.Capture(Id, ReadEntry(Selected, Raw), "");
        return VariationSequence.Capture(Id, Selected, Pattern ?? "", Entry => ReadEntry(Entry, Raw));
    }

    string ReadEntry(string Value, Dictionary<string, string> Raw)
    {
        if (Raw.TryGetValue(Section + "\n" + IdentityPrefix + Value, out var Identity))
            for (var i = 0; i < Entries.Length; i++)
                if (Identity == Entries[i].Id) return Identity;
        for (var i = 0; i < Entries.Length; i++)
            if (Value.EndsWith(" [" + Entries[i].Id + "]", StringComparison.Ordinal)) return Entries[i].Id;
        throw new InvalidDataException($"Variation group '{Id}' has an unknown entry '{Value}'. Use a listed entry name.");
    }

    internal static VariationGroupValues[] Capture(CustomBiomeConfig Configuration)
    {
        var Raw = ConfigDocument.ReadVariationSettings(Configuration.ConfigPath);
        var Values = new VariationGroupValues[Configuration.Variations.Count];
        for (var i = 0; i < Values.Length; i++)
            Values[i] = Configuration.Variations[i].Capture(Raw, Configuration.BiomeId);
        return Values;
    }
}

internal sealed class VariationGroupValues
{
    readonly string[] EntryIds;
    readonly float[] Weights;

    internal VariationGroupValues(string Id, VariationSequence Selection, string[] EntryIds, float[] Weights)
    {
        this.Id = Id;
        this.Selection = Selection;
        this.EntryIds = EntryIds;
        this.Weights = Weights;
    }

    internal string Id { get; }
    internal VariationSequence Selection { get; }
    internal float EffectiveWeight(string EntryId, int OccurrenceIndex)
    {
        var Selected = Selection.At(OccurrenceIndex);
        for (var i = 0; i < EntryIds.Length; i++)
            if (EntryIds[i] == EntryId)
                return Selected.Length == 0 ? Weights[i] : Selected == EntryId ? 1f : 0f;
        throw new InvalidOperationException($"Variation group '{Id}' has no captured entry '{EntryId}'.");
    }

    internal static void Write(StringBuilder Writer, IReadOnlyList<VariationGroupValues> Groups)
    {
        LengthPrefixedText.Append(Writer, Groups.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var Group in Groups)
        {
            LengthPrefixedText.Append(Writer, Group.Id);
            LengthPrefixedText.Append(Writer, Group.Selection.Serialized);
            LengthPrefixedText.Append(Writer, Group.EntryIds.Length.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < Group.EntryIds.Length; i++)
            {
                LengthPrefixedText.Append(Writer, Group.EntryIds[i]);
                LengthPrefixedText.Append(Writer, Group.Weights[i].ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }

    internal static bool TryRead(string Data, ref int Offset, IReadOnlyList<VariationGroupConfig> Expected, out VariationGroupValues[] Groups)
    {
        Groups = Array.Empty<VariationGroupValues>();
        if (!LengthPrefixedText.TryRead(Data, ref Offset, out var CountValue)
            || !int.TryParse(CountValue, NumberStyles.None, CultureInfo.InvariantCulture, out var Count)
            || Count != Expected.Count) return false;
        var Result = new VariationGroupValues[Count];
        for (var i = 0; i < Count; i++)
        {
            var Group = Expected[i];
            if (!LengthPrefixedText.TryRead(Data, ref Offset, out var Id) || Id != Group.Id
                || !LengthPrefixedText.TryRead(Data, ref Offset, out var Selected)
                || !VariationSequence.TryParse(Selected, out var Selection)
                || !LengthPrefixedText.TryRead(Data, ref Offset, out var EntryCount)
                || !int.TryParse(EntryCount, NumberStyles.None, CultureInfo.InvariantCulture, out var Entries)
                || Entries != Group.EntryCount) return false;
            var EntryIds = new string[Entries];
            var Weights = new float[Entries];
            var Enabled = false;
            for (var j = 0; j < Entries; j++)
            {
                if (!LengthPrefixedText.TryRead(Data, ref Offset, out var EntryId) || EntryId != Group.EntryId(j)
                    || !LengthPrefixedText.TryRead(Data, ref Offset, out var WeightValue)
                    || !float.TryParse(WeightValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var Weight)
                    || !PllAuthoringValueContract.IsVariationWeight(Weight)) return false;
                EntryIds[j] = EntryId;
                Weights[j] = Weight == 0f ? 0f : Weight;
                Enabled |= Weight > 0f;
            }
            if (!Enabled && Selection.IsRandom) return false;
            for (var j = 0; j < Selection.Count; j++)
                if (Array.IndexOf(EntryIds, Selection.At(j)) < 0) return false;
            Result[i] = new VariationGroupValues(Id, Selection, EntryIds, Weights);
        }
        Groups = Result;
        return true;
    }
}
