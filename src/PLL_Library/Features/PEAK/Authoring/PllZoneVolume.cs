using System;
using UnityEngine;

namespace PLL.Library;

public enum PllZoneShape : byte
{
    Box = 0,
    Sphere = 1
}

[Serializable]
public struct PllZoneVolume
{
    [SerializeField] PllZoneShape shape;
    [SerializeField] Vector3 center;
    [SerializeField] Vector3 size;
    [SerializeField, Min(0f)] float radius;

    internal PllZoneVolume(PllZoneShape Shape, Vector3 Center, Vector3 Size, float Radius)
    {
        shape = Shape; center = Center; size = Size; radius = Radius;
    }

    public PllZoneShape Shape => shape;
    public Vector3 Center => center;
    public Vector3 Size => size;
    public float Radius => radius;
}

static class PllZoneMath
{
    internal static bool Contains(Transform root, PllZoneVolume volume, Vector3 worldPoint)
        => Contains(volume, root.InverseTransformPoint(worldPoint));

    internal static bool Contains(PllZoneVolume Volume, Vector3 LocalPoint)
    {
        var Point = LocalPoint - Volume.Center;
        if (Volume.Shape == PllZoneShape.Sphere) return Point.sqrMagnitude <= Volume.Radius * Volume.Radius;
        var Extents = Volume.Size * 0.5f;
        return Mathf.Abs(Point.x) <= Extents.x && Mathf.Abs(Point.y) <= Extents.y && Mathf.Abs(Point.z) <= Extents.z;
    }
}
