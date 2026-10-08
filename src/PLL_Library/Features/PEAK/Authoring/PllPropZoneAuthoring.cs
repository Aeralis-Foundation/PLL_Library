using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Spawning/Prop Zone")]
public sealed class PllPropZoneAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume volume = default;
#endif
    [SerializeField, HideInInspector] PllZoneShape BundleShape = default;
    [SerializeField, HideInInspector] Vector3 BundleCenter = default;
    [SerializeField, HideInInspector] Vector3 BundleSize = default;
    [SerializeField, HideInInspector] float BundleRadius = 0f;
    [SerializeField] PropSpawner[] spawners = Array.Empty<PropSpawner>();
    [SerializeField] bool inverted = false;

    public GameObject Root => root;
#if PLL_CREATOR_SDK
    public PllZoneVolume Volume => volume;

    internal void SealBundleSerialization()
    {
        BundleShape = volume.Shape; BundleCenter = volume.Center;
        BundleSize = volume.Size; BundleRadius = volume.Radius;
    }
#else
    public PllZoneVolume Volume => new(BundleShape, BundleCenter, BundleSize, BundleRadius);
#endif
    public IReadOnlyList<PropSpawner> Spawners => spawners ?? Array.Empty<PropSpawner>();
    public bool Inverted => inverted;
}

[Serializable]
public sealed class PllPropZoneConstraint : PropSpawnerConstraint
{
    [SerializeField] Transform root = null!;
#if PLL_CREATOR_SDK
    [SerializeField] PllZoneVolume volume;
#endif
    [SerializeField, HideInInspector] PllZoneShape ShapeValue;
    [SerializeField, HideInInspector] Vector3 CenterValue;
    [SerializeField, HideInInspector] Vector3 SizeValue;
    [SerializeField, HideInInspector] float RadiusValue;
    [SerializeField] bool inverted;

    public PllPropZoneConstraint(Transform root, PllZoneVolume volume, bool inverted)
    {
        this.root = root;
#if PLL_CREATOR_SDK
        this.volume = volume;
#endif
        ShapeValue = volume.Shape; CenterValue = volume.Center;
        SizeValue = volume.Size; RadiusValue = volume.Radius;
        this.inverted = inverted;
    }

    internal Transform Root => root;
#if PLL_CREATOR_SDK
    internal PllZoneVolume Volume => volume;
#else
    internal PllZoneVolume Volume => new(ShapeValue, CenterValue, SizeValue, RadiusValue);
#endif
    internal bool Inverted => inverted;

    public override bool CheckConstraint(PropSpawner.SpawnData data)
    {
        var inside = PllZoneMath.Contains(root, Volume, data.pos);
        return inverted ? !inside : inside;
    }
}
