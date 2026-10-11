using System;
using UnityEngine;

namespace PLL.Library;

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllBundleSerialization
{
    internal const int Format = 2;
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    const int MaximumHookedActions = 256;

#if PLL_CREATOR_SDK
    public static void SealFluidDefinition(PllFluidDefinition Definition) => Definition.SealBundleSerialization();

    public static void SealPrefab(GameObject Root)
    {
        foreach (var Component in Root.GetComponentsInChildren<MonoBehaviour>(true))
            switch (Component)
            {
                case PllFogZoneAuthoring Zone: Zone.SealBundleSerialization(); break;
                case PllEffectZoneAuthoring Zone: Zone.SealBundleSerialization(); break;
                case PllPropZoneAuthoring Zone: Zone.SealBundleSerialization(); break;
                case PllAmuletSpawnerAuthoring Amulet: Amulet.SealBundleSerialization(); break;
                case PllEntityZoneAuthoring Zone: Zone.SealBundleSerialization(); break;
                case PllHazardZoneAuthoring Zone: Zone.SealBundleSerialization(); break;
                case PllHazardAuthoring Hazard: Hazard.SealBundleSerialization(); break;
                case PllEntityAuthoring Entity: Entity.SealBundleSerialization(); break;
                case PllBiomeStingerAuthoring Stinger: Stinger.SealBundleSerialization(); break;
                case PllCustomVisualAuthoring Visual: Visual.SealBundleSerialization(); break;
                case PllEffectAuthoring Effect: Effect.SealBundleSerialization(); break;
                case PLLNativeVisualResolver Visual: Visual.SealBundleSerialization(); break;
                case PllSoundEmitterAuthoring Sound: Sound.SealBundleSerialization(); break;
                case PllVisualAuthoring Visual: Visual.SealBundleSerialization(); break;
                case PllNativeMaterialAuthoring Material: Material.SealBundleSerialization(); break;
                case PLLNativeResolver Resolver: Resolver.SealBundleSerialization(); break;
                case PllVariationAuthoring Variations: Variations.SealBundleSerialization(); break;
                case PllLuggageAuthoring Luggage: Luggage.SealBundleSerialization(); break;
                case PllClimbSurfaceMaterialAuthoring Materials: Materials.SealBundleSerialization(); break;
                case CustomSegmentRuntime Segment: Segment.SealBundleSerialization(); break;
            }
    }

    internal static object[] SealArray<T>(T[] Source) where T : class
    {
        if (Source.Length == 0) return Array.Empty<object>();
        var Result = new object[Source.Length];
        Array.Copy(Source, Result, Source.Length);
        return Result;
    }

    internal static string[] SealBiomeIds(BiomeId[] Source)
    {
        if (Source.Length == 0) return Array.Empty<string>();
        var Result = new string[Source.Length];
        for (var Index = 0; Index < Result.Length; Index++)
            Result[Index] = Source[Index].Value;
        return Result;
    }
#else
    internal static T[] ReadArray<T>(object[]? Source, string Field) where T : class
    {
        if (Source is null) throw Missing(Field);
        if (Source.Length == 0) return Array.Empty<T>();
        var Result = new T[Source.Length];
        for (var Index = 0; Index < Result.Length; Index++)
            Result[Index] = Source[Index] as T
                ?? throw new InvalidOperationException(
                    $"Compiled authoring field '{Field}[{Index}]' must contain {typeof(T).Name}.");
        return Result;
    }

    internal static BiomeId[] ReadBiomeIds(string[]? Source, string Field)
    {
        if (Source is null) throw Missing(Field);
        if (Source.Length == 0) return Array.Empty<BiomeId>();
        var Result = new BiomeId[Source.Length];
        for (var Index = 0; Index < Result.Length; Index++)
            if (!BiomeId.TryParse(Source[Index], out Result[Index]))
                throw new InvalidOperationException(
                    $"Compiled authoring field '{Field}[{Index}]' has an invalid biome ID.");
        return Result;
    }

#endif

    internal static InvalidOperationException Missing(string Field) => new(
        $"Compiled authoring data for '{Field}' is missing. Rebuild the bundle with PLL Creator {PLLLibrary.PluginVersion}.");
}
