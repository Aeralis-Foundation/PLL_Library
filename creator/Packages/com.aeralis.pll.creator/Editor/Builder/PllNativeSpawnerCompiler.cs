using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEngine;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal static class PllNativeSpawnerCompiler
    {
        internal static void Validate(SourceIdentity Identity, IReadOnlyList<PllNativeSpawnerAuthoring> Sources)
        {
            var ManagedByRoot = new Dictionary<GameObject, List<Transform>>();
            foreach (var Source in Sources)
            {
                Source.Capture();
                var Node = OwnerNode(Identity.Graph, Source, "Food or Content Spawner");
                if (!Node.IsRoot && Source is PllContentSpawnerAuthoring)
                    throw new InvalidOperationException("Content Spawner requires a stable biome zone, not a generated candidate.");
                RequireStableOwned(Node.SourceRoot.transform, Source.transform, "Food or Content Spawner");
                if (Node.IsRoot) RequireFeatureOwned(Source.transform, Identity.Floor, Identity.Campfire, "Food or Content Spawner");
                if (Source.GetComponents<PllNativeSpawnerAuthoring>().Length != 1
                    || Source.GetComponents<ISpawner>().Length != 0 || Source.GetComponent<PropSpawner>()
                    || Source.GetComponent<SpawnList>() || Source.GetComponent<Peak.SpawnedItemTracker>())
                    throw new InvalidOperationException("Food and Content Spawners cannot share a GameObject with another spawner.");
                if (Source is PllContentSpawnerAuthoring
                    && (Source.transform.childCount != 0 || Source.GetComponents<Component>().Length != 2))
                    throw new InvalidOperationException("Content Spawner needs an empty zone GameObject.");
                if (!ManagedByRoot.TryGetValue(Node.SourceRoot, out var ManagedRoots))
                {
                    ManagedByRoot.Add(Node.SourceRoot, ManagedRoots = new List<Transform>());
                    foreach (var Component in Node.SourceRoot.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (Component is PllLuggageAuthoring or PllStatueAuthoring or PllItemSpawnerAuthoring) ManagedRoots.Add(Component.transform);
                        else if (Component is PllEffectAuthoring Effect && Effect.Root) ManagedRoots.Add(Effect.Root.transform);
                        else if (Component is PllVisualAuthoring Visual && Visual.Root) ManagedRoots.Add(Visual.Root.transform);
                        else if (Component is PllSoundEmitterAuthoring Sound && Sound.Root) ManagedRoots.Add(Sound.Root.transform);
                    }
                }
                foreach (var Managed in ManagedRoots)
                    if (TransformOwnership.Overlaps(Managed, Source.transform))
                        throw new InvalidOperationException("Food and Content Spawners cannot overlap another managed item or presentation root.");
            }
        }

        internal static void Translate(GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PllNativeSpawnerAuthoring> Sources)
        {
            foreach (var Source in Sources)
            {
                var Staged = PllBuildArtifacts.MapComponent(SourceRoot, StagedRoot, Source, "native spawner");
                var Selection = Staged.Capture();
                PllNativeSpawnerShape.Configure(Staged, Selection);
                Staged.SealBundleSerialization(Selection);
            }
        }
    }
}
