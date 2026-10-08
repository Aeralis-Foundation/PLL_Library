using System;
using UnityEngine;

namespace PLL.Library;

internal static class NativeResolverVolume
{
    internal static Bounds WorldBounds(Transform Root, PllZoneVolume Volume)
    {
        var Center = Root.TransformPoint(Volume.Center);
        if (!float.IsFinite(Center.x) || !float.IsFinite(Center.y) || !float.IsFinite(Center.z))
            throw new InvalidOperationException("A native resolver has a non-finite world center.");
        if (Volume.Shape == PllZoneShape.Sphere)
        {
            var Radius = WorldSphereRadius(Root, Volume);
            return new Bounds(Center, Vector3.one * (Radius * 2f));
        }
        var Extents = Volume.Size * 0.5f;
        var X = Root.TransformVector(new Vector3(Extents.x, 0f, 0f));
        var Y = Root.TransformVector(new Vector3(0f, Extents.y, 0f));
        var Z = Root.TransformVector(new Vector3(0f, 0f, Extents.z));
        if (!PositiveFinite(X.magnitude) || !PositiveFinite(Y.magnitude) || !PositiveFinite(Z.magnitude))
            throw new InvalidOperationException("A native resolver has a degenerate world Box transform.");
        var WorldExtents = new Vector3(
            Mathf.Abs(X.x) + Mathf.Abs(Y.x) + Mathf.Abs(Z.x),
            Mathf.Abs(X.y) + Mathf.Abs(Y.y) + Mathf.Abs(Z.y),
            Mathf.Abs(X.z) + Mathf.Abs(Y.z) + Mathf.Abs(Z.z));
        if (!PositiveFinite(WorldExtents.x) || !PositiveFinite(WorldExtents.y) || !PositiveFinite(WorldExtents.z))
            throw new InvalidOperationException("A native resolver has invalid world Box bounds.");
        return new Bounds(Center, WorldExtents * 2f);
    }

    internal static float WorldSphereRadius(Transform Root, PllZoneVolume Volume)
    {
        var X = Root.TransformVector(Vector3.right);
        var Y = Root.TransformVector(Vector3.up);
        var Z = Root.TransformVector(Vector3.forward);
        var XLength = X.magnitude;
        var YLength = Y.magnitude;
        var ZLength = Z.magnitude;
        if (!PositiveFinite(XLength) || !PositiveFinite(YLength) || !PositiveFinite(ZLength))
            throw new InvalidOperationException("A SporeCloud resolver has a degenerate world transform.");
        var Scale = (XLength + YLength + ZLength) / 3f;
        if (Mathf.Abs(XLength - Scale) > Scale * 0.0001f
            || Mathf.Abs(YLength - Scale) > Scale * 0.0001f
            || Mathf.Abs(ZLength - Scale) > Scale * 0.0001f)
            throw new InvalidOperationException("A SporeCloud resolver must preserve a uniformly scaled world sphere.");
        if (Mathf.Abs(Vector3.Dot(X / XLength, Y / YLength)) > 0.0001f
            || Mathf.Abs(Vector3.Dot(X / XLength, Z / ZLength)) > 0.0001f
            || Mathf.Abs(Vector3.Dot(Y / YLength, Z / ZLength)) > 0.0001f)
            throw new InvalidOperationException("A SporeCloud resolver world transform contains shear.");
        return Volume.Radius * Scale;
    }
    static bool PositiveFinite(float Value) => Value > 0f && float.IsFinite(Value);
}
