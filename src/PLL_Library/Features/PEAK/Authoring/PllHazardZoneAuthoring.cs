using System;
using System.Collections;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public abstract class PllHazardZoneSource { }

[Serializable]
public sealed class PllTornadoZoneSource : PllHazardZoneSource
{
    [Min(0)] public int MaximumAlive = 1;
    public Vector3[] Points = new[] { new Vector3(-5f, 0f, 0f), Vector3.zero, new Vector3(5f, 0f, 0f) };
}

[Serializable]
public sealed class PllTumbleweedZoneSource : PllHazardZoneSource
{
    [Min(0)] public int MaximumAlive = 4;
    public Vector3[] Positions = new[] { Vector3.zero };
    public Vector3 SpawnOffset = Vector3.up;
}

[Serializable]
public sealed class PllAuthoredHazardZoneSource : PllHazardZoneSource
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
static class PllHazardZoneValueContract
{
#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? Failure(PllHazardZoneAuthoring Zone)
    {
        if (!PllAuthoringValueContract.IsLocalId(Zone.Id))
            return "Hazard Zone ID must be a stable local ID without spaces or path separators.";
        var Volume = Zone.Volume;
        return SourceFailure(Zone.Source, Volume.Shape, Volume.Center, Volume.Size, Volume.Radius);
    }

#if PLL_CREATOR_SDK
    public
#else
    internal
#endif
    static string? SourceFailure(PllHazardZoneSource Source, PllZoneShape Shape,
        Vector3 Center, Vector3 Size, float Radius)
    {
        var Volume = new PllZoneVolume(Shape, Center, Size, Radius);
        if (PllAuthoringValueContract.ZoneVolumeFailure(Volume) is { } Failure) return Failure.Message;
        return Source switch
        {
            PllTornadoZoneSource Tornado => PointsFailure(Volume, Tornado.Points, Tornado.MaximumAlive, Vector3.zero),
            PllTumbleweedZoneSource Tumbleweed => PointsFailure(Volume, Tumbleweed.Positions, Tumbleweed.MaximumAlive, Tumbleweed.SpawnOffset),
            PllAuthoredHazardZoneSource Authored => AuthoredFailure(Authored),
            _ => "Hazard Zone source is unsupported. Select a native source or authored Hazard prefab."
        };
    }

    static string? AuthoredFailure(PllAuthoredHazardZoneSource Source)
    {
        if (!Source.Prefab || Source.Prefab.scene.IsValid()
            || !Source.Prefab.TryGetComponent<PllHazardAuthoring>(out var Definition))
            return "Authored Hazard source needs a prefab with one root Hazard definition.";
        return PllEntityZoneValueContract.GroundFailure(Source.MaximumInitialCount,
            Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
            ?? PllHazardValueContract.Failure(Definition);
    }

    static string? PointsFailure(PllZoneVolume Volume, Vector3[] Points, int MaximumAlive, Vector3 Offset)
    {
        if (MaximumAlive < 0) return "Hazard maximum alive count must be nonnegative.";
        if (!PllAuthoringValueContract.CanEncode(Offset)) return "Hazard spawn offset must be finite.";
        if (Points is null || Points.Length == 0) return "Hazard source needs at least one local position.";
        foreach (var Point in Points)
            if (!PllAuthoringValueContract.CanEncode(Point) || !PllZoneMath.Contains(Volume, Point + Offset))
                return "Hazard initial positions must be finite and inside the zone volume.";
        return null;
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Hazard Zone")]
public sealed class PllHazardZoneAuthoring : LevelGenStep
{
    [SerializeField] string IdValue = string.Empty;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume VolumeValue = default;
    [SerializeReference] PllHazardZoneSource SourceValue = new PllTornadoZoneSource();
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
    public PllHazardZoneSource Source => SourceValue;
    internal void SealBundleSerialization()
    {
        BundleShape = VolumeValue.Shape; BundleCenter = VolumeValue.Center;
        BundleSize = VolumeValue.Size; BundleRadius = VolumeValue.Radius; BundleSourceValue = SourceValue;
    }
    public override IEnumerator Execute() => throw new InvalidOperationException("Hazard Zone generation runs in PEAK.");
    public override void ExecuteImmediately() => throw new InvalidOperationException("Hazard Zone generation runs in PEAK.");
    public override void Clear() { }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
    public PllHazardZoneSource Source => BundleSourceValue as PllHazardZoneSource
        ?? throw PllBundleSerialization.Missing(nameof(BundleSourceValue));
    internal HazardZoneBinding? Binding;
    public override IEnumerator Execute() => RequireBinding().Generate();
    public override void ExecuteImmediately() => RequireBinding().GenerateImmediately();
    public override void Clear() => Binding?.Clear();
    HazardZoneBinding RequireBinding() => Binding
        ?? throw new InvalidOperationException($"Hazard Zone '{Id}' has no occurrence-owned recipe.");
#endif
}
