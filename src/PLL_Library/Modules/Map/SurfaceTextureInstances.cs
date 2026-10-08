using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class SurfaceTextureInstances
{
    readonly MapRun Run;
    readonly Dictionary<PllSurfaceTextureAuthoring, SurfaceTextureRuntime[]> Entries = new();
    readonly Dictionary<Material, Material> Profiles = new();
    readonly HashSet<Renderer> Renderers = new();
    readonly HashSet<FeatureOccurrence> FixedOccurrences = new();

    internal SurfaceTextureInstances(MapRun Run) => this.Run = Run;

    internal bool OwnsRenderer(Renderer Renderer) => Renderers.Contains(Renderer);
    internal Material CanonicalMaterial(Material Material) => Profiles.TryGetValue(Material, out var Profile) ? Profile : Material;

    internal void Enroll(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots)
        {
            if (Slot.CustomResolved is not { } Resolved) continue;
            if (!BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract))
                throw new InvalidOperationException("Custom occurrence lost its surface texture contract.");
            if (Contract.SurfaceTextureContracts.Count == 0) return;
            var Root = Resolved.Root.Value;
            if (FixedOccurrences.Add(Occurrence))
                foreach (var Definition in Contract.SurfaceTextureContracts)
                    if (!Definition.Generated && !Definition.FluidSource)
                        Bind(Definition, Definition.Binding.Resolve(Root, Contract.Id, "surface textures"), Root);
            foreach (var Source in Root.GetComponentsInChildren<PllSurfaceTextureAuthoring>(true))
            {
                if (Entries.ContainsKey(Source) || Source.Target == PllSurfaceTarget.GeneratedFluidSurface) continue;
                var Matched = false;
                foreach (var Definition in Contract.SurfaceTextureContracts)
                    if (Definition.Generated && Definition.TryMatchGenerated(Source, Contract.Id, out var Instance))
                    {
                        Bind(Definition, Source, Instance); Matched = true; break;
                    }
                if (!Matched && Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Source.transform))
                    throw new InvalidOperationException("Generated surface textures has no sealed candidate binding.");
            }
            return;

            void Bind(SurfaceTextureAuthoringContract Definition, PllSurfaceTextureAuthoring Source, Transform Instance)
            {
                var Bindings = Definition.BindAuthored(Instance, Occurrence.RequireCustomFrame(Contract.Id), Contract.Id);
                Entries.Add(Source, Bindings);
                foreach (var Binding in Bindings)
                {
                    Profiles[Binding.Material] = Definition.Material;
                    Renderers.Add(Binding.Target);
                }
            }
        }
    }

    internal void Retire(GameObject Root)
    {
        List<PllSurfaceTextureAuthoring>? Removed = null;
        foreach (var Entry in Entries)
            if (!Entry.Key || TransformOwnership.Contains(Root.transform, Entry.Key.transform))
            {
                foreach (var Binding in Entry.Value)
                {
                    Profiles.Remove(Binding.Material); Binding.Dispose();
                }
                (Removed ??= new()).Add(Entry.Key!);
            }
        if (Removed is null) return;
        foreach (var Source in Removed) Entries.Remove(Source);
        Renderers.Clear();
        foreach (var Entry in Entries.Values)
            foreach (var Binding in Entry) Renderers.Add(Binding.Target);
        FixedOccurrences.RemoveWhere(Occurrence => !Occurrence.Container
            || TransformOwnership.Contains(Root.transform, Occurrence.Container));
    }

    internal void Clear()
    {
        foreach (var Entry in Entries.Values)
            foreach (var Binding in Entry) Binding.Dispose();
        Entries.Clear(); Profiles.Clear(); Renderers.Clear(); FixedOccurrences.Clear();
    }
}
