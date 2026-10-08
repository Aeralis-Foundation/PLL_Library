using System;

namespace PLL.Library;

internal static class PhotonPropertyProtocol
{
    internal const string StatePrefix = PLLLibrary.PluginGuid + "/state/";
    internal static object Snapshot(object value)
    {
        if (value is byte[] bytes) return (byte[])bytes.Clone();
        if (value is int[] integers) return (int[])integers.Clone();
        if (value is float[] Floats) return (float[])Floats.Clone();
        if (value is not object[] values) return value;
        var result = new object[values.Length];
        for (var i = 0; i < values.Length; i++) result[i] = Snapshot(values[i]);
        return result;
    }

    internal static bool SameValue(object? actual, object? expected)
    {
        if (expected is byte[] expectedBytes)
        {
            if (actual is not byte[] actualBytes || actualBytes.Length != expectedBytes.Length) return false;
            for (var i = 0; i < expectedBytes.Length; i++)
                if (actualBytes[i] != expectedBytes[i]) return false;
            return true;
        }
        if (expected is int[] expectedInts)
        {
            if (actual is not int[] actualInts || actualInts.Length != expectedInts.Length) return false;
            for (var i = 0; i < expectedInts.Length; i++)
                if (actualInts[i] != expectedInts[i]) return false;
            return true;
        }
        if (expected is float[] ExpectedFloats)
        {
            if (actual is not float[] ActualFloats || ActualFloats.Length != ExpectedFloats.Length) return false;
            for (var i = 0; i < ExpectedFloats.Length; i++)
                if (ActualFloats[i] != ExpectedFloats[i]) return false;
            return true;
        }
        if (expected is object[] expectedObjects)
        {
            if (actual is not object[] actualObjects || actualObjects.Length != expectedObjects.Length) return false;
            for (var i = 0; i < expectedObjects.Length; i++)
                if (!SameValue(actualObjects[i], expectedObjects[i])) return false;
            return true;
        }
        return Equals(actual, expected);
    }

    internal static object[] RunValue(string epoch, int revision, object value) =>
        new object[] { epoch, revision, value };

    internal static bool SameRunValue(object? value, string epoch, int revision, object expected) =>
        value is object[] { Length: 3 } data
        && data[0] is string actualEpoch && actualEpoch == epoch
        && data[1] is int actualRevision && actualRevision == revision
        && data[2] is { } actual && SameValue(actual, expected);

    internal static bool TryReadRunValue<T>(object? value, string epoch, int revision, out T result)
    {
        result = default!;
        if (value is not object[] { Length: 3 } data
            || data[0] is not string actualEpoch || actualEpoch != epoch
            || data[1] is not int actualRevision || actualRevision != revision
            || data[2] is not T typed) return false;
        result = typed;
        return true;
    }
}
