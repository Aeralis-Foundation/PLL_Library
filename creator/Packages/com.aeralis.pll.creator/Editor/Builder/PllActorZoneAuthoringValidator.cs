using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllActorZoneAuthoringValidator
    {
        internal static void Validate(
            GameObject Root, Transform Floor, Transform Campfire,
            IReadOnlyList<PllEntityZoneAuthoring> Entities,
            IReadOnlyList<PllHazardZoneAuthoring> Hazards, string Label)
        {
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Zone in Entities)
                if (SourceFailure(Root.transform, Floor, Campfire, Zone, Ids) is { } Failure)
                    throw new InvalidOperationException(Failure + " Biome '" + Label + "', Entity Zone '" + Zone.Id + "'.");
            foreach (var Zone in Hazards)
                if (SourceFailure(Root.transform, Floor, Campfire, Zone, Ids) is { } Failure)
                    throw new InvalidOperationException(Failure + " Biome '" + Label + "', Hazard Zone '" + Zone.Id + "'.");
        }

        internal static void ValidateNewSourceParent(
            GameObject Root, Transform Floor, Transform Campfire, Transform Parent, string Id)
        {
            if (PllActorZoneSourceContract.NewSourceParentFailure(Root.transform, Floor, Campfire, Parent, Id) is { } Failure)
                throw new InvalidOperationException(Failure);
        }

        internal static void ValidateComponent(Component Component, HashSet<string> Ids = null)
        {
            var Anchors = Component.GetComponentInParent<PllBiomePrefabAnchors>(true);
            if (!Anchors || !Anchors.FloorRoot || !Anchors.CampfireRoot)
                throw new InvalidOperationException("Actor Zone requires valid biome anchors.");
            if (SourceFailure(Anchors.transform, Anchors.FloorRoot.transform, Anchors.CampfireRoot.transform,
                (LevelGenStep)Component, Ids) is { } Reason) throw new InvalidOperationException(Reason);
        }

        static string SourceFailure(Transform Root, Transform Floor, Transform Campfire, LevelGenStep Zone, HashSet<string> Ids)
        {
            var Fields = Zone switch
            {
                PllEntityZoneAuthoring Entity => (Entity.Id, Entity.OutputRoot, FieldFailure: PllEntityZoneValueContract.Failure(Entity)),
                PllHazardZoneAuthoring Hazard => (Hazard.Id, Hazard.OutputRoot, FieldFailure: PllHazardZoneValueContract.Failure(Hazard)),
                _ => throw new InvalidOperationException("The actor-zone component is unsupported.")
            };
            return Fields.FieldFailure ?? PllActorZoneSourceContract.SourceFailure(Root, Floor, Campfire, Zone, Fields.OutputRoot)
                ?? (Ids == null ? PllActorZoneSourceContract.DuplicateIdFailure(Root, Fields.Id, Zone)
                    : PllActorZoneSourceContract.RegisterId(Ids, Fields.Id));
        }

        internal static void Translate(LevelGenStep Zone)
        {
            var Output = new GameObject(Zone switch
            {
                PllEntityZoneAuthoring => "__PLL_EntityOutput",
                PllHazardZoneAuthoring => "__PLL_HazardOutput",
                _ => throw new InvalidOperationException("The actor-zone component is unsupported.")
            });
            Output.transform.SetParent(Zone.transform, false);
            using (var Serialized = new UnityEditor.SerializedObject(Zone))
            {
                PllAuthoringCommands.Property(Serialized, "OutputRootValue").objectReferenceValue = Output.transform;
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
