using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateAmuletSpawnerWindow : EditorWindow
    {
        enum Placement { Normal, Late, Final }

        GameObject Root;
        PllAmuletOpportunityCategory Category = PllAmuletOpportunityCategory.InheritBiome;
        Placement Timing;
        bool Custom;
        GameObject Model;
        Transform Anchor;
        Material SurfaceMatch;

        [MenuItem("PLL/Props/Create Amulet Spawner in Selected Biome", priority = 25)]
        static void Open()
        {
            try
            {
                var Selected = Selection.activeGameObject;
                PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Selected);
                var Window = GetWindow<PllCreateAmuletSpawnerWindow>("Create Amulet Spawner");
                Window.Root = Selected;
                Window.Show();
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        void OnGUI()
        {
            var Link = Root ? Root.GetComponent<PllBiomeRoot>() : null;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Biome", Root, typeof(GameObject), true);
            Category = (PllAmuletOpportunityCategory)EditorGUILayout.EnumPopup("Opportunity Category", Category);
            EditorGUILayout.HelpBox(
                "Categories select distinct slots in the shared run rotation, not a named amulet. An Authored biome needs an explicit category.",
                MessageType.Info);
            Timing = (Placement)EditorGUILayout.EnumPopup("Placement", Timing);
            Custom = EditorGUILayout.Toggle("Custom Pedestal", Custom);
            if (Custom)
            {
                Model = (GameObject)EditorGUILayout.ObjectField("Model Prefab", Model, typeof(GameObject), false);
                Anchor = (Transform)EditorGUILayout.ObjectField("Amulet Anchor", Anchor, typeof(Transform), false);
                SurfaceMatch = (Material)EditorGUILayout.ObjectField(
                    "Native Surface Match Material", SurfaceMatch, typeof(Material), false);
                EditorGUILayout.HelpBox(
                    "Leave the match material empty to keep custom materials. The model needs solid collision and an owned anchor; it cannot contain executable behavior.",
                    MessageType.Info);
            }
            using (new EditorGUI.DisabledScope(!Link || !Link.Definition
                || Category == PllAmuletOpportunityCategory.InheritBiome && !Link.Definition.SupportsNativeProgressionContents
                || Custom && (!Model || !Anchor || Anchor != Model.transform && !Anchor.IsChildOf(Model.transform))))
                if (GUILayout.Button("Create Amulet Spawner"))
                {
                    try
                    {
                        Create(Root, Category, Timing, Custom
                            ? new PllCustomAmuletPedestal(Model, Anchor, SurfaceMatch)
                            : new PllNativeAmuletPedestal());
                        Close();
                    }
                    catch (ArgumentException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        static void Create(GameObject Root, PllAmuletOpportunityCategory Category,
            Placement Timing, PllAmuletPresentation Presentation)
        {
            var Link = Root.GetComponent<PllBiomeRoot>();
            PllAmuletSpawnerAuthoring.ResolveCategory(Category, Link.Definition.Behavior);
            if (Presentation is PllCustomAmuletPedestal Custom) Custom.RequireValid();
            const string Action = "Create Amulet Spawner";
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            var Scope = new GameObject("Amulet Spawner");
            Undo.RegisterCreatedObjectUndo(Scope, Action);
            Scope.transform.SetParent(Root.GetComponent<PllBiomePrefabAnchors>().FloorRoot.transform, false);
            if (Timing == Placement.Late)
                Undo.AddComponent<PropGrouper>(Scope).timing = PropGrouper.PropGrouperTiming.Late;
            else if (Timing == Placement.Final)
            {
                Undo.AddComponent<PLLFinalGeneration>(Scope);
            }
            var Owner = new GameObject("Amulet Opportunity");
            Undo.RegisterCreatedObjectUndo(Owner, Action);
            Owner.transform.SetParent(Scope.transform, false);
            var Source = Undo.AddComponent<PllAmuletSpawnerAuthoring>(Owner);
            using (var Serialized = new SerializedObject(Source))
            {
                Serialized.FindProperty("CategoryValue").intValue = (int)Category;
                Serialized.FindProperty("PresentationValue").managedReferenceValue = Presentation;
                var Volume = Serialized.FindProperty("VolumeValue");
                Volume.FindPropertyRelative("shape").intValue = (int)PllZoneShape.Box;
                Volume.FindPropertyRelative("center").vector3Value = new Vector3(0f, 3f, 0f);
                Volume.FindPropertyRelative("size").vector3Value = new Vector3(5f, 6f, 5f);
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Undo.CollapseUndoOperations(Group);
            Selection.activeGameObject = Owner;
            EditorGUIUtility.PingObject(Owner);
        }
    }
}
