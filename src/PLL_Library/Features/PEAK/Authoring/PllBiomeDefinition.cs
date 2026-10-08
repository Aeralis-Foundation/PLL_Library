using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[CreateAssetMenu(fileName = "PllBiomeDefinition", menuName = "PLL/Biome Definition")]
public sealed class PllBiomeDefinition : ScriptableObject, ISerializationCallbackReceiver
{
#if PLL_CREATOR_SDK
    const string BundleIdentityPrefix = PLLLibrary.PluginGuid + "/biome-definition/v1>";

    [SerializeField] BiomeId biomeId = default;
#endif
    [SerializeField] PllBiomeBehavior behavior = PllBiomeBehavior.Undefined;
    [SerializeField] string displayTitle = "";
    [SerializeField] string contentRevision = "1";
#if PLL_CREATOR_SDK
    [SerializeField] BiomeId[] favorites = Array.Empty<BiomeId>();
    [SerializeField] BiomeId[] overwrites = Array.Empty<BiomeId>();
    [SerializeField] BiomeId[] before = Array.Empty<BiomeId>();
    [SerializeField] BiomeId[] after = Array.Empty<BiomeId>();
#else
    PreparedBundleIdentity? PreparedIdentity;
#endif
#pragma warning disable CS0414
    [SerializeField, HideInInspector] int BundleFormat = 0;
#pragma warning restore CS0414
    [SerializeField, HideInInspector] string BundleBiomeId = "";
    [SerializeField, HideInInspector] string[]? BundleFavorites = null;
    [SerializeField, HideInInspector] string[]? BundleOverwrites = null;
    [SerializeField, HideInInspector] string[]? BundleBefore = null;
    [SerializeField, HideInInspector] string[]? BundleAfter = null;
    [SerializeField, Range(0, 100)] int spawnChance = 100;
    [SerializeField] GameObject rootPrefab = null!;
    [SerializeField] PllBiomePrefabAnchors anchors = null!;
    [SerializeField] DayNightProfile dayNightProfile = null!;
    [SerializeField, HideInInspector] PllVisualAuthoring[] visuals = Array.Empty<PllVisualAuthoring>();
    [SerializeField] Color color = Color.white;
    [SerializeField] AudioClip[] ambienceSounds = Array.Empty<AudioClip>();
    [SerializeField] AudioClip[] effectSounds = Array.Empty<AudioClip>();
    [SerializeField] AudioClip[] enterSounds = Array.Empty<AudioClip>();
    [SerializeField, HideInInspector] PllLuggageAuthoring[] luggageDefinitions = Array.Empty<PllLuggageAuthoring>();
    [SerializeField, HideInInspector] PllVariationAuthoring[] variationDefinitions = Array.Empty<PllVariationAuthoring>();
    [SerializeField, HideInInspector] PllEffectAuthoring[] effectDefinitions = Array.Empty<PllEffectAuthoring>();
    [SerializeField, HideInInspector] PllSoundEmitterAuthoring[] soundEmitters = Array.Empty<PllSoundEmitterAuthoring>();
    [SerializeField, HideInInspector] private PllBiomeStingerAuthoring[] stingerDefinitions = Array.Empty<PllBiomeStingerAuthoring>();
#pragma warning disable CS0414
    [SerializeField, HideInInspector] byte[]? BundleGeneratedPropAssetCatalog = null;
    [SerializeField, HideInInspector] GameObject[]? BundleGeneratedPropPrefabs = null;
    [SerializeField, HideInInspector] Mesh[]? BundleGeneratedPropMeshes = null;
    [SerializeField, HideInInspector] Material[]? BundleGeneratedPropMaterials = null;
    [SerializeField, HideInInspector] PhysicsMaterial[]? BundleGeneratedPropPhysicsMaterials = null;
#pragma warning restore CS0414
    [SerializeReference, HideInInspector] object[]? BundleFogShaderAdapters = null;
    PllFogShaderAdapter[]? PreparedFogShaderAdapters;

#if PLL_CREATOR_SDK
    [SerializeField, HideInInspector] PllFogShaderAdapter[] FogShaderAdaptersValue = Array.Empty<PllFogShaderAdapter>();
    public BiomeId BiomeId => biomeId;
    public IReadOnlyList<PllFogShaderAdapter> FogShaderAdapters => FogShaderAdaptersValue ?? Array.Empty<PllFogShaderAdapter>();
    public IReadOnlyList<PllFogShaderAdapter> CompiledFogShaderAdapters => BundleFogShaderAdapters is null
        ? Array.Empty<PllFogShaderAdapter>() : CaptureFogShaderAdapters();

    public void SetPreparedFogShaderAdapter(PllFogShaderAdapter Adapter)
    {
        if (Adapter is null) throw new ArgumentNullException(nameof(Adapter));
        var Values = FogShaderAdapters;
        var Index = Values.Count;
        for (var i = 0; i < Values.Count; i++)
            if (Values[i].OriginalShader == Adapter.OriginalShader) { Index = i; break; }
        var Result = new PllFogShaderAdapter[Index == Values.Count ? Values.Count + 1 : Values.Count];
        for (var i = 0; i < Values.Count; i++) Result[i] = Values[i];
        Result[Index] = Adapter;
        FogShaderAdaptersValue = Result;
    }

    public void SealBundleIdentity()
    {
        if (!biomeId.IsValid) throw new InvalidOperationException("Cannot seal an invalid biome ID.");
        name = BundleIdentityPrefix + biomeId.Value;
        BundleBiomeId = biomeId.Value;
        BundleFavorites = PllBundleSerialization.SealBiomeIds(favorites ?? Array.Empty<BiomeId>());
        BundleOverwrites = PllBundleSerialization.SealBiomeIds(overwrites ?? Array.Empty<BiomeId>());
        BundleBefore = PllBundleSerialization.SealBiomeIds(before ?? Array.Empty<BiomeId>());
        BundleAfter = PllBundleSerialization.SealBiomeIds(after ?? Array.Empty<BiomeId>());
        PreparedFogShaderAdapters = PllFogShaderAdapter.Capture(FogShaderAdapters);
        BundleFogShaderAdapters = PllBundleSerialization.SealArray(PreparedFogShaderAdapters);
        BundleFormat = PllBundleSerialization.Format;
    }
#else
    public BiomeId BiomeId => Identity.BiomeId;

    PreparedBundleIdentity Identity => PreparedIdentity ??= ReadBundleIdentity();

    PreparedBundleIdentity ReadBundleIdentity()
    {
        if (BundleFormat != PllBundleSerialization.Format)
            throw new InvalidOperationException(
                $"Compiled authoring format '{BundleFormat}' is unsupported. Rebuild the bundle with PLL Creator {PLLLibrary.PluginVersion}.");
        if (!global::PLL.Library.BiomeId.TryParse(BundleBiomeId, out var Id))
            throw new InvalidOperationException("Compiled authoring data has an invalid biome ID.");
        return new PreparedBundleIdentity(
            Id,
            PllBundleSerialization.ReadBiomeIds(BundleFavorites, "favorites"),
            PllBundleSerialization.ReadBiomeIds(BundleOverwrites, "overwrites"),
            PllBundleSerialization.ReadBiomeIds(BundleBefore, "before"),
            PllBundleSerialization.ReadBiomeIds(BundleAfter, "after"));
    }

    sealed class PreparedBundleIdentity
    {
        internal PreparedBundleIdentity(
            BiomeId BiomeId, BiomeId[] Favorites, BiomeId[] Overwrites,
            BiomeId[] Before, BiomeId[] After)
        {
            this.BiomeId = BiomeId; this.Favorites = Favorites;
            this.Overwrites = Overwrites; this.Before = Before; this.After = After;
        }

        internal BiomeId BiomeId { get; }
        internal BiomeId[] Favorites { get; }
        internal BiomeId[] Overwrites { get; }
        internal BiomeId[] Before { get; }
        internal BiomeId[] After { get; }
    }

    internal GeneratedPropAssetCatalog? PrepareGeneratedPropAssetCatalog(
        IReadOnlyList<Transform> Roots, AuthoringCandidateGraph Graph,
        out int[][] AllowedPrefabOrdinals) =>
        GeneratedPropAssetCatalog.Create(BiomeId, BundleGeneratedPropAssetCatalog,
            BundleGeneratedPropPrefabs, BundleGeneratedPropMeshes, BundleGeneratedPropMaterials,
            BundleGeneratedPropPhysicsMaterials, Roots, Graph, out AllowedPrefabOrdinals);
#endif

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        PreparedFogShaderAdapters = null;
#if !PLL_CREATOR_SDK
        PreparedIdentity = null;
#endif
    }

    internal PllFogShaderAdapter[] CaptureFogShaderAdapters() => PreparedFogShaderAdapters ??=
        PllFogShaderAdapter.Capture(BundleFogShaderAdapters
            ?? throw PllBundleSerialization.Missing("fog shader adapters"));

    public PllBiomeBehavior Behavior => behavior;
    public bool SupportsNativeProgressionContents =>
        PllBiomeBehaviorCompatibility.Resolve(behavior).HasValue;
    public string DisplayTitle => displayTitle ?? "";
    public string ContentRevision => contentRevision ?? "";
#if PLL_CREATOR_SDK
    public IReadOnlyList<BiomeId> Favorites => favorites ?? Array.Empty<BiomeId>();
    public IReadOnlyList<BiomeId> Overwrites => overwrites ?? Array.Empty<BiomeId>();
    public IReadOnlyList<BiomeId> Before => before ?? Array.Empty<BiomeId>();
    public IReadOnlyList<BiomeId> After => after ?? Array.Empty<BiomeId>();
#else
    public IReadOnlyList<BiomeId> Favorites => Identity.Favorites;
    public IReadOnlyList<BiomeId> Overwrites => Identity.Overwrites;
    public IReadOnlyList<BiomeId> Before => Identity.Before;
    public IReadOnlyList<BiomeId> After => Identity.After;
#endif
    public int SpawnChance => spawnChance;
    public GameObject RootPrefab => rootPrefab;
    public PllBiomePrefabAnchors Anchors => anchors;
    public DayNightProfile DayNightProfile => dayNightProfile;
    public IReadOnlyList<PllVisualAuthoring> Visuals => visuals ?? Array.Empty<PllVisualAuthoring>();
    public Color Color => color;
    public IReadOnlyList<AudioClip> AmbienceSounds => ambienceSounds ?? Array.Empty<AudioClip>();
    public IReadOnlyList<AudioClip> EffectSounds => effectSounds ?? Array.Empty<AudioClip>();
    public IReadOnlyList<AudioClip> EnterSounds => enterSounds ?? Array.Empty<AudioClip>();
    public IReadOnlyList<PllLuggageAuthoring> LuggageDefinitions => luggageDefinitions ?? Array.Empty<PllLuggageAuthoring>();
    public IReadOnlyList<PllVariationAuthoring> VariationDefinitions => variationDefinitions ?? Array.Empty<PllVariationAuthoring>();
    public IReadOnlyList<PllEffectAuthoring> EffectDefinitions => effectDefinitions ?? Array.Empty<PllEffectAuthoring>();
    public IReadOnlyList<PllSoundEmitterAuthoring> SoundEmitters => soundEmitters ?? Array.Empty<PllSoundEmitterAuthoring>();
    public IReadOnlyList<PllBiomeStingerAuthoring> StingerDefinitions =>
        stingerDefinitions ?? Array.Empty<PllBiomeStingerAuthoring>();
}
