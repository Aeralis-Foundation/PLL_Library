using System;
using System.Collections.Generic;

namespace PLL.Library;

internal static class Effects
{
    internal static void Commit()
    {
        WeatherProjection.CommitWeather();
        SpecialDayProjection.Commit();
    }

    internal static void Release(bool RestoreNative) =>
        ReleaseProjections(RestoreNative, SpecialDayProjection.Release);

    internal static void Rollback() =>
        ReleaseProjections(true, SpecialDayProjection.Rollback);

    static void ReleaseProjections(bool RestoreNative, Action ReleaseSpecialDay)
    {
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, WeatherProjection.RollbackWeather);
        CleanupOperations.Capture(ref Failures, () => FogCutoutProjection.Release(RestoreNative));
        CleanupOperations.Capture(ref Failures, ReleaseSpecialDay);
        CleanupOperations.Capture(ref Failures, () => TerrainSplatProjection.Release(RestoreNative));
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Map effect release failed.", Failures);
    }
}
