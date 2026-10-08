#nullable enable
using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library
{
    internal static class PllHookedPropGenerationSourceContract
    {
        internal static bool UnsupportedPresentation(Component Component) => Component is
            PllEffectAuthoring or PllCustomVisualAuthoring or PllVisualAuthoring
            or PLLNativeVisualResolver or ParticleSystem or ParticleSystemRenderer
            || Component is PllEffectZoneAuthoring Zone && (Zone.ActionCount == 0
                || Zone.PropTriggers.Count != 0)
            || Component is PLLNativeResolver Resolver && (Resolver.Form != PllNativeResolverForm.Source
                || Resolver.Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid));

        internal static void ValidateCandidate(Transform Root, Func<Component, bool>? UnsupportedConfiguration = null)
        {
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
            {
                if (!Component || UnsupportedPresentation(Component)
                    || Component is global::Campfire or PllCampfireAuthoring or PllHookedPropGenerationAuthoring or PllAmuletSpawnerAuthoring
                        or PropGrouper or ILevelGenFinalizer or PLLFinalGeneration
                        or PLLCriticalGeneration or PllVariationAuthoring
                        or VariantObjectSelector or VariationSwapper or ChildMaterialSwapper
                    || UnsupportedConfiguration?.Invoke(Component) == true
                    || Component is LevelGenStep Step && (Step is not PropSpawner Spawner
                        || Spawner.GetType() != typeof(PropSpawner) && Spawner.GetType() != typeof(PropSpawner_Line)
                            && Spawner.GetType() != typeof(PropSpawner_Sphere)))
                    throw new ArgumentException("Hooked generation references unsupported presentation or executable ownership.");
                if (Component is RockMaterialSwapper Swapper)
                {
                    if (Swapper.parents is null) throw new ArgumentException("Hooked material swapper has no parent inventory.");
                    foreach (var Parent in Swapper.parents)
                        if (Parent && !TransformOwnership.Contains(Root, Parent))
                            throw new ArgumentException("Hooked material swapper would mutate a foreign transform.");
                }
            }
        }

        internal static (PropSpawner? Spawner, PropGrouper? Grouper) Validate(
            PllBiomeDefinition Definition, PllHookedPropGenerationAuthoring Marker,
            bool Compiled, IReadOnlyList<Transform> UnstableRoots,
            Func<Component, bool>? UnsupportedConfiguration = null)
        {
            return ValidateMarker(Definition.RootPrefab.transform, Definition.Anchors.FloorRoot.transform,
                Definition.Anchors.CampfireRoot.transform, Marker, Compiled, UnstableRoots, UnsupportedConfiguration);
        }

        internal static (PropSpawner? Spawner, PropGrouper? Grouper) ValidateMarker(
            Transform Template, Transform Floor, Transform Campfire, PllHookedPropGenerationAuthoring Marker,
            bool Compiled, IReadOnlyList<Transform> UnstableRoots, Func<Component, bool>? UnsupportedConfiguration = null)
        {
            if (!Marker.enabled) throw new ArgumentException("Hooked generation source is disabled.");
            return ValidateRoot(Template, Floor, Campfire, Marker.transform, Marker, Compiled, UnstableRoots, UnsupportedConfiguration);
        }

        internal static (PropSpawner? Spawner, PropGrouper? Grouper) ValidateSelection(
            Transform Template, Transform Floor, Transform Campfire, Transform Root,
            IReadOnlyList<Transform> UnstableRoots) =>
            ValidateRoot(Template, Floor, Campfire, Root, null, false, UnstableRoots, null);

        static (PropSpawner? Spawner, PropGrouper? Grouper) ValidateRoot(
            Transform Template, Transform Floor, Transform Campfire, Transform Root,
            PllHookedPropGenerationAuthoring? Marker, bool Compiled,
            IReadOnlyList<Transform> UnstableRoots, Func<Component, bool>? UnsupportedConfiguration)
        {
            if (!TransformOwnership.DiscoverableWhenActivated(Template, Root)
                || !TransformOwnership.Contains(Floor, Root)
                    && !TransformOwnership.Contains(Campfire, Root)
                || Root.GetComponents<PllHookedPropGenerationAuthoring>().Length != (Marker is null ? 0 : 1))
                throw new ArgumentException("Hooked generation requires one enabled stable source under FloorRoot or CampfireRoot.");
            for (var Parent = Root.parent; Parent && Parent != Template; Parent = Parent.parent)
                if (Parent.GetComponent<PropSpawner>() || Parent.GetComponent<PLLFinalGeneration>()
                    || Parent.GetComponent<PllHookedPropGenerationAuthoring>() || Parent.GetComponent<PLLCriticalGeneration>()
                    || Parent.GetComponent<VariationSwapper>() && !Parent.GetComponent<PllVariationAuthoring>()
                    || Parent.GetComponent<VariantObject>())
                    throw new ArgumentException("Hooked generation has a competing generator ancestor.");
            foreach (var Unstable in UnstableRoots)
                if (TransformOwnership.Contains(Unstable, Root))
                    throw new ArgumentException("Hooked generation cannot belong to a selectable variation.");
            var Direct = Root.GetComponents<PropSpawner>();
            var Groupers = Root.GetComponentsInChildren<PropGrouper>(true);
            if (Direct.Length > 1 || Direct.Length == 1 && Root.childCount != 0
                || Groupers.Length != (Compiled || Direct.Length == 0 ? 1 : 0)
                || Groupers.Length == 1 && Groupers[0].transform != Root)
                throw new ArgumentException("Hooked generation requires a source grouper or one spawner leaf.");
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                if (!Component || Component is ILevelGenFinalizer or PLLFinalGeneration or PLLCriticalGeneration
                    or VariantObjectSelector or VariationSwapper or ChildMaterialSwapper or RockMaterialSwapper
                    or PllVariationAuthoring or VariantObject
                    or global::Campfire or PllCampfireAuthoring or PhotonView
                    or PllLuggageAuthoring or PllItemSpawnerAuthoring or PllRestPointAuthoring
                    or PllHangingRopeAuthoring or PllStatueAuthoring or PllAmuletSpawnerAuthoring
                    || UnsupportedPresentation(Component)
                    || UnsupportedConfiguration?.Invoke(Component) == true
                    || Component is PllHookedPropGenerationAuthoring Nested && Nested != Marker)
                    throw new ArgumentException("Hooked generation contains competing generation or presentation ownership.");
            foreach (var Step in Root.GetComponentsInChildren<LevelGenStep>(true))
                if (Step is not PropSpawner Spawner || !Spawner.enabled || Spawner.transform.childCount != 0
                    || Spawner.GetType() != typeof(PropSpawner) && Spawner.GetType() != typeof(PropSpawner_Line)
                        && Spawner.GetType() != typeof(PropSpawner_Sphere))
                    throw new ArgumentException("Hooked generation supports only enabled base, line, or sphere spawner leaves.");
            return (Direct.Length == 0 ? null : Direct[0], Groupers.Length == 0 ? null : Groupers[0]);
        }

        internal static IReadOnlyList<Transform> UnstableRoots(PllBiomeDefinition Definition) => UnstableRoots(Definition.RootPrefab.transform);

        internal static IReadOnlyList<Transform> UnstableRoots(Transform Template)
        {
            var Roots = new List<Transform>();
            foreach (var Variation in Template.GetComponentsInChildren<PllVariationAuthoring>(true))
                foreach (var Entry in Variation.Entries)
                {
                    if (Entry is null || !Entry.Root)
                        throw new ArgumentException("Hooked generation references invalid variation roots.");
                    Roots.Add(Entry.Root.transform);
                }
            return Roots;
        }

        internal readonly struct Action
        {
            internal Action(PllEffectZoneTransition Transition, int TargetIndex)
            {
                this.Transition = Transition; this.TargetIndex = TargetIndex;
            }
            internal PllEffectZoneTransition Transition { get; }
            internal int TargetIndex { get; }
        }

        internal static Action[] ValidateActions(PllEffectZoneAuthoring Zone, bool Generated,
            IReadOnlyList<PllHookedPropGenerationAuthoring> Targets,
            Dictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring> Owners)
        {
            var ActionCount = Zone.ActionCount;
            if (ActionCount > PllBundleSerialization.MaximumHookedActions
                || Generated && Zone.PropTriggers.Count != 0
                || Zone.Looped && ActionCount == 0
                || Zone.Effects.Count == 0 && Zone.OccupancyHook.Length == 0
                    && ActionCount == 0
                    && Zone.EnvironmentFog == PllEffectZoneEnvironmentFog.Inherit)
                throw new ArgumentException("Effect zone has no capability or unsupported generated actions.");
            if (Zone.PropTriggers.Count == 0) return Array.Empty<Action>();
            var Actions = ParseActions(Zone.transform, Zone.Looped, Zone.PropTriggers.Count,
                Index => Zone.PropTriggers[Index] is { } Trigger ? (Trigger.Transition, Trigger.Target)
                    : throw new ArgumentException("Effect zone has a missing generation action."), Targets, Owners);
            foreach (var Trigger in Zone.PropTriggers) Owners[Trigger.Target] = Zone;
            return Actions;
        }

        internal static Action[] ValidateActionSelection(Transform Zone, bool Looped,
            IReadOnlyList<(PllEffectZoneTransition Transition, PllHookedPropGenerationAuthoring Target)> Triggers,
            IReadOnlyList<PllHookedPropGenerationAuthoring> Targets,
            IReadOnlyDictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring> Owners) =>
            ParseActions(Zone, Looped, Triggers.Count, Index => Triggers[Index], Targets, Owners);

        static Action[] ParseActions(Transform Zone, bool Looped, int Count,
            Func<int, (PllEffectZoneTransition Transition, PllHookedPropGenerationAuthoring Target)> Read,
            IReadOnlyList<PllHookedPropGenerationAuthoring> Targets,
            IReadOnlyDictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring> Owners)
        {
            if (Looped && Count == 0 || Count > PllBundleSerialization.MaximumHookedActions)
                throw new ArgumentException("Effect zone generation action count is unsupported.");
            var Actions = new Action[Count];
            var Pairs = new HashSet<(PllEffectZoneTransition, PllHookedPropGenerationAuthoring)>();
            for (var i = 0; i < Count; i++)
            {
                var Trigger = Read(i);
                if (Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                    || !Trigger.Target || Trigger.Target.GenerateOnLoad || !Pairs.Add(Trigger))
                    throw new ArgumentException("Effect zone contains a missing, duplicate, or invalid generation action.");
                var Index = -1;
                for (var j = 0; j < Targets.Count; j++) if (Targets[j] == Trigger.Target) { Index = j; break; }
                if (Index < 0 || Owners.TryGetValue(Trigger.Target, out var Owner) && Owner.transform != Zone)
                    throw new ArgumentException("Effect zone generation target is foreign or owned by another zone.");
                Actions[i] = new Action(Trigger.Transition, Index);
            }
            return Actions;
        }
    }
}
