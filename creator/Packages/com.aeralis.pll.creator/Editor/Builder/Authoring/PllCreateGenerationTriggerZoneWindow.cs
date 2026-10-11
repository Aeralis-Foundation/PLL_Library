using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateGenerationTriggerZoneWindow : EditorWindow
    {
        GameObject Target;
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        readonly List<(PllEffectZoneTransition Transition, PllHookedPropGenerationAuthoring Target)> Triggers =
            new List<(PllEffectZoneTransition, PllHookedPropGenerationAuthoring)>();
        bool Looped;
        Vector2 Scroll;

        [MenuItem("PLL/Effects/Add Generation Trigger Zone to Selected Source", priority = 23)]
        static void Open()
        {
            var Window = GetWindow<PllCreateGenerationTriggerZoneWindow>("Add Generation Trigger Zone");
            Window.Target = Selection.activeGameObject;
            Window.Triggers.Clear(); Window.Looped = false;
            Window.Show();
        }

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Zone Source", Target, typeof(GameObject), true);
            Volume.Draw();
            Looped = EditorGUILayout.Toggle("Looped", Looped);
            for (var Index = 0; Index < Triggers.Count; Index++)
            {
                var Trigger = Triggers[Index];
                Trigger.Transition = (PllEffectZoneTransition)EditorGUILayout.EnumPopup("Transition", Trigger.Transition);
                Trigger.Target = (PllHookedPropGenerationAuthoring)EditorGUILayout.ObjectField(
                    "Hooked Generation", Trigger.Target, typeof(PllHookedPropGenerationAuthoring), true);
                Triggers[Index] = Trigger;
                if (GUILayout.Button("Remove Trigger"))
                {
                    Triggers.RemoveAt(Index);
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("Add Trigger"))
            {
                Triggers.Add((PllEffectZoneTransition.Enter, null));
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.HelpBox("Enter triggers when the first living player enters; Exit triggers when the last leaves. Choose deferred Hooked sources in this biome that are not bound to another zone. With Looped off, each target generates once per transition in each biome occurrence.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || Triggers.Count == 0))
                if (GUILayout.Button("Add Generation Trigger Zone"))
                {
                    try { Create(Target, Volume, Looped, Triggers); Close(); }
                    catch (Exception Failure) when (Failure is InvalidOperationException || Failure is ArgumentException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PllEffectZoneAuthoring Create(GameObject Target, PllAuthoringVolume Volume,
            bool Looped, IReadOnlyList<(PllEffectZoneTransition Transition, PllHookedPropGenerationAuthoring Target)> Triggers)
        {
            const string Action = "Add Generation Trigger Zone";
            if (Triggers.Count == 0)
                throw new InvalidOperationException("Generation Trigger Zone requires at least one Enter or Exit target.");
            Volume.RequireValid();
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllEffectZoneAuthoring>(Target, Action);
            if (!Owner.IsBiomeRoot || Target.transform == Owner.Floor || Target.transform == Owner.Campfire)
                throw new InvalidOperationException("Generation Trigger Zone requires a separate biome source object.");
            var Targets = Owner.StageRoot.GetComponentsInChildren<PllHookedPropGenerationAuthoring>(true);
            var Owners = new Dictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring>();
            foreach (var Zone in Owner.StageRoot.GetComponentsInChildren<PllEffectZoneAuthoring>(true))
            {
                if (TransformOwnership.Overlaps(Target.transform, Zone.transform))
                    throw new InvalidOperationException("Effect Zone roots cannot overlap.");
                PllHookedPropGenerationSourceContract.ValidateActions(Zone, false, Targets, Owners);
            }
            PllHookedPropGenerationSourceContract.ValidateActionSelection(Target.transform, Looped, Triggers, Targets, Owners);
            var CheckedTargets = new HashSet<PllHookedPropGenerationAuthoring>();
            foreach (var Trigger in Triggers)
                if (CheckedTargets.Add(Trigger.Target))
                    PllSourceIdentityValidator.ValidateCreatorComponent(Trigger.Target, Action);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Zone = Undo.AddComponent<PllEffectZoneAuthoring>(Target);
                using (var Serialized = new SerializedObject(Zone))
                {
                    PllAuthoringCommands.SetReference(Serialized, "root", Target);
                    Volume.Apply(Serialized);
                    PllAuthoringCommands.Property(Serialized, "LoopedValue").boolValue = Looped;
                    var Entries = PllAuthoringCommands.Property(Serialized, "PropTriggersValue");
                    Entries.arraySize = Triggers.Count;
                    for (var Index = 0; Index < Triggers.Count; Index++)
                    {
                        var Entry = Entries.GetArrayElementAtIndex(Index);
                        Entry.FindPropertyRelative("TransitionValue").intValue = (int)Triggers[Index].Transition;
                        Entry.FindPropertyRelative("TargetValue").objectReferenceValue = Triggers[Index].Target;
                    }
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Zone;
            });
        }
    }
}
