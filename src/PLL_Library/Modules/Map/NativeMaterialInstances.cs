using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeMaterialInstances
{
    readonly MapRun Run;
    readonly Dictionary<PllNativeMaterialAuthoring, (Renderer Renderer, int Slot, Material Previous, Material Applied)> Entries = new();
    readonly Dictionary<Material, Material> Previews = new();
    readonly HashSet<(Renderer Renderer, int Slot)> Slots = new();

    internal NativeMaterialInstances(MapRun Run) => this.Run = Run;

    internal bool OwnsSlot(Renderer Renderer, int Slot) => Slots.Contains((Renderer, Slot));
    internal Material CanonicalMaterial(Material Material) => Previews.TryGetValue(Material, out var Preview) ? Preview : Material;

    internal void Enroll(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots)
        {
            if (Slot.CustomResolved is not { } Resolved) continue;
            if (!BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract))
                throw new InvalidOperationException("Custom occurrence lost its native material contract.");
            if (Contract.NativeMaterialContracts.Count == 0) return;
            var HasGenerated = false;
            foreach (var Definition in Contract.NativeMaterialContracts)
            {
                if (Definition.Generated) { HasGenerated = true; continue; }
                var Source = Definition.Binding.Resolve(Resolved.Root.Value, Contract.Id, "native material");
                if (!Entries.ContainsKey(Source)) Bind(Definition, Source, Resolved.Root.Value);
            }
            if (HasGenerated)
                foreach (var Source in Resolved.Root.Value.GetComponentsInChildren<PllNativeMaterialAuthoring>(true))
                {
                    if (Entries.ContainsKey(Source)) continue;
                    var Matched = false;
                    foreach (var Definition in Contract.NativeMaterialContracts)
                        if (Definition.Generated && Definition.TryMatchGenerated(Source, Contract.Id, out var Root))
                        {
                            Bind(Definition, Source, Root); Matched = true; break;
                        }
                    if (!Matched && Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Source.transform))
                        throw new InvalidOperationException("Generated Native Material has no sealed candidate binding.");
                }
            return;

            void Bind(NativeMaterialContract Definition, PllNativeMaterialAuthoring Source, Transform Root)
            {
                var Renderer = Definition.Resolve(Root, Contract.Id);
                var Materials = ReadCanonicalMaterials(Renderer);
                var Previous = Materials[Definition.Slot];
                NativeFogRenderRuntime.RetireRenderer(Renderer);
                Materials[Definition.Slot] = Definition.Material;
                Renderer.sharedMaterials = Materials;
                Entries.Add(Source, (Renderer, Definition.Slot, Previous, Definition.Material));
                Previews.TryAdd(Definition.Material, Definition.Selection.Preview);
                Slots.Add((Renderer, Definition.Slot));
                NativeFogRenderRuntime.BindRenderer(Renderer);
            }
        }
    }

    internal void Retire(GameObject Root)
    {
        List<PllNativeMaterialAuthoring>? Removed = null;
        foreach (var Entry in Entries)
            if (!Entry.Key || TransformOwnership.Contains(Root.transform, Entry.Key.transform))
            {
                Restore(Entry.Value);
                (Removed ??= new()).Add(Entry.Key!);
            }
        if (Removed is null) return;
        foreach (var Source in Removed) Entries.Remove(Source);
        Slots.Clear();
        foreach (var Entry in Entries.Values) Slots.Add((Entry.Renderer, Entry.Slot));
    }

    internal void Clear()
    {
        foreach (var Entry in Entries.Values) Restore(Entry);
        Entries.Clear(); Previews.Clear(); Slots.Clear();
    }

    static void Restore((Renderer Renderer, int Slot, Material Previous, Material Applied) Entry)
    {
        if (!Entry.Renderer) return;
        var Materials = ReadCanonicalMaterials(Entry.Renderer);
        if (Entry.Slot >= Materials.Length
            || Materials[Entry.Slot] != Entry.Applied) return;
        NativeFogRenderRuntime.RetireRenderer(Entry.Renderer);
        Materials[Entry.Slot] = Entry.Previous;
        Entry.Renderer.sharedMaterials = Materials;
        NativeFogRenderRuntime.BindRenderer(Entry.Renderer);
    }

    static Material[] ReadCanonicalMaterials(Renderer Renderer)
    {
        var Materials = Renderer.sharedMaterials;
        for (var Index = 0; Index < Materials.Length; Index++)
            if (Materials[Index]) Materials[Index] = NativeFogRenderRuntime.CanonicalMaterial(Materials[Index]);
        return Materials;
    }
}
