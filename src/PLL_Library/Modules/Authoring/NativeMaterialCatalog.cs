using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace PLL.Library;

internal sealed class NativeMaterialCatalog
{
    readonly Dictionary<string, Material?> Materials = new(StringComparer.Ordinal);
    readonly Dictionary<Material, byte[]> Fingerprints = new();

    internal NativeMaterialCatalog(IReadOnlyDictionary<string, LayoutSegmentSource> Sources, Scene Scene)
    {
        if (!Scene.IsValid() || !Scene.isLoaded)
            throw new InvalidOperationException("The native material scene is not loaded.");
        var Roots = new HashSet<GameObject>();
        var Pending = new Queue<GameObject>();
        foreach (var Source in Sources.Values)
        {
            Enqueue(Source.SegmentParent); Enqueue(Source.SegmentCampfire);
            Enqueue(Source.WallNext); Enqueue(Source.WallPrevious);
        }
        while (Pending.Count != 0)
        {
            var Root = Pending.Dequeue();
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                switch (Component)
                {
                    case Renderer Renderer:
                        foreach (var Material in Renderer.sharedMaterials)
                        {
                            if (!Material) continue;
                            var Canonical = NativeFogRenderRuntime.CanonicalMaterial(Material);
                            if (!Materials.TryAdd(Canonical.name, Canonical)
                                && Materials[Canonical.name] != Canonical)
                                Materials[Canonical.name] = null;
                        }
                        break;
                    case PropSpawner Spawner:
                        if (NativePrefabContracts.EffectiveProps(Spawner) is { } Candidates)
                            foreach (var Candidate in Candidates) Enqueue(Candidate);
                        break;
                    case LavaRiver River:
                        Enqueue(River.splash); Enqueue(River.spawnAlongSpline);
                        if (River.detailsAlongSpline is { } Details)
                            foreach (var Detail in Details) Enqueue(Detail);
                        break;
                }
        }

        void Enqueue(GameObject? Root)
        {
            if (Root && Roots.Add(Root)) Pending.Enqueue(Root);
        }
    }

    internal Material Require(string Name)
    {
        if (!Materials.TryGetValue(Name, out var Material))
            throw new ArgumentException($"PEAK material '{Name}' is not in the native biome catalog.");
        if (!Material)
            throw new ArgumentException($"PEAK material name '{Name}' is ambiguous in the native biome catalog.");
        return Material;
    }

    internal byte[] Fingerprint(Material Material)
    {
        if (!Fingerprints.TryGetValue(Material, out var Value))
        {
            Value = VisualFingerprintWriter.HashSerializedObject(Material, "PLL/NativeMaterialSource/v1");
            Fingerprints.Add(Material, Value);
        }
        return Value;
    }

    internal NativeContentMaterial[] Export(Action<NativeContentEntry, Texture2D?>? CaptureTexture = null)
    {
        var Schemas = new Dictionary<Shader, NativeContentMaterialProperty[]>();
        var Result = Materials.Values.Where(Source => Source).Select(Value =>
        {
            var Source = Value!;
            var Shader = NativeFogRenderRuntime.CanonicalShader(Source.shader);
            if (!Schemas.TryGetValue(Shader, out var Schema))
            {
                Schema = Enumerable.Range(0, Shader.GetPropertyCount()).Where(Index =>
                    Shader.FindPropertyIndex(Shader.GetPropertyName(Index)) == Index
                    && (Shader.GetPropertyFlags(Index) & (ShaderPropertyFlags.HideInInspector
                        | ShaderPropertyFlags.PerRendererData | ShaderPropertyFlags.NonModifiableTextureData)) == 0).Select(Index =>
                {
                    var Type = Shader.GetPropertyType(Index);
                    var Range = Type == ShaderPropertyType.Range ? Shader.GetPropertyRangeLimits(Index) : default;
                    return new NativeContentMaterialProperty
                    {
                        Name = Shader.GetPropertyName(Index), Label = Shader.GetPropertyDescription(Index), Type = Type,
                        Dimension = Type == ShaderPropertyType.Texture ? Shader.GetPropertyTextureDimension(Index) : TextureDimension.None,
                        Minimum = Math.Min(Range.x, Range.y), Maximum = Math.Max(Range.x, Range.y)
                    };
                }).ToArray();
                Schemas.Add(Shader, Schema);
            }
            var Properties = new NativeContentMaterialProperty[Schema.Length];
            for (var i = 0; i < Properties.Length; i++)
            {
                var Definition = Schema[i];
                var Property = Properties[i] = new NativeContentMaterialProperty
                {
                    Name = Definition.Name, Label = Definition.Label, Type = Definition.Type,
                    Dimension = Definition.Dimension, Minimum = Definition.Minimum, Maximum = Definition.Maximum
                };
                var Name = Property.Name;
                switch (Property.Type)
                {
                    case ShaderPropertyType.Color:
                        var Color = Source.GetColor(Name); Property.Values = new[] { Color.r, Color.g, Color.b, Color.a }; break;
                    case ShaderPropertyType.Vector:
                        var Vector = Source.GetVector(Name); Property.Values = new[] { Vector.x, Vector.y, Vector.z, Vector.w }; break;
                    case ShaderPropertyType.Int:
                        Property.Integer = Source.GetInteger(Name); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        Property.Values = new[] { Source.GetFloat(Name) }; break;
                    case ShaderPropertyType.Texture:
                        var Scale = Source.GetTextureScale(Name); var Offset = Source.GetTextureOffset(Name);
                        Property.Values = new[] { Scale.x, Scale.y, Offset.x, Offset.y };
                        var Texture = Source.GetTexture(Name);
                        if (!Texture) break;
                        Property.Texture = Texture.name;
                        Property.Image = new NativeContentEntry
                        {
                            Kind = NativeContentPickKind.Visual, Id = Source.name + "/" + Name,
                            Name = Texture.name, CandidateCount = 1, ImageAbsence = "Texture preview is unavailable."
                        };
                        CaptureTexture?.Invoke(Property.Image, Texture as Texture2D);
                        break;
                    default: throw new InvalidOperationException($"Shader property '{Name}' has unsupported type '{Property.Type}'.");
                }
            }
            return new NativeContentMaterial { Name = Source.name, Shader = Shader.name, RenderQueue = Source.renderQueue, Properties = Properties };
        }).ToArray();
        Array.Sort(Result, (Left, Right) => string.CompareOrdinal(Left.Name, Right.Name));
        return Result;
    }
}
