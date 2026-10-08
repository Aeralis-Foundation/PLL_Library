using System;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeCampfireCheckpoint
{
    NativeCampfireCheckpoint(float Radius, float InteractTime)
    {
        this.Radius = Radius;
        this.InteractTime = InteractTime;
    }

    internal float Radius { get; }
    internal float InteractTime { get; }

    internal static NativeCampfireCheckpoint Capture(MapHandler Handler)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        if (Handler.segments is null || Handler.segments.Length <= (int)Segment.Beach)
            throw new InvalidOperationException("PEAK's native Beach segment is unavailable.");
        var Beach = Handler.segments[(int)Segment.Beach]
            ?? throw new InvalidOperationException("PEAK's native Beach segment is unavailable.");
        var Root = Beach._segmentCampfire;
        var Campfires = Root
            ? Root.GetComponentsInChildren<global::Campfire>(true)
            : Array.Empty<global::Campfire>();
        if (Campfires.Length != 1)
            throw new InvalidOperationException("PEAK's native Beach campfire interaction contract is invalid.");
        var Campfire = Campfires[0];
        if (!(Campfire.moraleBoostRadius > 0f) || !float.IsFinite(Campfire.moraleBoostRadius)
            || !(Campfire.cookTime > 0f) || !float.IsFinite(Campfire.cookTime))
            throw new InvalidOperationException("PEAK's native Beach campfire interaction contract is invalid.");
        return new NativeCampfireCheckpoint(Campfire.moraleBoostRadius, Campfire.cookTime);
    }
}