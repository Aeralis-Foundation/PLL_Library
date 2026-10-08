using System;
using System.Collections;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public abstract class PllEntityZoneSource { }

[Serializable]
public sealed class PllBeetleZoneSource : PllEntityZoneSource
{
    [SerializeReference] public PllNativeEntityAppearance? Appearance;
    [Min(0)] public int MaximumInitialCount = 15;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    [Range(0f, 90f)] public float MaximumSlope = 45f;
    [Min(0f)] public float MinimumSpacing = 10f;
}

[Serializable]
public sealed class PllMushroomZombieZoneSource : PllEntityZoneSource
{
    [Min(0)] public int MaximumInitialCount = 15;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    [Range(0f, 90f)] public float MaximumSlope = 45f;
    [Min(0f)] public float MinimumSpacing = 10f;
    public bool CullRandomly = true;
}

[Serializable]
public sealed class PllCondorZoneSource : PllEntityZoneSource
{
    [SerializeReference] public PllNativeEntityAppearance? Appearance;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    public Vector3 NestPosition = new(0f, 0f, 10f);
}

[Serializable]
public sealed class PllAuthoredEntityZoneSource : PllEntityZoneSource
{
    public GameObject Prefab = null!;
    [Min(0)] public int MaximumInitialCount = 15;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    [Range(0f, 90f)] public float MaximumSlope = 45f;
    [Min(0f)] public float MinimumSpacing = 10f;
}

#if PLL_CREATOR_SDK
public
#else
internal
#endif
static class PllEntityZoneValueContract
{
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllEntityZoneAuthoring Zone)
        => MetadataFailure(Zone) ?? SourceContentFailure(Zone.Source);

    internal static string? MetadataFailure(PllEntityZoneAuthoring Zone)
    {
        if (!PllAuthoringValueContract.IsLocalId(Zone.Id))
            return "Entity Zone ID must be a stable local ID without spaces or path separators.";
        if (PllAuthoringValueContract.ZoneVolumeFailure(Zone.Volume) is { } VolumeFailure)
            return VolumeFailure.Message;
        return SourceMetadataFailure(Zone.Source);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? SourceFailure(PllEntityZoneSource Source)
        => SourceMetadataFailure(Source) ?? SourceContentFailure(Source);

    static string? SourceContentFailure(PllEntityZoneSource Source) => Source switch
    {
        PllPluginEntityZoneSource Plugin => PllPluginEntityZoneValueContract.PrefabFailure(Plugin.Prefab),
        PllAuthoredEntityZoneSource Authored => PllEntityValueContract.Failure(Authored.Prefab.GetComponent<PllEntityAuthoring>()),
        _ => PllNativeEntityAppearanceValueContract.Failure(PllNativeEntityAppearanceValueContract.Appearance(Source))
    };

    static string? SourceMetadataFailure(PllEntityZoneSource Source)
    {
        return Source switch
        {
            PllBeetleZoneSource Beetles => GroundFailure(Beetles.MaximumInitialCount,
                Beetles.SpawnChance, Beetles.MaximumSlope, Beetles.MinimumSpacing),
            PllMushroomZombieZoneSource Zombies => GroundFailure(Zombies.MaximumInitialCount,
                Zombies.SpawnChance, Zombies.MaximumSlope, Zombies.MinimumSpacing),
            PllCondorZoneSource Condor when !PllAuthoringValueContract.CanEncode(Condor.SpawnChance)
                || Condor.SpawnChance is < 0f or > 1f => "Condor spawn chance must be finite and between 0 and 1.",
            PllCondorZoneSource Condor when !PllAuthoringValueContract.CanEncode(Condor.NestPosition.x)
                || !PllAuthoringValueContract.CanEncode(Condor.NestPosition.y)
                || !PllAuthoringValueContract.CanEncode(Condor.NestPosition.z)
                => "Condor nest position must be finite.",
            PllCondorZoneSource => null,
            PllPluginEntityZoneSource Plugin => PllPluginEntityZoneValueContract.MetadataFailure(Plugin),
            PllAuthoredEntityZoneSource Authored => AuthoredFailure(Authored),
            _ => "Entity Zone source is unsupported. Select a native source or installed plugin factory."
        };
    }

    static string? AuthoredFailure(PllAuthoredEntityZoneSource Source)
    {
        if (!Source.Prefab || Source.Prefab.scene.IsValid()
            || !Source.Prefab.TryGetComponent<PllEntityAuthoring>(out var Definition))
            return "Authored Entity source needs a prefab with one root Entity definition.";
        if (!Definition.InitiallyEnabled)
            return "Authored Entity Zone requires an initially enabled definition.";
        if (Definition.Behavior is PllCondorBehavior && Source.MaximumInitialCount > 1)
            return "The native Condor Zone supports at most one initial Condor.";
        return GroundFailure(Source.MaximumInitialCount, Source.SpawnChance,
            Source.MaximumSlope, Source.MinimumSpacing);
    }

    internal static string? GroundFailure(int Count, float Chance, float Slope, float Spacing)
    {
        if (Count < 0) return "Entity initial count must be nonnegative.";
        if (!PllAuthoringValueContract.CanEncode(Chance) || Chance is < 0f or > 1f)
            return "Entity spawn chance must be finite and between 0 and 1.";
        if (!PllAuthoringValueContract.CanEncode(Slope) || Slope is < 0f or > 90f)
            return "Entity maximum slope must be finite and between 0 and 90 degrees.";
        if (!PllAuthoringValueContract.CanEncode(Spacing) || Spacing < 0f)
            return "Entity minimum spacing must be finite and nonnegative.";
        return null;
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Entity Zone")]
public sealed class PllEntityZoneAuthoring : LevelGenStep
{
    [SerializeField] string IdValue = string.Empty;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume VolumeValue = default;
    [SerializeReference] PllEntityZoneSource SourceValue = new PllBeetleZoneSource();
#endif
    [SerializeField, HideInInspector] PllZoneShape BundleShape = default;
    [SerializeField, HideInInspector] Vector3 BundleCenter = default;
    [SerializeField, HideInInspector] Vector3 BundleSize = default;
    [SerializeField, HideInInspector] float BundleRadius = 0f;
    [SerializeReference, HideInInspector] object BundleSourceValue = null!;
    [SerializeField, HideInInspector] Transform OutputRootValue = null!;

    public string Id => IdValue;
    public Transform OutputRoot => OutputRootValue;
#if PLL_CREATOR_SDK
    public PllZoneVolume Volume => VolumeValue;
    public PllEntityZoneSource Source => SourceValue;

    internal void SealBundleSerialization()
    {
        BundleShape = VolumeValue.Shape; BundleCenter = VolumeValue.Center;
        BundleSize = VolumeValue.Size; BundleRadius = VolumeValue.Radius;
        BundleSourceValue = SourceValue;
    }

    public override IEnumerator Execute() => throw new InvalidOperationException("Entity Zone generation runs in PEAK.");
    public override void ExecuteImmediately() => throw new InvalidOperationException("Entity Zone generation runs in PEAK.");
    public override void Clear() { }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
    public PllEntityZoneSource Source => BundleSourceValue as PllEntityZoneSource
        ?? throw PllBundleSerialization.Missing(nameof(BundleSourceValue));
    internal EntityZoneBinding? Binding;

    public override IEnumerator Execute() => RequireBinding().Generate();
    public override void ExecuteImmediately() => RequireBinding().GenerateImmediately();
    public override void Clear() => Binding?.Clear();

    void OnDestroy() => Binding?.ReleaseAppearance();

    EntityZoneBinding RequireBinding() => Binding
        ?? throw new InvalidOperationException($"Entity Zone '{Id}' has no occurrence-owned recipe.");
#endif
}
