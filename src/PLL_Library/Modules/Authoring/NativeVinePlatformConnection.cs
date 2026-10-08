using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal static class NativeVinePlatformConnection
{
    internal static void Build(JungleVine Vine, IReadOnlyList<TreePlatform> Platforms, Transform Scope)
    {
        if (!NativeFeatureGeneration.IsActiveDescendant(Vine.transform))
            throw new InvalidOperationException("Native platform connection requires the current generation lease.");
        var Owned = new List<TreePlatform>(Platforms.Count); var Seen = new HashSet<TreePlatform>();
        foreach (var Platform in Platforms)
        {
            if (!Platform || !TransformOwnership.Contains(Scope, Platform.transform)
                || !Platform.gameObject.activeInHierarchy || !Seen.Add(Platform)
                || Platform.connectedPlatforms is null)
                throw new InvalidOperationException("Native platform inventory must contain unique ready owned platforms.");
            Owned.Add(Platform);
        }
        if (Owned.Count < 2) throw new InvalidOperationException("Native platform connection needs at least two owned platforms.");
        var Prior = SpawnConnectingBridge.parent;
        var Connections = new Dictionary<TreePlatform, HashSet<TreePlatform>>(Owned.Count);
        foreach (var Platform in Owned) Connections.Add(Platform, new(Platform.connectedPlatforms));
        var Completed = false;
        try
        {
            SpawnConnectingBridge.parent = Scope;
            Vine.PickTreePlatforms(Owned);
            if (!Vine || !Vine.startTreePlatform || !Vine.endTreePlatform
                || !Seen.Contains(Vine.startTreePlatform) || !Seen.Contains(Vine.endTreePlatform)
                || !Vine.colliderRoot || Vine.colliderRoot.childCount == 0)
                throw new InvalidOperationException("Native platform connection did not produce an owned supported span.");
            Completed = true;
        }
        finally
        {
            SpawnConnectingBridge.parent = Prior;
            if (!Completed)
                foreach (var Platform in Owned)
                    Platform.connectedPlatforms.RemoveAll(Value => !Connections[Platform].Contains(Value) && Seen.Contains(Value));
        }
    }
}
