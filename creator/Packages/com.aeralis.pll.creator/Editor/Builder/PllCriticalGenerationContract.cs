#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library
{
    internal static class PllCriticalGenerationContract
    {
        internal static Component[] ValidateScope(
            PllBiomePrefabAnchors Anchors, Transform Scope,
            string Context, bool SourceAuthoring)
        {
            var Root = Anchors.transform;
            var Floor = Anchors.FloorRoot.transform;
            var Campfire = Anchors.CampfireRoot.transform;
            if (Scope == Floor || Scope == Campfire
                || TransformOwnership.Contains(Floor, Scope)
                    == TransformOwnership.Contains(Campfire, Scope))
                throw new InvalidOperationException(Context
                    + " must be a strict descendant of exactly one FloorRoot or CampfireRoot.");
            if (!Scope.gameObject.activeSelf)
                throw new InvalidOperationException(Context + " must be active in the source prefab.");
            foreach (var Marker in Scope.GetComponentsInChildren<PLLCriticalGeneration>(true))
                if (Marker.transform != Scope)
                    throw new InvalidOperationException(Context + " overlaps another Critical Generation block.");
            if (Scope.GetComponentInChildren<PLLFinalGeneration>(true))
                throw new InvalidOperationException(Context + " overlaps a Final Generation block.");
            for (var Current = Scope.parent; Current && Current != Root; Current = Current.parent)
            {
                if (Current.GetComponent<PLLCriticalGeneration>())
                    throw new InvalidOperationException(Context + " overlaps another Critical Generation block.");
                if (Current.GetComponent<PLLFinalGeneration>())
                    throw new InvalidOperationException(Context + " overlaps a Final Generation block.");
                if (Current.GetComponent<PropSpawner>())
                    throw new InvalidOperationException(Context + " belongs to a PropSpawner's generated-child storage.");
                if (Current.gameObject.activeSelf) continue;
                var VariationEntry = false;
                foreach (var Variation in Current.parent.GetComponents<PllVariationAuthoring>())
                    foreach (var Entry in Variation.Entries)
                        if (Entry?.Root && Entry.Root.transform == Current) VariationEntry = true;
                if (!VariationEntry)
                    throw new InvalidOperationException(Context + " has an inactive non-variation ancestor.");
            }

            var Sources = new List<Component>();
            foreach (var Step in Scope.GetComponentsInChildren<LevelGenStep>(true))
            {
                if (Step.GetType() != typeof(PropSpawner) && Step is not (PllEntityZoneAuthoring or PllHazardZoneAuthoring)
                    && !(Step is Peak.PropSpawner_AmuletStatues
                        && Step.GetComponentInParent<PllAmuletSpawnerAuthoring>(true)))
                    throw new InvalidOperationException(Context + " contains unsupported primary step '"
                        + Step.GetType().FullName + "'. Use an admitted PropSpawner source.");
                RequireEarly(Step.transform, Root, Context);
                if (Step.transform != Scope && Step.transform.parent
                    && TransformOwnership.HasPropSpawnerInPath(Scope, Step.transform.parent))
                    throw new InvalidOperationException(Context
                        + " contains a primary step in a PropSpawner's generated-child storage.");
                Sources.Add(Step);
            }
            if (SourceAuthoring)
            {
                foreach (var Amulet in Scope.GetComponentsInChildren<PllAmuletSpawnerAuthoring>(true))
                {
                    RequireEarly(Amulet.transform, Root, Context);
                    Sources.Add(Amulet);
                }
                foreach (var Child in Scope.GetComponentsInChildren<PllChildPropAuthoring>(true))
                {
                    if (Child.Timing != PllChildPropTiming.Immediate)
                        throw new InvalidOperationException(Context
                            + " contains child props whose timing is not Immediate.");
                    if (Child.Points.Count == 0 || Child.Props.Count == 0)
                        throw new InvalidOperationException(Context + " contains child props without Points or Props.");
                    RequireEarly(Child.transform, Root, Context);
                    Sources.Add(Child);
                }
            }
            if (Sources.Count == 0)
                throw new InvalidOperationException(Context + " contains no supported primary generation step.");
            return Sources.ToArray();
        }

        internal static void ValidateMarker(PLLCriticalGeneration Marker, string Context)
        {
            if (!Marker.enabled || !Marker.gameObject.activeSelf
                || Marker.GetComponents<PLLCriticalGeneration>().Length != 1
                || Marker.GetComponent<PropSpawner>())
                throw new InvalidOperationException(Context
                    + " must be an active, enabled, non-spawner container with exactly one marker.");
        }

        static void RequireEarly(Transform Value, Transform Root, string Context)
        {
            for (var Current = Value; Current; Current = Current.parent)
            {
                foreach (var Grouper in Current.GetComponents<PropGrouper>())
                    if (Grouper.timing != PropGrouper.PropGrouperTiming.Early)
                        throw new InvalidOperationException(Context + " contains a step with a non-Early PropGrouper ancestor.");
                if (Current == Root) return;
            }
        }
    }
}
