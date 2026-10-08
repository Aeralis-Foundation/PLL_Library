using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal static class ManifestEncoding
{
    static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal static void WritePose(BinaryWriter Writer, Pose Value)
    {
        WriteVector(Writer, Value.position);
        var Rotation = Normalize(Value.rotation);
        WriteQuantized(Writer, Rotation.x); WriteQuantized(Writer, Rotation.y);
        WriteQuantized(Writer, Rotation.z); WriteQuantized(Writer, Rotation.w);
    }

    internal static void WriteVector(BinaryWriter Writer, Vector3 Value)
    {
        WriteQuantized(Writer, Value.x); WriteQuantized(Writer, Value.y); WriteQuantized(Writer, Value.z);
    }

    internal static void WriteScalar(BinaryWriter Writer, float Value) => WriteQuantized(Writer, Value);

    internal static void WriteColor(BinaryWriter Writer, Color Value)
    {
        WriteScalar(Writer, Value.r); WriteScalar(Writer, Value.g);
        WriteScalar(Writer, Value.b); WriteScalar(Writer, Value.a);
    }

    internal static bool CanEncode(float Value) =>
        PllAuthoringValueContract.CanEncode(Value);

    internal static bool CanEncodeExactly(float Value) =>
        PllAuthoringValueContract.CanEncodeExactly(Value);

    internal static string CreateRevision(Action<BinaryWriter> WriteManifest)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, StrictUtf8, true)) WriteManifest(Writer);
        using var Sha = SHA256.Create();
        var Hash = Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
        var Result = new StringBuilder(Hash.Length * 2);
        foreach (var Value in Hash) Result.Append(Value.ToString("X2"));
        return Result.ToString();
    }

    static void WriteQuantized(BinaryWriter Writer, float Value) =>
        Writer.Write(checked((long)Math.Round(Value * PllAuthoringValueContract.ManifestQuantum, MidpointRounding.AwayFromZero)));

    static Quaternion Normalize(Quaternion Value)
    {
        var Magnitude = Math.Sqrt(Value.x * Value.x + Value.y * Value.y + Value.z * Value.z + Value.w * Value.w);
        if (Magnitude == 0d || double.IsNaN(Magnitude) || double.IsInfinity(Magnitude))
            throw new InvalidOperationException("A segment pose contains an invalid rotation.");
        var Result = new Quaternion(
            (float)(Value.x / Magnitude), (float)(Value.y / Magnitude),
            (float)(Value.z / Magnitude), (float)(Value.w / Magnitude));
        if (Result.w < 0f || Result.w == 0f && (Result.z < 0f
            || Result.z == 0f && (Result.y < 0f || Result.y == 0f && Result.x < 0f)))
            Result = new Quaternion(-Result.x, -Result.y, -Result.z, -Result.w);
        return Result;
    }
}
