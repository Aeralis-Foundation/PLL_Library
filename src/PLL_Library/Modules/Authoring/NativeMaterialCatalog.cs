using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
}
