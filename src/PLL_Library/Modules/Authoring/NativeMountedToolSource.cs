using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeMountedToolSource
{
    readonly ShittyPiton Template;
    readonly byte[] Fingerprint;
    readonly List<GameObject> Models = new();

    NativeMountedToolSource(ShittyPiton Template)
    {
        this.Template = Template;
        var Handle = Template.GetComponent<ClimbHandle>();
        if (!Handle || !Handle.isPickaxe
            || !Template.crack || !Template.vfx
            || !TransformOwnership.Contains(Template.transform, Template.crack.transform)
            || !TransformOwnership.Contains(Template.transform, Template.vfx.transform)
            || Template.crack.activeSelf || Template.vfx.activeSelf
            || Template.cracKSFX is null || Template.breakSFX is null)
            throw new InvalidOperationException("The native mounted tool has an incomplete handle or presentation graph.");
        foreach (Transform Child in Template.transform)
            if (!TransformOwnership.Overlaps(Child, Template.crack.transform)
                && !TransformOwnership.Overlaps(Child, Template.vfx.transform)
                && Child.GetComponentInChildren<Renderer>(true)) Models.Add(Child.gameObject);
        if (Models.Count == 0) throw new InvalidOperationException("The native mounted tool has no separate tool model.");
        Fingerprint = NativeEntityAppearance.Hash(Writer =>
        {
            Writer.Write("PLL/NativeMountedTool/v2");
            Writer.Write(DeterministicObjectIdentity.HashObjectHierarchy(Template.transform, "PLL/MountedToolGraph/v1"));
            WriteSounds(Template.cracKSFX); WriteSounds(Template.breakSFX);
            void WriteSounds(SFX_Instance[] Sounds)
            {
                Writer.Write(Sounds.Length);
                foreach (var Sound in Sounds)
                {
                    if (!Sound) throw new InvalidOperationException("Native mounted tool sound is missing.");
                    NativeFlowProvider.WriteSfx(Writer, Sound);
                }
            }
        });
    }

    internal static NativeMountedToolSource Capture(NativeSegmentCatalog Catalog)
    {
        var Pending = new Queue<GameObject>(); var Seen = new HashSet<GameObject>();
        NativeMountedToolSource? Result = null;
        Pending.Enqueue(Catalog.RequireOrdinary("Mesa").Container.gameObject);
        while (Pending.Count != 0)
        {
            var Root = Pending.Dequeue();
            if (!Seen.Add(Root)) continue;
            if (!Root.scene.IsValid())
                foreach (var Tool in Root.GetComponentsInChildren<ShittyPiton>(true))
                {
                    var Candidate = new NativeMountedToolSource(Tool);
                    if (Result is null) Result = Candidate;
                    else if (!Result.Fingerprint.AsSpan().SequenceEqual(Candidate.Fingerprint))
                        throw new InvalidOperationException("Native mounted tool providers have different retained recipes.");
                }
            foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
                foreach (var Candidate in NativePrefabContracts.EffectiveProps(Spawner) ?? Array.Empty<GameObject>())
                {
                    if (!Candidate) throw new InvalidOperationException("The mounted tool provider has a missing native candidate.");
                    Pending.Enqueue(Candidate);
                }
        }
        return Result ?? throw new InvalidOperationException("PEAK's mounted tool template is unavailable.");
    }

    internal GameObject CreateModel(Transform Parent)
    {
        var Root = new GameObject("PLL_MountedToolModel"); Root.SetActive(false);
        Root.transform.SetParent(Parent, false);
        foreach (var Model in Models) UnityEngine.Object.Instantiate(Model, Root.transform, false);
        Root.SetActive(true); return Root;
    }

    internal GameObject CreatePresentation(PllFixturePresentation Profile, GameObject Custom,
        Transform Parent, bool Crack)
    {
        if (Profile == PllFixturePresentation.Custom) return Custom;
        if (Profile == PllFixturePresentation.Native)
            return UnityEngine.Object.Instantiate(Crack ? Template.crack : Template.vfx, Parent, false);
        var Root = new GameObject(Crack ? "PLL_MountedCrackPose" : "PLL_MountedBreakPose");
        Root.SetActive(false); Root.transform.SetParent(Parent, false); return Root;
    }

    internal void BindSounds(ShittyPiton Target, PllFixturePresentation Crack, PllFixturePresentation Break)
    {
        Target.cracKSFX = Crack == PllFixturePresentation.Native ? Template.cracKSFX : Array.Empty<SFX_Instance>();
        Target.breakSFX = Break == PllFixturePresentation.Native ? Template.breakSFX : Array.Empty<SFX_Instance>();
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
}
