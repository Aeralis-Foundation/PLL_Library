using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace PLL.Library;

internal sealed class BiomeWallBox
{
    BiomeWallBox(Vector3 Center, Vector3 Size, Quaternion Rotation)
    {
        this.Center = Center; this.Size = Size; this.Rotation = Rotation;
    }
    internal Vector3 Center { get; }
    internal Vector3 Size { get; }
    internal Quaternion Rotation { get; }
    internal static bool TryParse(string Text, bool Configured, out BiomeWallBox Box)
    {
        Box = null!;
        if (!BiomeSpatialSettings.TryNumbers(Text, Configured ? 9 : 10, out var Numbers)
            || Numbers[3] <= 0f || Numbers[4] <= 0f || Numbers[5] <= 0f) return false;
        var Rotation = Configured ? Quaternion.Euler(Numbers[6], Numbers[7], Numbers[8]).normalized
            : new Quaternion(Numbers[6], Numbers[7], Numbers[8], Numbers[9]);
        if (!float.IsFinite(Rotation.x) || !float.IsFinite(Rotation.y) || !float.IsFinite(Rotation.z)
            || !float.IsFinite(Rotation.w) || Math.Abs(Quaternion.Dot(Rotation, Rotation) - 1f) > 0.00001f) return false;
        Box = new BiomeWallBox(new Vector3(Numbers[0], Numbers[1], Numbers[2]),
            new Vector3(Numbers[3], Numbers[4], Numbers[5]), Rotation);
        return true;
    }
}

internal sealed class FogCutoutOverride
{
    FogCutoutOverride(float Minimum, float Maximum, float Amount)
    {
        this.Minimum = Minimum; this.Maximum = Maximum; this.Amount = Amount;
    }
    internal float Minimum { get; }
    internal float Maximum { get; }
    internal float Amount { get; }
    internal static bool TryParse(string Text, out FogCutoutOverride Override)
    {
        Override = null!;
        if (!BiomeSpatialSettings.TryNumbers(Text, 3, out var Numbers) || Numbers[0] < 0f || Numbers[0] >= Numbers[1]
            || Numbers[2] < 0f || Numbers[2] > 1f) return false;
        Override = new FogCutoutOverride(Numbers[0], Numbers[1], Numbers[2]);
        return true;
    }
}

internal sealed class BiomeSpatialSettings
{
    BiomeSpatialSettings(BiomeId Id, BiomeWallBox[] Boxes, FogCutoutOverride? FogCutout)
    {
        this.Id = Id; this.Boxes = Array.AsReadOnly(Boxes); this.FogCutout = FogCutout;
    }
    internal BiomeId Id { get; }
    internal IReadOnlyList<BiomeWallBox> Boxes { get; }
    internal FogCutoutOverride? FogCutout { get; }
    internal static BiomeSpatialSettings Empty(BiomeId Id) => new(Id, Array.Empty<BiomeWallBox>(), null);

    internal static BiomeSpatialSettings ParseConfigured(BiomeId Id, string Boxes, string Fog)
    {
        if (!TryParse(Id, Boxes, Fog, true, out var Parsed))
            throw new InvalidDataException($"Biome '{Id}' has invalid Wall Boxes or Fog Cutout settings.");
        return Parsed;
    }

    internal static bool TryParse(BiomeId Id, string Boxes, string Fog, bool Configured, out BiomeSpatialSettings Parsed)
    {
        Parsed = null!;
        if (Encoding.UTF8.GetByteCount(Boxes) > RunConfigurationCodec.MaximumPayloadBytes
            || Encoding.UTF8.GetByteCount(Fog) > RunConfigurationCodec.MaximumPayloadBytes) return false;
        var Rows = Boxes.Length == 0 ? Array.Empty<string>() : Boxes.Split(';');
        var Values = new BiomeWallBox[Rows.Length];
        for (var i = 0; i < Rows.Length; i++)
            if (!BiomeWallBox.TryParse(Rows[i], Configured, out Values[i])) return false;
        FogCutoutOverride? Override = null;
        if (Fog.Length != 0)
            if (!FogCutoutOverride.TryParse(Fog, out Override)) return false;
        Parsed = new BiomeSpatialSettings(Id, Values, Override);
        return true;
    }

    internal static bool TryNumbers(string Text, int Count, out float[] Values)
    {
        Values = Array.Empty<float>();
        if (Encoding.UTF8.GetByteCount(Text) > RunConfigurationCodec.MaximumPayloadBytes) return false;
        var Fields = 1;
        foreach (var Character in Text) if (Character == ',') Fields++;
        if (Fields != Count) return false;
        var Tokens = Text.Split(','); Values = new float[Count];
        for (var i = 0; i < Count; i++)
            if (!float.TryParse(Tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out Values[i])
                || !float.IsFinite(Values[i])) return false;
        return true;
    }

    internal void Write(StringBuilder Writer)
    {
        LengthPrefixedText.Append(Writer, Id.Value);
        var Walls = new StringBuilder();
        foreach (var Box in Boxes)
        {
            if (Walls.Length != 0) Walls.Append(';');
            Append(Box.Center.x); Append(Box.Center.y); Append(Box.Center.z);
            Append(Box.Size.x); Append(Box.Size.y); Append(Box.Size.z);
            Append(Box.Rotation.x); Append(Box.Rotation.y); Append(Box.Rotation.z); Append(Box.Rotation.w, true);
        }
        LengthPrefixedText.Append(Writer, Walls.ToString());
        LengthPrefixedText.Append(Writer, FogCutout is { } Fog
            ? Scalar(Fog.Minimum) + "," + Scalar(Fog.Maximum) + "," + Scalar(Fog.Amount) : string.Empty);

        void Append(float Value, bool Last = false)
        {
            Walls.Append(Scalar(Value)); if (!Last) Walls.Append(',');
        }
    }
    static string Scalar(float Value) => (Value == 0f ? 0f : Value).ToString("R", CultureInfo.InvariantCulture);
}

internal sealed class BiomeSpatialConfig
{
    internal BiomeSpatialConfig(ConfigFile File, BiomeId Id,
        ConfigDefinition BoxesDefinition, ConfigDefinition FogDefinition)
    {
        this.Id = Id;
        Boxes = File.Bind(BoxesDefinition, string.Empty, new ConfigDescription(
            "Explicit segment-root-local wall boxes: center X,Y,Z,size X,Y,Z,Euler X,Y,Z degrees. Separate boxes with ;. Positive full sizes; empty adds no walls. PLL layouts replace scene-global edge walls; authored colliders remain."));
        FogCutout = File.Bind(FogDefinition, string.Empty, new ConfigDescription(
            "Visual fog cutout minimum,maximum,amount. World radial distances satisfy 0 <= minimum < maximum; amount is 0 to 1. Empty preserves authored fog; override applies after Fogless."));
    }
    internal BiomeId Id { get; }
    internal ConfigEntry<string> Boxes { get; }
    internal ConfigEntry<string> FogCutout { get; }
    internal BiomeSpatialSettings Capture() => BiomeSpatialSettings.ParseConfigured(Id, Boxes.Value, FogCutout.Value);
}
