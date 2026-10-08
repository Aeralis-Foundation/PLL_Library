using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

public enum PllFogShaderAdapterKind : byte { Wrapped = 0, LayeredDirect = 1 }

[Serializable]
public sealed class PllFogShaderAdapter
{
    [SerializeField] Shader OriginalShaderValue = null!;
    [SerializeField] Shader FogShaderValue = null!;
    [SerializeField] byte[] PreparedContentValue = Array.Empty<byte>();
    [SerializeField] PllFogShaderAdapterKind KindValue = PllFogShaderAdapterKind.Wrapped;

    PllFogShaderAdapter(Shader OriginalShader, Shader FogShader, ReadOnlySpan<byte> PreparedContent,
        PllFogShaderAdapterKind Kind = PllFogShaderAdapterKind.Wrapped)
    {
        OriginalShaderValue = OriginalShader; FogShaderValue = FogShader;
        PreparedContentValue = PreparedContent.ToArray();
        KindValue = Kind;
    }

    public Shader OriginalShader => OriginalShaderValue;
    public Shader FogShader => FogShaderValue;
    public ReadOnlySpan<byte> PreparedContent => PreparedContentValue;
    public PllFogShaderAdapterKind Kind => KindValue;

#if PLL_CREATOR_SDK
    public static PllFogShaderAdapter FromPreparedContent(
        Shader OriginalShader, Shader FogShader, ReadOnlySpan<byte> PreparedContent)
    {
        Validate(OriginalShader, FogShader, PreparedContent, PllFogShaderAdapterKind.Wrapped);
        return new PllFogShaderAdapter(OriginalShader, FogShader, PreparedContent);
    }

    public static PllFogShaderAdapter FromPreparedLayeredContent(Shader Shader, ReadOnlySpan<byte> PreparedContent)
    {
        Validate(Shader, Shader, PreparedContent, PllFogShaderAdapterKind.LayeredDirect);
        return new PllFogShaderAdapter(Shader, Shader, PreparedContent, PllFogShaderAdapterKind.LayeredDirect);
    }
#endif

    internal static PllFogShaderAdapter[] Capture(IReadOnlyList<object>? Source)
    {
        if (Source is null || Source.Count == 0) return Array.Empty<PllFogShaderAdapter>();
        var Originals = new HashSet<Shader>(); var Adapted = new HashSet<Shader>();
        foreach (var Value in Source)
        {
            if (Value is not PllFogShaderAdapter Adapter)
                throw new ArgumentException("A compiled fog shader adapter is missing or has the wrong type.");
            Validate(Adapter.OriginalShader, Adapter.FogShader, Adapter.PreparedContent, Adapter.Kind);
            if (!Originals.Add(Adapter.OriginalShader) || !Adapted.Add(Adapter.FogShader))
                throw new ArgumentException("Fog shader adapters contain duplicate shader references.");
        }
        foreach (PllFogShaderAdapter Value in Source)
            if (Value.Kind == PllFogShaderAdapterKind.Wrapped && Originals.Contains(Value.FogShader))
                throw new ArgumentException("A wrapped fog shader cannot also be an original shader.");
        var Result = new PllFogShaderAdapter[Source.Count];
        for (var i = 0; i < Result.Length; i++)
        {
            var Value = (PllFogShaderAdapter)Source[i];
            Result[i] = new PllFogShaderAdapter(
                Value.OriginalShader, Value.FogShader, Value.PreparedContent, Value.Kind);
        }
        return Result;
    }

    static void Validate(Shader OriginalShader, Shader FogShader, ReadOnlySpan<byte> PreparedContent, PllFogShaderAdapterKind Kind)
    {
        if (!OriginalShader || !FogShader || Kind is not (PllFogShaderAdapterKind.Wrapped or PllFogShaderAdapterKind.LayeredDirect)
            || Kind == PllFogShaderAdapterKind.Wrapped && OriginalShader == FogShader
            || Kind == PllFogShaderAdapterKind.LayeredDirect
                && (OriginalShader != FogShader || OriginalShader.name != PllSurfaceProfileContract.ShaderName))
            throw new ArgumentException("A fog shader adapter has an invalid wrapped or direct shader identity.");
        if (PreparedContent.Length != 32)
            throw new ArgumentException("A fog shader preparation receipt must contain 32 bytes.");
    }
}
