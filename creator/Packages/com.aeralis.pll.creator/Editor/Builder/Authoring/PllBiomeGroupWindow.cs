using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllBiomeGroupWindow : EditorWindow
    {
        GameObject Root;
        readonly List<PllBiomeDefinition> Prefixes = new List<PllBiomeDefinition>();
        Vector2 Scroll;

        [MenuItem("PLL/Biomes/Add Biome Group to Selected Biome", priority = 25)]
        static void Open()
        {
            try
            {
                var Selected = Selection.activeGameObject;
                PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Selected);
                var Window = GetWindow<PllBiomeGroupWindow>("Biome Group");
                Window.Root = Selected;
                Window.Prefixes.Clear();
                var Existing = Selected.GetComponent<PllBiomeGroupAuthoring>();
                if (Existing) Window.Prefixes.AddRange(Existing.Prefixes);
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
                EditorGUILayout.ObjectField("Owner", Link ? Link.Definition : null,
                    typeof(PllBiomeDefinition), false);
            EditorGUILayout.HelpBox(
                "Prefixes generate in this order, followed by the Owner. Only the Owner controls layout selection. Prefixes must have Spawn Chance 100 and no layout relationships.",
                MessageType.Info);
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            for (var Index = 0; Index < Prefixes.Count; Index++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                Prefixes[Index] = (PllBiomeDefinition)EditorGUILayout.ObjectField(
                    "Prefix " + (Index + 1), Prefixes[Index], typeof(PllBiomeDefinition), false);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(Index == 0))
                    if (GUILayout.Button("Move Up"))
                    {
                        (Prefixes[Index - 1], Prefixes[Index]) = (Prefixes[Index], Prefixes[Index - 1]);
                        GUIUtility.ExitGUI();
                    }
                using (new EditorGUI.DisabledScope(Index + 1 == Prefixes.Count))
                    if (GUILayout.Button("Move Down"))
                    {
                        (Prefixes[Index + 1], Prefixes[Index]) = (Prefixes[Index], Prefixes[Index + 1]);
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Remove"))
                {
                    Prefixes.RemoveAt(Index);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Add Prefix")) Prefixes.Add(null);
            using (new EditorGUI.DisabledScope(!Root || Prefixes.Count == 0))
                if (GUILayout.Button("Apply Biome Group"))
                {
                    try
                    {
                        Apply(Root, Prefixes);
                        Close();
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PllBiomeGroupAuthoring Apply(
            GameObject Root, IReadOnlyList<PllBiomeDefinition> Prefixes)
        {
            const string Action = "Apply Biome Group";
            PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Root);
            var Link = Root.GetComponent<PllBiomeRoot>();
            if (!Link || !Link.Definition || !Link.Definition.RootPrefab
                || !AssetDatabase.IsMainAsset(Link.Definition)
                || !AssetDatabase.IsMainAsset(Link.Definition.RootPrefab)
                || PrefabStageUtility.GetCurrentPrefabStage().assetPath
                    != AssetDatabase.GetAssetPath(Link.Definition.RootPrefab))
                throw new InvalidOperationException("The biome root must link to its own saved definition.");
            var Owner = Link.Definition;
            var Draft = PllBiomeGroupContract.Prepare(Owner, Root, Prefixes);
            var Values = Draft.Prefixes;
            var Definitions = new PllBiomeDefinition[Values.Count + 1];
            var Groups = new List<PllBiomeGroupContract>();
            var Roots = new GameObject[Definitions.Length];
            var Ports = new Dictionary<int, BiomeConnectorPorts>();
            for (var Index = 0; Index < Values.Count; Index++)
            {
                var Prefix = Values[Index];
                if (!AssetDatabase.IsMainAsset(Prefix) || !Prefix.RootPrefab
                    || !AssetDatabase.IsMainAsset(Prefix.RootPrefab)
                    || !PrefabUtility.IsPartOfPrefabAsset(Prefix.RootPrefab))
                    throw new InvalidOperationException("Every Prefix must be a saved biome definition with a root prefab.");
                Definitions[Index] = Prefix;
                Roots[Index] = Prefix.RootPrefab;
                var Existing = PllBiomeGroupContract.Capture(Prefix);
                if (Existing != null) Groups.Add(Existing);
                Ports.Add(Prefix.GetInstanceID(), BiomeConnectorPorts.Capture(Prefix));
            }
            Definitions[Values.Count] = Owner;
            Roots[Values.Count] = Root;
            Ports.Add(Owner.GetInstanceID(), BiomeConnectorPorts.Capture(Owner, Root));
            Groups.Add(Draft);
            PllSourceCatalogValidator.ValidateConnectors(
                PllBiomeGroupContract.ValidateScope(Definitions, Roots, Groups, Draft), Ports);
            var Carrier = Root.GetComponent<PllBiomeGroupAuthoring>();

            Undo.IncrementCurrentGroup();
            var UndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                if (!Carrier) Carrier = Undo.AddComponent<PllBiomeGroupAuthoring>(Root);
                Undo.RecordObject(Carrier, Action);
                using (var Serialized = new SerializedObject(Carrier))
                {
                    var Field = PllAuthoringCommands.Property(Serialized, "PrefixesValue");
                    Field.arraySize = Values.Count;
                    for (var Index = 0; Index < Values.Count; Index++)
                        Field.GetArrayElementAtIndex(Index).objectReferenceValue = Values[Index];
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(Carrier);
                EditorSceneManager.MarkSceneDirty(Root.scene);
                Undo.CollapseUndoOperations(UndoGroup);
                Selection.activeObject = Carrier;
                return Carrier;
            }
            catch
            {
                Undo.RevertAllDownToGroup(UndoGroup);
                throw;
            }
        }
    }
}
